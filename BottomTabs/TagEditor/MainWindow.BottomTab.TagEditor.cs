using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.BottomTabs.TagEditor;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private static readonly string[] TagEditorFixedPalette =
        [
            "★",
            "★★",
            "★★★",
            "★★★★",
            "★★★★★",
        ];

        private readonly TagIndexCacheService _tagIndexCacheService = new();
        private TagEditorTabPresenter _tagEditorTabPresenter;
        private bool _tagIndexCacheSubscribed;

        private void InitializeTagEditorTabSupport()
        {
            EnsureTagIndexCacheSubscription();

            if (_tagEditorTabPresenter == null && TagEditorBottomTab != null)
            {
                _tagEditorTabPresenter = new TagEditorTabPresenter(
                    TagEditorBottomTab,
                    ApplyTagEditorCurrentState
                );
            }

            if (TagEditorTabViewHost != null)
            {
                TagEditorTabViewHost.RegisteredTagSearchRequested -= TagEditorTabViewHost_RegisteredTagSearchRequested;
                TagEditorTabViewHost.RegisteredTagRemoveRequested -= TagEditorTabViewHost_RegisteredTagRemoveRequested;
                TagEditorTabViewHost.RegisteredTagToggleRequested -= TagEditorTabViewHost_RegisteredTagToggleRequested;
                TagEditorTabViewHost.PaletteTagToggleRequested -= TagEditorTabViewHost_PaletteTagToggleRequested;
                TagEditorTabViewHost.PaletteTagAddRequested -= TagEditorTabViewHost_PaletteTagAddRequested;
                TagEditorTabViewHost.CustomTagAddRequested -= TagEditorTabViewHost_CustomTagAddRequested;

                TagEditorTabViewHost.RegisteredTagSearchRequested += TagEditorTabViewHost_RegisteredTagSearchRequested;
                TagEditorTabViewHost.RegisteredTagRemoveRequested += TagEditorTabViewHost_RegisteredTagRemoveRequested;
                TagEditorTabViewHost.RegisteredTagToggleRequested += TagEditorTabViewHost_RegisteredTagToggleRequested;
                TagEditorTabViewHost.PaletteTagToggleRequested += TagEditorTabViewHost_PaletteTagToggleRequested;
                TagEditorTabViewHost.PaletteTagAddRequested += TagEditorTabViewHost_PaletteTagAddRequested;
                TagEditorTabViewHost.CustomTagAddRequested += TagEditorTabViewHost_CustomTagAddRequested;
            }

            _tagEditorTabPresenter?.Initialize();
        }

        private void EnsureTagIndexCacheSubscription()
        {
            if (_tagIndexCacheSubscribed)
            {
                return;
            }

            _tagIndexCacheService.SnapshotUpdated += TagIndexCacheService_SnapshotUpdated;
            _tagIndexCacheSubscribed = true;
        }

        private bool IsTagEditorTabVisibleOrSelected()
        {
            return _tagEditorTabPresenter?.IsVisibleOrSelected() == true;
        }

        private void MarkTagEditorTabDirty()
        {
            _tagEditorTabPresenter?.MarkDirty();
        }

        private void ApplyTagEditorCurrentState()
        {
            _tagEditorTabPresenter?.ClearDirty();

            if ((MainVM?.DbInfo?.SearchCount ?? 0) == 0)
            {
                HideTagEditor();
                return;
            }

            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null)
            {
                TagEditorTabViewHost?.ShowPlaceholder(BuildTagEditorPaletteItems());
                return;
            }

            ShowTagEditor(record);
        }

        private void ShowTagEditor(MovieRecords record)
        {
            if (record == null)
            {
                HideTagEditor();
                return;
            }

            if (!IsTagEditorTabVisibleOrSelected())
            {
                MarkTagEditorTabDirty();
            }

            EnsureTagCollection(record);
            TagEditorTabViewHost?.ShowRecord(
                record,
                BuildTagEditorPaletteItems(record),
                GetCurrentTagEditorSearchTokens()
            );
        }

        private void HideTagEditor()
        {
            _tagEditorTabPresenter?.ClearDirty();
            TagEditorTabViewHost?.ShowPlaceholder(BuildTagEditorPaletteItems());
        }

        private void UpdateTagEditorVisibilityBySearchCount()
        {
            if (!IsTagEditorTabVisibleOrSelected())
            {
                MarkTagEditorTabDirty();
                return;
            }

            if ((MainVM?.DbInfo?.SearchCount ?? 0) == 0)
            {
                HideTagEditor();
                return;
            }

            ApplyTagEditorCurrentState();
        }

        internal void RefreshTagEditorView()
        {
            if (!IsTagEditorTabVisibleOrSelected())
            {
                MarkTagEditorTabDirty();
                return;
            }

            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null)
            {
                HideTagEditor();
                return;
            }

            ShowTagEditor(record);
        }

        private async void TagEditorTabViewHost_RegisteredTagSearchRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            await ExecuteSearchKeywordAsync(e.TagName, true);
        }

        private void TagEditorTabViewHost_RegisteredTagRemoveRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            ApplyTagEditorRecordTagChange(e.TagName, forceAdd: false);
        }

        private async void TagEditorTabViewHost_PaletteTagToggleRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            await ToggleTagEditorSearchFilterAsync(e.TagName);
        }

        private async void TagEditorTabViewHost_RegisteredTagToggleRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            MovieRecords currentRecord = GetSelectedItemByTabIndex();
            await ToggleTagEditorSearchFilterAsync(e.TagName, currentRecord?.Movie_Id ?? 0);
        }

        private void TagEditorTabViewHost_PaletteTagAddRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            ApplyTagEditorRecordTagChange(e.TagName, forceAdd: true);
        }

        private void TagEditorTabViewHost_CustomTagAddRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            ApplyTagEditorRecordTagChange(e.TagName, forceAdd: true);
        }

        private void ApplyTagEditorRecordTagChange(string tagName, bool? forceAdd)
        {
            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null || string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            EnsureTagCollection(record);
            bool exists = record.Tag.Any(x =>
                string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
            );
            bool shouldAdd = forceAdd ?? !exists;

            if (shouldAdd)
            {
                if (!exists)
                {
                    record.Tag.Add(tagName);
                }
            }
            else
            {
                record.Tag.RemoveAll(x =>
                    string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
                );
            }

            PersistTagEditorRecord(record);
            RefreshViewsAfterTagEditorRecordChange(record);
        }

        private async Task ToggleTagEditorSearchFilterAsync(
            string tagName,
            long preferredMovieId = 0
        )
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            List<string> tokens = GetCurrentTagEditorSearchTokens();
            int existingIndex = tokens.FindIndex(x =>
                string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
            );
            if (existingIndex >= 0)
            {
                tokens.RemoveAt(existingIndex);
            }
            else
            {
                tokens.Add(tagName);
            }

            string nextKeyword = string.Join(" ", tokens);
            await ExecuteSearchKeywordAsync(nextKeyword, true);
            ReselectTagEditorMovieIfVisible(preferredMovieId);
            RefreshTagEditorView();
        }

        private void ReselectTagEditorMovieIfVisible(long movieId)
        {
            if (movieId <= 0 || MainVM?.FilteredMovieRecs == null)
            {
                return;
            }

            // 左タブ起点の絞り込み後は、結果内に同じ動画が残っていれば
            // その動画へ選択を戻して操作の連続性を保つ。
            MovieRecords reselection = MainVM.FilteredMovieRecs.FirstOrDefault(x =>
                x?.Movie_Id == movieId
            );
            if (reselection == null)
            {
                return;
            }

            SelectCurrentUpperTabMovieRecord(reselection);
        }

        private List<string> GetCurrentTagEditorSearchTokens()
        {
            string currentKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
            return currentKeyword
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void PersistTagEditorRecord(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            EnsureTagCollection(record);
            record.Tag = record
                .Tag.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            record.Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. record.Tag]);
            _mainDbMovieMutationFacade.UpdateTag(MainVM.DbInfo.DBFullPath, record.Movie_Id, record.Tags);
            NotifyTagEditorTagIndexChanged(record);
        }

        private void RefreshViewsAfterTagEditorRecordChange(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            // タグ追加・削除はユーザー操作起点なので、
            // 左側表示はその場で最新状態へ寄せる。
            TagEditorTabViewHost?.ShowRecord(
                record,
                BuildTagEditorPaletteItems(record),
                GetCurrentTagEditorSearchTokens()
            );

            if (!IsTagEditorTabVisibleOrSelected())
            {
                MarkTagEditorTabDirty();
            }

            if (IsExtensionTabVisibleOrSelected())
            {
                ExtensionTabViewHost?.ShowRecord(record);
                ExtensionTabViewHost?.RefreshDetail();
                return;
            }

            MarkExtensionTabDirty();
        }

        private static void EnsureTagCollection(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            // いったん Tag リストが存在した後は、それを正本として扱う。
            // 最後の1件削除後に古い Tags 文字列から復元しないためのガード。
            if (record.Tag != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(record.Tags))
            {
                record.Tag = [];
                return;
            }

            record.Tag = TagTextParser
                .SplitDistinct(record.Tags, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        internal void NotifyTagEditorTagIndexChanged(MovieRecords record)
        {
            if (record == null || record.Movie_Id <= 0)
            {
                return;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            EnsureTagCollection(record);
            _tagIndexCacheService.UpdateMovieTags(dbFullPath, record.Movie_Id, record.Tag);
        }

        private void TagIndexCacheService_SnapshotUpdated(
            object sender,
            TagIndexSnapshotChangedEventArgs e
        )
        {
            if (Dispatcher.CheckAccess())
            {
                ApplyTagIndexSnapshotUpdated(e);
                return;
            }

            _ = Dispatcher.BeginInvoke(new Action(() => ApplyTagIndexSnapshotUpdated(e)));
        }

        private void ApplyTagIndexSnapshotUpdated(TagIndexSnapshotChangedEventArgs e)
        {
            if (
                e == null
                || !AreSameMainDbPath(MainVM?.DbInfo?.DBFullPath ?? "", e.DbFullPath)
            )
            {
                return;
            }

            RefreshTagEditorView();
        }

        private TagEditorPaletteItem[] BuildTagEditorPaletteItems(MovieRecords selectedRecord = null)
        {
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            HashSet<string> activeTokens = new(
                GetCurrentTagEditorSearchTokens(),
                StringComparer.CurrentCultureIgnoreCase
            );
            TagIndexSnapshot snapshot = _tagIndexCacheService.TryGetSnapshot(dbFullPath);
            if (snapshot == null)
            {
                _tagIndexCacheService.EnsureSnapshot(dbFullPath);
                return BuildTagEditorPaletteItemsCore(
                    TagEditorFixedPalette,
                    selectedRecord?.Tag,
                    null,
                    activeTokens
                );
            }

            return BuildTagEditorPaletteItemsCore(
                TagEditorFixedPalette,
                selectedRecord?.Tag,
                snapshot.TagCounts,
                activeTokens
            );
        }

        internal static TagEditorPaletteItem[] BuildTagEditorPaletteItemsCore(
            IEnumerable<string> fixedTags,
            IEnumerable<string> selectedTags,
            IReadOnlyDictionary<string, int> tagCounts,
            IReadOnlyCollection<string> activeTags
        )
        {
            HashSet<string> emittedTags = new(StringComparer.CurrentCultureIgnoreCase);
            HashSet<string> activeTokenSet = new(
                activeTags ?? Array.Empty<string>(),
                StringComparer.CurrentCultureIgnoreCase
            );
            List<TagEditorPaletteItem> items = [];

            foreach (string tagName in fixedTags ?? Array.Empty<string>())
            {
                if (!emittedTags.Add(tagName))
                {
                    continue;
                }

                items.Add(
                    new TagEditorPaletteItem
                    {
                        TagName = tagName,
                        DisplayLabel = tagName,
                        IsActive = activeTokenSet.Contains(tagName),
                    }
                );
            }

            // 選択中動画のタグは、固定タグの次にその動画の並び順を保って差し込む。
            foreach (string tagName in selectedTags ?? Array.Empty<string>())
            {
                if (!emittedTags.Add(tagName))
                {
                    continue;
                }

                items.Add(
                    new TagEditorPaletteItem
                    {
                        TagName = tagName,
                        DisplayLabel = tagName,
                        IsActive = activeTokenSet.Contains(tagName),
                    }
                );
            }

            if (tagCounts == null)
            {
                return items.ToArray();
            }

            foreach (
                KeyValuePair<string, int> tagEntry in tagCounts
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            )
            {
                if (!emittedTags.Add(tagEntry.Key))
                {
                    continue;
                }

                items.Add(
                    new TagEditorPaletteItem
                    {
                        TagName = tagEntry.Key,
                        DisplayLabel = $"{tagEntry.Key} ({tagEntry.Value})",
                        IsActive = activeTokenSet.Contains(tagEntry.Key),
                    }
                );
            }

            return items.ToArray();
        }
    }
}
