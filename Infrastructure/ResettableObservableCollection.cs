using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// 全件差し替え時の通知を1回の Reset にまとめ、UI の無駄反応を減らす。
    /// </summary>
    public class ResettableObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();

            Items.Clear();
            if (items != null)
            {
                foreach (T item in items)
                {
                    Items.Add(item);
                }
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
