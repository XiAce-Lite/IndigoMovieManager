using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private bool _suppressSearchBoxTextChangedHandling = false;
        private SearchExecutionController _searchExecutor;
        private long _searchHistoryRefreshStamp;

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
                // フォーカス離脱時の実績記録は背景へ送り、UIイベントをDB I/Oで止めない。
                QueueSearchHistoryUsageRecord(
                    MainVM.DbInfo.DBFullPath,
                    MainVM.DbInfo.SearchKeyword
                );
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
                    CancelIncrementalSearchDebounce();
                    // 検索解除で一覧を即時に戻す時だけ、従来どおりサムネ常駐を再起動して競合を避ける。
                    RestartThumbnailTask();
                    FilterAndSort(MainVM.DbInfo.Sort, IsStartupFeedPartialActive);
                    SelectFirstItem();
                    return;
                }

                // 通常時だけ debounce で検索確定し、連打入力でも UI を詰まらせにくくする。
                QueueIncrementalSearch(text);
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
                        SearchHistoryService.DeleteHistoryEntry(
                            MainVM.DbInfo.DBFullPath,
                            selectedHistory.Find_Id
                        )
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

                // 通常の検索実行: Enterキー で検索を確定し、必要なら履歴も同期する
                if (e.Key == Key.Enter)
                {
                    // Enter は既定ボタンへ流さず、検索ボックス起点の共通入口へ揃える。
                    CancelIncrementalSearchDebounce();
                    _searchBoxItemSelectedByUser = false;
                    if (combo.IsDropDownOpen)
                    {
                        combo.IsDropDownOpen = false;
                    }

                    e.Handled = true;

                    string enteredText = combo.Text ?? "";
                    bool searchExecuted = await ExecuteSearchKeywordAsync(enteredText, false);
                    if (!searchExecuted)
                    {
                        return;
                    }

                    // Editable ComboBox の KeyDown 連鎖が一段落してから履歴同期する。
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    PersistSearchHistoryAfterSearch(enteredText);
                    return;
                }
            }
        }

        /// <summary>
        /// いよいよ検索処理の本丸への突撃！入力キーワードをViewModelに叩き込み、後続のフィルタ部隊を全軍突撃させるぞ！⚔️🔥
        /// </summary>
        private void DoSearchBoxSearch()
        {
            CancelIncrementalSearchDebounce();
            _ = ExecuteSearchKeywordAsync(SearchBox?.Text ?? "", false);
        }

        // Enter 確定後だけ履歴保存をまとめ、検索結果ゼロや空白入力は静かに流す。
        private void PersistSearchHistoryAfterSearch(string text)
        {
            string keyword = text ?? "";
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int searchCount = MainVM?.DbInfo?.SearchCount ?? 0;
            string currentText = SearchBox?.Text ?? "";
            QueueSearchHistoryRefresh(dbFullPath, keyword, searchCount, currentText);
        }

        private void QueueSearchHistoryRefresh(
            string dbFullPath,
            string keyword,
            int searchCount,
            string currentText
        )
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(keyword)
                || searchCount <= 0
            )
            {
                return;
            }

            long refreshStamp = Interlocked.Increment(ref _searchHistoryRefreshStamp);
            _ = Task.Run(
                    () =>
                    {
                        SearchHistoryService.PersistSuccessfulSearch(
                            dbFullPath,
                            keyword,
                            searchCount
                        );
                        return SearchHistoryService.LoadLatestHistory(dbFullPath);
                    }
                )
                .ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            DebugRuntimeLog.Write(
                                "search-history",
                                $"history refresh failed: db='{dbFullPath}' keyword='{keyword}' err='{task.Exception?.GetBaseException().Message}'"
                            );
                            return;
                        }

                        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                        {
                            return;
                        }

                        _ = Dispatcher.BeginInvoke(
                            new Action(
                                () =>
                                {
                                    if (
                                        refreshStamp != _searchHistoryRefreshStamp
                                        || !AreSameMainDbPath(
                                            dbFullPath,
                                            MainVM?.DbInfo?.DBFullPath ?? ""
                                        )
                                    )
                                    {
                                        return;
                                    }

                                    ApplySearchHistoryRecords(task.Result, currentText);
                                }
                            ),
                            DispatcherPriority.Background
                        );
                    },
                    TaskScheduler.Default
                );
        }

        private void QueueSearchHistoryUsageRecord(string dbFullPath, string keyword)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            _ = Task.Run(
                () =>
                {
                    try
                    {
                        SearchHistoryService.RecordSearchUsage(dbFullPath, keyword);
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "search-history",
                            $"history usage record failed: db='{dbFullPath}' keyword='{keyword}' err='{ex.Message}'"
                        );
                    }
                }
            );
        }

        // 検索 UI が複数になっても、本体検索の入口は 1 つへ寄せる。
        private async Task<bool> ExecuteSearchKeywordAsync(string text, bool syncSearchBoxText)
        {
            return await SearchExecutor.ExecuteAsync(text, syncSearchBoxText);
        }

        // 外部スキン検索は SearchBox を同期しつつ、本体検索だけを再利用する。
        private async Task<bool> ExecuteExternalSkinSearchAsync(string text)
        {
            bool executed = await ExecuteSearchKeywordAsync(text, true);
            if (executed)
            {
                PersistSearchHistoryAfterSearch(text);
            }

            return executed;
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

        private SearchExecutionController SearchExecutor =>
            _searchExecutor ??= new SearchExecutionController(
                getDbFullPath: () => MainVM?.DbInfo?.DBFullPath ?? "",
                getSortId: () => MainVM?.DbInfo?.Sort ?? "",
                setSearchKeyword: keyword => MainVM.DbInfo.SearchKeyword = keyword,
                syncSearchBoxText: UpdateSearchBoxTextWithoutSideEffects,
                beginUserPriorityWork: BeginUserPriorityWork,
                endUserPriorityWork: EndUserPriorityWork,
                restartThumbnailTask: RestartThumbnailTask,
                refreshSearchResultsAsync: RefreshSearchResultsAsync,
                selectFirstItem: SelectFirstItem
            );

        // 検索確定は通常時は query-only で軽く流し、起動直後の部分ロード中だけ full reload を維持する。
        private Task RefreshSearchResultsAsync(string sortId)
        {
            bool shouldReload = IsStartupFeedPartialActive;
            return FilterAndSortAsync(sortId, shouldReload);
        }

        public async Task ApplySearchKeywordFromLinkAsync(string keyword)
        {
            // タグ・詳細・ブックマークリンク検索を検索正本へ合流させ、
            // 通常時のDB再読込を避ける。
            try
            {
                await SearchExecutor.ExecuteAsync(keyword ?? "", syncSearchText: true);
                // 既に検索欄にフォーカスがある時は再要求しない。
                if (SearchBox != null && !SearchBox.IsKeyboardFocusWithin)
                {
                    SearchBox.Focus();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"link search failed: {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        // 変換途中の記号入力や部分ロード中は既存の確定検索へ寄せ、通常時だけ debounce で流す。
        private void QueueIncrementalSearch(string text)
        {
            if (!CanRunIncrementalSearch(text))
            {
                CancelIncrementalSearchDebounce();
                return;
            }

            StopDispatcherTimerSafely(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
            TryStartDispatcherTimer(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
        }

        // Enter や履歴選択と二重発火しないよう、保留中の debounce 検索を止める。
        private void CancelIncrementalSearchDebounce()
        {
            StopDispatcherTimerSafely(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
        }

        // 起動直後の full reload 連打と、未完成な特殊構文の途中評価を避ける。
        private bool CanRunIncrementalSearch(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (IsStartupFeedPartialActive)
            {
                return false;
            }

            int openIdx = text.IndexOf('{');
            int closeIdx = text.IndexOf('}');
            if (openIdx >= 0 && (closeIdx < 0 || closeIdx < openIdx))
            {
                return false;
            }

            char lastChar = text[^1];
            return lastChar != '-' && lastChar != '|' && lastChar != '{';
        }

        // タイピングが一段落した時だけ、現在テキストを query-only 検索へ流す。
        private async void SearchInputDebounceTimer_Tick(object? sender, EventArgs e)
        {
            CancelIncrementalSearchDebounce();

            if (_imeFlag || SearchBox == null)
            {
                return;
            }

            string text = SearchBox.Text ?? "";
            if (!CanRunIncrementalSearch(text))
            {
                return;
            }

            if (string.Equals(MainVM.DbInfo.SearchKeyword ?? "", text, StringComparison.Ordinal))
            {
                return;
            }

            await ExecuteSearchKeywordAsync(text, false);
        }
    }
}
