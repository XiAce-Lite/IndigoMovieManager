using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // =================================================================================
        // リスト選択に関する UI イベント処理 (View層のロジック)
        // ユーザーが動画一覧(DataGrid / 各種ListView)から動画を選んだときの挙動。
        // 詳細情報パネルへのデータバインディングや、エラーサムネイルの再作成などを制御する。
        // =================================================================================

        /// <summary>
        /// 詳細表示用のリスト（DataGrid）で選択行が変わった際の処理。
        /// 選択された1件の動画情報を「詳細パネル（viewExtDetail）」のデータ元(DataContext)としてバインドする。
        /// </summary>
        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                // 未選択時は詳細パネル自体を隠す
                viewExtDetail.Visibility = Visibility.Collapsed;
                return;
            }

            // 選択されたMovieRecordsをセットし、XAML側のバインディングを機能させる
            viewExtDetail.DataContext = mv;
            viewExtDetail.Visibility = Visibility.Visible;

            // 選択した動画が過去に「サムネ生成エラー」を起こした記録を持っていた場合、
            // 「今ならファイルにアクセスできて生成できるかもしれない」と判断し、
            // バックグラウンドのサムネイル作成キュー(QueueDB)に再投入する。
            if (
                !string.IsNullOrEmpty(mv.ThumbDetail)
                && mv.ThumbDetail.Contains("error", StringComparison.CurrentCultureIgnoreCase)
            )
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    // 強制的に再作成させるためなどの理由で便宜的に99等のインデックスを振って投入
                    Tabindex = 99,
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }
        }

        /// <summary>
        /// DataGrid上でのセルの直接編集（F2キーやダブルクリック等）を禁止する。
        /// 編集操作は右クリックなどの専用「プロパティ変更・名前変更」の導線のみに絞るため。
        /// </summary>
        private void ListDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            e.Cancel = true;
        }

        /// <summary>
        /// サムネイルなどが乗っているカスタム枠（ListView内のItem等）がクリックされた際の処理。
        /// クリックされた位置(Label内のどこか)を記憶しつつ、対応するデータを一覧の選択行として設定する。
        /// </summary>
        private void Label_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Label label && label.DataContext is MovieRecords record)
            {
                // UI上でのクリックとロジック上の選択を同期させる
                ListDataGrid.SelectedItem = record;
                // 後続のドラッグ＆ドロップ判定などに使うため、クリックした座標を保存
                lbClickPoint = e.GetPosition(label);
            }
        }

        /// <summary>
        /// SmallList表示（サムネイル小）のアイテム内でマウスがクリックされた際の処理。
        /// </summary>
        private void SmallListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                // Ctrlキー押下時の複数選択モードのときは、標準機能に任せるので何もしない
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                // 未選択なら選択済みにし、List全体のSelectedItemにも指定する
                if (!item.IsSelected)
                {
                    item.IsSelected = true;
                    SmallList.SelectedItem = item.DataContext;
                }
            }
        }

        /// <summary>
        /// BigList表示（サムネイル大）のアイテム内でマウスがクリックされた際の処理。
        /// 処理の流れはSmallListと同様。
        /// </summary>
        private void BigListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                if (!item.IsSelected)
                {
                    item.IsSelected = true;
                    BigList.SelectedItem = item.DataContext;
                }
            }
        }

        /// <summary>
        /// Windows10風などの特殊ListView表示（BigList10）のアイテム内でマウスがクリックされた際の処理。
        /// 処理の流れはSmallListと同様。
        /// </summary>
        private void BigList10Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                if (!item.IsSelected)
                {
                    item.IsSelected = true;
                    BigList10.SelectedItem = item.DataContext;
                }
            }
        }
    }
}
