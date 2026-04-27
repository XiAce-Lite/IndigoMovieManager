using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
                HideTagEditor();
                return;
            }

            // 選択されたMovieRecordsをセットし、XAML側のバインディングを機能させる
            ShowExtensionDetail(mv);
            ShowTagEditor(mv);

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
        private async void Label_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Label label && label.DataContext is MovieRecords record)
            {
                lbClickPoint = e.GetPosition(label);

                if (
                    e.ChangedButton == MouseButton.Left
                    && TabPlayer?.IsSelected == true
                )
                {
                    SelectPlayerThumbnailRecordWithoutScroll(label, record);
                    await OpenMovieInPlayerTabAsync(
                        record,
                        0,
                        playImmediately: true,
                        mute: false,
                        focusTimeSlider: false,
                        syncPlayerSelection: false
                    );
                    return;
                }

                // 画像クリック時の選択同期は、現在前面にいる通常タブ helper へ寄せる
                SelectCurrentUpperTabMovieRecord(record);
                // 後続のドラッグ＆ドロップ判定などに使うため、クリックした座標を保存
            }
        }

        // プレーヤータブ内のクリックは現在スクロール位置を守り、選択だけを同期する。
        private void SelectPlayerThumbnailRecordWithoutScroll(Label label, MovieRecords record)
        {
            ListView sourceList = FindVisualAncestor<ListView>(label);

            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                if (sourceList != null)
                {
                    if (!ReferenceEquals(sourceList.SelectedItem, record))
                    {
                        sourceList.SelectedItem = record;
                    }
                }

                SyncPlayerThumbnailSelectionAcrossViews(sourceList, record);
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }

            ShowExtensionDetail(record);
            ShowTagEditor(record);
        }

        private static T FindVisualAncestor<T>(DependencyObject source)
            where T : DependencyObject
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
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

                // 未選択ならタブ側 helper へ渡して同期する
                TrySyncUpperTabSmallSelectionFromItem(item);
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

                TrySyncUpperTabBigSelectionFromItem(item);
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

                TrySyncUpperTabBig10SelectionFromItem(item);
            }
        }
    }
}
