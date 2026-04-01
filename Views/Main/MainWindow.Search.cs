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
        private bool _suppressSearchBoxTextChangedHandling = false;

        // =================================================================================
        // 検索に関する UI イベント処理 (View層のロジック)
        // ユーザーがUI画面（SearchBox等のコントロール）で行った操作を受け取り、
        // 最終的に ViewModel(MainVM) 側の絞り込み/検索処理へ委譲する導線となる。
        // =================================================================================

        /// <summary>
        /// 検索コンボボックスの選択が切り替わった瞬間のイベントだぜ！🎯
        /// </summary>
        private void SearchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }

            // ドロップダウンが開いている状態でユーザーが選択を変更した（マウス・キー操作等）場合のみ、
            // 「ユーザー起因の検索」としてフラグを立てて後続処理(DropDownClosed等)での実行を促す。
            if (SearchBox.IsDropDownOpen)
            {
                _searchBoxItemSelectedByUser = true;
            }

            if (e.Source is ComboBox)
            {
                // [MVVM移行メモ]
                // 以前はここで即時フィルタ(FilterAndSort等)を走らせていたが、
                // 挙動が重くなる・意図しないタイミングで走る問題があるため無効化している。
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

        /// <summary>
        /// ドロップダウンの履歴にマウスが乗ったら、自動で「それな！」と選択状態にしてやる超親切処理！✨
        /// </summary>
        private void SearchBoxItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is ComboBoxItem item && item.IsMouseOver)
            {
                item.IsSelected = true;
            }
        }

        /// <summary>
        /// 検索ボックスからフォーカスが外れた時が勝負！今のキーワードを「今回の実績」としてDBの歴史に深く刻み込むぜ！🛡️
        /// </summary>
        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                // FindFact はフォーカス離脱時に「実際に使われた」として記録する
                InsertFindFactTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                // 検索キーワードがある場合は検索履歴(History)へも追加する
                InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
            }
        }

        /// <summary>
        /// おっと検索テキストに変更があったな！？すかさず状態をキャッチするイベントだ！👀
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_suppressSearchBoxTextChangedHandling)
            {
                return;
            }
            // IME入力確定前のフリックや変換中の文字では処理を走らせない
            if (_imeFlag)
            {
                return;
            }

            // 本アプリ独自の挙動: 検索文字が変化した = 画面状態が激しく更新される可能性があるため、
            // 負荷が高いサムネイル作成タスクを一旦停止し、DB処理が終わる頃合で再起動させて競合を防ぐ。
            RestartThumbnailTask();

            if (e.Source is ComboBox combo)
            {
                var text = combo.Text;

                // [MVVM移行メモ]
                // 以前搭載されていた1文字ごとのインクリメンタルサーチ処理。
                // テキスト変化のたびにDBやリストを走査するため負荷が高く「美しくない」と判断され、現在無効化されている。
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

                // 唯一有効なのは、テキスト入力が完全に消された(空になった)場合。
                // 絞り込みを解除し、全件表示へ戻す。
                if (string.IsNullOrEmpty(text))
                {
                    FilterAndSort(MainVM.DbInfo.Sort, true);
                    SelectFirstItem();
                }
            }
        }

        /// <summary>
        /// ドロップダウンが閉じた時、ユーザーが「これだ！」と選んだ履歴なら爆速で検索を走らせる！🏃‍♂️
        /// </summary>
        private void SearchBox_DropDownClosed(object sender, EventArgs e)
        {
            if (_searchBoxItemSelectedByUser)
            {
                DoSearchBoxSearch();
                _searchBoxItemSelectedByUser = false;
            }
        }

        /// <summary>
        /// 履歴をマウスで直撃クリックしたな！「ユーザーの強い意志」としてフラグを力強く立てるぜ！🚩
        /// </summary>
        private void SearchBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _searchBoxItemSelectedByUser = true;
        }

        /// <summary>
        /// 検索ボックスにカーソルがある時のキーボード入力監視網！エンターキーを打つ隙は逃さない！🔫
        /// </summary>
        private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_imeFlag)
            {
                return;
            }

            if (e.Source is ComboBox combo)
            {
                // 例外操作: 履歴を開いている最中に Deleteキー が押されたら、その履歴エントリーを消去する
                if (
                    e.Key == Key.Delete
                    && combo.IsDropDownOpen
                    && combo.SelectedItem is History selectedHistory
                )
                {
                    int idx = combo.SelectedIndex;

                    // まずViewModelから即座に消すことでUIの反応を良く見せる
                    MainVM.HistoryRecs.Remove(selectedHistory);

                    // 実際のDBからの履歴データ削除は少し重いためバックグラウンドで処理
                    await Task.Run(() =>
                        DeleteHistoryTable(MainVM.DbInfo.DBFullPath, selectedHistory.Find_Id)
                    );

                    // 削除後にカーソルが消えないよう、次のアイテムにフォーカスを当てる処理
                    if (MainVM.HistoryRecs.Count > 0)
                    {
                        if (idx >= MainVM.HistoryRecs.Count)
                        {
                            idx = MainVM.HistoryRecs.Count - 1;
                        }
                        combo.SelectedIndex = idx;
                    }

                    // Deleteキーが文字入力欄の1文字削除などへ誤爆しないようブロック
                    e.Handled = true;
                    return;
                }

                // 通常の検索実行: Enterキー が押されたら、検索結果のヒット有無を確認して履歴に保存する
                if (e.Key == Key.Enter)
                {
                    if (
                        !string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword)
                        && (MainVM.DbInfo.SearchCount > 0)
                    )
                    {
                        InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                        // 履歴追加後、ドロップダウンの表示更新用にもう一度DBから引っ張ってくる（MVVM的には直追加が良いが現在はDB再読み込み）
                        GetHistoryTable(MainVM.DbInfo.DBFullPath);
                    }
                }
            }
        }

        /// <summary>
        /// いよいよ検索処理の本丸への突撃！入力キーワードをViewModelに叩き込み、後続のフィルタ部隊を全軍突撃させるぞ！⚔️🔥
        /// </summary>
        private void DoSearchBoxSearch()
        {
            ExecuteSearchKeyword(SearchBox?.Text ?? "", false);
        }

        // 検索 UI が複数になっても、本体検索の入口は 1 つへ寄せる。
        private bool ExecuteSearchKeyword(string text, bool syncSearchBoxText)
        {
            if (string.IsNullOrEmpty(MainVM?.DbInfo?.DBFullPath))
            {
                return false;
            }

            string normalizedText = text ?? "";
            if (syncSearchBoxText)
            {
                UpdateSearchBoxTextWithoutSideEffects(normalizedText);
            }

            MainVM.DbInfo.SearchKeyword = normalizedText;
            RestartThumbnailTask();
            FilterAndSort(MainVM.DbInfo.Sort, true);
            SelectFirstItem();
            return true;
        }

        // 外部スキン検索は SearchBox を同期しつつ、本体検索だけを再利用する。
        private bool ExecuteExternalSkinSearch(string text)
        {
            return ExecuteSearchKeyword(text, true);
        }

        private void UpdateSearchBoxTextWithoutSideEffects(string text)
        {
            if (SearchBox == null)
            {
                return;
            }

            string normalizedText = text ?? "";
            if (string.Equals(SearchBox.Text ?? "", normalizedText, StringComparison.Ordinal))
            {
                return;
            }

            _suppressSearchBoxTextChangedHandling = true;
            try
            {
                SearchBox.Text = normalizedText;
            }
            finally
            {
                _suppressSearchBoxTextChangedHandling = false;
            }
        }
    }
}
