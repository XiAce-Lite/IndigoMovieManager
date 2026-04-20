using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.Skin.Host;
using IndigoMovieManager.Skin.Runtime;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class WhiteBrowserSkinRuntimeBridgeIntegrationTests
{
    [Test]
    public async Task ExternalThumbnailRoute_実WebView2で200_403_404とヘッダーを返せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-webview2");

        try
        {
            RuntimeBridgeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyExternalThumbnailResponsesAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.OkResponse.StatusCode, Is.EqualTo(200));
                Assert.That(result.OkResponse.ReasonPhrase, Is.EqualTo("OK"));
                Assert.That(result.OkResponse.ContentType, Does.StartWith("image/png"));
                Assert.That(result.OkResponse.CacheControl, Does.Contain("no-store"));
                Assert.That(result.OkResponse.BodyLength, Is.GreaterThan(0));

                Assert.That(result.ForbiddenResponse.StatusCode, Is.EqualTo(403));
                Assert.That(result.ForbiddenResponse.ReasonPhrase, Is.EqualTo("Forbidden"));
                Assert.That(result.ForbiddenResponse.CacheControl, Does.Contain("no-store"));

                Assert.That(result.MissingResponse.StatusCode, Is.EqualTo(404));
                Assert.That(result.MissingResponse.ReasonPhrase, Is.EqualTo("Not Found"));
                Assert.That(result.MissingResponse.CacheControl, Does.Contain("no-store"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task HandleSkinLeaveAsync_実WebView2でclear_leaveを一度だけ返せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-lifecycle");

        try
        {
            RuntimeBridgeLifecycleVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyHandleSkinLeaveAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(
                result.LifecycleEvents,
                Is.EqualTo(["focus:90:false", "select:90:false", "clear", "leave"])
            );
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_実WebView2で初回update_focus_leave_clearまで流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-tutorial-grid");

        try
        {
            TutorialCallbackGridVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTutorialCallbackGridAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.UpdateStartIndex, Is.EqualTo(0));
                Assert.That(result.UpdateCount, Is.EqualTo(200));
                Assert.That(result.FirstFocusRequestMovieId, Is.EqualTo(42));
                Assert.That(result.SecondFocusRequestMovieId, Is.EqualTo(84));
                Assert.That(result.ItemCountBeforeLeave, Is.EqualTo(2));
                Assert.That(result.TitleTextBeforeLeave, Is.EqualTo("Alpha.mp4"));
                Assert.That(result.FocusedImageClassBeforeLeave, Is.EqualTo("img_base img_f"));
                Assert.That(result.SelectedThumbClassBeforeLeave, Is.EqualTo("thum_base thum_s"));
                Assert.That(result.ItemCountAfterRefresh, Is.EqualTo(1));
                Assert.That(result.TitleTextAfterRefresh, Is.EqualTo("Gamma.mkv"));
                Assert.That(result.FocusedImageClassAfterRefresh, Is.EqualTo("img_base img_f"));
                Assert.That(result.SelectedThumbClassAfterRefresh, Is.EqualTo("thum_base thum_s"));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultList_実WebView2でdefault_onUpdateとscroll_list描画を流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-default-list");

        try
        {
            WhiteBrowserDefaultListVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyWhiteBrowserDefaultListAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(1));
                Assert.That(result.UpdateStartIndex, Is.EqualTo(0));
                Assert.That(result.UpdateCount, Is.EqualTo(200));
                Assert.That(result.ItemCountBeforeLeave, Is.EqualTo(2));
                Assert.That(result.TitleTextBeforeLeave, Is.EqualTo("Beta.avi"));
                Assert.That(result.SizeTextBeforeLeave, Is.EqualTo("2.0 GB"));
                Assert.That(result.LengthTextBeforeLeave, Is.EqualTo("01:23:45"));
                Assert.That(result.ScrollElementId, Is.EqualTo("scroll"));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultList_実WebView2で既定onUpdateThumfallbackがimg_srcを差し替えられる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-default-list-thumb-update");

        try
        {
            WhiteBrowserDefaultListThumbUpdateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyWhiteBrowserDefaultListThumbUpdateFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.BeforeThumbSrc, Is.EqualTo("data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA="));
                Assert.That(result.AfterThumbSrc, Is.EqualTo("data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw=="));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultList_実WebView2でstartIndex付きupdateを追記描画できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-default-list-append");

        try
        {
            WhiteBrowserDefaultListAppendVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyWhiteBrowserDefaultListAppendAsync(tempRootPath, useSeamlessScroll: false)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.UpdateStartIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(result.UpdateCounts, Is.EqualTo(new[] { 200, 1 }));
                Assert.That(result.ItemCountAfterAppend, Is.EqualTo(3));
                Assert.That(
                    result.TitlesAfterAppend,
                    Is.EqualTo(new[] { "Alpha.mp4", "Beta.avi", "Gamma.mkv" })
                );
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultList_実WebView2でconfig_seamless_scroll追記できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-default-list-seamless");

        try
        {
            WhiteBrowserDefaultListAppendVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyWhiteBrowserDefaultListAppendAsync(tempRootPath, useSeamlessScroll: true)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.UpdateStartIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(result.UpdateCounts, Is.EqualTo(new[] { 200, 1 }));
                Assert.That(result.ItemCountAfterAppend, Is.EqualTo(3));
                Assert.That(
                    result.TitlesAfterAppend,
                    Is.EqualTo(new[] { "Alpha.mp4", "Beta.avi", "Gamma.mkv" })
                );
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB")]
    [TestCase("Chappy")]
    [TestCase("Search_table")]
    [TestCase("Alpha2")]
    [TestCase("#TagInputRelation")]
    [TestCase("#umlFindTreeEve")]
    public async Task build出力skin_実WebView2で初回サムネ表示を流せる(string skinFolderName)
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-{skinFolderName.TrimStart('#').ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbnailVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyBuildOutputSkinThumbnailAsync(tempRootPath, skinFolderName)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                int expectedUpdateRequestCount = string.Equals(
                    skinFolderName,
                    "Search_table",
                    StringComparison.Ordinal
                )
                    ? 2
                    : 1;
                Assert.That(result.UpdateRequestCount, Is.EqualTo(expectedUpdateRequestCount));
                Assert.That(result.ThumbnailCountBeforeLeave, Is.GreaterThanOrEqualTo(2));
                Assert.That(result.Image77Src, Is.Not.Empty);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でonExtensionUpdatedから候補タグを生成できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-extension");

        try
        {
            string[] result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationExtensionUpdatedAsync(tempRootPath)
            );

            Assert.That(result, Is.EqualTo(new[] { "idol", "live", "sample" }));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でIncludeとSaveから選択タグ追加を流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-save");

        try
        {
            TagInputRelationSaveVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationIncludeAndSaveAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterInclude, Is.EqualTo("series-a, sample"));
                Assert.That(result.AddTagRequests, Is.EqualTo(new[] { "series-a", "sample", "idol" }));
                Assert.That(result.InputAfterSave, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonExtensionUpdated再実行しても候補再生成できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-rerender");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveAndRerenderAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.Not.Empty);
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonExtensionUpdated再実行すると初期候補へ重複なく戻せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-get-rerender");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetAndRerenderAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGetと候補クリックから入力候補を広げられる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-get");

        try
        {
            TagInputRelationGetVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetAndSetAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.RelationLimits, Does.Contain(20));
                Assert.That(result.RelationLimits, Does.Contain(30));
                Assert.That(result.SelectionAfterGet, Is.EqualTo(new[] { "fresh", "idol", "live", "sample" }));
                Assert.That(result.InputAfterSet, Is.EqualTo("fresh"));
                Assert.That(result.SelectionAfterSet, Is.EqualTo(new[] { "idol", "live", "sample" }));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でdirty状態からumlFindTreeEveへchangeSkinしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-changeskin-tree");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationChangeSkinToUmlFindTreeEveAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Folders"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にchangeSkinしてもtree_footerを次skinへ持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-save-changeskin-tree");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveChangeSkinToUmlFindTreeEveAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonSkinLeaveしてからchangeSkinしてもtree_footerを次skinへ持ち越さない()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-save-skinleave-changeskin-tree"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveSkinLeaveChangeSkinToUmlFindTreeEveAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonClearAllしてからchangeSkinしてもtree_footerを次skinへ持ち越さない()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-save-clearall-changeskin-tree"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveTerminalChangeSkinToUmlFindTreeEveAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonSkinLeaveしてからchangeSkinしてもtree_footerを次skinへ持ち越さない()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-get-skinleave-changeskin-tree"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetTerminalChangeSkinToUmlFindTreeEveAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonClearAllしてからchangeSkinしてもtree_footerを次skinへ持ち越さない()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-get-clearall-changeskin-tree"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetTerminalChangeSkinToUmlFindTreeEveAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonSkinLeaveしてからchangeSkin失敗の直後にumlFindTreeEveへchangeSkinしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-get-skinleave-missing-then-changeskin-tree"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetTerminalMissingThenChangeSkinToUmlFindTreeEveAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonClearAllしてからchangeSkin失敗の直後にumlFindTreeEveへchangeSkinしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-get-clearall-missing-then-changeskin-tree"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetTerminalMissingThenChangeSkinToUmlFindTreeEveAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#umlFindTreeEve"));
                Assert.That(result.HasInput, Is.False);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でonSkinLeave再入後も候補を重複なく再生成できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-leave-rerender");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationLeaveAndRerenderAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.Not.Empty);
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でonClearAllしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-clear-rerender");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationTerminalRerenderAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でonSkinLeaveしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-terminal-leave-rerender");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationTerminalRerenderAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonClearAllしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-get-clear");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetAndTerminalRerenderAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonSkinLeaveしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-get-leave");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetAndTerminalRerenderAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonClearAllしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-save-clear");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveAndTerminalRerenderAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonSkinLeaveしても入力候補を持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-save-leave");

        try
        {
            TagInputRelationRerenderVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveAndTerminalRerenderAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.InputAfterRerender, Is.EqualTo(string.Empty));
                Assert.That(result.CandidateTextsAfterRerender, Is.EqualTo(new[] { "idol", "live", "sample" }));
                Assert.That(
                    result.CandidateTextsAfterRerender.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(result.CandidateTextsAfterRerender.Length)
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でchangeSkin失敗しても現在skin状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-missing-changeskin");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo("sample"));
                Assert.That(result.SelectionCount, Is.EqualTo(3));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でonSkinLeaveしてからchangeSkin失敗しても終端状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-bare-skinleave-missing-changeskin"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationBareTerminalMissingChangeSkinAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(4));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でonClearAllしてからchangeSkin失敗しても終端状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-bare-clearall-missing-changeskin"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationBareTerminalMissingChangeSkinAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(4));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonSkinEnterからtreeとfooterを生成できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-extension");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveSkinEnterAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Folders"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にchangeSkin失敗しても候補拡張状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-get-missing-changeskin");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(4));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にchangeSkin失敗しても保存後状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-taginputrelation-save-missing-changeskin");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(4));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonSkinLeaveしてからchangeSkin失敗しても候補拡張終端状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-get-skinleave-missing-changeskin"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetTerminalMissingChangeSkinAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でGet後にonClearAllしてからchangeSkin失敗しても候補拡張終端状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-get-clearall-missing-changeskin"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationGetTerminalMissingChangeSkinAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonSkinLeaveしてからchangeSkin失敗しても保存後終端状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-save-skinleave-missing-changeskin"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveTerminalMissingChangeSkinAsync(tempRootPath, "onSkinLeave")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(4));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TagInputRelation_実WebView2でSave後にonClearAllしてからchangeSkin失敗しても保存後終端状態を維持できる()
    {
        string tempRootPath = CreateTempDirectory(
            "imm-wbskin-runtimebridge-taginputrelation-save-clearall-missing-changeskin"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyTagInputRelationSaveTerminalMissingChangeSkinAsync(tempRootPath, "onClearAll")
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(4));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-missing-changeskin");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後にchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-tag-missing-changeskin");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveTagMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後にchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-register-missing-changeskin");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-series"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後にchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-path-missing-changeskin");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEvePathMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後にchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-remove-missing-changeskin");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Not.Contain("series-a"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後に再入してもtag_treeとfooterを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-tag-renter");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveTagRefreshAndRenavigateAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でregister後にTagInputRelationへchangeSkinしてもtree_footerを持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-register-changeskin-taginput");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredChangeSkinToTagInputRelationAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後にTagInputRelationへchangeSkinしてもtree_footerを持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-tag-changeskin-taginput");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveTagChangeSkinToTagInputRelationAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("onClearAll")]
    [TestCase("onSkinLeave")]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後にterminalを挟んでTagInputRelationへchangeSkinしてもtree_footerを持ち越さない(
        string terminalCallbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-umlfindtreeeve-tag-terminal-{terminalCallbackName.ToLowerInvariant()}-changeskin-taginput"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveTagTerminalChangeSkinToTagInputRelationAsync(
                    tempRootPath,
                    terminalCallbackName
                )
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後にTagInputRelationへchangeSkinしてもtree_footerを持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-path-changeskin-taginput");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEvePathChangeSkinToTagInputRelationAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("onClearAll")]
    [TestCase("onSkinLeave")]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後にterminalを挟んでTagInputRelationへchangeSkinしてもtree_footerを持ち越さない(
        string terminalCallbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-umlfindtreeeve-path-terminal-{terminalCallbackName.ToLowerInvariant()}-changeskin-taginput"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEvePathTerminalChangeSkinToTagInputRelationAsync(
                    tempRootPath,
                    terminalCallbackName
                )
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後にTagInputRelationへchangeSkinしてもtree_footerを持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-remove-changeskin-taginput");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveChangeSkinToTagInputRelationAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("onClearAll")]
    [TestCase("onSkinLeave")]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後にterminalを挟んでTagInputRelationへchangeSkinしてもtree_footerを持ち越さない(
        string terminalCallbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-umlfindtreeeve-remove-terminal-{terminalCallbackName.ToLowerInvariant()}-changeskin-taginput"
        );

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveTerminalChangeSkinToTagInputRelationAsync(
                    tempRootPath,
                    terminalCallbackName
                )
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("onClearAll")]
    [TestCase("onSkinLeave")]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後にterminalを挟んでTagInputRelationへchangeSkinしてもtree_footerを持ち越さない(
        string terminalCallbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-umlfindtreeeve-register-terminal-changeskin-{terminalCallbackName}"
        );

        try
        {
            CrossSkinDomSnapshot snapshot = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredTerminalChangeSkinToTagInputRelationAsync(
                    tempRootPath,
                    terminalCallbackName
                )
            );

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(snapshot.HasInput, Is.True);
                Assert.That(snapshot.InputValue, Is.Empty);
                Assert.That(snapshot.SelectionCount, Is.EqualTo(0));
                Assert.That(snapshot.UmlText, Is.Empty);
                Assert.That(snapshot.FooterText, Is.Empty);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("onClearAll")]
    [TestCase("onSkinLeave")]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後にterminalを挟んでchangeSkin失敗してもtree_footerと更新済みtreeを維持できる(
        string terminalCallbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-umlfindtreeeve-register-terminal-missing-{terminalCallbackName}"
        );

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredTerminalMissingChangeSkinAsync(
                    tempRootPath,
                    terminalCallbackName
                )
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-series"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後にonClearAllしてからRefreshしても新規tag_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-register-clear-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredFileClearAllAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-series"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後にonSkinLeaveしてからRefreshしても新規tag_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-register-leave-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredFileSkinLeaveAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-series"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonClearAll後にTagInputRelationへchangeSkinしてもtree_footerを持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-clearall-changeskin-taginput");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveClearAllChangeSkinToTagInputRelationAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonSkinLeave後にTagInputRelationへchangeSkinしてもtree_footerを持ち越さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-skinleave-changeskin-taginput");

        try
        {
            CrossSkinDomSnapshot result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveSkinLeaveChangeSkinToTagInputRelationAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(string.Empty));
                Assert.That(result.SelectionCount, Is.EqualTo(0));
                Assert.That(result.FooterText, Is.EqualTo(string.Empty));
                Assert.That(result.UmlText, Is.EqualTo(string.Empty));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonSkinLeave後にchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-skinleave-changeskin-missing");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveSkinLeaveMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonClearAll後にchangeSkin失敗してもtree_footerと更新済みtreeを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-clearall-changeskin-missing");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveClearAllMissingChangeSkinAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonClearAll後にRefreshしてもtree_footerを二重生成しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-clear-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveClearAllAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Folders"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonSkinLeave後にonSkinEnterすると初期tree_footerを1回だけ再生成できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-terminal-reenter");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveSkinLeaveAndSkinEnterAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("Folders"));
                Assert.That(result.UmlText, Does.Contain("Tags"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後に再入しても新規tag_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-register-renter");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredFileAndRenavigateAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-series"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後にRefreshするとtag_treeへ反映できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-refresh");

        try
        {
            string umlText = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveTagRefreshAsync(tempRootPath)
            );

            Assert.That(umlText, Does.Contain("fresh-tag"));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後にonClearAllしてからRefreshしても更新tag_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-modifytags-clear-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveModifyTagsClearAllAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyTags後にonSkinLeaveしてからRefreshしても更新tag_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-modifytags-leave-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveModifyTagsSkinLeaveAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh-tag"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRegistedFile後にRefreshすると新規tag_treeを追加できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-register");

        try
        {
            string umlText = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRegisteredFileRefreshAsync(tempRootPath)
            );

            Assert.That(umlText, Does.Contain("fresh-series"));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後にRefreshするとtag_treeから消せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-remove");

        try
        {
            string umlText = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveFileRefreshAsync(tempRootPath)
            );

            Assert.That(umlText, Does.Not.Contain("series-a"));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後にonClearAllしてからRefreshしても削除済みtreeを復活させない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-remove-clear-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveFileClearAllAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Not.Contain("series-a"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後にonSkinLeaveしてからRefreshしても削除済みtreeを復活させない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-remove-leave-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveFileSkinLeaveAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Not.Contain("series-a"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonRemoveFile後に再入しても削除済みtreeを復活させない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-remove-renter");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveRemoveFileAndRenavigateAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Not.Contain("series-a"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後にRefreshするとfolder_treeへ反映できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-path");

        try
        {
            string umlText = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveModifyPathRefreshAsync(tempRootPath)
            );

            Assert.That(umlText, Does.Contain("fresh"));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後にonClearAllしてからRefreshしても更新folder_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-modifypath-clear-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveModifyPathClearAllAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後にonSkinLeaveしてからRefreshしても更新folder_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-modifypath-leave-rerender");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveModifyPathSkinLeaveAndRefreshAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task umlFindTreeEve_実WebView2でonModifyPath後に再入してもfolder_treeを重複表示しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-umlfindtreeeve-path-renter");

        try
        {
            UmlFindTreeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyUmlFindTreeEveModifyPathAndRenavigateAsync(tempRootPath)
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.FooterText, Does.Contain("ClearCache"));
                Assert.That(result.UmlText, Does.Contain("fresh"));
                Assert.That(result.LineCount, Is.GreaterThanOrEqualTo(1));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB")]
    [TestCase("Chappy")]
    [TestCase("Search_table")]
    [TestCase("Alpha2")]
    public async Task build出力skinでも差分サムネ更新を流せる(string skinFolderName)
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-{skinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbUpdateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyBuildOutputSkinThumbUpdateAsync(tempRootPath, skinFolderName)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.BeforeThumbSrc, Is.Not.Empty);
                Assert.That(
                    result.AfterThumbSrc,
                    Is.EqualTo("data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==")
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB")]
    [TestCase("Chappy")]
    [TestCase("Search_table")]
    [TestCase("Alpha2")]
    public async Task build出力skinでも差分サムネ更新後にchangeSkinしても次skinへ持ち越さない(
        string skinFolderName
    )
    {
        string nextSkinFolderName = string.Equals(
            skinFolderName,
            "DefaultSmallWB",
            StringComparison.Ordinal
        )
            ? "Search_table"
            : "DefaultSmallWB";
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-changeskin-{skinFolderName.ToLowerInvariant()}-{nextSkinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinThumbChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            nextSkinFolderName
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(nextSkinFolderName));
                Assert.That(result.ThumbSrc, Is.Not.Empty);
                Assert.That(result.ThumbSrc, Does.Not.Contain("updated-build-thumb-77"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB")]
    [TestCase("Chappy")]
    [TestCase("Search_table")]
    [TestCase("Alpha2")]
    public async Task build出力skinでも差分サムネ更新後にchangeSkin失敗しても現在skinへ維持できる(
        string skinFolderName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-changefail-{skinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () => VerifyBuildOutputSkinThumbMissingChangeSkinAsync(tempRootPath, skinFolderName)
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(skinFolderName));
                Assert.That(result.ThumbSrc, Does.Contain("updated-build-thumb-77"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB", "onSkinLeave")]
    [TestCase("DefaultSmallWB", "onClearAll")]
    [TestCase("Chappy", "onSkinLeave")]
    [TestCase("Chappy", "onClearAll")]
    [TestCase("Search_table", "onSkinLeave")]
    [TestCase("Search_table", "onClearAll")]
    [TestCase("Alpha2", "onSkinLeave")]
    [TestCase("Alpha2", "onClearAll")]
    public async Task build出力skinでも差分サムネ更新後にterminal再入しても初期thumb表示へ戻せる(
        string skinFolderName,
        string callbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-terminal-{skinFolderName.ToLowerInvariant()}-{callbackName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbUpdateVerificationResult result = await RunOnStaDispatcherAsync(
                () =>
                    VerifyBuildOutputSkinThumbTerminalRerenderAsync(
                        tempRootPath,
                        skinFolderName,
                        callbackName
                    )
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.BeforeThumbSrc, Is.Not.Empty);
                Assert.That(result.AfterThumbSrc, Is.Not.Empty);
                Assert.That(result.AfterThumbSrc, Does.Not.Contain("updated-build-thumb-77"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB", "onSkinLeave")]
    [TestCase("DefaultSmallWB", "onClearAll")]
    [TestCase("Chappy", "onSkinLeave")]
    [TestCase("Chappy", "onClearAll")]
    [TestCase("Search_table", "onSkinLeave")]
    [TestCase("Search_table", "onClearAll")]
    [TestCase("Alpha2", "onSkinLeave")]
    [TestCase("Alpha2", "onClearAll")]
    public async Task build出力skinでも差分サムネ更新後にterminalを挟んでchangeSkinしても次skinへ持ち越さない(
        string skinFolderName,
        string callbackName
    )
    {
        string nextSkinFolderName = string.Equals(
            skinFolderName,
            "DefaultSmallWB",
            StringComparison.Ordinal
        )
            ? "Search_table"
            : "DefaultSmallWB";
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-terminal-changeskin-{skinFolderName.ToLowerInvariant()}-{callbackName.ToLowerInvariant()}-{nextSkinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinThumbTerminalChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            callbackName,
                            nextSkinFolderName
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(nextSkinFolderName));
                Assert.That(result.ThumbSrc, Is.Not.Empty);
                Assert.That(result.ThumbSrc, Does.Not.Contain("updated-build-thumb-77"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB", "onSkinLeave")]
    [TestCase("Chappy", "onSkinLeave")]
    [TestCase("Search_table", "onSkinLeave")]
    [TestCase("Alpha2", "onSkinLeave")]
    public async Task build出力skinでも差分サムネ更新後にterminalを挟んでchangeSkin失敗しても現在skinへ維持できる(
        string skinFolderName,
        string callbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-terminal-changefail-{skinFolderName.ToLowerInvariant()}-{callbackName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinThumbChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinThumbTerminalMissingChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            callbackName
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(skinFolderName));
                Assert.That(result.ThumbSrc, Does.Contain("updated-build-thumb-77"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB")]
    [TestCase("Chappy")]
    [TestCase("Search_table")]
    [TestCase("Alpha2")]
    public async Task build出力skinでも差分サムネ更新後にonClearAllを挟んでchangeSkin失敗すると空状態のまま現在skinへ維持できる(
        string skinFolderName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-thumb-terminal-changefail-{skinFolderName.ToLowerInvariant()}-onclearall"
        );

        try
        {
            BuildOutputSkinThumbChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinThumbTerminalMissingChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            "onClearAll"
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(skinFolderName));
                Assert.That(result.ThumbSrc, Is.Empty);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("DefaultSmallWB")]
    [TestCase("Chappy")]
    [TestCase("Search_table")]
    [TestCase("Alpha2")]
    public async Task build出力skinでもtag差分更新を流せる(string skinFolderName)
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-{skinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinModifyTagsVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyBuildOutputSkinModifyTagsAsync(tempRootPath, skinFolderName)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.BeforeTagText, Does.Contain("series-a"));
                Assert.That(result.AfterTagText, Does.Contain("fresh-tag"));
                Assert.That(result.AfterTagText, Does.Contain("idol"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("Search_table")]
    [TestCase("Chappy")]
    [TestCase("DefaultSmallWB")]
    [TestCase("Alpha2")]
    public async Task build出力skinでもtag差分更新後にchangeSkinしても次skinへ持ち越さない(
        string skinFolderName
    )
    {
        string nextSkinFolderName = string.Equals(
            skinFolderName,
            "DefaultSmallWB",
            StringComparison.Ordinal
        )
            ? "Search_table"
            : "DefaultSmallWB";
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-changeskin-{skinFolderName.ToLowerInvariant()}-{nextSkinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinModifyTagsChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinModifyTagsChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            nextSkinFolderName
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.ChangeSkinResult, Is.EqualTo("true"));
                Assert.That(result.CurrentSkin, Is.EqualTo(nextSkinFolderName));
                Assert.That(result.TagText, Does.Contain("series-a"));
                Assert.That(result.TagText, Does.Not.Contain("fresh-tag"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("Search_table", "onSkinLeave")]
    [TestCase("Search_table", "onClearAll")]
    [TestCase("Chappy", "onSkinLeave")]
    [TestCase("Chappy", "onClearAll")]
    [TestCase("DefaultSmallWB", "onSkinLeave")]
    [TestCase("DefaultSmallWB", "onClearAll")]
    [TestCase("Alpha2", "onSkinLeave")]
    [TestCase("Alpha2", "onClearAll")]
    public async Task build出力skinでもtag差分更新後にterminalを挟んでchangeSkinしても次skinへ持ち越さない(
        string skinFolderName,
        string callbackName
    )
    {
        string nextSkinFolderName = string.Equals(
            skinFolderName,
            "DefaultSmallWB",
            StringComparison.Ordinal
        )
            ? "Search_table"
            : "DefaultSmallWB";
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-terminal-changeskin-{skinFolderName.ToLowerInvariant()}-{callbackName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinModifyTagsChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinModifyTagsTerminalChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            callbackName,
                            nextSkinFolderName
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.ChangeSkinResult, Is.EqualTo("true"));
                Assert.That(result.CurrentSkin, Is.EqualTo(nextSkinFolderName));
                Assert.That(result.TagText, Does.Contain("series-a"));
                Assert.That(result.TagText, Does.Not.Contain("fresh-tag"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("Search_table", "onSkinLeave")]
    [TestCase("Search_table", "onClearAll")]
    [TestCase("Chappy", "onSkinLeave")]
    [TestCase("Chappy", "onClearAll")]
    [TestCase("DefaultSmallWB", "onSkinLeave")]
    [TestCase("DefaultSmallWB", "onClearAll")]
    [TestCase("Alpha2", "onSkinLeave")]
    [TestCase("Alpha2", "onClearAll")]
    public async Task build出力skinでもtag差分更新後にterminal再入しても初期tag表示へ戻せる(
        string skinFolderName,
        string callbackName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-terminal-{skinFolderName.ToLowerInvariant()}-{callbackName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinModifyTagsVerificationResult result = await RunOnStaDispatcherAsync(
                () =>
                    VerifyBuildOutputSkinModifyTagsTerminalRerenderAsync(
                        tempRootPath,
                        skinFolderName,
                        callbackName
                    )
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.BeforeTagText, Does.Contain("series-a"));
                Assert.That(result.AfterTagText, Does.Contain("series-a"));
                Assert.That(result.AfterTagText, Does.Not.Contain("fresh-tag"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("Search_table")]
    [TestCase("Chappy")]
    [TestCase("DefaultSmallWB")]
    [TestCase("Alpha2")]
    public async Task build出力skinでもtag差分更新後にchangeSkin失敗しても現在skinへ維持できる(
        string skinFolderName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-changefail-{skinFolderName.ToLowerInvariant()}"
        );

        try
        {
            BuildOutputSkinModifyTagsChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinModifyTagsMissingChangeSkinAsync(
                            tempRootPath,
                            skinFolderName
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.ChangeSkinResult, Is.EqualTo("false"));
                Assert.That(result.CurrentSkin, Is.EqualTo(skinFolderName));
                Assert.That(result.TagText, Does.Contain("idol"));
                Assert.That(result.TagText, Does.Contain("fresh-tag"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("Search_table")]
    [TestCase("Chappy")]
    [TestCase("DefaultSmallWB")]
    [TestCase("Alpha2")]
    public async Task build出力skinでもtag差分更新後にonClearAllを挟んでchangeSkin失敗すると空状態のまま現在skinへ維持できる(
        string skinFolderName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-terminal-changefail-{skinFolderName.ToLowerInvariant()}-onclearall"
        );

        try
        {
            BuildOutputSkinModifyTagsChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinModifyTagsTerminalMissingChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            "onClearAll"
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(skinFolderName));
                Assert.That(result.ChangeSkinResult, Is.EqualTo("false"));
                Assert.That(result.TagText, Is.Empty);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [TestCase("Search_table")]
    [TestCase("Chappy")]
    [TestCase("DefaultSmallWB")]
    [TestCase("Alpha2")]
    public async Task build出力skinでもtag差分更新後にonSkinLeaveを挟んでchangeSkin失敗すると更新済み状態のまま現在skinへ維持できる(
        string skinFolderName
    )
    {
        string tempRootPath = CreateTempDirectory(
            $"imm-wbskin-runtimebridge-build-modifytags-terminal-changefail-{skinFolderName.ToLowerInvariant()}-onskinleave"
        );

        try
        {
            BuildOutputSkinModifyTagsChangeSkinVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () =>
                        VerifyBuildOutputSkinModifyTagsTerminalMissingChangeSkinAsync(
                            tempRootPath,
                            skinFolderName,
                            "onSkinLeave"
                        )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo(skinFolderName));
                Assert.That(result.ChangeSkinResult, Is.EqualTo("false"));
                Assert.That(result.TagText, Does.Contain("idol"));
                Assert.That(result.TagText, Does.Contain("fresh-tag"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultGrid_実WebView2でdefault_onUpdateとgrid描画を流せる()
    {
        SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunSimpleWhiteBrowserDefaultFixtureAsync(
            "imm-wbskin-runtimebridge-default-grid",
            "WhiteBrowserDefaultGrid",
            expectedTitleText: "Beta.avi",
            expectedSelectedClass: "thum_select"
        );

        AssertSimpleDefaultFixture(result);
    }

    [Test]
    public async Task WhiteBrowserDefaultGrid_実WebView2でconfig_seamless_scroll追記できる()
    {
        SimpleWhiteBrowserDefaultFixtureAppendVerificationResult result =
            await RunSimpleWhiteBrowserDefaultFixtureAppendAsync(
                "imm-wbskin-runtimebridge-default-grid-seamless",
                "WhiteBrowserDefaultGrid",
                expectedAppendedTitleText: "Gamma.mkv"
            );

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task WhiteBrowserDefaultGrid_実WebView2で検索後にconfig_seamless_scroll追記できる()
    {
        SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult result =
            await RunSimpleWhiteBrowserDefaultFixtureSearchAppendAsync(
                "imm-wbskin-runtimebridge-default-grid-search-seamless",
                "WhiteBrowserDefaultGrid",
                expectedAppendedTitleText: "Movie003.mkv"
            );

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task WhiteBrowserDefaultSmall_実WebView2でscore付きsmall描画を流せる()
    {
        SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunSimpleWhiteBrowserDefaultFixtureAsync(
            "imm-wbskin-runtimebridge-default-small",
            "WhiteBrowserDefaultSmall",
            expectedTitleText: "Beta.avi",
            expectedSelectedClass: "thum_select",
            expectedScoreText: "88.5"
        );

        AssertSimpleDefaultFixture(result);
    }

    [Test]
    public async Task WhiteBrowserDefaultSmall_実WebView2でconfig_seamless_scroll追記できる()
    {
        SimpleWhiteBrowserDefaultFixtureAppendVerificationResult result =
            await RunSimpleWhiteBrowserDefaultFixtureAppendAsync(
                "imm-wbskin-runtimebridge-default-small-seamless",
                "WhiteBrowserDefaultSmall",
                expectedAppendedTitleText: "Gamma.mkv",
                expectedAppendedScoreText: "77.7"
            );

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task WhiteBrowserDefaultSmall_実WebView2で検索後にconfig_seamless_scroll追記できる()
    {
        SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult result =
            await RunSimpleWhiteBrowserDefaultFixtureSearchAppendAsync(
                "imm-wbskin-runtimebridge-default-small-search-seamless",
                "WhiteBrowserDefaultSmall",
                expectedAppendedTitleText: "Movie003.mkv",
                expectedAppendedScoreText: "77.7"
            );

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task WhiteBrowserDefaultBig_実WebView2でscore付きbig描画を流せる()
    {
        SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunSimpleWhiteBrowserDefaultFixtureAsync(
            "imm-wbskin-runtimebridge-default-big",
            "WhiteBrowserDefaultBig",
            expectedTitleText: "No.77 : Beta.avi",
            expectedSelectedClass: "thum_select",
            expectedScoreText: "88.5"
        );

        AssertSimpleDefaultFixture(result);
    }

    [Test]
    public async Task WhiteBrowserDefaultBig_実WebView2でconfig_seamless_scroll追記できる()
    {
        SimpleWhiteBrowserDefaultFixtureAppendVerificationResult result =
            await RunSimpleWhiteBrowserDefaultFixtureAppendAsync(
                "imm-wbskin-runtimebridge-default-big-seamless",
                "WhiteBrowserDefaultBig",
                expectedAppendedTitleText: "No.88 : Gamma.mkv",
                expectedAppendedScoreText: "77.7"
            );

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task WhiteBrowserDefaultBig_実WebView2で検索後にconfig_seamless_scroll追記できる()
    {
        SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult result =
            await RunSimpleWhiteBrowserDefaultFixtureSearchAppendAsync(
                "imm-wbskin-runtimebridge-default-big-search-seamless",
                "WhiteBrowserDefaultBig",
                expectedAppendedTitleText: "No.3 : Movie003.mkv",
                expectedAppendedScoreText: "77.7"
            );

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TutorialCallbackGridからWhiteBrowserDefaultListへ切替しても旧DOM残骸を残さず描画を切り替えられる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-switch-fixtures");

        try
        {
            FixtureSwitchVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyFixtureSwitchAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.FirstFixtureItemCount, Is.EqualTo(2));
                Assert.That(result.SecondFixtureItemCount, Is.EqualTo(2));
                Assert.That(result.FirstFixtureTitleText, Is.EqualTo("Alpha.mp4"));
                Assert.That(result.SecondFixtureTitleText, Is.EqualTo("Delta.mpg"));
                Assert.That(result.SecondFixtureScrollExists, Is.True);
                Assert.That(result.SecondFixtureLegacyNodeGone, Is.True);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGridを同一fixtureで再navigateしてもleave順を返して再描画できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-renavigate-same-fixture");

        try
        {
            SameFixtureRenavigateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySameFixtureRenavigateAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.FirstFocusRequestMovieId, Is.EqualTo(42));
                Assert.That(result.SecondFocusRequestMovieId, Is.EqualTo(84));
                Assert.That(
                    result.LifecycleEvents,
                    Is.EqualTo(["focus:42:false", "select:42:false", "clear", "leave"])
                );
                Assert.That(result.SecondItemCount, Is.EqualTo(1));
                Assert.That(result.SecondTitleText, Is.EqualTo("Gamma.mkv"));
                Assert.That(result.LegacyNodeGone, Is.True);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static async Task<RuntimeBridgeVerificationResult> VerifyExternalThumbnailResponsesAsync(
        string tempRootPath
    )
    {
        string skinRootPath = Path.Combine(tempRootPath, "skin");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        string okImagePath = Path.Combine(tempRootPath, "external-ok.png");
        string missingImagePath = Path.Combine(tempRootPath, "external-missing.png");
        string forbiddenImagePath = Path.Combine(tempRootPath, "external-forbidden.png");
        Directory.CreateDirectory(skinRootPath);
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        CreateSamplePng(okImagePath, 32, 18);

        WhiteBrowserSkinRuntimeBridge runtimeBridge = new();
        Window hostWindow = new()
        {
            Width = 160,
            Height = 120,
            Left = 8,
            Top = 8,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult attachResult =
                await runtimeBridge.TryEnsureAttachedAsync(
                    webView,
                    "RuntimeBridgeTest",
                    userDataFolderPath,
                    skinRootPath,
                    thumbRootPath
                );
            if (!attachResult.Succeeded)
            {
                return attachResult.RuntimeAvailable
                    ? RuntimeBridgeVerificationResult.Failed(
                        $"WebView2 初期化に失敗しました: {attachResult.ErrorType} {attachResult.ErrorMessage}"
                    )
                    : RuntimeBridgeVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため統合確認をスキップします: {attachResult.ErrorMessage}"
                    );
            }

            string okUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                okImagePath,
                thumbRootPath,
                "ok"
            );
            string forbiddenUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                forbiddenImagePath,
                thumbRootPath,
                "forbidden"
            );
            string missingUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                missingImagePath,
                thumbRootPath,
                "missing"
            );
            runtimeBridge.RegisterExternalThumbnailPath(okImagePath);
            runtimeBridge.RegisterExternalThumbnailPath(missingImagePath);

            ConcurrentDictionary<string, ExternalThumbnailResponseSnapshot> responses = new(
                StringComparer.Ordinal
            );
            ConcurrentBag<string> observedThumbnailUrls = [];
            TaskCompletionSource<bool> allResponsesArrived = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Dictionary<string, string> responseKeysByUrl = new(StringComparer.Ordinal)
            {
                [okUrl] = "ok",
                [forbiddenUrl] = "forbidden",
                [missingUrl] = "missing",
            };

            webView.CoreWebView2.WebResourceResponseReceived += (_, args) =>
            {
                _ = CaptureResponseAsync(args);
            };

            TaskCompletionSource<bool> navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                }
                else
                {
                    navigationCompleted.TrySetException(
                        new InvalidOperationException(
                            $"Navigation failed: {args.WebErrorStatus}"
                        )
                    );
                }
            };
            webView.NavigateToString("<html><body>runtime bridge integration</body></html>");

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return RuntimeBridgeVerificationResult.Failed(
                    "WebView2 の初期 document 読込が 10 秒以内に完了しませんでした。"
                );
            }

            string injectScript =
                $$"""
                (() => {
                  const urls = [
                    {{ToJavaScriptStringLiteral(okUrl)}},
                    {{ToJavaScriptStringLiteral(forbiddenUrl)}},
                    {{ToJavaScriptStringLiteral(missingUrl)}}
                  ];
                  for (const url of urls) {
                    const img = new Image();
                    img.src = url;
                    document.body.appendChild(img);
                  }
                  return urls.length;
                })();
                """;
            string imageProbeResultsJson = await webView.ExecuteScriptAsync(injectScript);

            Task completedTask = await Task.WhenAny(
                allResponsesArrived.Task,
                Task.Delay(TimeSpan.FromSeconds(30))
            );
            if (!ReferenceEquals(completedTask, allResponsesArrived.Task))
            {
                return RuntimeBridgeVerificationResult.Failed(
                    "WebView2 から thum.local 応答を 30 秒以内に回収できませんでした。"
                        + $" probes={imageProbeResultsJson}"
                        + $" observed=[{string.Join(", ", observedThumbnailUrls.OrderBy(x => x, StringComparer.Ordinal))}]"
                );
            }

            return RuntimeBridgeVerificationResult.Succeeded(
                responses["ok"],
                responses["forbidden"],
                responses["missing"]
            );

            async Task CaptureResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
            {
                if (
                    args?.Request == null
                )
                {
                    return;
                }

                if (
                    string.Equals(
                        new Uri(args.Request.Uri).Host,
                        WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    observedThumbnailUrls.Add(args.Request.Uri);
                }

                if (
                    !responseKeysByUrl.TryGetValue(args.Request.Uri, out string? responseKey)
                    || string.IsNullOrWhiteSpace(responseKey)
                )
                {
                    return;
                }

                ExternalThumbnailResponseSnapshot snapshot =
                    await ExternalThumbnailResponseSnapshot.CreateAsync(args.Response);
                responses[responseKey] = snapshot;
                if (
                    responses.ContainsKey("ok")
                    && responses.ContainsKey("forbidden")
                    && responses.ContainsKey("missing")
                )
                {
                    allResponsesArrived.TrySetResult(true);
                }
            }
        }
        finally
        {
            runtimeBridge.Dispose();
            hostWindow.Close();
        }
    }

    private static async Task<TutorialCallbackGridVerificationResult> VerifyTutorialCallbackGridAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("TutorialCallbackGrid");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 20,
            Top = 20,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int updateStartIndex = -1;
        int updateCount = -1;
        int firstFocusRequestMovieId = 0;
        int secondFocusRequestMovieId = 0;
        TaskCompletionSource<bool> firstFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                updateRequestCount += 1;
                if (e.Payload.ValueKind == JsonValueKind.Object)
                {
                    if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                    {
                        updateStartIndex = startIndexElement.GetInt32();
                    }

                    if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                    {
                        updateCount = countElement.GetInt32();
                    }
                }

                // 実 fixture が期待する旧 alias 形で返し、onCreateThum と focus 遷移を一気に確認する。
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    updateRequestCount == 1
                        ? new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 42,
                                    title = "Alpha",
                                    ext = ".mp4",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                                new
                                {
                                    id = 77,
                                    title = "Beta",
                                    ext = ".avi",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = false,
                                    select = 0,
                                },
                            },
                        }
                        : new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 84,
                                    title = "Gamma",
                                    ext = ".mkv",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                            },
                        }
                );
                return;
            }

            if (string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
            {
                if (e.Payload.ValueKind == JsonValueKind.Object
                    && e.Payload.TryGetProperty("movieId", out JsonElement movieIdElement))
                {
                    int requestedMovieId = movieIdElement.GetInt32();
                    if (!firstFocusResolved.Task.IsCompleted)
                    {
                        firstFocusRequestMovieId = requestedMovieId;
                    }
                    else
                    {
                        secondFocusRequestMovieId = requestedMovieId;
                    }
                }

                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    !firstFocusResolved.Task.IsCompleted
                        ? new
                        {
                            movieId = 42,
                            id = 42,
                            focused = true,
                            focusedMovieId = 42,
                            selected = true,
                        }
                        : new
                        {
                            movieId = 84,
                            id = 84,
                            focused = true,
                            focusedMovieId = 84,
                            selected = true,
                        }
                );
                if (!firstFocusResolved.Task.IsCompleted)
                {
                    firstFocusResolved.TrySetResult(true);
                }
                else
                {
                    secondFocusResolved.TrySetResult(true);
                }
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "TutorialCallbackGrid"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? TutorialCallbackGridVerificationResult.Failed(
                        $"TutorialCallbackGrid 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : TutorialCallbackGridVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため TutorialCallbackGrid 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                firstFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "TutorialCallbackGrid の初回 focus 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 2
                  && document.getElementById('img42')?.className === 'img_base img_f'
                  && document.getElementById('thum42')?.className === 'thum_base thum_s'
                """,
                TimeSpan.FromSeconds(5),
                "TutorialCallbackGrid の初回描画完了を待てませんでした。"
            );

            TutorialCallbackGridDomSnapshot beforeLeave = await ReadTutorialCallbackGridSnapshotAsync(
                webView,
                42
            );

            await webView.ExecuteScriptAsync("wb.update(0, 200);");
            await WaitAsync(
                secondFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "TutorialCallbackGrid の再 update 後 focus 要求を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 1
                  && document.getElementById('title84')?.textContent === 'Gamma.mkv'
                  && document.getElementById('img84')?.className === 'img_base img_f'
                  && document.getElementById('thum84')?.className === 'thum_base thum_s'
                  && !document.getElementById('thum42')
                """,
                TimeSpan.FromSeconds(5),
                "TutorialCallbackGrid の再 update 後 clear + 再描画完了を待てませんでした。"
            );

            TutorialCallbackGridDomSnapshot afterRefresh = await ReadTutorialCallbackGridSnapshotAsync(
                webView,
                84
            );

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#view .thum_base').length === 0",
                TimeSpan.FromSeconds(5),
                "TutorialCallbackGrid の leave 後 clear 完了を待てませんでした。"
            );

            TutorialCallbackGridDomSnapshot afterLeave = await ReadTutorialCallbackGridSnapshotAsync(
                webView,
                84
            );

            return TutorialCallbackGridVerificationResult.Succeeded(
                updateRequestCount,
                updateStartIndex,
                updateCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                beforeLeave.ItemCount,
                beforeLeave.TitleText,
                beforeLeave.FocusedImageClass,
                beforeLeave.SelectedThumbClass,
                afterRefresh.ItemCount,
                afterRefresh.TitleText,
                afterRefresh.FocusedImageClass,
                afterRefresh.SelectedThumbClass,
                afterLeave.ItemCount
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<WhiteBrowserDefaultListVerificationResult> VerifyWhiteBrowserDefaultListAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("WhiteBrowserDefaultList");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 24,
            Top = 24,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int updateStartIndex = -1;
        int updateCount = -1;
        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            updateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    updateStartIndex = startIndexElement.GetInt32();
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    updateCount = countElement.GetInt32();
                }
            }

            _ = hostControl.ResolveRequestAsync(
                e.MessageId,
                new
                {
                    items = new object[]
                    {
                        new
                        {
                            id = 42,
                            title = "Alpha",
                            ext = ".mp4",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 0,
                            size = "1.0 GB",
                            len = "00:10:00",
                        },
                        new
                        {
                            id = 77,
                            title = "Beta",
                            ext = ".avi",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = false,
                            select = 1,
                            size = "2.0 GB",
                            len = "01:23:45",
                        },
                    },
                }
            );
            updateResolved.TrySetResult(true);
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "WhiteBrowserDefaultList",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "WhiteBrowserDefaultList"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? WhiteBrowserDefaultListVerificationResult.Failed(
                        $"WhiteBrowserDefaultList 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : WhiteBrowserDefaultListVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため WhiteBrowserDefaultList 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                "WhiteBrowserDefaultList の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 2
                  && document.getElementById('title77')?.textContent === 'Beta.avi'
                """,
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の初回描画完了を待てませんでした。"
            );

            WhiteBrowserDefaultListDomSnapshot beforeLeave =
                await ReadWhiteBrowserDefaultListSnapshotAsync(webView);

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#view tr').length === 0",
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の leave 後 clear 完了を待てませんでした。"
            );

            WhiteBrowserDefaultListDomSnapshot afterLeave =
                await ReadWhiteBrowserDefaultListSnapshotAsync(webView);

            return WhiteBrowserDefaultListVerificationResult.Succeeded(
                updateRequestCount,
                updateStartIndex,
                updateCount,
                beforeLeave.ItemCount,
                beforeLeave.TitleText,
                beforeLeave.SizeText,
                beforeLeave.LengthText,
                beforeLeave.ScrollElementId,
                afterLeave.ItemCount
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<WhiteBrowserDefaultListThumbUpdateVerificationResult> VerifyWhiteBrowserDefaultListThumbUpdateFallbackAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("WhiteBrowserDefaultList");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 24,
            Top = 24,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            _ = hostControl.ResolveRequestAsync(
                e.MessageId,
                new
                {
                    items = new object[]
                    {
                        new
                        {
                            id = 42,
                            movieId = 42,
                            title = "Alpha",
                            ext = ".mp4",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 0,
                            size = "1.0 GB",
                            len = "00:10:00",
                        },
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            ext = ".avi",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 1,
                            size = "2.0 GB",
                            len = "01:23:45",
                        },
                    },
                }
            );
            updateResolved.TrySetResult(true);
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "WhiteBrowserDefaultList",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "WhiteBrowserDefaultList"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? WhiteBrowserDefaultListThumbUpdateVerificationResult.Failed(
                        $"WhiteBrowserDefaultList の thumb fallback 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : WhiteBrowserDefaultListThumbUpdateVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため WhiteBrowserDefaultList thumb fallback 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                "WhiteBrowserDefaultList の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 2
                  && document.getElementById('img77')
                """,
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の初回描画完了を待てませんでした。"
            );

            string beforeThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl = "data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==",
                    thum = "data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==",
                    thumbRevision = "thumb-2",
                    thumbSourceKind = "managed-thumbnail",
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === 'data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw=='
                """,
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の既定 onUpdateThum fallback 反映を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return WhiteBrowserDefaultListThumbUpdateVerificationResult.Succeeded(
                beforeThumbSrc,
                afterThumbSrc
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<WhiteBrowserDefaultListAppendVerificationResult> VerifyWhiteBrowserDefaultListAppendAsync(
        string tempRootPath,
        bool useSeamlessScroll
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("WhiteBrowserDefaultList");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 24,
            Top = 24,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        List<int> updateStartIndices = [];
        List<int> updateCounts = [];
        int updateRequestCount = 0;
        TaskCompletionSource<bool> initialUpdateResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> appendUpdateResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string modeLabel = useSeamlessScroll ? "seamless" : "append";

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            updateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    updateStartIndices.Add(startIndexElement.GetInt32());
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    updateCounts.Add(countElement.GetInt32());
                }
            }

            object responsePayload =
                updateRequestCount == 1
                    ? new
                    {
                        startIndex = 0,
                        requestedCount = 200,
                        totalCount = 3,
                        items = new object[]
                        {
                            new
                            {
                                id = 42,
                                title = "Alpha",
                                ext = ".mp4",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = true,
                                select = 0,
                                size = "1.0 GB",
                                len = "00:10:00",
                            },
                            new
                            {
                                id = 77,
                                title = "Beta",
                                ext = ".avi",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = false,
                                select = 1,
                                size = "2.0 GB",
                                len = "01:23:45",
                            },
                        },
                    }
                    : new
                    {
                        startIndex = 2,
                        requestedCount = 200,
                        totalCount = 3,
                        items = new object[]
                        {
                            new
                            {
                                id = 88,
                                title = "Gamma",
                                ext = ".mkv",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = true,
                                select = 0,
                                size = "3.0 GB",
                                len = "00:11:22",
                            },
                        },
                    };

            _ = hostControl.ResolveRequestAsync(e.MessageId, responsePayload);
            if (updateRequestCount == 1)
            {
                initialUpdateResolved.TrySetResult(true);
            }
            else if (updateRequestCount == 2)
            {
                appendUpdateResolved.TrySetResult(true);
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "WhiteBrowserDefaultList",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "WhiteBrowserDefaultList"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? WhiteBrowserDefaultListAppendVerificationResult.Failed(
                        $"WhiteBrowserDefaultList {modeLabel} 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : WhiteBrowserDefaultListAppendVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため WhiteBrowserDefaultList {modeLabel} 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                initialUpdateResolved.Task,
                TimeSpan.FromSeconds(10),
                "WhiteBrowserDefaultList の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 2
                  && document.getElementById('title77')?.textContent === 'Beta.avi'
                """,
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の初回描画完了を待てませんでした。"
            );

            if (useSeamlessScroll)
            {
                await webView.ExecuteScriptAsync(
                    """
                    (() => {
                      const scroll = document.getElementById('scroll');
                      if (!scroll) {
                        return false;
                      }

                      scroll.style.maxHeight = '120px';
                      scroll.style.overflowY = 'auto';
                      scroll.scrollTop = scroll.scrollHeight;
                      scroll.dispatchEvent(new Event('scroll'));
                      return true;
                    })();
                    """
                );
            }
            else
            {
                await webView.ExecuteScriptAsync(
                    """(async () => { await wb.update(2, 1); return true; })();"""
                );
            }
            await WaitAsync(
                appendUpdateResolved.Task,
                TimeSpan.FromSeconds(10),
                useSeamlessScroll
                    ? "WhiteBrowserDefaultList の seamless scroll 追記 update 要求を待てませんでした。"
                    : "WhiteBrowserDefaultList の追記 update 要求を待てませんでした。"
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 3
                  && document.getElementById('title88')?.textContent === 'Gamma.mkv'
                """,
                TimeSpan.FromSeconds(5),
                useSeamlessScroll
                    ? "WhiteBrowserDefaultList の seamless scroll 追記描画完了を待てませんでした。"
                    : "WhiteBrowserDefaultList の追記描画完了を待てませんでした。"
            );

            string titlesJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify(Array.from(document.querySelectorAll('#view tr h3')).map(x => x.textContent || ''))
                """
            );
            string[] titlesAfterAppend = DeserializeStringArray(titlesJson);
            int itemCountAfterAppend = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view tr').length"
            );

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#view tr').length === 0",
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の leave 後 clear 完了を待てませんでした。"
            );

            int itemCountAfterLeave = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view tr').length"
            );

            return WhiteBrowserDefaultListAppendVerificationResult.Succeeded(
                updateRequestCount,
                [.. updateStartIndices],
                [.. updateCounts],
                itemCountAfterAppend,
                titlesAfterAppend,
                itemCountAfterLeave
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureVerificationResult> RunSimpleWhiteBrowserDefaultFixtureAsync(
        string tempDirectoryPrefix,
        string fixtureName,
        string expectedTitleText,
        string expectedSelectedClass,
        string expectedScoreText = ""
    )
    {
        string tempRootPath = CreateTempDirectory(tempDirectoryPrefix);

        try
        {
            SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySimpleWhiteBrowserDefaultFixtureAsync(tempRootPath, fixtureName)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(1));
                Assert.That(result.UpdateStartIndex, Is.EqualTo(0));
                Assert.That(result.UpdateCount, Is.EqualTo(200));
                Assert.That(result.ItemCountBeforeLeave, Is.EqualTo(2));
                Assert.That(result.TitleTextBeforeLeave, Is.EqualTo(expectedTitleText));
                Assert.That(result.SelectedClassBeforeLeave, Is.EqualTo(expectedSelectedClass));
                Assert.That(result.ScoreTextBeforeLeave, Is.EqualTo(expectedScoreText));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });

            return result;
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureAppendVerificationResult> RunSimpleWhiteBrowserDefaultFixtureAppendAsync(
        string tempDirectoryPrefix,
        string fixtureName,
        string expectedAppendedTitleText,
        string expectedAppendedScoreText = ""
    )
    {
        string tempRootPath = CreateTempDirectory(tempDirectoryPrefix);

        try
        {
            SimpleWhiteBrowserDefaultFixtureAppendVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () => VerifySimpleWhiteBrowserDefaultFixtureAppendAsync(tempRootPath, fixtureName)
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.UpdateStartIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(result.UpdateCounts, Is.EqualTo(new[] { 200, 1 }));
                Assert.That(result.ItemCountAfterAppend, Is.EqualTo(3));
                Assert.That(result.TitlesAfterAppend, Has.Length.EqualTo(3));
                Assert.That(result.TitlesAfterAppend[2], Is.EqualTo(expectedAppendedTitleText));
                Assert.That(result.AppendedScoreText, Is.EqualTo(expectedAppendedScoreText));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });

            return result;
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult> RunSimpleWhiteBrowserDefaultFixtureSearchAppendAsync(
        string tempDirectoryPrefix,
        string fixtureName,
        string expectedAppendedTitleText,
        string expectedAppendedScoreText = ""
    )
    {
        string tempRootPath = CreateTempDirectory(tempDirectoryPrefix);

        try
        {
            SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult result =
                await RunOnStaDispatcherAsync(
                    () => VerifySimpleWhiteBrowserDefaultFixtureSearchAppendAsync(
                        tempRootPath,
                        fixtureName
                    )
                );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.InitialUpdateRequestCount, Is.EqualTo(1));
                Assert.That(result.FindRequestCount, Is.EqualTo(1));
                Assert.That(result.FindKeyword, Is.EqualTo("Movie"));
                Assert.That(result.FindStartIndex, Is.EqualTo(0));
                Assert.That(result.FindCount, Is.EqualTo(200));
                Assert.That(result.AppendUpdateRequestCount, Is.EqualTo(1));
                Assert.That(result.AppendUpdateStartIndex, Is.EqualTo(2));
                Assert.That(result.AppendUpdateCount, Is.EqualTo(1));
                Assert.That(result.ItemCountAfterAppend, Is.EqualTo(3));
                Assert.That(result.TitlesAfterAppend, Has.Length.EqualTo(3));
                Assert.That(result.TitlesAfterAppend[2], Is.EqualTo(expectedAppendedTitleText));
                Assert.That(result.AppendedScoreText, Is.EqualTo(expectedAppendedScoreText));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });

            return result;
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static void AssertSimpleDefaultFixture(
        SimpleWhiteBrowserDefaultFixtureVerificationResult result
    )
    {
        Assert.That(result, Is.Not.Null);
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureVerificationResult> VerifySimpleWhiteBrowserDefaultFixtureAsync(
        string tempRootPath,
        string fixtureName
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat(fixtureName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 28,
            Top = 28,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int updateStartIndex = -1;
        int updateCount = -1;
        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            updateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    updateStartIndex = startIndexElement.GetInt32();
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    updateCount = countElement.GetInt32();
                }
            }

            _ = hostControl.ResolveRequestAsync(
                e.MessageId,
                new
                {
                    items = new object[]
                    {
                        new
                        {
                            id = 42,
                            title = "Alpha",
                            ext = ".mp4",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 0,
                            score = 11.0,
                        },
                        new
                        {
                            id = 77,
                            title = "Beta",
                            ext = ".avi",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = false,
                            select = 1,
                            score = 88.5,
                        },
                    },
                }
            );
            updateResolved.TrySetResult(true);
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                fixtureName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, fixtureName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? SimpleWhiteBrowserDefaultFixtureVerificationResult.Failed(
                        $"{fixtureName} 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : SimpleWhiteBrowserDefaultFixtureVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {fixtureName} 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view #thum77').length === 1
                  && document.getElementById('title77') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の初回描画完了を待てませんでした。"
            );

            SimpleWhiteBrowserDefaultFixtureDomSnapshot beforeLeave =
                await ReadSimpleWhiteBrowserDefaultFixtureSnapshotAsync(webView);

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 0
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の leave 後 clear 完了を待てませんでした。"
            );

            SimpleWhiteBrowserDefaultFixtureDomSnapshot afterLeave =
                await ReadSimpleWhiteBrowserDefaultFixtureSnapshotAsync(webView);

            return SimpleWhiteBrowserDefaultFixtureVerificationResult.Succeeded(
                updateRequestCount,
                updateStartIndex,
                updateCount,
                beforeLeave.ItemCount,
                beforeLeave.TitleText,
                beforeLeave.SelectedClass,
                beforeLeave.ScoreText,
                afterLeave.ItemCount
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureAppendVerificationResult> VerifySimpleWhiteBrowserDefaultFixtureAppendAsync(
        string tempRootPath,
        string fixtureName
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat(fixtureName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 30,
            Top = 30,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        List<int> updateStartIndices = [];
        List<int> updateCounts = [];
        int updateRequestCount = 0;
        TaskCompletionSource<bool> initialUpdateResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> appendUpdateResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            updateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    updateStartIndices.Add(startIndexElement.GetInt32());
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    updateCounts.Add(countElement.GetInt32());
                }
            }

            object responsePayload =
                updateRequestCount == 1
                    ? new
                    {
                        startIndex = 0,
                        requestedCount = 200,
                        totalCount = 3,
                        items = new object[]
                        {
                            new
                            {
                                id = 42,
                                title = "Alpha",
                                ext = ".mp4",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = true,
                                select = 0,
                                score = 11.0,
                            },
                            new
                            {
                                id = 77,
                                title = "Beta",
                                ext = ".avi",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = false,
                                select = 1,
                                score = 88.5,
                            },
                        },
                    }
                    : new
                    {
                        startIndex = 2,
                        requestedCount = 200,
                        totalCount = 3,
                        items = new object[]
                        {
                            new
                            {
                                id = 88,
                                title = "Gamma",
                                ext = ".mkv",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = true,
                                select = 0,
                                score = 77.7,
                            },
                        },
                    };

            _ = hostControl.ResolveRequestAsync(e.MessageId, responsePayload);
            if (updateRequestCount == 1)
            {
                initialUpdateResolved.TrySetResult(true);
            }
            else if (updateRequestCount == 2)
            {
                appendUpdateResolved.TrySetResult(true);
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                fixtureName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, fixtureName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? SimpleWhiteBrowserDefaultFixtureAppendVerificationResult.Failed(
                        $"{fixtureName} seamless 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : SimpleWhiteBrowserDefaultFixtureAppendVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {fixtureName} seamless 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                initialUpdateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 2
                  && document.getElementById('title77') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の初回描画完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  const view = document.getElementById('view');
                  if (!view) {
                    return false;
                  }

                  // config の seamless-scroll を、実際の scroll 発火で次ページ要求する。
                  view.style.maxHeight = '120px';
                  view.style.overflowY = 'auto';
                  view.scrollTop = view.scrollHeight;
                  view.dispatchEvent(new Event('scroll'));
                  return true;
                })();
                """
            );
            await WaitAsync(
                appendUpdateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の seamless scroll 追記 update 要求を待てませんでした。"
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 3
                  && document.getElementById('title88') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の seamless scroll 追記描画完了を待てませんでした。"
            );

            string titlesJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify(Array.from(document.getElementById('view').querySelectorAll('[id^="title"]')).map(x => x.textContent || ''))
                """
            );
            string[] titlesAfterAppend = DeserializeStringArray(titlesJson);
            string appendedScoreText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('score88') ? document.getElementById('score88').textContent || '' : ''"
            );
            int itemCountAfterAppend = await ReadJsonIntAsync(
                webView,
                "document.getElementById('view') ? document.getElementById('view').children.length : 0"
            );

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 0
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の leave 後 clear 完了を待てませんでした。"
            );

            int itemCountAfterLeave = await ReadJsonIntAsync(
                webView,
                "document.getElementById('view') ? document.getElementById('view').children.length : 0"
            );

            return SimpleWhiteBrowserDefaultFixtureAppendVerificationResult.Succeeded(
                updateRequestCount,
                [.. updateStartIndices],
                [.. updateCounts],
                itemCountAfterAppend,
                titlesAfterAppend,
                appendedScoreText,
                itemCountAfterLeave
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult> VerifySimpleWhiteBrowserDefaultFixtureSearchAppendAsync(
        string tempRootPath,
        string fixtureName
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat(fixtureName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 30,
            Top = 30,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int initialUpdateRequestCount = 0;
        int appendUpdateRequestCount = 0;
        int findRequestCount = 0;
        int findStartIndex = -1;
        int findCount = -1;
        int appendUpdateStartIndex = -1;
        int appendUpdateCount = -1;
        string findKeyword = "";
        bool searchModeEntered = false;
        TaskCompletionSource<bool> initialUpdateResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> findResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> appendUpdateResolved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "find", StringComparison.Ordinal))
            {
                findRequestCount += 1;
                searchModeEntered = true;
                if (e.Payload.ValueKind == JsonValueKind.Object)
                {
                    if (e.Payload.TryGetProperty("keyword", out JsonElement keywordElement))
                    {
                        findKeyword = keywordElement.GetString() ?? "";
                    }

                    if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                    {
                        findStartIndex = startIndexElement.GetInt32();
                    }

                    if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                    {
                        findCount = countElement.GetInt32();
                    }
                }

                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    new
                    {
                        startIndex = 0,
                        requestedCount = 200,
                        totalCount = 3,
                        items = new object[]
                        {
                            new
                            {
                                id = 1,
                                title = "Movie001",
                                ext = ".mp4",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = true,
                                select = 0,
                                score = 11.0,
                            },
                            new
                            {
                                id = 2,
                                title = "Movie002",
                                ext = ".avi",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = false,
                                select = 1,
                                score = 88.5,
                            },
                        },
                    }
                );
                findResolved.TrySetResult(true);
                return;
            }

            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            if (!searchModeEntered)
            {
                initialUpdateRequestCount += 1;
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    new
                    {
                        startIndex = 0,
                        requestedCount = 200,
                        totalCount = 3,
                        items = new object[]
                        {
                            new
                            {
                                id = 1,
                                title = "Movie001",
                                ext = ".mp4",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = true,
                                select = 0,
                                score = 11.0,
                            },
                            new
                            {
                                id = 2,
                                title = "Movie002",
                                ext = ".avi",
                                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                exist = false,
                                select = 1,
                                score = 88.5,
                            },
                        },
                    }
                );
                initialUpdateResolved.TrySetResult(true);
                return;
            }

            appendUpdateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    appendUpdateStartIndex = startIndexElement.GetInt32();
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    appendUpdateCount = countElement.GetInt32();
                }
            }

            // find 後の scroll でも、追加ページは update(startIndex=2) で継続要求される。
            _ = hostControl.ResolveRequestAsync(
                e.MessageId,
                new
                {
                    startIndex = 2,
                    requestedCount = 200,
                    totalCount = 3,
                    items = new object[]
                    {
                        new
                        {
                            id = 3,
                            title = "Movie003",
                            ext = ".mkv",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 0,
                            score = 77.7,
                        },
                    },
                }
            );
            appendUpdateResolved.TrySetResult(true);
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                fixtureName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, fixtureName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult.Failed(
                        $"{fixtureName} search seamless 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {fixtureName} search seamless 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                initialUpdateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 2
                  && document.getElementById('title2') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の初回描画完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """(async () => { await wb.find("Movie", 0); return true; })();"""
            );
            await WaitAsync(
                findResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の find 要求を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 2
                  && document.getElementById('title2') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の検索結果初回描画完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  const view = document.getElementById('view');
                  if (!view) {
                    return false;
                  }

                  view.style.maxHeight = '120px';
                  view.style.overflowY = 'auto';
                  view.scrollTop = view.scrollHeight;
                  view.dispatchEvent(new Event('scroll'));
                  return true;
                })();
                """
            );
            await WaitAsync(
                appendUpdateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の検索後 seamless scroll 追記 update 要求を待てませんでした。"
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 3
                  && document.getElementById('title3') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の検索後 seamless scroll 追記描画完了を待てませんでした。"
            );

            string titlesJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify(Array.from(document.getElementById('view').querySelectorAll('[id^="title"]')).map(x => x.textContent || ''))
                """
            );
            string[] titlesAfterAppend = DeserializeStringArray(titlesJson);
            string appendedScoreText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('score3') ? document.getElementById('score3').textContent || '' : ''"
            );
            int itemCountAfterAppend = await ReadJsonIntAsync(
                webView,
                "document.getElementById('view') ? document.getElementById('view').children.length : 0"
            );

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 0
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の leave 後 clear 完了を待てませんでした。"
            );

            int itemCountAfterLeave = await ReadJsonIntAsync(
                webView,
                "document.getElementById('view') ? document.getElementById('view').children.length : 0"
            );

            return SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult.Succeeded(
                initialUpdateRequestCount,
                findRequestCount,
                findKeyword,
                findStartIndex,
                findCount,
                appendUpdateRequestCount,
                appendUpdateStartIndex,
                appendUpdateCount,
                itemCountAfterAppend,
                titlesAfterAppend,
                appendedScoreText,
                itemCountAfterLeave
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<FixtureSwitchVerificationResult> VerifyFixtureSwitchAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat(
            "TutorialCallbackGrid",
            "WhiteBrowserDefaultList"
        );
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 240,
            Height = 180,
            Left = 32,
            Top = 32,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        TaskCompletionSource<bool> firstFixtureReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondFixtureReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> firstFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                updateRequestCount += 1;
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    updateRequestCount == 1
                        ? new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 42,
                                    title = "Alpha",
                                    ext = ".mp4",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                                new
                                {
                                    id = 77,
                                    title = "Beta",
                                    ext = ".avi",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = false,
                                    select = 0,
                                },
                            },
                        }
                        : new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 501,
                                    title = "Gamma",
                                    ext = ".wmv",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                    size = "512 MB",
                                    len = "00:20:00",
                                },
                                new
                                {
                                    id = 777,
                                    title = "Delta",
                                    ext = ".mpg",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 1,
                                    size = "4.0 GB",
                                    len = "02:34:56",
                                },
                            },
                        }
                );

                if (updateRequestCount == 2)
                {
                    secondFixtureReady.TrySetResult(true);
                }

                return;
            }

            if (string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
            {
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    new
                    {
                        movieId = 42,
                        id = 42,
                        focused = true,
                        focusedMovieId = 42,
                        selected = true,
                    }
                );
                firstFocusResolved.TrySetResult(true);
                firstFixtureReady.TrySetResult(true);
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "TutorialCallbackGrid"),
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                return firstNavigateResult.RuntimeAvailable
                    ? FixtureSwitchVerificationResult.Failed(
                        $"TutorialCallbackGrid 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                    )
                    : FixtureSwitchVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため fixture 切替確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                firstFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "最初の fixture focus 完了を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 2
                  && document.getElementById('title42')?.textContent === 'Alpha.mp4'
                """,
                TimeSpan.FromSeconds(5),
                "最初の fixture 描画完了を待てませんでした。"
            );
            string firstFixtureTitleText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('title42') ? document.getElementById('title42').textContent : ''"
            );
            int firstFixtureItemCount = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view .thum_base').length"
            );

            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "WhiteBrowserDefaultList",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "WhiteBrowserDefaultList"),
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                return FixtureSwitchVerificationResult.Failed(
                    $"WhiteBrowserDefaultList 読込に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitAsync(
                secondFixtureReady.Task,
                TimeSpan.FromSeconds(10),
                "2つ目の fixture update 完了を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 2
                  && document.getElementById('title777')?.textContent === 'Delta.mpg'
                  && document.getElementById('scroll') != null
                  && document.getElementById('title42') == null
                """,
                TimeSpan.FromSeconds(5),
                "2つ目の fixture 描画完了を待てませんでした。"
            );

            int secondFixtureItemCount = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view tr').length"
            );
            string secondFixtureTitleText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('title777') ? document.getElementById('title777').textContent : ''"
            );
            bool secondFixtureScrollExists = await ReadJsonBoolAsync(
                webView,
                "document.getElementById('scroll') != null"
            );
            bool secondFixtureLegacyNodeGone = await ReadJsonBoolAsync(
                webView,
                "document.getElementById('title42') == null"
            );

            return FixtureSwitchVerificationResult.Succeeded(
                updateRequestCount,
                firstFixtureItemCount,
                secondFixtureItemCount,
                firstFixtureTitleText,
                secondFixtureTitleText,
                secondFixtureScrollExists,
                secondFixtureLegacyNodeGone
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SameFixtureRenavigateVerificationResult> VerifySameFixtureRenavigateAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("TutorialCallbackGrid");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 240,
            Height = 180,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int firstFocusRequestMovieId = 0;
        int secondFocusRequestMovieId = 0;
        List<string> lifecycleEvents = [];
        TaskCompletionSource<bool> firstFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> leaveResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                updateRequestCount += 1;
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    updateRequestCount == 1
                        ? new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 42,
                                    title = "Alpha",
                                    ext = ".mp4",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                                new
                                {
                                    id = 77,
                                    title = "Beta",
                                    ext = ".avi",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = false,
                                    select = 0,
                                },
                            },
                        }
                        : new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 84,
                                    title = "Gamma",
                                    ext = ".mkv",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                            },
                        }
                );
                return;
            }

            if (string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
            {
                if (e.Payload.ValueKind == JsonValueKind.Object
                    && e.Payload.TryGetProperty("movieId", out JsonElement movieIdElement))
                {
                    int movieId = movieIdElement.GetInt32();
                    if (!firstFocusResolved.Task.IsCompleted)
                    {
                        firstFocusRequestMovieId = movieId;
                    }
                    else
                    {
                        secondFocusRequestMovieId = movieId;
                    }
                }

                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    !firstFocusResolved.Task.IsCompleted
                        ? new
                        {
                            movieId = 42,
                            id = 42,
                            focused = true,
                            focusedMovieId = 42,
                            selected = true,
                        }
                        : new
                        {
                            movieId = 84,
                            id = 84,
                            focused = true,
                            focusedMovieId = 84,
                            selected = true,
                        }
                );

                if (!firstFocusResolved.Task.IsCompleted)
                {
                    firstFocusResolved.TrySetResult(true);
                }
                else
                {
                    secondFocusResolved.TrySetResult(true);
                }

                return;
            }

            if (string.Equals(e.Method, "probeSequence", StringComparison.Ordinal))
            {
                if (e.Payload.ValueKind == JsonValueKind.Object
                    && e.Payload.TryGetProperty("event", out JsonElement eventElement))
                {
                    string eventName = eventElement.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(eventName))
                    {
                        lifecycleEvents.Add(eventName);
                        if (string.Equals(eventName, "leave", StringComparison.Ordinal))
                        {
                            leaveResolved.TrySetResult(true);
                        }
                    }
                }
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string tutorialHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                "TutorialCallbackGrid"
            );
            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                tutorialHtmlPath,
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                return firstNavigateResult.RuntimeAvailable
                    ? SameFixtureRenavigateVerificationResult.Failed(
                        $"TutorialCallbackGrid 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                    )
                    : SameFixtureRenavigateVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため同一 fixture 再 navigate 確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                firstFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "最初の TutorialCallbackGrid focus 完了を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  const originalFocus = typeof wb.onSetFocus === "function" ? wb.onSetFocus : null;
                  const originalSelect = typeof wb.onSetSelect === "function" ? wb.onSetSelect : null;
                  const originalClear = typeof wb.onClearAll === "function" ? wb.onClearAll : null;
                  const originalLeave = typeof wb.onSkinLeave === "function" ? wb.onSkinLeave : null;
                  function postEvent(name) {
                    chrome.webview.postMessage(JSON.stringify({
                      id: "probe-" + String(Math.random()),
                      method: "probeSequence",
                      payload: { event: name }
                    }));
                  }

                  wb.onSetFocus = function(id, isFocus) {
                    postEvent("focus:" + String(id || 0) + ":" + String(!!isFocus));
                    return originalFocus ? originalFocus.apply(this, arguments) : true;
                  };

                  wb.onSetSelect = function(id, isSel) {
                    postEvent("select:" + String(id || 0) + ":" + String(!!isSel));
                    return originalSelect ? originalSelect.apply(this, arguments) : true;
                  };

                  wb.onClearAll = function() {
                    postEvent("clear");
                    return originalClear ? originalClear.apply(this, arguments) : true;
                  };

                  wb.onSkinLeave = function() {
                    postEvent("leave");
                    return originalLeave ? originalLeave.apply(this, arguments) : true;
                  };

                  return true;
                })();
                """
            );

            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                tutorialHtmlPath,
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                return SameFixtureRenavigateVerificationResult.Failed(
                    $"TutorialCallbackGrid 再 navigate に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitAsync(
                leaveResolved.Task,
                TimeSpan.FromSeconds(10),
                "再 navigate 時の leave 完了を待てませんでした。"
            );
            await WaitAsync(
                secondFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "再 navigate 後の focus 完了を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 1
                  && document.getElementById('title84')?.textContent === 'Gamma.mkv'
                  && document.getElementById('title42') == null
                """,
                TimeSpan.FromSeconds(5),
                "再 navigate 後の TutorialCallbackGrid 描画完了を待てませんでした。"
            );

            int secondItemCount = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view .thum_base').length"
            );
            string secondTitleText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('title84') ? document.getElementById('title84').textContent : ''"
            );
            bool legacyNodeGone = await ReadJsonBoolAsync(
                webView,
                "document.getElementById('title42') == null"
            );

            return SameFixtureRenavigateVerificationResult.Succeeded(
                updateRequestCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                [.. lifecycleEvents],
                secondItemCount,
                secondTitleText,
                legacyNodeGone
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<RuntimeBridgeLifecycleVerificationResult> VerifyHandleSkinLeaveAsync(
        string tempRootPath
    )
    {
        string skinRootPath = Path.Combine(tempRootPath, "skin");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(skinRootPath);
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            throw new AssertionException($"compat script が見つかりません: {compatScriptPath}");
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        WhiteBrowserSkinRuntimeBridge runtimeBridge = new();
        Window hostWindow = new()
        {
            Width = 180,
            Height = 120,
            Left = 16,
            Top = 16,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult attachResult =
                await runtimeBridge.TryEnsureAttachedAsync(
                    webView,
                    "RuntimeBridgeLifecycleTest",
                    userDataFolderPath,
                    skinRootPath,
                    thumbRootPath
                );
            if (!attachResult.Succeeded)
            {
                return attachResult.RuntimeAvailable
                    ? RuntimeBridgeLifecycleVerificationResult.Failed(
                        $"WebView2 初期化に失敗しました: {attachResult.ErrorType} {attachResult.ErrorMessage}"
                    )
                    : RuntimeBridgeLifecycleVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため lifecycle 統合確認をスキップします: {attachResult.ErrorMessage}"
                    );
            }

            runtimeBridge.WebMessageReceived += (_, e) =>
            {
                if (!string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
                {
                    return;
                }

                _ = runtimeBridge.ResolveRequestAsync(
                    e.MessageId,
                    new
                    {
                        movieId = 90,
                        id = 90,
                        focused = true,
                        focusedMovieId = 90,
                        selected = true,
                    }
                );
            };

            TaskCompletionSource<bool> navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                }
                else
                {
                    navigationCompleted.TrySetException(
                        new InvalidOperationException(
                            $"Navigation failed: {args.WebErrorStatus}"
                        )
                    );
                }
            };

            webView.NavigateToString(BuildLifecycleHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                throw new AssertionException(
                    "runtime bridge lifecycle harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbDone = false;
                  window.__wbError = "";
                  window.__wbSequence = [];
                  wb.focusThum(90).then(function () {
                    window.__wbSequence = [];
                    window.__wbDone = true;
                  }).catch(function (error) {
                    window.__wbError = String(error && error.message ? error.message : error);
                    window.__wbDone = true;
                  });
                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbDone");

            string error = await ReadJsonStringAsync(webView, "window.__wbError || \"\"");
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            await runtimeBridge.HandleSkinLeaveAsync();
            await runtimeBridge.HandleSkinLeaveAsync();

            string lifecycleJson = await ReadJsonStringAsync(
                webView,
                "JSON.stringify(window.__wbSequence)"
            );
            return RuntimeBridgeLifecycleVerificationResult.Succeeded(
                DeserializeStringArray(lifecycleJson)
            );
        }
        finally
        {
            runtimeBridge.Dispose();
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(
            () =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                );
                _ = ExecuteAsync();
                Dispatcher.Run();

                async Task ExecuteAsync()
                {
                    try
                    {
                        T result = await action();
                        completion.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
                            DispatcherPriority.Background
                        );
                    }
                }
            }
        );
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void CreateSamplePng(string filePath, int width, int height)
    {
        using Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.DarkSeaGreen);
        graphics.FillRectangle(Brushes.Crimson, 0, 0, width / 2, height / 2);
        bitmap.Save(filePath, ImageFormat.Png);
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static async Task WaitForWebFlagAsync(WebView2 webView, string flagName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            string resultJson = await webView.ExecuteScriptAsync($"Boolean(window.{flagName})");
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"WebView2 側の待機フラグ '{flagName}' が立ちませんでした。");
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
    }

    private static async Task WaitForWebConditionAsync(
        WebView2 webView,
        string conditionScript,
        TimeSpan timeout,
        string timeoutMessage
    )
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            string resultJson = await webView.ExecuteScriptAsync($"Boolean({conditionScript})");
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static async Task<string> ReadJsonStringAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<int> ReadJsonIntAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<int>(resultJson);
    }

    private static async Task<bool> ReadJsonBoolAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<bool>(resultJson);
    }

    private static async Task<TutorialCallbackGridDomSnapshot> ReadTutorialCallbackGridSnapshotAsync(
        WebView2 webView,
        int movieId
    )
    {
        string movieKey = movieId.ToString();
        string json = await ReadJsonStringAsync(
            webView,
            $$"""
            JSON.stringify({
              itemCount: document.querySelectorAll('#view .thum_base').length,
              titleText: document.getElementById('title{{movieKey}}') ? document.getElementById('title{{movieKey}}').textContent : '',
              focusedImageClass: document.getElementById('img{{movieKey}}') ? document.getElementById('img{{movieKey}}').className : '',
              selectedThumbClass: document.getElementById('thum{{movieKey}}') ? document.getElementById('thum{{movieKey}}').className : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new TutorialCallbackGridDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("titleText").GetString() ?? "",
            document.RootElement.GetProperty("focusedImageClass").GetString() ?? "",
            document.RootElement.GetProperty("selectedThumbClass").GetString() ?? ""
        );
    }

    private static async Task<WhiteBrowserDefaultListDomSnapshot> ReadWhiteBrowserDefaultListSnapshotAsync(
        WebView2 webView
    )
    {
        string json = await ReadJsonStringAsync(
            webView,
            """
            JSON.stringify({
              itemCount: document.querySelectorAll('#view tr').length,
              titleText: document.getElementById('title77') ? document.getElementById('title77').textContent : '',
              sizeText: document.querySelector('#thum77 td:nth-child(3) h4') ? document.querySelector('#thum77 td:nth-child(3) h4').textContent : '',
              lengthText: document.querySelector('#thum77 td:nth-child(4) h4') ? document.querySelector('#thum77 td:nth-child(4) h4').textContent : '',
              scrollElementId: document.getElementById('scroll') ? 'scroll' : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new WhiteBrowserDefaultListDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("titleText").GetString() ?? "",
            document.RootElement.GetProperty("sizeText").GetString() ?? "",
            document.RootElement.GetProperty("lengthText").GetString() ?? "",
            document.RootElement.GetProperty("scrollElementId").GetString() ?? ""
        );
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureDomSnapshot> ReadSimpleWhiteBrowserDefaultFixtureSnapshotAsync(
        WebView2 webView
    )
    {
        string json = await ReadJsonStringAsync(
            webView,
            """
            JSON.stringify({
              itemCount: document.getElementById('view') ? document.getElementById('view').children.length : 0,
              titleText: document.getElementById('title77') ? document.getElementById('title77').textContent : '',
              selectedClass: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
              scoreText: document.getElementById('score77') ? document.getElementById('score77').textContent : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new SimpleWhiteBrowserDefaultFixtureDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("titleText").GetString() ?? "",
            document.RootElement.GetProperty("selectedClass").GetString() ?? "",
            document.RootElement.GetProperty("scoreText").GetString() ?? ""
        );
    }

    private static async Task<BuildOutputSkinThumbnailVerificationResult> VerifyBuildOutputSkinThumbnailAsync(
        string tempRootPath,
        string skinFolderName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    updateRequestCount += 1;
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, skinFolderName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbnailVerificationResult.Failed(
                        $"{skinFolderName} 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbnailVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build skin 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            try
            {
                await WaitForWebConditionAsync(
                    webView,
                    """
                    document.getElementById('view')
                      && document.getElementById('img77')
                      && (document.getElementById('img77').getAttribute('src') || '') !== ''
                    """,
                    TimeSpan.FromSeconds(15),
                    $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
                );
            }
            catch (TimeoutException)
            {
                string debugJson = await ReadJsonStringAsync(
                    webView,
                    """
                    JSON.stringify({
                      hasView: !!document.getElementById('view'),
                      imageCount: document.querySelectorAll('img[id^="img"]').length,
                      titleCount: document.querySelectorAll('[id^="title"]').length,
                      image77Src: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : '',
                      title77Text: document.getElementById('title77') ? (document.getElementById('title77').textContent || '') : '',
                      compatErrors: Array.isArray(window.__immCompatErrors) ? window.__immCompatErrors.slice(-5) : [],
                      bodyHead: (document.body ? (document.body.innerHTML || '').slice(0, 800) : '')
                    })
                    """
                );
                throw new AssertionException(
                    $"{skinFolderName} の初回サムネ表示完了を待てませんでした。 debug={debugJson}"
                );
            }

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  thumbnailCount: document.getElementById('view') ? document.getElementById('view').querySelectorAll('img[id^="img"]').length : 0,
                  image77Src: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : '',
                  title77Text: document.getElementById('title77') ? (document.getElementById('title77').textContent || '') : ''
                })
                """
            );
            using JsonDocument beforeDocument = JsonDocument.Parse(snapshotJson);

            await hostControl.HandleSkinLeaveAsync();

            return BuildOutputSkinThumbnailVerificationResult.Succeeded(
                updateRequestCount,
                beforeDocument.RootElement.GetProperty("thumbnailCount").GetInt32(),
                beforeDocument.RootElement.GetProperty("image77Src").GetString() ?? "",
                beforeDocument.RootElement.GetProperty("title77Text").GetString() ?? "",
                0
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinThumbUpdateVerificationResult> VerifyBuildOutputSkinThumbUpdateAsync(
        string tempRootPath,
        string skinFolderName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 38,
            Top = 38,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, skinFolderName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbUpdateVerificationResult.Failed(
                        $"{skinFolderName} thumb 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbUpdateVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build thumb 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
            );

            string beforeThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl =
                        "data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==",
                    thum =
                        "data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==",
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === 'data:image/gif;base64,R0lGODlhAQABAPAAAP//AAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw=='
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の差分サムネ更新完了を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return BuildOutputSkinThumbUpdateVerificationResult.Succeeded(
                beforeThumbSrc,
                afterThumbSrc
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinThumbChangeSkinVerificationResult> VerifyBuildOutputSkinThumbChangeSkinAsync(
        string tempRootPath,
        string skinFolderName,
        string nextSkinFolderName
    )
    {
        const string UpdatedThumbUrl = "about:blank#updated-build-thumb-77";
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName, nextSkinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object
                        && e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} thumb changeSkin 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build thumb changeSkin 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl = UpdatedThumbUrl,
                    thum = UpdatedThumbUrl,
                }
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の changeSkin成功 前差分サムネ更新完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                $$"""(async () => { await wb.changeSkin("{{nextSkinFolderName}}"); return true; })();"""
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                  && document.getElementById('img77').getAttribute('src') !== '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の changeSkin 後サムネ表示完了を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return BuildOutputSkinThumbChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                afterThumbSrc
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の build thumb changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<BuildOutputSkinThumbChangeSkinVerificationResult> VerifyBuildOutputSkinThumbMissingChangeSkinAsync(
        string tempRootPath,
        string skinFolderName
    )
    {
        const string UpdatedThumbUrl = "about:blank#updated-build-thumb-77";
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} thumb changeSkin失敗 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build thumb changeSkin失敗 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl = UpdatedThumbUrl,
                    thum = UpdatedThumbUrl,
                }
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の changeSkin失敗 前差分サムネ更新完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immBuildThumbMissingChangeSkinResult = "";
                  (async () => {
                    const result = await wb.changeSkin("MissingSkin");
                    window.__immBuildThumbMissingChangeSkinResult = String(result);
                  })();
                  return true;
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                "window.__immBuildThumbMissingChangeSkinResult === 'false'",
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の changeSkin失敗 result 取得完了を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return BuildOutputSkinThumbChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                afterThumbSrc
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinModifyTagsVerificationResult> VerifyBuildOutputSkinModifyTagsAsync(
        string tempRootPath,
        string skinFolderName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 38,
            Top = 38,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, skinFolderName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinModifyTagsVerificationResult.Failed(
                        $"{skinFolderName} modifyTags 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinModifyTagsVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build modifyTags 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回 tag 表示完了を待てませんでした。"
            );

            string beforeTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, new[] { "idol", "fresh-tag" } },
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の tag 差分更新完了を待てませんでした。"
            );

            string afterTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            return BuildOutputSkinModifyTagsVerificationResult.Succeeded(
                beforeTagText,
                afterTagText
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinModifyTagsChangeSkinVerificationResult> VerifyBuildOutputSkinModifyTagsMissingChangeSkinAsync(
        string tempRootPath,
        string skinFolderName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinModifyTagsChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} modifyTags changeSkin失敗 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinModifyTagsChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build modifyTags changeSkin失敗 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回 tag 表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, new[] { "idol", "fresh-tag" } },
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の changeSkin失敗 前 tag 差分更新完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immBuildModifyTagsMissingChangeSkinResult = { done: false, result: "" };
                  (async () => {
                    const result = await wb.changeSkin("MissingSkin");
                    window.__immBuildModifyTagsMissingChangeSkinResult = { done: true, result: String(result) };
                  })();
                  return true;
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                """
                window.__immBuildModifyTagsMissingChangeSkinResult
                  && window.__immBuildModifyTagsMissingChangeSkinResult.done === true
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の changeSkin失敗 result 取得完了を待てませんでした。"
            );

            string changeResult = await ReadJsonStringAsync(
                webView,
                "window.__immBuildModifyTagsMissingChangeSkinResult ? (window.__immBuildModifyTagsMissingChangeSkinResult.result || '') : ''"
            );

            string afterTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            return BuildOutputSkinModifyTagsChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                changeResult,
                afterTagText
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinModifyTagsChangeSkinVerificationResult> VerifyBuildOutputSkinModifyTagsTerminalMissingChangeSkinAsync(
        string tempRootPath,
        string skinFolderName,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinModifyTagsChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} modifyTags terminal changeSkin失敗 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinModifyTagsChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build modifyTags terminal changeSkin失敗 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回 tag 表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, new[] { "idol", "fresh-tag" } },
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal changeSkin失敗 前 tag 差分更新完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immBuildModifyTagsTerminalMissingChangeSkinResult = { done: false, result: "" };
                  (async () => {
                    const result = await wb.changeSkin("MissingSkin");
                    window.__immBuildModifyTagsTerminalMissingChangeSkinResult = { done: true, result: String(result) };
                  })();
                  return true;
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                """
                window.__immBuildModifyTagsTerminalMissingChangeSkinResult
                  && window.__immBuildModifyTagsTerminalMissingChangeSkinResult.done === true
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal changeSkin失敗 result 取得完了を待てませんでした。"
            );

            string changeResult = await ReadJsonStringAsync(
                webView,
                "window.__immBuildModifyTagsTerminalMissingChangeSkinResult ? (window.__immBuildModifyTagsTerminalMissingChangeSkinResult.result || '') : ''"
            );

            string afterTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            return BuildOutputSkinModifyTagsChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                changeResult,
                afterTagText
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinModifyTagsChangeSkinVerificationResult> VerifyBuildOutputSkinModifyTagsTerminalChangeSkinAsync(
        string tempRootPath,
        string skinFolderName,
        string callbackName,
        string nextSkinFolderName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(
            skinFolderName,
            nextSkinFolderName
        );
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object
                        && e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinModifyTagsChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} modifyTags terminal changeSkin 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinModifyTagsChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build modifyTags terminal changeSkin 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回 tag 表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, new[] { "idol", "fresh-tag" } },
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal changeSkin 前 tag 差分更新完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });

            await webView.ExecuteScriptAsync(
                $$"""(async () => { await wb.changeSkin("{{nextSkinFolderName}}"); return true; })();"""
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の terminal changeSkin 後 tag 表示完了を待てませんでした。"
            );

            string afterTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            return BuildOutputSkinModifyTagsChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                "true",
                afterTagText
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の build terminal changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<BuildOutputSkinModifyTagsChangeSkinVerificationResult> VerifyBuildOutputSkinModifyTagsChangeSkinAsync(
        string tempRootPath,
        string skinFolderName,
        string nextSkinFolderName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName, nextSkinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object
                        && e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinModifyTagsChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} modifyTags changeSkin 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinModifyTagsChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build modifyTags changeSkin 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回 tag 表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, new[] { "idol", "fresh-tag" } },
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の changeSkin成功 前 tag 差分更新完了を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                $$"""
                (() => {
                  window.__immBuildModifyTagsChangeSkinResult = "";
                  (async () => {
                    const result = await wb.changeSkin("{{nextSkinFolderName}}");
                    window.__immBuildModifyTagsChangeSkinResult = String(result);
                  })();
                  return true;
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') < 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の changeSkin 後 tag 表示完了を待てませんでした。"
            );

            string afterTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            return BuildOutputSkinModifyTagsChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                "true",
                afterTagText
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の build changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<BuildOutputSkinModifyTagsVerificationResult> VerifyBuildOutputSkinModifyTagsTerminalRerenderAsync(
        string tempRootPath,
        string skinFolderName,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, skinFolderName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinModifyTagsVerificationResult.Failed(
                        $"{skinFolderName} modifyTags terminal rerender 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinModifyTagsVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build modifyTags terminal rerender 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回 tag 表示完了を待てませんでした。"
            );

            string beforeTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, new[] { "idol", "fresh-tag" } },
                }
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal 前 tag 差分更新完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            updateResolved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            WhiteBrowserSkinHostOperationResult renavigateResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!renavigateResult.Succeeded)
            {
                return BuildOutputSkinModifyTagsVerificationResult.Failed(
                    $"{skinFolderName} modifyTags terminal 再 navigate に失敗しました: {renavigateResult.ErrorType} {renavigateResult.ErrorMessage}"
                );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal 後 update 要求を待てませんでした。"
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('tag77')
                  && (document.getElementById('tag77').textContent || '').indexOf('series-a') >= 0
                  && (document.getElementById('tag77').textContent || '').indexOf('fresh-tag') < 0
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal 後初期 tag 表示復帰を待てませんでした。"
            );

            string afterTagText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('tag77') ? (document.getElementById('tag77').textContent || '') : ''"
            );

            return BuildOutputSkinModifyTagsVerificationResult.Succeeded(beforeTagText, afterTagText);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinThumbUpdateVerificationResult> VerifyBuildOutputSkinThumbTerminalRerenderAsync(
        string tempRootPath,
        string skinFolderName,
        string callbackName
    )
    {
        const string UpdatedThumbUrl = "about:blank#updated-build-thumb-77";
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, skinFolderName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbUpdateVerificationResult.Failed(
                        $"{skinFolderName} thumb terminal rerender 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbUpdateVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build thumb terminal rerender 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
            );

            string beforeThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl = UpdatedThumbUrl,
                    thum = UpdatedThumbUrl,
                }
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal rerender 前差分サムネ更新完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });

            WhiteBrowserSkinHostOperationResult rerenderResult = await hostControl.TryNavigateAsync(
                skinFolderName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, skinFolderName),
                thumbRootPath
            );
            if (!rerenderResult.Succeeded)
            {
                throw new AssertionException(
                    $"{skinFolderName} の terminal rerender 再読込に失敗しました: {rerenderResult.ErrorType} {rerenderResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                  && document.getElementById('img77').getAttribute('src') !== '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の terminal rerender 後サムネ表示完了を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return BuildOutputSkinThumbUpdateVerificationResult.Succeeded(beforeThumbSrc, afterThumbSrc);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<BuildOutputSkinThumbChangeSkinVerificationResult> VerifyBuildOutputSkinThumbTerminalChangeSkinAsync(
        string tempRootPath,
        string skinFolderName,
        string callbackName,
        string nextSkinFolderName
    )
    {
        const string UpdatedThumbUrl = "about:blank#updated-build-thumb-77";
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName, nextSkinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object
                        && e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} thumb terminal changeSkin 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build thumb terminal changeSkin 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl = UpdatedThumbUrl,
                    thum = UpdatedThumbUrl,
                }
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal changeSkin 前差分サムネ更新完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });

            await webView.ExecuteScriptAsync(
                $$"""(async () => { await wb.changeSkin("{{nextSkinFolderName}}"); return true; })();"""
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                  && document.getElementById('img77').getAttribute('src') !== '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の terminal changeSkin 後サムネ表示完了を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return BuildOutputSkinThumbChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                afterThumbSrc
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の build thumb terminal changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<BuildOutputSkinThumbChangeSkinVerificationResult> VerifyBuildOutputSkinThumbTerminalMissingChangeSkinAsync(
        string tempRootPath,
        string skinFolderName,
        string callbackName
    )
    {
        const string UpdatedThumbUrl = "about:blank#updated-build-thumb-77";
        string skinRootPath = CreateBuildOutputSkinRootWithCompat(skinFolderName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = skinFolderName;

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 40,
            Top = 40,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                case "find":
                case "sort":
                case "addWhere":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    updateResolved.TrySetResult(true);
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName.TrimStart('#'));
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? BuildOutputSkinThumbChangeSkinVerificationResult.Failed(
                        $"{skinFolderName} thumb terminal changeSkin失敗 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : BuildOutputSkinThumbChangeSkinVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {skinFolderName} build thumb terminal changeSkin失敗 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('img77')
                  && (document.getElementById('img77').getAttribute('src') || '') !== ''
                """,
                TimeSpan.FromSeconds(15),
                $"{skinFolderName} の初回サムネ表示完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onUpdateThum",
                new
                {
                    movieId = 77,
                    id = 77,
                    recordKey = "db-main:77",
                    thumbUrl = UpdatedThumbUrl,
                    thum = UpdatedThumbUrl,
                }
            );

            await WaitForWebConditionAsync(
                webView,
                $$"""
                document.getElementById('img77')
                  && document.getElementById('img77').getAttribute('src') === '{{UpdatedThumbUrl}}'
                """,
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal changeSkin失敗 前差分サムネ更新完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immBuildThumbTerminalMissingChangeSkinResult = "";
                  (async () => {
                    const result = await wb.changeSkin("MissingSkin");
                    window.__immBuildThumbTerminalMissingChangeSkinResult = String(result);
                  })();
                  return true;
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                "window.__immBuildThumbTerminalMissingChangeSkinResult === 'false'",
                TimeSpan.FromSeconds(10),
                $"{skinFolderName} の terminal changeSkin失敗 result 取得完了を待てませんでした。"
            );

            string afterThumbSrc = await ReadJsonStringAsync(
                webView,
                "document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''"
            );

            return BuildOutputSkinThumbChangeSkinVerificationResult.Succeeded(
                currentSkinName,
                afterThumbSrc
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<string[]> VerifyTagInputRelationExtensionUpdatedAsync(string tempRootPath)
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 24,
            Top = 24,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation extension 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            try
            {
                await WaitForWebConditionAsync(
                    webView,
                    "document.querySelectorAll('#Selection li').length === 3",
                    TimeSpan.FromSeconds(5),
                    "TagInputRelation の候補タグ生成を待てませんでした。"
                );
            }
            catch (TimeoutException)
            {
                string debugJson = await ReadJsonStringAsync(
                    webView,
                    """
                    JSON.stringify({
                      selectionHtml: document.getElementById('Selection') ? document.getElementById('Selection').innerHTML : '',
                      selectionCount: document.querySelectorAll('#Selection li').length,
                      compatErrors: Array.isArray(window.__immCompatErrors) ? window.__immCompatErrors.slice(-5) : [],
                      hasCallback: typeof wb.onExtensionUpdated === 'function'
                    })
                    """
                );
                throw new AssertionException(
                    $"TagInputRelation の候補タグ生成を待てませんでした。 debug={debugJson}"
                );
            }

            string tagsJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify(Array.from(document.querySelectorAll('#Selection li a')).map(x => (x.textContent || '').trim()))
                """
            );

            return DeserializeStringArray(tagsJson);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationSaveVerificationResult> VerifyTagInputRelationIncludeAndSaveAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 25,
            Top = 25,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        List<string> addTagRequests = [];
        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                        }
                    );
                    break;
                case "addTag":
                    if (e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("tag", out JsonElement tagElement))
                    {
                        addTagRequests.Add(tagElement.GetString() ?? "");
                    }

                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Save 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length >= 1",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonInclude();");
            string inputAfterInclude = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            await webView.ExecuteScriptAsync(
                "document.getElementById('input').value += ', idol'; ButtonSave();"
            );

            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === ''",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Save 後クリアを待てませんでした。"
            );

            string inputAfterSave = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationSaveVerificationResult(
                inputAfterInclude,
                [.. addTagRequests],
                inputAfterSave
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationGetVerificationResult> VerifyTagInputRelationGetAndSetAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 27,
            Top = 27,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        List<int> relationLimits = [];
        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int limit = 0;
                    if (e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement))
                    {
                        limit = limitElement.GetInt32();
                    }

                    relationLimits.Add(limit);
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        limit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Get 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );

            string selectionAfterGetJson = await ReadJsonStringAsync(
                webView,
                "JSON.stringify(Array.from(document.querySelectorAll('#Selection li a')).map(x => (x.textContent || '').trim()))"
            );

            await webView.ExecuteScriptAsync("ButtonSet('fresh');");

            string inputAfterSet = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );
            string selectionAfterSetJson = await ReadJsonStringAsync(
                webView,
                "JSON.stringify(Array.from(document.querySelectorAll('#Selection li a')).map(x => (x.textContent || '').trim()))"
            );

            return new TagInputRelationGetVerificationResult(
                [.. relationLimits],
                DeserializeStringArray(selectionAfterGetJson),
                inputAfterSet,
                DeserializeStringArray(selectionAfterSetJson)
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationRerenderVerificationResult> VerifyTagInputRelationSaveAndRerenderAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 29,
            Top = 29,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                        }
                    );
                    break;
                case "addTag":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation 再候補生成確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length >= 1",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                "document.getElementById('input').value = 'idol'; ButtonSave();"
            );
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === ''",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Save 後クリアを待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length >= 1",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Save 後候補再生成を待てませんでした。"
            );

            string candidateTextsJson = await webView.ExecuteScriptAsync(
                """
                Array.from(document.querySelectorAll('#Selection li'))
                  .map(function (li) { return (li.textContent || '').trim(); })
                  .filter(function (text) { return text.length > 0; })
                """
            );
            string inputAfterRerender = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationRerenderVerificationResult(
                JsonSerializer.Deserialize<string[]>(candidateTextsJson) ?? [],
                inputAfterRerender
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationRerenderVerificationResult> VerifyTagInputRelationGetAndRerenderAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 28,
            Top = 28,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Get rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補拡張を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後再候補生成を待てませんでした。"
            );

            string candidateTextsJson = await webView.ExecuteScriptAsync(
                """
                Array.from(document.querySelectorAll('#Selection li'))
                  .map(function (li) { return (li.textContent || '').trim(); })
                  .filter(function (text) { return text.length > 0; })
                """
            );
            string inputAfterRerender = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationRerenderVerificationResult(
                JsonSerializer.Deserialize<string[]>(candidateTextsJson) ?? [],
                inputAfterRerender
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationChangeSkinToUmlFindTreeEveAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation", "#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonSet('fresh');");
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === 'fresh'",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の入力反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#umlFindTreeEve');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && !document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                """,
                TimeSpan.FromSeconds(10),
                "TagInputRelation から umlFindTreeEve への changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationSaveChangeSkinToUmlFindTreeEveAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation", "#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "addTag":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Save後 changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonInclude();");
            await webView.ExecuteScriptAsync(
                "document.getElementById('input').value += ', idol'; ButtonSave();"
            );
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === ''",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Save 後クリアを待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#umlFindTreeEve');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && !document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                """,
                TimeSpan.FromSeconds(10),
                "TagInputRelation の Save 後から umlFindTreeEve への changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の Save後 changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationSaveSkinLeaveChangeSkinToUmlFindTreeEveAsync(
        string tempRootPath
    )
    {
        return await VerifyTagInputRelationSaveTerminalChangeSkinToUmlFindTreeEveAsync(
            tempRootPath,
            "onSkinLeave"
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationSaveTerminalChangeSkinToUmlFindTreeEveAsync(
        string tempRootPath,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation", "#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "addTag":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Save後 {callbackName} changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonInclude();");
            await webView.ExecuteScriptAsync(
                "document.getElementById('input').value += ', idol'; ButtonSave();"
            );
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === ''",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Save 後クリアを待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#umlFindTreeEve');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && !document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                """,
                TimeSpan.FromSeconds(10),
                $"TagInputRelation の Save と {callbackName} 後から umlFindTreeEve への changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の Save後 {callbackName} changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationGetTerminalChangeSkinToUmlFindTreeEveAsync(
        string tempRootPath,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation", "#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Get後 {callbackName} changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#umlFindTreeEve');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && !document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                """,
                TimeSpan.FromSeconds(10),
                $"TagInputRelation の Get後 {callbackName} から umlFindTreeEve への changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の Get後 changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationGetTerminalMissingThenChangeSkinToUmlFindTreeEveAsync(
        string tempRootPath,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation", "#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Get後 {callbackName} missing->changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  window.__immTagInputMissingThenResult1 = await wb.changeSkin('MissingSkin');
                  window.__immTagInputMissingThenResult2 = await wb.changeSkin('#umlFindTreeEve');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                window.__immTagInputMissingThenResult1 === false
                  && window.__immTagInputMissingThenResult2 === true
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && !document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                """,
                TimeSpan.FromSeconds(10),
                $"TagInputRelation の Get後 {callbackName} missing->umlFindTreeEve changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の Get後 missing->changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<TagInputRelationRerenderVerificationResult> VerifyTagInputRelationLeaveAndRerenderAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 32,
            Top = 32,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "addTag":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation clear rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonSet('fresh');");
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === 'fresh'",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の入力反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            WhiteBrowserSkinHostOperationResult renavigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!renavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"TagInputRelation の再 navigate に失敗しました: {renavigateResult.ErrorType} {renavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('Selection') && document.getElementById('input')",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の再入後 DOM 準備完了を待てませんでした。"
            );
            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の再入後候補再生成を待てませんでした。"
            );

            string candidateTextsJson = await webView.ExecuteScriptAsync(
                """
                Array.from(document.querySelectorAll('#Selection li'))
                  .map(function (li) { return (li.textContent || '').trim(); })
                  .filter(function (text) { return text.length > 0; })
                """
            );
            string inputAfterRerender = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationRerenderVerificationResult(
                JsonSerializer.Deserialize<string[]>(candidateTextsJson) ?? [],
                inputAfterRerender
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationRerenderVerificationResult> VerifyTagInputRelationTerminalRerenderAsync(
        string tempRootPath,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 32,
            Top = 32,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation terminal rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            WhiteBrowserSkinHostOperationResult renavigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!renavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"TagInputRelation の terminal 再 navigate に失敗しました: {renavigateResult.ErrorType} {renavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('Selection') && document.getElementById('input')",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の再入後 DOM 準備完了を待てませんでした。"
            );
            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の terminal 再入後候補再生成を待てませんでした。"
            );

            string candidateTextsJson = await webView.ExecuteScriptAsync(
                """
                Array.from(document.querySelectorAll('#Selection li'))
                  .map(function (li) { return (li.textContent || '').trim(); })
                  .filter(function (text) { return text.length > 0; })
                """
            );
            string inputAfterRerender = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationRerenderVerificationResult(
                JsonSerializer.Deserialize<string[]>(candidateTextsJson) ?? [],
                inputAfterRerender
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationRerenderVerificationResult> VerifyTagInputRelationGetAndTerminalRerenderAsync(
        string tempRootPath,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 34,
            Top = 34,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation get terminal rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補拡張を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            WhiteBrowserSkinHostOperationResult renavigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!renavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"TagInputRelation の get terminal 再 navigate に失敗しました: {renavigateResult.ErrorType} {renavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('Selection') && document.getElementById('input')",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の get terminal 再入後 DOM 準備完了を待てませんでした。"
            );
            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の get terminal 再入後候補再生成を待てませんでした。"
            );

            string candidateTextsJson = await webView.ExecuteScriptAsync(
                """
                Array.from(document.querySelectorAll('#Selection li'))
                  .map(function (li) { return (li.textContent || '').trim(); })
                  .filter(function (text) { return text.length > 0; })
                """
            );
            string inputAfterRerender = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationRerenderVerificationResult(
                JsonSerializer.Deserialize<string[]>(candidateTextsJson) ?? [],
                inputAfterRerender
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<TagInputRelationRerenderVerificationResult> VerifyTagInputRelationSaveAndTerminalRerenderAsync(
        string tempRootPath,
        string callbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 33,
            Top = 33,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "addTag":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation Save後 terminal rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonInclude();");
            await webView.ExecuteScriptAsync(
                "document.getElementById('input').value += ', idol'; ButtonSave();"
            );
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === ''",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Save 後クリアを待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(callbackName, new { });
            WhiteBrowserSkinHostOperationResult renavigateResult = await hostControl.TryNavigateAsync(
                "#TagInputRelation",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#TagInputRelation"),
                thumbRootPath
            );
            if (!renavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"TagInputRelation の terminal 再 navigate に失敗しました: {renavigateResult.ErrorType} {renavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('Selection') && document.getElementById('input')",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の terminal 後 DOM 準備完了を待てませんでした。"
            );
            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の terminal 後候補再生成を待てませんでした。"
            );

            string candidateTextsJson = await webView.ExecuteScriptAsync(
                """
                Array.from(document.querySelectorAll('#Selection li'))
                  .map(function (li) { return (li.textContent || '').trim(); })
                  .filter(function (text) { return text.length > 0; })
                """
            );
            string inputAfterRerender = await ReadJsonStringAsync(
                webView,
                "document.getElementById('input') ? (document.getElementById('input').value || '') : ''"
            );

            return new TagInputRelationRerenderVerificationResult(
                JsonSerializer.Deserialize<string[]>(candidateTextsJson) ?? [],
                inputAfterRerender
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 260,
            Left = 34,
            Top = 34,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );
            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );
            await webView.ExecuteScriptAsync("ButtonSet('sample');");
            await WaitForWebConditionAsync(
                webView,
                "document.getElementById('input') && document.getElementById('input').value === 'sample'",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の入力反映を待てませんでした。"
            );
            await webView.ExecuteScriptAsync(
                """
                window.__immTagInputMissingSkinResult = null;
                (async () => {
                  window.__immTagInputMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTagInputMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の changeSkin 失敗結果を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationGetMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        return await VerifyTagInputRelationDirtyMissingChangeSkinAsync(
            tempRootPath,
            """
            window.__immTagInputDirtyMissingSkinResult = await wb.changeSkin('MissingSkin');
            """,
            string.Empty,
            4
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationBareTerminalMissingChangeSkinAsync(
        string tempRootPath,
        string callbackName
    )
    {
        return await VerifyTagInputRelationDirtyMissingChangeSkinAsync(
            tempRootPath,
            $"""
            wb.{callbackName}();
            window.__immTagInputDirtyMissingSkinResult = await wb.changeSkin('MissingSkin');
            """,
            string.Empty,
            4
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationGetTerminalMissingChangeSkinAsync(
        string tempRootPath,
        string callbackName
    )
    {
        return await VerifyTagInputRelationDirtyMissingChangeSkinAsync(
            tempRootPath,
            $"""
            ButtonGet();
            wb.{callbackName}();
            window.__immTagInputDirtyMissingSkinResult = await wb.changeSkin('MissingSkin');
            """,
            string.Empty,
            0
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationSaveMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        return await VerifyTagInputRelationDirtyMissingChangeSkinAsync(
            tempRootPath,
            """
            ButtonInclude();
            document.getElementById('input').value += ', idol';
            ButtonSave();
            window.__immTagInputDirtyMissingSkinResult = await wb.changeSkin('MissingSkin');
            """,
            string.Empty,
            4
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationSaveTerminalMissingChangeSkinAsync(
        string tempRootPath,
        string callbackName
    )
    {
        return await VerifyTagInputRelationDirtyMissingChangeSkinAsync(
            tempRootPath,
            $"""
            ButtonInclude();
            document.getElementById('input').value += ', idol';
            ButtonSave();
            wb.{callbackName}();
            window.__immTagInputDirtyMissingSkinResult = await wb.changeSkin('MissingSkin');
            """,
            string.Empty,
            4
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyTagInputRelationDirtyMissingChangeSkinAsync(
        string tempRootPath,
        string preChangeSkinScript,
        string expectedInputValue,
        int expectedSelectionCount
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#TagInputRelation";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 35,
            Top = 35,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 77,
                            movieId = 77,
                            title = "Beta",
                            tags = new[] { "series-a", "sample" },
                        }
                    );
                    break;
                case "getRelation":
                    int relationLimit =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("limit", out JsonElement limitElement)
                            ? limitElement.GetInt32()
                            : 0;
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        relationLimit >= 30
                            ? new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample", "fresh" } },
                            }
                            : new object[]
                            {
                                new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                                new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                            }
                    );
                    break;
                case "addTag":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            found = true,
                            changed = true,
                            hasTag = true,
                            movieId = 77,
                            id = 77,
                            tag = e.Payload.TryGetProperty("tag", out JsonElement requestedTag)
                                ? requestedTag.GetString() ?? ""
                                : "",
                            tags = new[] { "series-a", "sample", "idol" },
                        }
                    );
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため TagInputRelation dirty changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"TagInputRelation 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await hostControl.DispatchCallbackAsync("onExtensionUpdated", new { });
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 3",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の初期候補タグ生成を待てませんでした。"
            );

            await webView.ExecuteScriptAsync("ButtonGet();");
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#Selection li').length === 4",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の Get 後候補追加を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                "window.__immTagInputDirtyMissingSkinResult = null;\n"
                    + "(async () => {\n"
                    + preChangeSkinScript
                    + "\n})();"
            );

            await WaitForWebConditionAsync(
                webView,
                $"document.getElementById('input') && document.getElementById('input').value === '{expectedInputValue}'",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の dirty state 入力反映を待てませんでした。"
            );

            await WaitForWebConditionAsync(
                webView,
                "window.__immTagInputDirtyMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "TagInputRelation の dirty changeSkin 失敗結果を待てませんでした。"
            );

            CrossSkinDomSnapshot result = await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
            Assert.Multiple(() =>
            {
                Assert.That(result.CurrentSkin, Is.EqualTo("#TagInputRelation"));
                Assert.That(result.HasInput, Is.True);
                Assert.That(result.InputValue, Is.EqualTo(expectedInputValue));
                Assert.That(result.SelectionCount, Is.EqualTo(expectedSelectionCount));
            });
            return result;
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveSkinEnterAsync(string tempRootPath)
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 26,
            Top = 26,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve extension 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            try
            {
                await WaitForWebConditionAsync(
                    webView,
                    """
                    document.getElementById('footer')
                      && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                      && document.getElementById('uml')
                      && (document.getElementById('uml').textContent || '').indexOf('Folders') >= 0
                      && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                    """,
                    TimeSpan.FromSeconds(10),
                    "umlFindTreeEve の tree/footer 生成完了を待てませんでした。"
                );
            }
            catch (TimeoutException)
            {
                string debugJson = await ReadJsonStringAsync(
                    webView,
                    """
                    JSON.stringify({
                      readyState: document.readyState || '',
                      footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                      umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                      lineCount: document.querySelectorAll('#uml .line').length,
                      compatErrors: Array.isArray(window.__immCompatErrors) ? window.__immCompatErrors.slice(-5) : [],
                      hasOnSkinEnter: typeof wb.onSkinEnter === 'function',
                      hasGetTreeFunction: typeof wb.GetTreeObj === 'function',
                      hasTreeObj: typeof wb.GetTreeObj === 'function' && !!wb.GetTreeObj(),
                      hasFindTreeObj: typeof FindTreeObj_t === 'function',
                      bodyHead: document.body ? (document.body.innerHTML || '').slice(0, 600) : ''
                    })
                    """
                );
                throw new AssertionException(
                    $"umlFindTreeEve の tree/footer 生成完了を待てませんでした。 debug={debugJson}"
                );
            }

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveRegisteredChangeSkinToTagInputRelationAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve", "#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#umlFindTreeEve";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 33,
            Top = 33,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#TagInputRelation');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('Selection')
                  && document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                  && !document.getElementById('uml')
                  && !document.getElementById('footer')
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve から TagInputRelation への changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEvePathChangeSkinToTagInputRelationAsync(
        string tempRootPath
    )
    {
        return await VerifyUmlFindTreeEveDirtyChangeSkinToTagInputRelationAsync(
            tempRootPath,
            "onModifyPath",
            new object[] { 77, "F:", "\\fresh\\", "Beta", ".avi", "" },
            """
            document.getElementById('uml')
              && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
            """,
            "umlFindTreeEve の path refresh 反映を待てませんでした。"
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEvePathTerminalChangeSkinToTagInputRelationAsync(
        string tempRootPath,
        string terminalCallbackName
    )
    {
        return await VerifyUmlFindTreeEveDirtyTerminalChangeSkinToTagInputRelationAsync(
            tempRootPath,
            "onModifyPath",
            new object[] { 77, "F:", "\\fresh\\", "Beta", ".avi", "" },
            """
            document.getElementById('uml')
              && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
            """,
            "umlFindTreeEve の path refresh 反映を待てませんでした。",
            terminalCallbackName
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveTagChangeSkinToTagInputRelationAsync(
        string tempRootPath
    )
    {
        return await VerifyUmlFindTreeEveDirtyChangeSkinToTagInputRelationAsync(
            tempRootPath,
            "onModifyTags",
            new object[]
            {
                77,
                new[] { "series-a", "sample", "fresh-tag" },
            },
            """
            document.getElementById('uml')
              && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
            """,
            "umlFindTreeEve の tag refresh 反映を待てませんでした。"
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveTagTerminalChangeSkinToTagInputRelationAsync(
        string tempRootPath,
        string terminalCallbackName
    )
    {
        return await VerifyUmlFindTreeEveDirtyTerminalChangeSkinToTagInputRelationAsync(
            tempRootPath,
            "onModifyTags",
            new object[]
            {
                77,
                new[] { "series-a", "sample", "fresh-tag" },
            },
            """
            document.getElementById('uml')
              && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
            """,
            "umlFindTreeEve の tag refresh 反映を待てませんでした。",
            terminalCallbackName
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveRemoveChangeSkinToTagInputRelationAsync(
        string tempRootPath
    )
    {
        return await VerifyUmlFindTreeEveDirtyChangeSkinToTagInputRelationAsync(
            tempRootPath,
            "onRemoveFile",
            new object[] { 77 },
            """
            document.getElementById('uml')
              && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
            """,
            "umlFindTreeEve の remove refresh 反映を待てませんでした。"
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveRemoveTerminalChangeSkinToTagInputRelationAsync(
        string tempRootPath,
        string terminalCallbackName
    )
    {
        return await VerifyUmlFindTreeEveDirtyTerminalChangeSkinToTagInputRelationAsync(
            tempRootPath,
            "onRemoveFile",
            new object[] { 77 },
            """
            document.getElementById('uml')
              && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
            """,
            "umlFindTreeEve の remove refresh 反映を待てませんでした。",
            terminalCallbackName
        );
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveRegisteredTerminalChangeSkinToTagInputRelationAsync(
        string tempRootPath,
        string terminalCallbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve", "#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#umlFindTreeEve";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 33,
            Top = 33,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register terminal changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(terminalCallbackName, new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#TagInputRelation');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('Selection')
                  && document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                  && !document.getElementById('uml')
                  && !document.getElementById('footer')
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の register terminal changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の register terminal changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRegisteredTerminalMissingChangeSkinAsync(
        string tempRootPath,
        string terminalCallbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 48,
            Top = 48,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register terminal changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(terminalCallbackName, new { });
            await webView.ExecuteScriptAsync(
                """
                window.__immTreeRegisterTerminalMissingSkinResult = null;
                (async () => {
                  window.__immTreeRegisterTerminalMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeRegisterTerminalMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register terminal changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveClearAllChangeSkinToTagInputRelationAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve", "#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#umlFindTreeEve";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 41,
            Top = 41,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve onClearAll changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#TagInputRelation');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('Selection')
                  && document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                  && !document.getElementById('uml')
                  && !document.getElementById('footer')
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の onClearAll 後 changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveSkinLeaveChangeSkinToTagInputRelationAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve", "#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#umlFindTreeEve";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 43,
            Top = 43,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve onSkinLeave changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#TagInputRelation');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('Selection')
                  && document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                  && !document.getElementById('uml')
                  && !document.getElementById('footer')
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の onSkinLeave 後 changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveDirtyChangeSkinToTagInputRelationAsync(
        string tempRootPath,
        string callbackName,
        object[] callArgs,
        string refreshReadyExpression,
        string refreshTimeoutMessage
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve", "#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#umlFindTreeEve";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 37,
            Top = 37,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve dirty changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                callbackName,
                new
                {
                    __immCallArgs = callArgs,
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                refreshReadyExpression,
                TimeSpan.FromSeconds(5),
                refreshTimeoutMessage
            );

            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#TagInputRelation');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('Selection')
                  && document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                  && !document.getElementById('uml')
                  && !document.getElementById('footer')
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の dirty changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の dirty changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<CrossSkinDomSnapshot> VerifyUmlFindTreeEveDirtyTerminalChangeSkinToTagInputRelationAsync(
        string tempRootPath,
        string callbackName,
        object[] callArgs,
        string refreshReadyExpression,
        string refreshTimeoutMessage,
        string terminalCallbackName
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve", "#TagInputRelation");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string currentSkinName = "#umlFindTreeEve";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 37,
            Top = 37,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    string requestedSkinName =
                        e.Payload.ValueKind == JsonValueKind.Object &&
                        e.Payload.TryGetProperty("skinName", out JsonElement skinNameElement)
                            ? skinNameElement.GetString() ?? ""
                            : "";
                    _ = HandleChangeSkinAsync(requestedSkinName, e.MessageId);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinUpdatePayload());
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinFindInfo());
                    break;
                case "getFocusThum":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, 77);
                    break;
                case "getSelectThums":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, new[] { 77 });
                    break;
                case "getRelation":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new object[]
                        {
                            new { id = 42, title = "Alpha", tags = new[] { "idol", "live" } },
                            new { id = 91, title = "Beta Next", tags = new[] { "sample" } },
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, currentSkinName);
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                currentSkinName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, currentSkinName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve dirty terminal changeSkin 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                callbackName,
                new
                {
                    __immCallArgs = callArgs,
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                refreshReadyExpression,
                TimeSpan.FromSeconds(5),
                refreshTimeoutMessage
            );

            await hostControl.DispatchCallbackAsync(terminalCallbackName, new { });
            await webView.ExecuteScriptAsync(
                """
                (async () => {
                  await wb.changeSkin('#TagInputRelation');
                })();
                """
            );

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('Selection')
                  && document.getElementById('input')
                  && document.querySelectorAll('#Selection li').length === 0
                  && !document.getElementById('uml')
                  && !document.getElementById('footer')
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の dirty terminal changeSkin 完了を待てませんでした。"
            );

            return await ReadCrossSkinDomSnapshotAsync(webView, currentSkinName);
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }

        async Task HandleChangeSkinAsync(string requestedSkinName, string messageId)
        {
            string requestedHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                requestedSkinName
            );
            if (string.IsNullOrWhiteSpace(requestedHtmlPath) || !File.Exists(requestedHtmlPath))
            {
                await hostControl.ResolveRequestAsync(messageId, false);
                return;
            }

            currentSkinName = requestedSkinName;
            await hostControl.ResolveRequestAsync(messageId, true);
            WhiteBrowserSkinHostOperationResult changeResult = await hostControl.TryNavigateAsync(
                requestedSkinName,
                userDataFolderPath,
                skinRootPath,
                requestedHtmlPath,
                thumbRootPath
            );
            if (!changeResult.Succeeded)
            {
                throw new AssertionException(
                    $"runtime bridge の dirty terminal changeSkin 遷移に失敗しました: {changeResult.ErrorType} {changeResult.ErrorMessage}"
                );
            }
        }
    }

    private static async Task<string> VerifyUmlFindTreeEveTagRefreshAsync(string tempRootPath)
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 28,
            Top = 28,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve tag refresh 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );

            await webView.ExecuteScriptAsync("Refresh();");

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の tag refresh 反映を待てませんでした。"
            );

            return await ReadJsonStringAsync(
                webView,
                "document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : ''"
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveTagRefreshAndRenavigateAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 35,
            Top = 35,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                if (!firstNavigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve 再入確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );
            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の tag refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"umlFindTreeEve の再 navigate に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の再入後 tree / footer 再生成完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveModifyTagsClearAllAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 35,
            Top = 35,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve modifytags clear rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );
            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の modifytags refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh-tag/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の modifytags 後 onClearAll refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveModifyTagsSkinLeaveAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 32,
            Top = 32,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags: movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags: movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve modifytags leave rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[] { 77, movie77Tags },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の modifytags refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh-tag/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の modifytags 後 onSkinLeave refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );
            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の tag refresh 反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                window.__immTreeMissingSkinResult = null;
                (async () => {
                  window.__immTreeMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveTagMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve tag changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );
            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の tag refresh 反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                window.__immTreeMissingSkinResult = null;
                (async () => {
                  window.__immTreeMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveSkinLeaveMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 44,
            Top = 44,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve onSkinLeave changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );
            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の tag refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await webView.ExecuteScriptAsync(
                """
                window.__immTreeSkinLeaveMissingSkinResult = null;
                (async () => {
                  window.__immTreeSkinLeaveMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeSkinLeaveMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の onSkinLeave 後 changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveClearAllMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 46,
            Top = 46,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve onClearAll changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyTags",
                new
                {
                    __immCallArgs = new object[]
                    {
                        77,
                        new[] { "series-a", "sample", "fresh-tag" },
                    },
                }
            );
            movie77Tags = ["series-a", "sample", "fresh-tag"];
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-tag') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の tag refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync(
                """
                window.__immTreeClearAllMissingSkinResult = null;
                (async () => {
                  window.__immTreeClearAllMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeClearAllMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の onClearAll 後 changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveClearAllAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve clear rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Folders') >= 0
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Folders') >= 0
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の onClearAll 後 refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveSkinLeaveAndSkinEnterAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve leave reenter 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Folders') >= 0
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await hostControl.DispatchCallbackAsync("onSkinEnter", new { });
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Folders') >= 0
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の onSkinLeave 後再入完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEvePathMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve path changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyPath",
                new
                {
                    __immCallArgs = new object[] { 77, "F:", "\\fresh\\", "Beta", ".avi", "" },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の path refresh 反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                window.__immTreePathMissingSkinResult = null;
                (async () => {
                  window.__immTreePathMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreePathMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の path changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRegisteredMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        string[] movie77Tags = ["series-a", "sample"];

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(movie77Tags),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(movie77Tags)
                    );
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                window.__immTreeRegisterMissingSkinResult = null;
                (async () => {
                  window.__immTreeRegisterMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeRegisterMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRemoveMissingChangeSkinAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "changeSkin":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, false);
                    break;
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "#umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve remove changeSkin失敗確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                  && document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRemoveFile",
                new
                {
                    __immCallArgs = new object[] { 77 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の remove refresh 反映を待てませんでした。"
            );

            await webView.ExecuteScriptAsync(
                """
                window.__immTreeRemoveMissingSkinResult = null;
                (async () => {
                  window.__immTreeRemoveMissingSkinResult = await wb.changeSkin('MissingSkin');
                })();
                """
            );
            await WaitForWebConditionAsync(
                webView,
                "window.__immTreeRemoveMissingSkinResult === false",
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の remove changeSkin 失敗結果を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<string> VerifyUmlFindTreeEveRegisteredFileRefreshAsync(string tempRootPath)
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 30,
            Top = 30,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register refresh 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );

            await webView.ExecuteScriptAsync("Refresh();");

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            return await ReadJsonStringAsync(
                webView,
                "document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : ''"
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRegisteredFileAndRenavigateAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        bool includeMovie91 = false;

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(includeMovie91: includeMovie91),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = includeMovie91 ? 3 : 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(includeMovie91: includeMovie91)
                    );
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = includeMovie91 ? 3 : 2,
                            total = includeMovie91 ? 3 : 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                if (!firstNavigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register 再入確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            includeMovie91 = true;
            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"umlFindTreeEve の register 後再 navigate に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh-series/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の register 後再入完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRegisteredFileClearAllAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        bool includeMovie91 = false;

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(includeMovie91: includeMovie91),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = includeMovie91 ? 3 : 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(includeMovie91: includeMovie91)
                    );
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = includeMovie91 ? 3 : 2,
                            total = includeMovie91 ? 3 : 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register clear rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            includeMovie91 = true;
            await hostControl.DispatchCallbackAsync(
                "onRegistedFile",
                new
                {
                    __immCallArgs = new object[] { 91 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh-series/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の register 後 onClearAll refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRegisteredFileSkinLeaveAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        bool includeMovie91 = false;

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 31,
            Top = 31,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(includeMovie91: includeMovie91),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = includeMovie91 ? 3 : 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(includeMovie91: includeMovie91)
                    );
                    break;
                case "getInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            id = 91,
                            movieId = 91,
                            title = "Gamma",
                            ext = ".mkv",
                            drive = "E:",
                            dir = "\\incoming\\",
                            kana = "",
                            tags = new[] { "fresh-series", "sample" },
                        }
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = includeMovie91 ? 3 : 2,
                            total = includeMovie91 ? 3 : 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve register leave rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('Tags') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            includeMovie91 = true;
            await hostControl.DispatchCallbackAsync("onRegistedFile", new { __immCallArgs = new object[] { 91 } });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の register refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh-series') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh-series/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の register 後 onSkinLeave refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<string> VerifyUmlFindTreeEveRemoveFileRefreshAsync(string tempRootPath)
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 32,
            Top = 32,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve remove refresh 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tag tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onRemoveFile",
                new
                {
                    __immCallArgs = new object[] { 77 },
                }
            );

            await webView.ExecuteScriptAsync("Refresh();");

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の remove refresh 反映を待てませんでした。"
            );

            return await ReadJsonStringAsync(
                webView,
                "document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : ''"
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRemoveFileAndRenavigateAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        bool includeMovie77 = true;

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 33,
            Top = 33,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(includeMovie77: includeMovie77),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = includeMovie77 ? 2 : 1,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(includeMovie77: includeMovie77)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = includeMovie77 ? 2 : 1,
                            total = includeMovie77 ? 2 : 1,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                if (!firstNavigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve remove 再入確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            includeMovie77 = false;
            await hostControl.DispatchCallbackAsync(
                "onRemoveFile",
                new
                {
                    __immCallArgs = new object[] { 77 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の remove refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"umlFindTreeEve の remove 後再 navigate に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の remove 更新後再入完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRemoveFileClearAllAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        bool includeMovie77 = true;

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 33,
            Top = 33,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(includeMovie77: includeMovie77),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = includeMovie77 ? 2 : 1,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(includeMovie77: includeMovie77)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = includeMovie77 ? 2 : 1,
                            total = includeMovie77 ? 2 : 1,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve remove clear rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            includeMovie77 = false;
            await hostControl.DispatchCallbackAsync(
                "onRemoveFile",
                new
                {
                    __immCallArgs = new object[] { 77 },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の remove refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の remove 後 onClearAll refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveRemoveFileSkinLeaveAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        bool includeMovie77 = true;

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 33,
            Top = 33,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(includeMovie77: includeMovie77),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = includeMovie77 ? 2 : 1,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(includeMovie77: includeMovie77)
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = includeMovie77 ? 2 : 1,
                            total = includeMovie77 ? 2 : 1,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve remove leave rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            includeMovie77 = false;
            await hostControl.DispatchCallbackAsync("onRemoveFile", new { __immCallArgs = new object[] { 77 } });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の remove refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('series-a') < 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の remove 後 onSkinLeave refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<string> VerifyUmlFindTreeEveModifyPathRefreshAsync(string tempRootPath)
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 34,
            Top = 34,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, CreateBuildOutputSkinSampleMovies());
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve path refresh 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('archive') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 folder tree 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyPath",
                new
                {
                    __immCallArgs = new object[] { 77, "F:", "\\fresh\\", "Beta", ".avi", "" },
                }
            );

            await webView.ExecuteScriptAsync("Refresh();");

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の path refresh 反映を待てませんでした。"
            );

            return await ReadJsonStringAsync(
                webView,
                "document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : ''"
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveModifyPathAndRenavigateAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string movie77Drive = "D:";
        string movie77Dir = "\\archive\\";
        string movie77Ext = ".avi";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 35,
            Top = 35,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(
                                movie77Drive: movie77Drive,
                                movie77Dir: movie77Dir,
                                movie77Ext: movie77Ext
                            ),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(
                            movie77Drive: movie77Drive,
                            movie77Dir: movie77Dir,
                            movie77Ext: movie77Ext
                        )
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                if (!firstNavigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve path 再入確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('archive') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync(
                "onModifyPath",
                new
                {
                    __immCallArgs = new object[] { 77, "F:", "\\fresh\\", "Beta", ".avi", "" },
                }
            );
            movie77Drive = "F:";
            movie77Dir = "\\fresh\\";
            movie77Ext = ".avi";
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の path refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                throw new AssertionException(
                    $"umlFindTreeEve の path 更新後再 navigate に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の path 更新後再入完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveModifyPathClearAllAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string driveValue = "E:";
        string dirValue = "\\incoming\\";
        string titleValue = "Beta";
        string extValue = ".mp4";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 34,
            Top = 34,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(
                                movie77Drive: driveValue,
                                movie77Dir: dirValue,
                                movie77Ext: extValue
                            ),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(
                            movie77Drive: driveValue,
                            movie77Dir: dirValue,
                            movie77Ext: extValue
                        )
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve modifypath clear rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('incoming') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            driveValue = "F:";
            dirValue = "\\fresh\\";
            titleValue = "Beta";
            extValue = ".avi";
            await hostControl.DispatchCallbackAsync(
                "onModifyPath",
                new
                {
                    __immCallArgs = new object[] { 77, driveValue, dirValue, titleValue, extValue, "" },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の modifypath refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onClearAll", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の modifypath 後 onClearAll refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<UmlFindTreeVerificationResult> VerifyUmlFindTreeEveModifyPathSkinLeaveAndRefreshAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateBuildOutputSkinRootWithCompat("#umlFindTreeEve");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string driveValue = "E:";
        string dirValue = "\\incoming\\";
        string titleValue = "Beta";
        string extValue = ".mp4";

        Window hostWindow = new()
        {
            Width = 420,
            Height = 320,
            Left = 34,
            Top = 34,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        hostControl.WebMessageReceived += (_, e) =>
        {
            switch (e.Method)
            {
                case "update":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            items = CreateBuildOutputSkinSampleMovies(
                                movie77Drive: driveValue,
                                movie77Dir: dirValue,
                                movie77Ext: extValue
                            ),
                            startIndex = 0,
                            requestedCount = 200,
                            totalCount = 2,
                        }
                    );
                    break;
                case "getInfos":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        CreateBuildOutputSkinSampleMovies(
                            movie77Drive: driveValue,
                            movie77Dir: dirValue,
                            movie77Ext: extValue
                        )
                    );
                    break;
                case "getFindInfo":
                    _ = hostControl.ResolveRequestAsync(
                        e.MessageId,
                        new
                        {
                            find = "",
                            result = 2,
                            total = 2,
                            sort = new[] { "" },
                            filter = Array.Empty<string>(),
                        }
                    );
                    break;
                case "getDBName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "sample.wb");
                    break;
                case "getSkinName":
                    _ = hostControl.ResolveRequestAsync(e.MessageId, "umlFindTreeEve");
                    break;
                default:
                    _ = hostControl.ResolveRequestAsync(e.MessageId, true);
                    break;
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string skinHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "#umlFindTreeEve");
            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "#umlFindTreeEve",
                userDataFolderPath,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                if (!navigateResult.RuntimeAvailable)
                {
                    Assert.Ignore(
                        $"WebView2 Runtime 未導入のため umlFindTreeEve modifypath leave rerender 確認をスキップします: {navigateResult.ErrorMessage}"
                    );
                }

                throw new AssertionException(
                    $"umlFindTreeEve 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                );
            }

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('incoming') >= 0
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve 初期 tree / footer 生成完了を待てませんでした。"
            );

            driveValue = "F:";
            dirValue = "\\fresh\\";
            titleValue = "Beta";
            extValue = ".avi";
            await hostControl.DispatchCallbackAsync(
                "onModifyPath",
                new
                {
                    __immCallArgs = new object[] { 77, driveValue, dirValue, titleValue, extValue, "" },
                }
            );
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                """,
                TimeSpan.FromSeconds(5),
                "umlFindTreeEve の modifypath refresh 反映を待てませんでした。"
            );

            await hostControl.DispatchCallbackAsync("onSkinLeave", new { });
            await webView.ExecuteScriptAsync("Refresh();");
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('uml')
                  && (document.getElementById('uml').textContent || '').indexOf('fresh') >= 0
                  && ((document.getElementById('uml').textContent || '').match(/fresh/g) || []).length === 1
                  && document.getElementById('footer')
                  && (document.getElementById('footer').textContent || '').indexOf('ClearCache') >= 0
                """,
                TimeSpan.FromSeconds(10),
                "umlFindTreeEve の modifypath 後 onSkinLeave refresh 完了を待てませんでした。"
            );

            string snapshotJson = await ReadJsonStringAsync(
                webView,
                """
                JSON.stringify({
                  footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
                  umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : '',
                  lineCount: document.querySelectorAll('#uml .line').length
                })
                """
            );
            using JsonDocument document = JsonDocument.Parse(snapshotJson);
            return UmlFindTreeVerificationResult.Succeeded(
                document.RootElement.GetProperty("footerText").GetString() ?? "",
                document.RootElement.GetProperty("umlText").GetString() ?? "",
                document.RootElement.GetProperty("lineCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static object[] CreateBuildOutputSkinSampleMovies(
        string[]? movie77Tags = null,
        string? movie77Drive = null,
        string? movie77Dir = null,
        string? movie77Ext = null,
        bool includeMovie77 = true,
        bool includeMovie91 = false,
        string[]? movie91Tags = null
    )
    {
        string[] resolvedMovie77Tags = movie77Tags ?? ["series-a", "sample"];
        string resolvedMovie77Drive = movie77Drive ?? "D:";
        string resolvedMovie77Dir = movie77Dir ?? "\\archive\\";
        string resolvedMovie77Ext = movie77Ext ?? ".avi";
        string[] resolvedMovie91Tags = movie91Tags ?? ["fresh-series", "sample"];

        List<object> items =
        [
            new
            {
                id = 42,
                movieId = 42,
                recordKey = "db-main:42",
                title = "Alpha",
                ext = ".mp4",
                thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                exist = true,
                select = 0,
                score = 11.0,
                size = "1.0 GB",
                len = "00:10:00",
                lenSec = "600",
                extra = "",
                drive = "C:",
                dir = "\\movies\\",
                video = "1920x1080&nbsp;60fps",
                audio = "AAC&nbsp;128kbps",
                comments = "",
                tags = new[] { "idol", "beta" },
                fileDate = "2026-04-12 12:34:56",
                container = "MP4",
                offset = 1,
            },
        ];

        if (includeMovie77)
        {
            items.Add(
                new
                {
                    id = 77,
                    movieId = 77,
                    recordKey = "db-main:77",
                    title = "Beta",
                    ext = resolvedMovie77Ext,
                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                    exist = true,
                    select = 1,
                    score = 88.5,
                    size = "2.0 GB",
                    len = "01:23:45",
                    lenSec = "5025",
                    extra = "",
                    drive = resolvedMovie77Drive,
                    dir = resolvedMovie77Dir,
                    video = "1280x720&nbsp;30fps",
                    audio = "AAC&nbsp;192kbps",
                    comments = "",
                    tags = resolvedMovie77Tags,
                    fileDate = "2026-04-12 13:45:56",
                    container = "AVI",
                    offset = 2,
                }
            );
        }

        if (includeMovie91)
        {
            items.Add(
                new
                {
                    id = 91,
                    movieId = 91,
                    recordKey = "db-main:91",
                    title = "Gamma",
                    ext = ".mkv",
                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                    exist = true,
                    select = 0,
                    score = 18.0,
                    size = "3.0 GB",
                    len = "00:30:00",
                    lenSec = "1800",
                    extra = "",
                    drive = "E:",
                    dir = "\\incoming\\",
                    video = "1920x1080&nbsp;24fps",
                    audio = "AAC&nbsp;192kbps",
                    comments = "",
                    tags = resolvedMovie91Tags,
                    fileDate = "2026-04-11 08:15:33",
                    container = "MKV",
                    offset = 0,
                }
            );
        }

        return [.. items];
    }

    private static object CreateBuildOutputSkinUpdatePayload()
    {
        return new
        {
            items = CreateBuildOutputSkinSampleMovies(),
            startIndex = 0,
            requestedCount = 200,
            totalCount = 2,
        };
    }

    private static object CreateBuildOutputSkinFindInfo()
    {
        return new
        {
            find = "",
            result = 2,
            total = 2,
            sort = new[] { "ファイル名(降順)", "" },
            filter = Array.Empty<string>(),
            where = "",
        };
    }

    private static string[] DeserializeStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.String)
        {
            string wrapped = document.RootElement.GetString() ?? "[]";
            return JsonSerializer.Deserialize<string[]>(wrapped) ?? [];
        }

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            values.Add(item.GetString() ?? "");
        }

        return [.. values];
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        string current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine([current, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return "";
    }

    private static string CreateFixtureSkinRootWithCompat(params string[] fixtureNames)
    {
        return WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            fixtureNames,
            rewriteHtmlAsShiftJis: true
        );
    }

    private static string CreateBuildOutputSkinRootWithCompat(params string[] skinNames)
    {
        return WhiteBrowserSkinTestData.CreateBuildOutputSkinRootCopyWithCompat(skinNames);
    }

    private static string BuildLifecycleHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__wbDone = false;
                window.__wbError = "";
                window.__wbSequence = [];

                function onSetFocus(id, isFocus) {
                  window.__wbSequence.push("focus:" + String(id || 0) + ":" + String(!!isFocus));
                  return true;
                }

                function onSetSelect(id, isSel) {
                  window.__wbSequence.push("select:" + String(id || 0) + ":" + String(!!isSel));
                  return true;
                }

                function onClearAll() {
                  window.__wbSequence.push("clear");
                  return true;
                }

                function onSkinLeave() {
                  window.__wbSequence.push("leave");
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="config">multi-select : 1;</div>
            </body>
            </html>
            """;
    }

    private static string ToJavaScriptStringLiteral(string value)
    {
        string normalized = value ?? "";
        normalized = normalized.Replace("\\", "\\\\");
        normalized = normalized.Replace("'", "\\'");
        return $"'{normalized}'";
    }

    private static async Task<CrossSkinDomSnapshot> ReadCrossSkinDomSnapshotAsync(
        WebView2 webView,
        string currentSkinName
    )
    {
        string snapshotJson = await ReadJsonStringAsync(
            webView,
            """
            JSON.stringify({
              hasInput: !!document.getElementById('input'),
              inputValue: document.getElementById('input') ? (document.getElementById('input').value || '') : '',
              selectionCount: document.querySelectorAll('#Selection li').length,
              footerText: document.getElementById('footer') ? (document.getElementById('footer').textContent || '') : '',
              umlText: document.getElementById('uml') ? (document.getElementById('uml').textContent || '') : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(snapshotJson);
        return new CrossSkinDomSnapshot(
            currentSkinName,
            document.RootElement.GetProperty("hasInput").GetBoolean(),
            document.RootElement.GetProperty("inputValue").GetString() ?? "",
            document.RootElement.GetProperty("selectionCount").GetInt32(),
            document.RootElement.GetProperty("footerText").GetString() ?? "",
            document.RootElement.GetProperty("umlText").GetString() ?? ""
        );
    }

    private sealed record RuntimeBridgeVerificationResult(
        string IgnoreReason,
        string FailureMessage,
        ExternalThumbnailResponseSnapshot OkResponse,
        ExternalThumbnailResponseSnapshot ForbiddenResponse,
        ExternalThumbnailResponseSnapshot MissingResponse
    )
    {
        public static RuntimeBridgeVerificationResult Ignored(string reason)
        {
            return new RuntimeBridgeVerificationResult(
                reason,
                "",
                ExternalThumbnailResponseSnapshot.Empty,
                ExternalThumbnailResponseSnapshot.Empty,
                ExternalThumbnailResponseSnapshot.Empty
            );
        }

        public static RuntimeBridgeVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static RuntimeBridgeVerificationResult Succeeded(
            ExternalThumbnailResponseSnapshot okResponse,
            ExternalThumbnailResponseSnapshot forbiddenResponse,
            ExternalThumbnailResponseSnapshot missingResponse
        )
        {
            return new RuntimeBridgeVerificationResult(
                "",
                "",
                okResponse,
                forbiddenResponse,
                missingResponse
            );
        }
    }

    private sealed record RuntimeBridgeLifecycleVerificationResult(
        string IgnoreReason,
        string[] LifecycleEvents
    )
    {
        public static RuntimeBridgeLifecycleVerificationResult Ignored(string reason)
        {
            return new RuntimeBridgeLifecycleVerificationResult(reason, []);
        }

        public static RuntimeBridgeLifecycleVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static RuntimeBridgeLifecycleVerificationResult Succeeded(string[] lifecycleEvents)
        {
            return new RuntimeBridgeLifecycleVerificationResult("", lifecycleEvents);
        }
    }

    private sealed record TutorialCallbackGridDomSnapshot(
        int ItemCount,
        string TitleText,
        string FocusedImageClass,
        string SelectedThumbClass
    );

    private sealed record WhiteBrowserDefaultListDomSnapshot(
        int ItemCount,
        string TitleText,
        string SizeText,
        string LengthText,
        string ScrollElementId
    );

    private sealed record SimpleWhiteBrowserDefaultFixtureDomSnapshot(
        int ItemCount,
        string TitleText,
        string SelectedClass,
        string ScoreText
    );

    private sealed record TutorialCallbackGridVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int UpdateStartIndex,
        int UpdateCount,
        int FirstFocusRequestMovieId,
        int SecondFocusRequestMovieId,
        int ItemCountBeforeLeave,
        string TitleTextBeforeLeave,
        string FocusedImageClassBeforeLeave,
        string SelectedThumbClassBeforeLeave,
        int ItemCountAfterRefresh,
        string TitleTextAfterRefresh,
        string FocusedImageClassAfterRefresh,
        string SelectedThumbClassAfterRefresh,
        int ItemCountAfterLeave
    )
    {
        public static TutorialCallbackGridVerificationResult Ignored(string reason)
        {
            return new TutorialCallbackGridVerificationResult(
                reason,
                0,
                0,
                0,
                0,
                0,
                0,
                "",
                "",
                "",
                0,
                "",
                "",
                "",
                0
            );
        }

        public static TutorialCallbackGridVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static TutorialCallbackGridVerificationResult Succeeded(
            int updateRequestCount,
            int updateStartIndex,
            int updateCount,
            int firstFocusRequestMovieId,
            int secondFocusRequestMovieId,
            int itemCountBeforeLeave,
            string titleTextBeforeLeave,
            string focusedImageClassBeforeLeave,
            string selectedThumbClassBeforeLeave,
            int itemCountAfterRefresh,
            string titleTextAfterRefresh,
            string focusedImageClassAfterRefresh,
            string selectedThumbClassAfterRefresh,
            int itemCountAfterLeave
        )
        {
            return new TutorialCallbackGridVerificationResult(
                "",
                updateRequestCount,
                updateStartIndex,
                updateCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                itemCountBeforeLeave,
                titleTextBeforeLeave,
                focusedImageClassBeforeLeave,
                selectedThumbClassBeforeLeave,
                itemCountAfterRefresh,
                titleTextAfterRefresh,
                focusedImageClassAfterRefresh,
                selectedThumbClassAfterRefresh,
                itemCountAfterLeave
            );
        }
    }

    private sealed record WhiteBrowserDefaultListVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int UpdateStartIndex,
        int UpdateCount,
        int ItemCountBeforeLeave,
        string TitleTextBeforeLeave,
        string SizeTextBeforeLeave,
        string LengthTextBeforeLeave,
        string ScrollElementId,
        int ItemCountAfterLeave
    )
    {
        public static WhiteBrowserDefaultListVerificationResult Ignored(string reason)
        {
            return new WhiteBrowserDefaultListVerificationResult(
                reason,
                0,
                0,
                0,
                0,
                "",
                "",
                "",
                "",
                0
            );
        }

        public static WhiteBrowserDefaultListVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static WhiteBrowserDefaultListVerificationResult Succeeded(
            int updateRequestCount,
            int updateStartIndex,
            int updateCount,
            int itemCountBeforeLeave,
            string titleTextBeforeLeave,
            string sizeTextBeforeLeave,
            string lengthTextBeforeLeave,
            string scrollElementId,
            int itemCountAfterLeave
        )
        {
            return new WhiteBrowserDefaultListVerificationResult(
                "",
                updateRequestCount,
                updateStartIndex,
                updateCount,
                itemCountBeforeLeave,
                titleTextBeforeLeave,
                sizeTextBeforeLeave,
                lengthTextBeforeLeave,
                scrollElementId,
                itemCountAfterLeave
            );
        }
    }

    private sealed record WhiteBrowserDefaultListThumbUpdateVerificationResult(
        string IgnoreReason,
        string BeforeThumbSrc,
        string AfterThumbSrc
    )
    {
        public static WhiteBrowserDefaultListThumbUpdateVerificationResult Ignored(string reason)
        {
            return new WhiteBrowserDefaultListThumbUpdateVerificationResult(reason, "", "");
        }

        public static WhiteBrowserDefaultListThumbUpdateVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static WhiteBrowserDefaultListThumbUpdateVerificationResult Succeeded(
            string beforeThumbSrc,
            string afterThumbSrc
        )
        {
            return new WhiteBrowserDefaultListThumbUpdateVerificationResult(
                "",
                beforeThumbSrc,
                afterThumbSrc
            );
        }
    }

    private sealed record WhiteBrowserDefaultListAppendVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int[] UpdateStartIndices,
        int[] UpdateCounts,
        int ItemCountAfterAppend,
        string[] TitlesAfterAppend,
        int ItemCountAfterLeave
    )
    {
        public static WhiteBrowserDefaultListAppendVerificationResult Ignored(string reason)
        {
            return new WhiteBrowserDefaultListAppendVerificationResult(
                reason,
                0,
                [],
                [],
                0,
                [],
                0
            );
        }

        public static WhiteBrowserDefaultListAppendVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static WhiteBrowserDefaultListAppendVerificationResult Succeeded(
            int updateRequestCount,
            int[] updateStartIndices,
            int[] updateCounts,
            int itemCountAfterAppend,
            string[] titlesAfterAppend,
            int itemCountAfterLeave
        )
        {
            return new WhiteBrowserDefaultListAppendVerificationResult(
                "",
                updateRequestCount,
                updateStartIndices,
                updateCounts,
                itemCountAfterAppend,
                titlesAfterAppend,
                itemCountAfterLeave
            );
        }
    }

    private sealed record SimpleWhiteBrowserDefaultFixtureVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int UpdateStartIndex,
        int UpdateCount,
        int ItemCountBeforeLeave,
        string TitleTextBeforeLeave,
        string SelectedClassBeforeLeave,
        string ScoreTextBeforeLeave,
        int ItemCountAfterLeave
    )
    {
        public static SimpleWhiteBrowserDefaultFixtureVerificationResult Ignored(string reason)
        {
            return new SimpleWhiteBrowserDefaultFixtureVerificationResult(
                reason,
                0,
                0,
                0,
                0,
                "",
                "",
                "",
                0
            );
        }

        public static SimpleWhiteBrowserDefaultFixtureVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static SimpleWhiteBrowserDefaultFixtureVerificationResult Succeeded(
            int updateRequestCount,
            int updateStartIndex,
            int updateCount,
            int itemCountBeforeLeave,
            string titleTextBeforeLeave,
            string selectedClassBeforeLeave,
            string scoreTextBeforeLeave,
            int itemCountAfterLeave
        )
        {
            return new SimpleWhiteBrowserDefaultFixtureVerificationResult(
                "",
                updateRequestCount,
                updateStartIndex,
                updateCount,
                itemCountBeforeLeave,
                titleTextBeforeLeave,
                selectedClassBeforeLeave,
                scoreTextBeforeLeave,
                itemCountAfterLeave
            );
        }
    }

    private sealed record SimpleWhiteBrowserDefaultFixtureAppendVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int[] UpdateStartIndices,
        int[] UpdateCounts,
        int ItemCountAfterAppend,
        string[] TitlesAfterAppend,
        string AppendedScoreText,
        int ItemCountAfterLeave
    )
    {
        public static SimpleWhiteBrowserDefaultFixtureAppendVerificationResult Ignored(string reason)
        {
            return new SimpleWhiteBrowserDefaultFixtureAppendVerificationResult(
                reason,
                0,
                [],
                [],
                0,
                [],
                "",
                0
            );
        }

        public static SimpleWhiteBrowserDefaultFixtureAppendVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static SimpleWhiteBrowserDefaultFixtureAppendVerificationResult Succeeded(
            int updateRequestCount,
            int[] updateStartIndices,
            int[] updateCounts,
            int itemCountAfterAppend,
            string[] titlesAfterAppend,
            string appendedScoreText,
            int itemCountAfterLeave
        )
        {
            return new SimpleWhiteBrowserDefaultFixtureAppendVerificationResult(
                "",
                updateRequestCount,
                updateStartIndices,
                updateCounts,
                itemCountAfterAppend,
                titlesAfterAppend,
                appendedScoreText,
                itemCountAfterLeave
            );
        }
    }

    private sealed record SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult(
        string IgnoreReason,
        int InitialUpdateRequestCount,
        int FindRequestCount,
        string FindKeyword,
        int FindStartIndex,
        int FindCount,
        int AppendUpdateRequestCount,
        int AppendUpdateStartIndex,
        int AppendUpdateCount,
        int ItemCountAfterAppend,
        string[] TitlesAfterAppend,
        string AppendedScoreText,
        int ItemCountAfterLeave
    )
    {
        public static SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult Ignored(string reason)
        {
            return new SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult(
                reason,
                0,
                0,
                "",
                -1,
                -1,
                0,
                -1,
                -1,
                0,
                [],
                "",
                0
            );
        }

        public static SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult Succeeded(
            int initialUpdateRequestCount,
            int findRequestCount,
            string findKeyword,
            int findStartIndex,
            int findCount,
            int appendUpdateRequestCount,
            int appendUpdateStartIndex,
            int appendUpdateCount,
            int itemCountAfterAppend,
            string[] titlesAfterAppend,
            string appendedScoreText,
            int itemCountAfterLeave
        )
        {
            return new SimpleWhiteBrowserDefaultFixtureSearchAppendVerificationResult(
                "",
                initialUpdateRequestCount,
                findRequestCount,
                findKeyword,
                findStartIndex,
                findCount,
                appendUpdateRequestCount,
                appendUpdateStartIndex,
                appendUpdateCount,
                itemCountAfterAppend,
                titlesAfterAppend,
                appendedScoreText,
                itemCountAfterLeave
            );
        }
    }

    private sealed record FixtureSwitchVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int FirstFixtureItemCount,
        int SecondFixtureItemCount,
        string FirstFixtureTitleText,
        string SecondFixtureTitleText,
        bool SecondFixtureScrollExists,
        bool SecondFixtureLegacyNodeGone
    )
    {
        public static FixtureSwitchVerificationResult Ignored(string reason)
        {
            return new FixtureSwitchVerificationResult(reason, 0, 0, 0, "", "", false, false);
        }

        public static FixtureSwitchVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static FixtureSwitchVerificationResult Succeeded(
            int updateRequestCount,
            int firstFixtureItemCount,
            int secondFixtureItemCount,
            string firstFixtureTitleText,
            string secondFixtureTitleText,
            bool secondFixtureScrollExists,
            bool secondFixtureLegacyNodeGone
        )
        {
            return new FixtureSwitchVerificationResult(
                "",
                updateRequestCount,
                firstFixtureItemCount,
                secondFixtureItemCount,
                firstFixtureTitleText,
                secondFixtureTitleText,
                secondFixtureScrollExists,
                secondFixtureLegacyNodeGone
            );
        }
    }

    private sealed record SameFixtureRenavigateVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int FirstFocusRequestMovieId,
        int SecondFocusRequestMovieId,
        string[] LifecycleEvents,
        int SecondItemCount,
        string SecondTitleText,
        bool LegacyNodeGone
    )
    {
        public static SameFixtureRenavigateVerificationResult Ignored(string reason)
        {
            return new SameFixtureRenavigateVerificationResult(reason, 0, 0, 0, [], 0, "", false);
        }

        public static SameFixtureRenavigateVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static SameFixtureRenavigateVerificationResult Succeeded(
            int updateRequestCount,
            int firstFocusRequestMovieId,
            int secondFocusRequestMovieId,
            string[] lifecycleEvents,
            int secondItemCount,
            string secondTitleText,
            bool legacyNodeGone
        )
        {
            return new SameFixtureRenavigateVerificationResult(
                "",
                updateRequestCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                lifecycleEvents,
                secondItemCount,
                secondTitleText,
                legacyNodeGone
            );
        }
    }

    private sealed record BuildOutputSkinThumbnailVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int ThumbnailCountBeforeLeave,
        string Image77Src,
        string Title77Text,
        int ThumbnailCountAfterLeave
    )
    {
        public static BuildOutputSkinThumbnailVerificationResult Ignored(string reason)
        {
            return new BuildOutputSkinThumbnailVerificationResult(reason, 0, 0, "", "", 0);
        }

        public static BuildOutputSkinThumbnailVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static BuildOutputSkinThumbnailVerificationResult Succeeded(
            int updateRequestCount,
            int thumbnailCountBeforeLeave,
            string image77Src,
            string title77Text,
            int thumbnailCountAfterLeave
        )
        {
            return new BuildOutputSkinThumbnailVerificationResult(
                "",
                updateRequestCount,
                thumbnailCountBeforeLeave,
                image77Src,
                title77Text,
                thumbnailCountAfterLeave
            );
        }
    }

    private sealed record BuildOutputSkinThumbUpdateVerificationResult(
        string IgnoreReason,
        string BeforeThumbSrc,
        string AfterThumbSrc
    )
    {
        public static BuildOutputSkinThumbUpdateVerificationResult Ignored(string reason)
        {
            return new BuildOutputSkinThumbUpdateVerificationResult(reason, "", "");
        }

        public static BuildOutputSkinThumbUpdateVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static BuildOutputSkinThumbUpdateVerificationResult Succeeded(
            string beforeThumbSrc,
            string afterThumbSrc
        )
        {
            return new BuildOutputSkinThumbUpdateVerificationResult(
                "",
                beforeThumbSrc,
                afterThumbSrc
            );
        }
    }

    private sealed record BuildOutputSkinModifyTagsVerificationResult(
        string IgnoreReason,
        string BeforeTagText,
        string AfterTagText
    )
    {
        public static BuildOutputSkinModifyTagsVerificationResult Ignored(string reason)
        {
            return new BuildOutputSkinModifyTagsVerificationResult(reason, "", "");
        }

        public static BuildOutputSkinModifyTagsVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static BuildOutputSkinModifyTagsVerificationResult Succeeded(
            string beforeTagText,
            string afterTagText
        )
        {
            return new BuildOutputSkinModifyTagsVerificationResult(
                "",
                beforeTagText,
                afterTagText
            );
        }
    }

    private sealed record BuildOutputSkinThumbChangeSkinVerificationResult(
        string IgnoreReason,
        string CurrentSkin,
        string ThumbSrc
    )
    {
        public static BuildOutputSkinThumbChangeSkinVerificationResult Ignored(string reason)
        {
            return new BuildOutputSkinThumbChangeSkinVerificationResult(reason, "", "");
        }

        public static BuildOutputSkinThumbChangeSkinVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static BuildOutputSkinThumbChangeSkinVerificationResult Succeeded(
            string currentSkin,
            string thumbSrc
        )
        {
            return new BuildOutputSkinThumbChangeSkinVerificationResult("", currentSkin, thumbSrc);
        }
    }

    private sealed record BuildOutputSkinModifyTagsChangeSkinVerificationResult(
        string IgnoreReason,
        string CurrentSkin,
        string ChangeSkinResult,
        string TagText
    )
    {
        public static BuildOutputSkinModifyTagsChangeSkinVerificationResult Ignored(string reason)
        {
            return new BuildOutputSkinModifyTagsChangeSkinVerificationResult(reason, "", "", "");
        }

        public static BuildOutputSkinModifyTagsChangeSkinVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static BuildOutputSkinModifyTagsChangeSkinVerificationResult Succeeded(
            string currentSkin,
            string changeSkinResult,
            string tagText
        )
        {
            return new BuildOutputSkinModifyTagsChangeSkinVerificationResult(
                "",
                currentSkin,
                changeSkinResult,
                tagText
            );
        }
    }

    private sealed record UmlFindTreeVerificationResult(
        string FooterText,
        string UmlText,
        int LineCount
    )
    {
        public static UmlFindTreeVerificationResult Succeeded(
            string footerText,
            string umlText,
            int lineCount
        )
        {
            return new UmlFindTreeVerificationResult(footerText, umlText, lineCount);
        }
    }

    private sealed record TagInputRelationSaveVerificationResult(
        string InputAfterInclude,
        string[] AddTagRequests,
        string InputAfterSave
    );

    private sealed record TagInputRelationRerenderVerificationResult(
        string[] CandidateTextsAfterRerender,
        string InputAfterRerender
    );

    private sealed record TagInputRelationGetVerificationResult(
        int[] RelationLimits,
        string[] SelectionAfterGet,
        string InputAfterSet,
        string[] SelectionAfterSet
    );

    private sealed record CrossSkinDomSnapshot(
        string CurrentSkin,
        bool HasInput,
        string InputValue,
        int SelectionCount,
        string FooterText,
        string UmlText
    );

    private sealed record ExternalThumbnailResponseSnapshot(
        int StatusCode,
        string ReasonPhrase,
        string ContentType,
        string CacheControl,
        int BodyLength
    )
    {
        public static ExternalThumbnailResponseSnapshot Empty { get; } = new(0, "", "", "", 0);

        public static async Task<ExternalThumbnailResponseSnapshot> CreateAsync(
            CoreWebView2WebResourceResponseView response
        )
        {
            if (response == null)
            {
                return Empty;
            }

            string contentType = TryGetHeader(response, "Content-Type");
            string cacheControl = TryGetHeader(response, "Cache-Control");
            int bodyLength = await TryGetBodyLengthAsync(response);

            return new ExternalThumbnailResponseSnapshot(
                response.StatusCode,
                response.ReasonPhrase ?? "",
                contentType,
                cacheControl,
                bodyLength
            );
        }

        private static string TryGetHeader(
            CoreWebView2WebResourceResponseView response,
            string headerName
        )
        {
            try
            {
                return response.Headers.GetHeader(headerName) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static async Task<int> TryGetBodyLengthAsync(
            CoreWebView2WebResourceResponseView response
        )
        {
            if (response == null)
            {
                return 0;
            }

            try
            {
                using Stream contentStream = await response.GetContentAsync();
                if (contentStream == null)
                {
                    return 0;
                }

                if (contentStream.CanSeek)
                {
                    return checked((int)contentStream.Length);
                }

                using MemoryStream buffer = new();
                await contentStream.CopyToAsync(buffer);
                return checked((int)buffer.Length);
            }
            catch
            {
                return 0;
            }
        }
    }
}
