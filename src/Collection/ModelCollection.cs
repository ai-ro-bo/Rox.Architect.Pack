using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Rox
{
    public class ModelCollection<TModel>
        : ObservableCollection<TModel>
    {
        #region Constructor

        private readonly IEnumerable<string> KeyPropertyNames;
        private readonly Func<Task<IEnumerable<TModel>>> GetModels;
        private readonly Func<TModel> CreateModel;

        private IEnumerable<TModel> OldModels;
        private readonly IList<TModel> CurrentModels;

        public ModelCollection(IEnumerable<string> keyPropertyNames, Func<Task<IEnumerable<TModel>>> getModels, Func<TModel> createModel = null)
        {
            if (!keyPropertyNames.Any()) throw new ArgumentNullException(nameof(keyPropertyNames));
            KeyPropertyNames = keyPropertyNames;

            GetModels = getModels ?? throw new ArgumentNullException(nameof(getModels));

            if (createModel != null)
            {
                CreateModel = createModel;
            }
            else
            {
                Type modelType = typeof(TModel);
                ConstructorInfo modelConstructor = modelType.GetConstructor(Type.EmptyTypes);
                if (modelConstructor == null) throw new ArgumentNullException(nameof(CreateModel), $"{modelType.Name} does not contain an empty constructor.\n\nYou must provide a CreateModel function.");

                CreateModel = () =>
                {
                    TModel model = (TModel)modelConstructor.Invoke(new object[0]);

                    return model;
                };
            }

            CurrentModels = new List<TModel>();
            OldModels = new TModel[0];
        }

        #endregion

        #region Filters

        private readonly IList<FilterItem> Filters = new List<FilterItem>();

        //Note: AddFilter is added as an AND filter
        public ModelCollection<TModel> AddFilter(Func<TModel, bool> filter)
        {
            Filters.Add(new FilterItem(filter, true));

            //Fluent API
            return this;
        }

        public ModelCollection<TModel> AddOrFilter(Func<TModel, bool> filter)
        {
            Filters.Add(new FilterItem(filter, false));

            //Fluent API
            return this;
        }

        public ModelCollection<TModel> ClearFilters()
        {
            Filters.Clear();

            //Fluent API
            return this;
        }

        private IQueryable<TModel> CreateWhere(IQueryable<TModel> source)
        {
            Func<TModel, bool> whereFunction = null;
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
                Expression<Func<TModel, bool>> whereExpression = model => whereFunction(model);
                source = source.Where(whereExpression);
            }

            return source;
        }

        private class FilterItem
        {
            public FilterItem(Func<TModel, bool> filter, bool and)
            {
                Filter = filter;
                And = and;
            }

            public Func<TModel, bool> Filter { get; }

            public bool And { get; }
        }

        #endregion

        #region Sorts

        private readonly IList<SortItem> Sorts = new List<SortItem>();

        //Note: AddSort is added as an ASC sort
        public ModelCollection<TModel> AddSort<TKey>(Expression<Func<TModel, TKey>> keySelector)
        {
            //Fluent API
            return AddSort(keySelector, true);
        }

        public ModelCollection<TModel> AddSortDescending<TKey>(Expression<Func<TModel, TKey>> keySelector)
        {
            //Fluent API
            return AddSort(keySelector, false);
        }

        private ModelCollection<TModel> AddSort<TKey>(Expression<Func<TModel, TKey>> keySelector, bool ascending)
        {
            Type[] types = new Type[] { typeof(TModel), typeof(TKey) };
            UnaryExpression unaryExpression = Expression.Quote(keySelector);
            SortItem sortItem = new SortItem(types, unaryExpression, ascending);

            Sorts.Add(sortItem);

            //Fluent API
            return this;
        }

        private IQueryable<TModel> CreateOrderBys(IQueryable<TModel> source)
        {
            bool sortAdded = false;
            foreach (SortItem sortItem in Sorts)
            {
                string methodName = !sortAdded
                    ? (sortItem.Ascending ? nameof(Queryable.OrderBy) : nameof(Queryable.OrderByDescending))
                    : (sortItem.Ascending ? nameof(Queryable.ThenBy) : nameof(Queryable.ThenByDescending));
                MethodCallExpression sortExpression = Expression.Call(typeof(Queryable), methodName, sortItem.Types, source.Expression, sortItem.UnaryExpression);

                source = source.Provider.CreateQuery<TModel>(sortExpression);

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
            IEnumerable<TModel> newModels;
            if (GetModels == null)
            {
                newModels = new TModel[0];
            }
            else
            {
                newModels = await GetModels.Invoke();
            }

            IEnumerable<PropertyInfo> propertyInfos = typeof(TModel).GetPropertyInfos();
            IEnumerable<PropertyInfo> keyPropertyInfos = propertyInfos.GetKeyPropertyInfos(KeyPropertyNames);
            IEnumerable<PropertyInfo> changePropertyInfos = GetChangePropertyInfos(propertyInfos, keyPropertyInfos);

            //Note: Get the changes between oldModels and newModels
            IEnumerable<CompareItem> compareItems = OldModels.Compare(newModels, keyPropertyInfos, changePropertyInfos);

            //Note: Apply the changes to the currentModels
            CurrentModels.Digest(compareItems, CreateModel, keyPropertyInfos, changePropertyInfos);

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
            IQueryable<TModel> presentedQuery = CurrentModels.AsQueryable();

            //Note: Create Where from filter collection
            presentedQuery = CreateWhere(presentedQuery);

            //Note: Create OrderBy from sort collection
            presentedQuery = CreateOrderBys(presentedQuery);

            IEnumerable<TModel> presentedModels = presentedQuery.AsEnumerable();

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

        public void WriteChanges(IList<TModel> baseCollection)
        {
            IEnumerable<PropertyInfo> propertyInfos = typeof(TModel).GetPropertyInfos();
            IEnumerable<PropertyInfo> keyPropertyInfos = propertyInfos.GetKeyPropertyInfos(KeyPropertyNames);
            IEnumerable<PropertyInfo> changePropertyInfos = GetChangePropertyInfos(propertyInfos, keyPropertyInfos);

            //Note: Get the changes between baseCollection and CurrentModels
            IEnumerable<CompareItem> compareItems = baseCollection.Compare(CurrentModels, keyPropertyInfos, changePropertyInfos);

            //Note: Apply the changes to the baseCollection
            baseCollection.Digest(compareItems, CreateModel, keyPropertyInfos, changePropertyInfos);
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
                            foreach (TModel newItem in args.NewItems)
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
                            foreach (TModel oldItem in args.OldItems)
                            {
                                CurrentModels.Remove(oldItem);
                            }

                            break;
                        }
                    case NotifyCollectionChangedAction.Replace:
                        {
                            foreach (TModel oldItem in args.OldItems)
                            {
                                CurrentModels.Remove(oldItem);
                            }

                            foreach (TModel newItem in args.NewItems)
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