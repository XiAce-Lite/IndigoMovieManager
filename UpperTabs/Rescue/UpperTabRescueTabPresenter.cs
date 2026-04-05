using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IndigoMovieManager.UpperTabs.Rescue
{
    // 救済タブの初期化、対象選択、一覧バインドだけを担当する。
    internal sealed class UpperTabRescueTabPresenter
    {
        private readonly RescueTabView _view;
        private readonly ObservableCollection<UpperTabRescueTargetOption> _targets;
        private readonly ObservableCollection<UpperTabRescueListItemViewModel> _items;
        private readonly Func<IEnumerable<UpperTabRescueTargetOption>> _buildTargets;
        private readonly Func<int> _getDefaultTargetTabIndex;
        private readonly Action _onTargetSelectionChanged;
        private bool _targetSelectionHooked;

        public UpperTabRescueTabPresenter(
            RescueTabView view,
            ObservableCollection<UpperTabRescueTargetOption> targets,
            ObservableCollection<UpperTabRescueListItemViewModel> items,
            Func<IEnumerable<UpperTabRescueTargetOption>> buildTargets,
            Func<int> getDefaultTargetTabIndex,
            Action onTargetSelectionChanged
        )
        {
            _view = view;
            _targets = targets;
            _items = items;
            _buildTargets = buildTargets;
            _getDefaultTargetTabIndex = getDefaultTargetTabIndex;
            _onTargetSelectionChanged = onTargetSelectionChanged;
        }

        // 対象候補と一覧コレクションを view へ結び、既定選択だけ整える。
        public void Initialize()
        {
            if (_view == null)
            {
                return;
            }

            EnsureTargets();
            _view.TargetTabComboBoxControl.ItemsSource = _targets;
            _view.RescueListDataGridControl.ItemsSource = _items;
            EnsureDefaultSelection();
            HookSelectionChanged();
            _onTargetSelectionChanged?.Invoke();
        }

        public UpperTabRescueTargetOption GetSelectedTarget()
        {
            return _view?.TargetTabComboBoxControl?.SelectedItem as UpperTabRescueTargetOption;
        }

        public void ReplaceItems(IEnumerable<UpperTabRescueListItemViewModel> items)
        {
            _items.Clear();
            foreach (UpperTabRescueListItemViewModel item in items ?? [])
            {
                _items.Add(item);
            }
        }

        private void EnsureTargets()
        {
            if (_targets.Count > 0)
            {
                return;
            }

            foreach (UpperTabRescueTargetOption option in _buildTargets?.Invoke() ?? [])
            {
                _targets.Add(option);
            }
        }

        private void EnsureDefaultSelection()
        {
            if (_view?.TargetTabComboBoxControl?.SelectedItem != null)
            {
                return;
            }

            int defaultTabIndex = _getDefaultTargetTabIndex();
            UpperTabRescueTargetOption defaultTarget = _targets.FirstOrDefault(
                x => x.TabIndex == defaultTabIndex
            );
            _view.TargetTabComboBoxControl.SelectedItem = defaultTarget ?? _targets.FirstOrDefault();
        }

        private void HookSelectionChanged()
        {
            if (_targetSelectionHooked || _view == null)
            {
                return;
            }

            _view.TargetTabComboBoxControl.SelectionChanged += (_, _) =>
                _onTargetSelectionChanged?.Invoke();
            _targetSelectionHooked = true;
        }
    }
}
