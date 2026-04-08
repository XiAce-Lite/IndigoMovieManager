using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.BottomTabs.TagEditor;
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

        private TagEditorTabPresenter _tagEditorTabPresenter;

        private void InitializeTagEditorTabSupport()
        {
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
                TagEditorTabViewHost.PaletteTagToggleRequested -= TagEditorTabViewHost_PaletteTagToggleRequested;
                TagEditorTabViewHost.PaletteTagAddRequested -= TagEditorTabViewHost_PaletteTagAddRequested;

                TagEditorTabViewHost.RegisteredTagSearchRequested += TagEditorTabViewHost_RegisteredTagSearchRequested;
                TagEditorTabViewHost.RegisteredTagRemoveRequested += TagEditorTabViewHost_RegisteredTagRemoveRequested;
                TagEditorTabViewHost.PaletteTagToggleRequested += TagEditorTabViewHost_PaletteTagToggleRequested;
                TagEditorTabViewHost.PaletteTagAddRequested += TagEditorTabViewHost_PaletteTagAddRequested;
            }

            _tagEditorTabPresenter?.Initialize();
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
            TagEditorTabViewHost?.ShowRecord(record, BuildTagEditorPaletteItems());
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

        private void ApplyTagEditorRecordTagChange(string tagName, bool? forceAdd)
        {
            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null || string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            EnsureTagCollection(record);
            bool exists = record.Tag.Any(x => string.Equals(x, tagName, StringComparison.CurrentCulture));
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
                record.Tag.RemoveAll(x => string.Equals(x, tagName, StringComparison.CurrentCulture));
            }

            PersistTagEditorRecord(record);
            RefreshViewsAfterTagEditorRecordChange(record);
        }

        private async Task ToggleTagEditorSearchFilterAsync(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            List<string> tokens = GetCurrentTagEditorSearchTokens();
            int existingIndex = tokens.FindIndex(x =>
                string.Equals(x, tagName, StringComparison.CurrentCulture)
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
            RefreshTagEditorView();
        }

        private List<string> GetCurrentTagEditorSearchTokens()
        {
            string currentKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
            return currentKeyword
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.CurrentCulture)
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
                .Distinct(StringComparer.CurrentCulture)
                .ToList();
            record.Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. record.Tag]);
            _mainDbMovieMutationFacade.UpdateTag(MainVM.DbInfo.DBFullPath, record.Movie_Id, record.Tags);
        }

        private void RefreshViewsAfterTagEditorRecordChange(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            // タグ追加・削除はユーザー操作起点なので、
            // 左側表示はその場で最新状態へ寄せる。
            TagEditorTabViewHost?.ShowRecord(record, BuildTagEditorPaletteItems());

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

            string[] splitTags = record.Tags.Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries
            );
            record.Tag = splitTags
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCulture)
                .ToList();
        }

        private TagEditorPaletteItem[] BuildTagEditorPaletteItems()
        {
            HashSet<string> activeTokens = new(
                GetCurrentTagEditorSearchTokens(),
                StringComparer.CurrentCulture
            );
            return TagEditorFixedPalette
                .Select(tagName => new TagEditorPaletteItem
                {
                    TagName = tagName,
                    IsActive = activeTokens.Contains(tagName),
                })
                .ToArray();
        }
    }
}
