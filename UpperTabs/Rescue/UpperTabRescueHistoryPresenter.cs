using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace IndigoMovieManager.UpperTabs.Rescue
{
    // 救済タブ下段の履歴一覧と空表示だけを担当する。
    internal sealed class UpperTabRescueHistoryPresenter
    {
        private readonly RescueTabView _view;
        private readonly ObservableCollection<UpperTabRescueHistoryItemViewModel> _items;
        private bool _isBound;

        public UpperTabRescueHistoryPresenter(
            RescueTabView view,
            ObservableCollection<UpperTabRescueHistoryItemViewModel> items
        )
        {
            _view = view;
            _items = items;
        }

        public void Initialize()
        {
            if (_isBound || _view == null)
            {
                return;
            }

            _view.RescueHistoryDataGridControl.ItemsSource = _items;
            _isBound = true;
        }

        public void Clear(string targetText, string emptyMessage)
        {
            ReplaceItems([]);
            SetTargetText(targetText);
            SetEmptyMessage(emptyMessage, isVisible: true);
        }

        public void ShowItems(
            string targetText,
            IEnumerable<UpperTabRescueHistoryItemViewModel> items,
            string emptyMessageWhenNoItems
        )
        {
            ReplaceItems(items);
            SetTargetText(targetText);
            SetEmptyMessage(emptyMessageWhenNoItems, _items.Count < 1);
        }

        public void ShowUnavailable(string targetText, string emptyMessage)
        {
            ReplaceItems([]);
            SetTargetText(targetText);
            SetEmptyMessage(emptyMessage, isVisible: true);
        }

        private void ReplaceItems(IEnumerable<UpperTabRescueHistoryItemViewModel> items)
        {
            _items.Clear();
            foreach (UpperTabRescueHistoryItemViewModel item in items ?? [])
            {
                _items.Add(item);
            }
        }

        private void SetTargetText(string text)
        {
            if (_view == null)
            {
                return;
            }

            _view.HistoryTargetTextBlockControl.Text = text ?? "";
        }

        private void SetEmptyMessage(string text, bool isVisible)
        {
            if (_view == null)
            {
                return;
            }

            _view.HistoryEmptyTextBlockControl.Text = text ?? "";
            _view.HistoryEmptyTextBlockControl.Visibility = isVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
