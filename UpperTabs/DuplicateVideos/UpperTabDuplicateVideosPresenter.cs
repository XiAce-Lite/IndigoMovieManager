using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    // 重複動画タブの初期化、ItemsSource、ヘッダー表示だけを担当する。
    internal sealed class UpperTabDuplicateVideosPresenter
    {
        private readonly DuplicateVideosTabView _view;
        private readonly ObservableCollection<UpperTabDuplicateGroupViewModel> _groups;
        private readonly ObservableCollection<UpperTabDuplicateItemViewModel> _items;
        private readonly ObservableCollection<UpperTabDuplicateGroupSortOption> _sortOptions;

        public UpperTabDuplicateVideosPresenter(
            DuplicateVideosTabView view,
            ObservableCollection<UpperTabDuplicateGroupViewModel> groups,
            ObservableCollection<UpperTabDuplicateItemViewModel> items,
            ObservableCollection<UpperTabDuplicateGroupSortOption> sortOptions
        )
        {
            _view = view;
            _groups = groups;
            _items = items;
            _sortOptions = sortOptions;
        }

        // 起動直後でも空状態を崩さないよう、view とコレクションを結ぶ。
        public void Initialize()
        {
            if (_view == null)
            {
                return;
            }

            EnsureSortOptions();
            _view.GroupSortComboBoxControl.ItemsSource = _sortOptions;
            _view.GroupSortComboBoxControl.DisplayMemberPath = nameof(
                UpperTabDuplicateGroupSortOption.DisplayName
            );
            if (_view.GroupSortComboBoxControl.SelectedItem == null)
            {
                _view.GroupSortComboBoxControl.SelectedItem = _sortOptions.FirstOrDefault();
            }

            _view.DuplicateGroupSelectorControl.ItemsSource = _groups;
            _view.DuplicatePreviewListBoxControl.ItemsSource = _items;
            _view.DuplicateDetailDataGridControl.ItemsSource = _items;
            SetHeaderSummary(0, 0, "-");
        }

        public UpperTabDuplicateGroupSortOption GetSelectedSortOption()
        {
            return _view?.GroupSortComboBoxControl?.SelectedItem as UpperTabDuplicateGroupSortOption;
        }

        public void SetHeaderSummary(int groupCount, int selectedCount, string selectedHash)
        {
            if (_view == null)
            {
                return;
            }

            _view.GroupCountTextBlockControl.Text = groupCount.ToString();
            _view.SelectedCountTextBlockControl.Text = selectedCount.ToString();
            _view.SelectedHashTextBlockControl.Text =
                string.IsNullOrWhiteSpace(selectedHash) ? "-" : selectedHash;
        }

        private void EnsureSortOptions()
        {
            if (_sortOptions.Count > 0)
            {
                return;
            }

            _sortOptions.Add(new UpperTabDuplicateGroupSortOption("duplicate-count", "重複数"));
            _sortOptions.Add(new UpperTabDuplicateGroupSortOption("max-size", "サイズ(最大)"));
        }
    }
}
