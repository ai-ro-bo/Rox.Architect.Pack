using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Rox
{
    public class EntityCollection<TEntity, TModel>
        : ObservableCollection<TEntity>
        where TEntity : INotifyPropertyChanged
    {
        #region Constructor

        private readonly IEnumerable<string> KeyPropertyNames;
        private readonly Func<Task<IEnumerable<TModel>>> GetModels;
        private readonly Func<TModel, TEntity> CreateEntity;

        private IEnumerable<TEntity> OldModels;
        private readonly IList<TEntity> CurrentModels;

        public EntityCollection(IEnumerable<string> keyPropertyNames, Func<Task<IEnumerable<TModel>>> getModels, Func<TModel, TEntity> createEntity = null)
        {
            if (!keyPropertyNames.Any()) throw new ArgumentNullException(nameof(keyPropertyNames));
            KeyPropertyNames = keyPropertyNames;

            GetModels = getModels ?? throw new ArgumentNullException(nameof(getModels));

            if (createEntity != null)
            {
                CreateEntity = createEntity;
            }
            else
            {
                Type modelType = typeof(TModel);
                Type entityType = typeof(TEntity);
                ConstructorInfo entityConstructor = entityType.GetConstructor(new[] { modelType })
                    ?? throw new ArgumentNullException(nameof(CreateEntity), $"{entityType.Name} does not contain a constructor with one parameter of type {modelType.Name}.\n\nYou must provide a CreateEntity function.");

                CreateEntity = model =>
                {
                    TEntity entity = (TEntity)entityConstructor.Invoke(new object[0]);

                    return entity;
                };
            }

            CurrentModels = new List<TEntity>();
            OldModels = new TEntity[0];
        }

        #endregion

        #region Filters

        private readonly IList<FilterItem> Filters = new List<FilterItem>();

        //Note: AddFilter is added as an AND filter
        public ModelCollection<TEntity> AddFilter(Func<TEntity, bool> filter)
        {
            Filters.Add(new FilterItem(filter, true));

            //Fluent API
            return this;
        }

        public ModelCollection<TEntity> AddOrFilter(Func<TEntity, bool> filter)
        {
            Filters.Add(new FilterItem(filter, false));

            //Fluent API
            return this;
        }

        public ModelCollection<TEntity> ClearFilters()
        {
            Filters.Clear();

            //Fluent API
            return this;
        }

        private IQueryable<TEntity> CreateWhere(IQueryable<TEntity> source)
        {
            Func<TEntity, bool> whereFunction = null;
            foreach (FilterItem filterItem in Filters)
            {
                if (whereFunction != null)
                {
                    //Note: If first item is an OR filter then the OR is ignored
                    whereFunction = filterItem.Filter;
                }
                else
                {
                    if (filterItem.And)
                    {
                        whereFunction = (model) => whereFunction(model) && filterItem.Filter(model);
                    }
                    else
                    {
                        whereFunction = (model) => whereFunction(model) || filterItem.Filter(model);
                    }
                }
            }

            if (whereFunction != null)
            {
                Expression<Func<TEntity, bool>> whereExpression = model => whereFunction(model);
                source = source.Where(whereExpression);
            }

            return source;
        }

        private class FilterItem
        {
            public FilterItem(Func<TEntity, bool> filter, bool and)
            {
                Filter = filter;
                And = and;
            }

            public Func<TEntity, bool> Filter { get; }

            public bool And { get; }
        }

        #endregion

        #region Sorts

        private readonly IList<SortItem> Sorts = new List<SortItem>();

        //Note: AddSort is added as an ASC sort
        public ModelCollection<TEntity> AddSort<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            //Fluent API
            return AddSort(keySelector, true);
        }

        public ModelCollection<TEntity> AddSortDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            //Fluent API
            return AddSort(keySelector, false);
        }

        private ModelCollection<TEntity> AddSort<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool ascending)
        {
            Type[] types = new Type[] { typeof(TEntity), typeof(TKey) };
            UnaryExpression unaryExpression = Expression.Quote(keySelector);
            SortItem sortItem = new SortItem(types, unaryExpression, ascending);

            Sorts.Add(sortItem);

            //Fluent API
            return this;
        }

        private IQueryable<TEntity> CreateOrderBys(IQueryable<TEntity> source)
        {
            bool sortAdded = false;
            foreach (SortItem sortItem in Sorts)
            {
                string methodName = !sortAdded
                    ? (sortItem.Ascending ? nameof(Queryable.OrderBy) : nameof(Queryable.OrderByDescending))
                    : (sortItem.Ascending ? nameof(Queryable.ThenBy) : nameof(Queryable.ThenByDescending));
                MethodCallExpression sortExpression = Expression.Call(typeof(Queryable), methodName, sortItem.Types, source.Expression, sortItem.UnaryExpression);

                source = source.Provider.CreateQuery<TEntity>(sortExpression);

                sortAdded = true;
            }

            return source;
        }

        private class SortItem
        {
            public SortItem(Type[] types, UnaryExpression unaryExpression, bool ascending)
            {
                Types = types;
                UnaryExpression = unaryExpression;
                Ascending = ascending;
            }

            public Type[] Types { get; }

            public UnaryExpression UnaryExpression { get; }

            public bool Ascending { get; }
        }

        #endregion

        #region Refresh Data

        public async Task RefreshData()
        {
            IEnumerable<TEntity> newModels;
            if (GetModels == null)
            {
                newModels = new TEntity[0];
            }
            else
            {
                newModels = await GetModels.Invoke();
            }

            IEnumerable<PropertyInfo> propertyInfos = typeof(TEntity).GetPropertyInfos();
            IEnumerable<PropertyInfo> keyPropertyInfos = propertyInfos.GetKeyPropertyInfos(KeyPropertyNames);
            IEnumerable<PropertyInfo> changePropertyInfos = GetChangePropertyInfos(propertyInfos, keyPropertyInfos);

            //Note: Get the changes between oldModels and newModels
            IEnumerable<CompareItem> compareItems = OldModels.Compare(newModels, keyPropertyInfos, changePropertyInfos);

            //Note: Apply the changes to the currentModels
            CurrentModels.Digest(compareItems, CreateEntity, keyPropertyInfos, changePropertyInfos);

            await RefreshView();

            OldModels = newModels;
        }

        private static IEnumerable<PropertyInfo> GetChangePropertyInfos(IEnumerable<PropertyInfo> propertyInfos, IEnumerable<PropertyInfo> keyPropertyInfos)
        {
            ICollection<PropertyInfo> changePropertyInfos = new List<PropertyInfo>();
            foreach (PropertyInfo propertyInfo in propertyInfos)
            {
                if (!keyPropertyInfos.Contains(propertyInfo))
                {
                    changePropertyInfos.Add(propertyInfo);
                }
            }
            return changePropertyInfos;
        }

        #endregion

        #region Refresh View

        private bool RefreshingView = false;

        public Task RefreshView()
        {
            IQueryable<TEntity> presentedQuery = CurrentModels.AsQueryable();

            //Note: Create Where from filter collection
            presentedQuery = CreateWhere(presentedQuery);

            //Note: Create OrderBy from sort collection
            presentedQuery = CreateOrderBys(presentedQuery);

            IEnumerable<TEntity> presentedModels = presentedQuery.AsEnumerable();

            RefreshingView = true;
            try
            {
                this.UpdateByReference(presentedModels);
            }
            finally
            {
                RefreshingView = false;
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Write Changes

        public void WriteChanges(IList<TEntity> baseCollection)
        {
            IEnumerable<PropertyInfo> propertyInfos = typeof(TEntity).GetPropertyInfos();
            IEnumerable<PropertyInfo> keyPropertyInfos = propertyInfos.GetKeyPropertyInfos(KeyPropertyNames);
            IEnumerable<PropertyInfo> changePropertyInfos = GetChangePropertyInfos(propertyInfos, keyPropertyInfos);

            //Note: Get the changes between baseCollection and CurrentModels
            IEnumerable<CompareItem> compareItems = baseCollection.Compare(CurrentModels, keyPropertyInfos, changePropertyInfos);

            //Note: Apply the changes to the baseCollection
            baseCollection.Digest(compareItems, CreateEntity, keyPropertyInfos, changePropertyInfos);
        }

        #endregion

        #region Current Collection Changed

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            if (!RefreshingView)
            {
                switch (args.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        {
                            foreach (TEntity newItem in args.NewItems)
                            {
                                CurrentModels.Add(newItem);
                            }

                            break;
                        }
                    case NotifyCollectionChangedAction.Move:
                        {
                            //Note: By Reference so nothing to do

                            break;
                        }
                    case NotifyCollectionChangedAction.Remove:
                        {
                            foreach (TEntity oldItem in args.OldItems)
                            {
                                CurrentModels.Remove(oldItem);
                            }

                            break;
                        }
                    case NotifyCollectionChangedAction.Replace:
                        {
                            foreach (TEntity oldItem in args.OldItems)
                            {
                                CurrentModels.Remove(oldItem);
                            }

                            foreach (TEntity newItem in args.NewItems)
                            {
                                CurrentModels.Add(newItem);
                            }

                            break;
                        }
                    case NotifyCollectionChangedAction.Reset:
                    default:
                        {
                            int originalCount = this.Count;
                            int removeOldIndex = 0;
                            for (int oldIndex = 0; oldIndex < originalCount; oldIndex++)
                            {
                                CurrentModels.RemoveAt(removeOldIndex);
                            }

                            break;
                        }
                }
            }

            base.OnCollectionChanged(args);
        }

        #endregion
    }
}