using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;

namespace Rox
{
    public class ViewModelCollection<TViewModel, TModel>
        : IReadOnlyList<TViewModel>,
        INotifyCollectionChanged,
        INotifyPropertyChanged
    {
        #region Constructor

        private readonly Func<TModel, TViewModel> CreateViewModel;
        private readonly ViewModelObservableCollection ViewModels;

        public ViewModelCollection(Func<TModel, TViewModel> createViewModel = null)
        {
            if (createViewModel != null)
            {
                CreateViewModel = createViewModel;
            }
            else
            {
                Type modelType = typeof(TModel);
                Type viewModelType = typeof(TViewModel);
                ConstructorInfo viewModelConstructor = viewModelType.GetConstructor(new[] { modelType });
                if (viewModelConstructor == null) throw new ArgumentNullException(nameof(CreateViewModel), $"{viewModelType.Name} does not contain a constructor with one parameter of type {modelType.Name}.\n\nYou must provide a CreateViewModel function.");

                CreateViewModel = model =>
                {
                    TViewModel viewModel = (TViewModel)viewModelConstructor.Invoke(new object[] { model });

                    return viewModel;
                };
            }
            ViewModels = new ViewModelObservableCollection(this);
        }

        #endregion

        #region Set Models

        private IEnumerable<TModel> Models;

        public ViewModelCollection<TViewModel, TModel> SetModels<TNotify>(TNotify models)
            where TNotify : IEnumerable<TModel>, INotifyCollectionChanged
        {
            //Remove old handler - this will also filter null
            if (Models is INotifyCollectionChanged removeModels) removeModels.CollectionChanged -= ModelsCollectionChanged;

            //Clear the viewModel collection from start
            int originalCount = ViewModels.Count;
            int removeOldIndex = 0;
            for (int oldIndex = 0; oldIndex < originalCount; oldIndex++)
            {
                ViewModels.RemoveAt(removeOldIndex);
            }

            if (models != null)
            {
                //Add each viewModel created for each model
                foreach (TModel model in models)
                {
                    TViewModel viewModel = CreateViewModel(model);

                    if (viewModel != null) ViewModels.Add(viewModel);
                }

                //Add new handler
                models.CollectionChanged += ModelsCollectionChanged;
            }

            Models = models;

            //Fluent API
            return this;
        }

        #endregion

        #region Models Collection Changed

        private void ModelsCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        int insertNewIndex = args.NewStartingIndex;
                        foreach (TModel newItem in args.NewItems)
                        {
                            TViewModel newViewModel = CreateViewModel(newItem);
                            if (newViewModel != null)
                            {
                                ViewModels.Insert(insertNewIndex, newViewModel);
                                insertNewIndex++;
                            }
                        }

                        break;
                    }
                case NotifyCollectionChangedAction.Move:
                    {
                        IList<TViewModel> moveViewModels = new List<TViewModel>();
                        int removeOldIndex = args.OldStartingIndex;
                        for (int oldIndex = 0; oldIndex < args.OldItems.Count; oldIndex++)
                        {
                            moveViewModels.Add(ViewModels[removeOldIndex]);

                            ViewModels.RemoveAt(removeOldIndex);
                        }

                        int insertNewIndex = args.NewStartingIndex;
                        foreach (TViewModel moveViewModel in moveViewModels)
                        {
                            ViewModels.Insert(insertNewIndex, moveViewModel);
                            insertNewIndex++;
                        }

                        break;
                    }
                case NotifyCollectionChangedAction.Remove:
                    {
                        int removeOldIndex = args.OldStartingIndex;
                        for (int oldIndex = 0; oldIndex < args.OldItems.Count; oldIndex++)
                        {
                            ViewModels.RemoveAt(removeOldIndex);
                        }

                        break;
                    }
                case NotifyCollectionChangedAction.Replace:
                    {
                        int removeOldIndex = args.OldStartingIndex;
                        for (int oldIndex = 0; oldIndex < args.OldItems.Count; oldIndex++)
                        {
                            ViewModels.RemoveAt(removeOldIndex);
                        }

                        int insertNewIndex = args.NewStartingIndex;
                        foreach (TModel newItem in args.NewItems)
                        {
                            TViewModel newViewModel = CreateViewModel(newItem);
                            if (newViewModel != null)
                            {
                                ViewModels.Insert(insertNewIndex, newViewModel);
                                insertNewIndex++;
                            }
                        }

                        break;
                    }
                case NotifyCollectionChangedAction.Reset:
                default:
                    {
                        int originalCount = ViewModels.Count;
                        int removeOldIndex = 0;
                        for (int oldIndex = 0; oldIndex < originalCount; oldIndex++)
                        {
                            ViewModels.RemoveAt(removeOldIndex);
                        }

                        break;
                    }
            }
        }

        #endregion

        #region Interface

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add => PropertyChangedHandler += value;
            remove => PropertyChangedHandler -= value;
        }

        private event PropertyChangedEventHandler PropertyChangedHandler;

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChangedHandler?.Invoke(this, args);

        public TViewModel this[int index] => ViewModels[index];

        public int Count => ViewModels.Count;

        public IEnumerator<TViewModel> GetEnumerator() => ViewModels.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ViewModels.GetEnumerator();

        #endregion

        #region ViewModel Observable Collection

        private class ViewModelObservableCollection
            : ObservableCollection<TViewModel>
        {
            private readonly ViewModelCollection<TViewModel, TModel> ViewModelCollection;

            public ViewModelObservableCollection(ViewModelCollection<TViewModel, TModel> viewModelCollection)
            {
                ViewModelCollection = viewModelCollection;
            }

            protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
            {
                base.OnCollectionChanged(args);

                ViewModelCollection.OnCollectionChanged(args);
            }

            protected override void OnPropertyChanged(PropertyChangedEventArgs args)
            {
                base.OnPropertyChanged(args);

                ViewModelCollection.OnPropertyChanged(args);
            }
        }

        #endregion
    }
}