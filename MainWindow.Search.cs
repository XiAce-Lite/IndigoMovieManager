using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 検索UIの入力・履歴・実行をまとめて扱う。
        private void SearchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }

            // ドロップダウンが開いている間に選択が変わった場合のみフラグを立てる
            if (SearchBox.IsDropDownOpen)
            {
                _searchBoxItemSelectedByUser = true;
            }

            if (e.Source is ComboBox)
            {
                /*
                FilterAndSort(MainVM.DbInfo.Sort);  //サーチのコンボチェンジイベント。
                SelectFirstItem();
                if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
                {
                    //セレクションが変わってもHistoryに書いてるかも。
                    InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                }
                */
            }
        }

        private void SearchBoxItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is ComboBoxItem item && item.IsMouseOver)
            {
                item.IsSelected = true;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }

            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                // FindFact はフォーカス離脱時に記録する。
                InsertFindFactTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                // 検索キーワードがある場合は履歴へ追加する。
                InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }
            if (_imeFlag) { return; }

            // サムネイル作成タスクを停止し再起動
            RestartThumbnailTask();

            if (e.Source is ComboBox combo)
            {
                var text = combo.Text;
                /* インクリメントサーチ部。一旦コメントアウト。
                // 入力文字列の末尾が -, |, { のいずれかならサーチしない。}は終了なので、サーチスタート。
                if (!string.IsNullOrEmpty(text))
                {
                    // すでに{があり、}がまだ無い場合はreturn
                    int openIdx = text.IndexOf('{');
                    int closeIdx = text.IndexOf('}');
                    if (openIdx >= 0 && (closeIdx < 0 || closeIdx < openIdx))
                    {
                        return;
                    }

                    char lastChar = text[^1];
                    if (lastChar == '-' || lastChar == '|' || lastChar == '{')
                    {
                        return;
                    }
                }
                //インクリメンタルサーチがなぁ。ちょっと間隔で調整的な。美しくない。
                DateTime now = DateTime.Now;
                TimeSpan timeSinceLastUpdate = now - _lastInputTime;

                if (timeSinceLastUpdate >= _timeInputInterval)
                {
                    _lastInputTime = now;
                    FilterAndSort(MainVM.DbInfo.Sort);  //サーチのテキストチェンジイベント。
                    SelectFirstItem();
                }
                */
                if (string.IsNullOrEmpty(text))
                {
                    // テキストが空の場合は全件表示
                    FilterAndSort(MainVM.DbInfo.Sort, true);
                    SelectFirstItem();
                }
            }
        }

        // ドロップダウンリストでマウス選択時
        // DropDownClosedで、ユーザー操作による選択時のみ検索
        private void SearchBox_DropDownClosed(object sender, EventArgs e)
        {
            if (_searchBoxItemSelectedByUser)
            {
                DoSearchBoxSearch();
                _searchBoxItemSelectedByUser = false;
            }
        }

        // ドロップダウンリスト内でマウスクリック時にフラグを立てる
        private void SearchBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _searchBoxItemSelectedByUser = true;
        }

        private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }
            if (_imeFlag) { return; }
            if (e.Source is ComboBox combo)
            {
                // Deleteキーで履歴削除
                if (e.Key == Key.Delete && combo.IsDropDownOpen && combo.SelectedItem is History selectedHistory)
                {
                    int idx = combo.SelectedIndex;

                    // ViewModelから即時削除（UI応答性を優先）
                    MainVM.HistoryRecs.Remove(selectedHistory);

                    // DB削除をバックグラウンドで実行
                    await Task.Run(() => DeleteHistoryTable(MainVM.DbInfo.DBFullPath, selectedHistory.Find_Id));

                    // 削除後に次のアイテムを選択
                    if (MainVM.HistoryRecs.Count > 0)
                    {
                        if (idx >= MainVM.HistoryRecs.Count) { idx = MainVM.HistoryRecs.Count - 1; }
                        combo.SelectedIndex = idx;
                    }

                    e.Handled = true;
                    return;
                }

                // history への追加処理
                if (e.Key == Key.Enter)
                {
                    if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword) && (MainVM.DbInfo.SearchCount > 0))
                    {
                        InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                        GetHistoryTable(MainVM.DbInfo.DBFullPath);
                    }
                }
            }
        }

        // 検索実行処理
        private void DoSearchBoxSearch()
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }
            var text = SearchBox.Text;
            MainVM.DbInfo.SearchKeyword = text;
            FilterAndSort(MainVM.DbInfo.Sort, true);
            SelectFirstItem();
        }
    }
}
