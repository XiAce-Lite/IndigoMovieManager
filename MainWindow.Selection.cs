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
        /// DataGrid上で選択行が変わった時の処理だ！選ばれた主役の動画データを詳細パネルへバッチリ引き渡すぜ！🎬
        /// </summary>
        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                // 未選択時は詳細パネル自体を隠す
                HideExtensionDetail();
                return;
            }

            // 選択されたMovieRecordsをセットし、XAML側のバインディングを機能させる
            ShowExtensionDetail(mv);

            // error 代替画像が見えている個体は通常キューへ戻さず rescue レーンへ逃がす。
            if (IsThumbnailErrorPlaceholderPath(mv.ThumbDetail))
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Hash = mv.Hash,
                    // 強制的に再作成させるためなどの理由で便宜的に99等のインデックスを振って投入
                    Tabindex = 99,
                };
                _ = TryEnqueueThumbnailDisplayErrorRescueJob(
                    tempObj,
                    reason: "detail-selection-error-placeholder"
                );
            }
        }

        /// <summary>
        /// DataGridでうっかりセルを直接編集しちゃうのを全ブロック！編集は専用の窓口からやってもらう安全設計だ！🛡️
        /// </summary>
        private void ListDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            e.Cancel = true;
        }

        /// <summary>
        /// 画像やカスタム枠がカチッとクリックされた時！クリックされた場所を覚えつつ、そのデータをリストの「現在の選択」とシンクロさせる絆の処理！🤝
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
        /// リスト表示のアイテムが左クリックされた瞬間のキャッチ！Ctrlキーの複数選択ジャマはせず、未選択なら俺が選んでやるぜ！👆
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
        /// サムネ大（BigList）のアイテムがクリックされたらコイツの出番！小リストと同じく手厚くフォローするぜ！🖐️
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
        /// Windows10風スタイル（BigList10）のアイテムがクリックされた時の処理！どんな見た目でも俺たちの対応速度は変わらないぜ！⚡
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
