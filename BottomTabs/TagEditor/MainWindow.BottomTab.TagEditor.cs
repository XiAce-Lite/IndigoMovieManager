using System;
using System.Collections.Generic;
using System.Linq;
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

                TagEditorTabViewHost.RegisteredTagSearchRequested += TagEditorTabViewHost_RegisteredTagSearchRequested;
                TagEditorTabViewHost.RegisteredTagRemoveRequested += TagEditorTabViewHost_RegisteredTagRemoveRequested;
                TagEditorTabViewHost.PaletteTagToggleRequested += TagEditorTabViewHost_PaletteTagToggleRequested;
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
                TagEditorTabViewHost?.ShowPlaceholder(BuildTagEditorPaletteItems(null));
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
            TagEditorTabViewHost?.ShowRecord(record, BuildTagEditorPaletteItems(record));
        }

        private void HideTagEditor()
        {
            _tagEditorTabPresenter?.ClearDirty();
            TagEditorTabViewHost?.ShowPlaceholder(BuildTagEditorPaletteItems(null));
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

            ApplyTagEditorToggle(e.TagName, forceAdd: false);
        }

        private void TagEditorTabViewHost_PaletteTagToggleRequested(
            object sender,
            TagEditorTagActionEventArgs e
        )
        {
            if (e == null || string.IsNullOrWhiteSpace(e.TagName))
            {
                return;
            }

            ApplyTagEditorToggle(e.TagName, forceAdd: null);
        }

        private void ApplyTagEditorToggle(string tagName, bool? forceAdd)
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
            RefreshTagEditorView();
            RefreshExtensionDetailView();
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

        private static void EnsureTagCollection(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            if (record.Tag != null && record.Tag.Count > 0)
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

        private static TagEditorPaletteItem[] BuildTagEditorPaletteItems(MovieRecords record)
        {
            HashSet<string> activeTags = new(
                record?.Tag?.Where(x => !string.IsNullOrWhiteSpace(x)) ?? Array.Empty<string>(),
                StringComparer.CurrentCulture
            );
            return TagEditorFixedPalette
                .Select(tagName => new TagEditorPaletteItem
                {
                    TagName = tagName,
                    IsActive = activeTags.Contains(tagName),
                })
                .ToArray();
        }
    }
}
