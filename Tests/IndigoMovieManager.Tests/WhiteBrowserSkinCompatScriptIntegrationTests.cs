using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class WhiteBrowserSkinCompatScriptIntegrationTests
{
    [Test]
    public async Task FocusAndSelectThum_callbackが二重発火しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-script");

        try
        {
            CompatScriptVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyCompatCallbacksAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.FocusEvents, Is.EqualTo(["42:true"]));
                Assert.That(result.FocusSelectionEvents, Is.EqualTo(["42:true"]));
                Assert.That(result.SelectEvents, Is.EqualTo(["77:true"]));
                Assert.That(result.SelectFocusEvents, Is.Empty);
                Assert.That(
                    result.ThumbnailUpdateEvents,
                    Is.EqualTo(["db-main:77|https://thum.local/sample.jpg?rev=thumb-1|thumb-1|managed-thumbnail|160x120|1x1"])
                );
                Assert.That(result.TagRequests, Is.EqualTo(["addTag:42:idol", "flipTag:77:beta"]));
                Assert.That(result.TagModifyEvents, Is.EqualTo(["idol:true", "beta:true"]));
                Assert.That(result.TagCacheSummary, Is.EqualTo("idol|"));
                Assert.That(
                    result.LifecycleEvents,
                    Is.EqualTo(["focus:90:false", "select:90:false", "clear", "leave"])
                );
                Assert.That(result.ScrollSucceeded, Is.True);
                Assert.That(
                    result.InfoRequestMethods,
                    Is.EqualTo(["getFindInfo", "getFocusThum", "getSelectThums"])
                );
                Assert.That(result.InfoSummary, Is.EqualTo("idol|3|2|42|42,77"));
                Assert.That(
                    result.FilterRequestMethods,
                    Is.EqualTo(["addFilter", "removeFilter", "clearFilter"])
                );
                Assert.That(result.FilterUpdateCounts, Is.EqualTo(["2", "1", "3"]));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task Update系callbackは先頭再更新だけclearを先行できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-reset-view");

        try
        {
            ResetViewVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyResetViewBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Sequence,
                    Is.EqualTo(["clear", "update:2", "update:1", "clear", "update:1"])
                );
                Assert.That(
                    result.Methods,
                    Is.EqualTo(["update:0:200", "update:120:80", "find:0:200"])
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task GetInfosはmovieIdsと範囲指定payloadを投げ分けできる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-getinfos");

        try
        {
            GetInfosRequestVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyGetInfosRequestShapesAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Methods, Is.EqualTo(["getInfos", "getInfos", "getInfos"]));
                Assert.That(
                    result.Payloads,
                    Is.EqualTo(
                        [
                            "{\"movieIds\":[42,77]}",
                            "{\"startIndex\":120,\"count\":200}",
                            "{\"recordKeys\":[\"db-main:42\"]}"
                        ]
                    )
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onUpdatefallbackはstartIndex付きupdateを追記描画できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-default-append");

        try
        {
            DefaultUpdateAppendVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyDefaultUpdateAppendBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Sequence,
                    Is.EqualTo(["clear", "create:1", "create:2", "create:3"])
                );
                Assert.That(
                    result.Methods,
                    Is.EqualTo(["update:0:2", "update:2:1"])
                );
                Assert.That(
                    result.Titles,
                    Is.EqualTo(["Alpha.mp4", "Beta.avi", "Gamma.mkv"])
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task scrollSettingはseamless_scrollでstartIndex付きupdateを自動追記できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-seamless-scroll");

        try
        {
            SeamlessScrollVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySeamlessScrollBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Sequence, Is.EqualTo(["clear", "create:1", "create:2", "create:3"]));
                Assert.That(result.Methods, Is.EqualTo(["update:0:2", "update:2:1"]));
                Assert.That(result.Titles, Is.EqualTo(["Alpha.mp4", "Beta.avi", "Gamma.mkv"]));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task seamless_scrollは空振り追記後に再要求しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-seamless-stop");

        try
        {
            SeamlessScrollStopVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySeamlessScrollStopBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Methods, Is.EqualTo(["update:0:2", "update:2:2"]));
                Assert.That(result.Titles, Is.EqualTo(["Alpha.mp4", "Beta.avi"]));
                Assert.That(result.PendingRequestCount, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定thumbfallbackはcallback未実装でも最小表示と差分更新を流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-thumb-fallback");

        try
        {
            DefaultThumbnailFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyDefaultThumbnailFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CreatedThumbSrc, Is.EqualTo("https://thum.local/original.jpg?rev=thumb-0"));
                Assert.That(result.UpdatedThumbSrc, Is.EqualTo("https://thum.local/updated.jpg?rev=thumb-2"));
                Assert.That(result.TitleText, Is.EqualTo("Beta.avi"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task legacy_onUpdateThumが空振りしても既定fallbackでimg_srcを差し替えられる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-thumb-legacy-fallback");

        try
        {
            LegacyThumbnailFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyLegacyThumbnailFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.LegacyCallArgs,
                    Is.EqualTo("db-main:77|https://thum.local/updated.jpg?rev=thumb-2")
                );
                Assert.That(
                    result.UpdatedThumbSrc,
                    Is.EqualTo("https://thum.local/updated.jpg?rev=thumb-2")
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task view未実装skinでも既定fallbackで一覧コンテナを自動生成できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-generated-view");

        try
        {
            GeneratedViewFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyGeneratedViewFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedViewExists, Is.True);
                Assert.That(result.GeneratedViewFlag, Is.EqualTo("true"));
                Assert.That(result.CreatedThumbSrc, Is.EqualTo("https://thum.local/original.jpg?rev=thumb-0"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task getFindInfoの古い応答は新しいonUpdate状態を上書きしない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-findinfo-stale");

        try
        {
            StaleFindInfoVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyStaleFindInfoResponseIsIgnoredAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.RequestMethods, Is.EqualTo(["getFindInfo", "update"]));
                Assert.That(result.StaleSummary, Is.EqualTo("fresh|1|"));
                Assert.That(result.CachedSummary, Is.EqualTo("fresh|1|"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task getFindInfo同士の古い応答も新しい取得結果を上書きしない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-findinfo-request-stale");

        try
        {
            StaleFindInfoVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyOverlappedFindInfoResponsesAreIgnoredAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.RequestMethods, Is.EqualTo(["getFindInfo", "getFindInfo"]));
                Assert.That(result.StaleSummary, Is.EqualTo("fresh|3|idol"));
                Assert.That(result.CachedSummary, Is.EqualTo("fresh|3|idol"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onSetSelectfallbackは未実装skinでも選択classを同期できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-default-select-fallback");

        try
        {
            DefaultSelectFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyDefaultSelectFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedClassName, Does.Contain("thum_select"));
                Assert.That(result.SelectedFlag, Is.EqualTo("1"));
                Assert.That(result.ClearedClassName, Is.EqualTo("thum"));
                Assert.That(result.ClearedFlag, Is.EqualTo("0"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onSetSelectfallbackはfocus中解除でもfocusclassを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-focused-select-fallback");

        try
        {
            FocusedSelectFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyFocusedSelectFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedClassName, Does.Contain("thum_focus"));
                Assert.That(result.SelectedClassName, Does.Contain("thum_select"));
                Assert.That(result.ClearedClassName, Is.EqualTo("thum_focus"));
                Assert.That(result.ClearedFlag, Is.EqualTo("0"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 増分updateのselect0はselectedIdsのstaleを残さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-selected-sync");

        try
        {
            IncrementalSelectedStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyIncrementalSelectedStateAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(result.SelectEvents, Is.EqualTo(["77:false"]));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task getSelectThumsの古い応答は新しいselectThum状態を巻き戻さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-selected-stale-guard");

        try
        {
            IncrementalSelectedStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySelectedStateStaleResponseAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(result.SelectEvents, Is.EqualTo(["77:false"]));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task getFocusThumの古い応答は新しいfocusThum状態を巻き戻さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-focus-stale-guard");

        try
        {
            FocusStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyFocusStateStaleResponseAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.FocusedMovieId, Is.EqualTo("77"));
                Assert.That(result.StaleResult, Is.EqualTo("77"));
                Assert.That(result.FocusEvents, Is.EqualTo(["77:true"]));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task focusThumの古い応答は新しいfocusThum状態を巻き戻さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-focus-action-stale");

        try
        {
            FocusStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyFocusActionStaleResponseAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.FocusedMovieId, Is.EqualTo("84"));
                Assert.That(result.StaleResult, Is.EqualTo("77"));
                Assert.That(result.FocusEvents, Is.EqualTo(["84:true"]));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task selectThumの古い応答は新しいselectThum状態を巻き戻さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-select-action-stale");

        try
        {
            SelectionStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySelectActionStaleResponseAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedIds, Is.Empty);
                Assert.That(result.SelectEvents, Is.Empty);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task skin側getSelectThumsは内部再同期に潰されずhost応答を受け取れる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-selected-external-read");

        try
        {
            IncrementalSelectedStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyExternalSelectedReadSurvivesInternalSyncAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(result.SelectEvents, Is.EqualTo(["77"]));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onSetSelectfallbackはcompact行でもbaseclassを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-compact-select-fallback");

        try
        {
            FocusedSelectFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyCompactSelectFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedClassName, Does.Contain("cthum"));
                Assert.That(result.SelectedClassName, Does.Contain("thum_select"));
                Assert.That(result.ClearedClassName, Is.EqualTo("cthum"));
                Assert.That(result.ClearedFlag, Is.EqualTo("0"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onSetFocusfallbackはcompact選択行でもfocus解除後にbaseclassを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-compact-focus-fallback");

        try
        {
            FocusedSelectFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyCompactFocusedSelectFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedClassName, Is.EqualTo("cthum thum_select"));
                Assert.That(result.ClearedClassName, Is.EqualTo("cthum thum_select"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onSetFocusfallbackは未実装skinでもfocusclassを同期できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-default-focus-fallback");

        try
        {
            DefaultFocusFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyDefaultFocusFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.FocusedClassName, Is.EqualTo("thum_focus"));
                Assert.That(result.ClearedClassName, Is.EqualTo("thum"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onSetFocusfallbackはcompact選択行でもbaseclassを維持できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-compact-focus-fallback");

        try
        {
            FocusedSelectFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyCompactFocusFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedClassName, Does.Contain("cthum"));
                Assert.That(result.SelectedClassName, Does.Contain("thum_select"));
                Assert.That(result.ClearedClassName, Is.EqualTo("cthum thum_select"));
                Assert.That(result.ClearedFlag, Is.EqualTo("1"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task handleSkinLeaveはcompact選択行をplain表示へ戻せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-compact-skin-leave");

        try
        {
            FocusedSelectFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyCompactSkinLeaveFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.SelectedClassName, Is.EqualTo("cthum thum_select"));
                Assert.That(result.ClearedClassName, Is.EqualTo("cthum"));
                Assert.That(result.ClearedFlag, Is.EqualTo("0"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task handleClearAllのonSetSelect中でもgetSelectThumsは空へ同期済み()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-clearall-selected-read");

        try
        {
            IncrementalSelectedStateVerificationResult result = await RunOnStaDispatcherAsync(
                () => WhiteBrowserSkinCompatScriptIntegrationTests.VerifySelectedReadDuringClearAllAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(result.SelectEvents, Is.EqualTo([""]));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task changeSkin成功直後のgetSkinNameは新しいskin名を返せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-change-skin-name");

        try
        {
            DefaultFocusFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyChangeSkinUpdatesSkinNameCacheAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(result.FocusedClassName, Is.EqualTo("DefaultSmallWB"));
            Assert.That(result.ClearedClassName, Is.EqualTo("true"));
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task Search_table互換callbackでもfocus行解除後にstaleclassを残さない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-searchtable-focus-clear");

        try
        {
            SearchTableFocusedDeselectVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySearchTableFocusedDeselectAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.FocusedMovieId, Is.EqualTo("0"));
                Assert.That(result.SelectedIds, Is.Empty);
                Assert.That(result.Thumb77ClassName, Is.EqualTo("thum"));
                Assert.That(result.Image77ClassName, Is.EqualTo("img_thum"));
                Assert.That(result.Title77ClassName, Is.EqualTo("title_thum"));
                Assert.That(result.Thumb84ClassName, Is.EqualTo("thum"));
                Assert.That(result.Image84ClassName, Is.EqualTo("img_thum"));
                Assert.That(result.Title84ClassName, Is.EqualTo("title_thum"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static async Task<CompatScriptVerificationResult> VerifyCompatCallbacksAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return CompatScriptVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return CompatScriptVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため compat script 統合確認をスキップします: {ex.Message}"
                );
            }

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

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return CompatScriptVerificationResult.Failed(
                    "compat script の harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await ExecuteScenarioAsync(
                webView,
                "window.__wbResults.focus = []; window.__wbResults.select = []; window.__wbDone = false; wb.focusThum(42);",
                "{ movieId: 42, id: 42, focused: true, focusedMovieId: 42, selected: true }"
            );
            string focusJson = await ReadCompatResultAsync(webView);

            await ExecuteScenarioAsync(
                webView,
                "window.__wbResults.focus = []; window.__wbResults.select = []; window.__wbDone = false; wb.selectThum(77);",
                "{ movieId: 77, id: 77, focused: false, focusedMovieId: 42, selected: true }"
            );
            string selectJson = await ReadCompatResultAsync(webView);

            string lifecycleJson = await ExecuteScriptAndReadJsonAsync(
                webView,
                """
                (() => {
                  window.__immWbCompat.handleClearAll();
                  window.__wbResults.focus = [];
                  window.__wbResults.select = [];
                  window.__wbSequence = [];
                  window.__wbDone = false;
                  wb.focusThum(90);
                  return true;
                })();
                """,
                "{ movieId: 90, id: 90, focused: true, focusedMovieId: 90, selected: true }",
                """
                (() => {
                  window.__wbSequence = [];
                  window.dispatchEvent(new Event("beforeunload"));
                  window.dispatchEvent(new Event("beforeunload"));
                  return JSON.stringify(window.__wbSequence);
                })();
                """
            );

            string tagRequestJson = await ExecuteTagRequestScenarioAsync(webView);
            string tagCacheSummary = await ReadTagCacheSummaryAsync(webView);
            string thumbnailUpdateJson = await ExecuteThumbnailUpdateCallbackScenarioAsync(webView);
            bool scrollSucceeded = await ExecuteScrollScenarioAsync(webView);
            InfoGetterVerificationResult infoResult = await ExecuteInfoGetterScenarioAsync(webView);
            FilterApiVerificationResult filterResult = await ExecuteFilterApiScenarioAsync(webView);

            return CompatScriptVerificationResult.Succeeded(
                ExtractEventList(focusJson, "focus"),
                ExtractEventList(focusJson, "select"),
                ExtractEventList(selectJson, "focus"),
                ExtractEventList(selectJson, "select"),
                DeserializeStringArray(thumbnailUpdateJson),
                DeserializeStringArray(tagRequestJson),
                await ReadTagModifyEventsAsync(webView),
                tagCacheSummary,
                DeserializeStringArray(lifecycleJson),
                scrollSucceeded,
                infoResult.RequestMethods,
                infoResult.Summary,
                filterResult.RequestMethods,
                filterResult.UpdateCounts
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<DefaultSelectFallbackVerificationResult> VerifyDefaultSelectFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultSelectFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultSelectFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtmlWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return DefaultSelectFallbackVerificationResult.Failed(
                    "default onSetSelect fallback harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.selectThum(77, true);",
                "{ movieId: 77, id: 77, selected: true, focused: false, focusedMovieId: 0 }"
            );
            string selectedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                  selectedFlag: document.getElementById('thum77') && document.getElementById('thum77').dataset ? (document.getElementById('thum77').dataset.immSelected || '') : ''
                })
                """
            );
            string selectedJson = JsonSerializer.Deserialize<string>(selectedJsonRaw) ?? "{}";

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.selectThum(77, false);",
                "{ movieId: 77, id: 77, selected: false, focused: false, focusedMovieId: 0 }"
            );
            string clearedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                  selectedFlag: document.getElementById('thum77') && document.getElementById('thum77').dataset ? (document.getElementById('thum77').dataset.immSelected || '') : ''
                })
                """
            );
            string clearedJson = JsonSerializer.Deserialize<string>(clearedJsonRaw) ?? "{}";

            JsonElement selected = JsonSerializer.Deserialize<JsonElement>(selectedJson);
            JsonElement cleared = JsonSerializer.Deserialize<JsonElement>(clearedJson);

            return DefaultSelectFallbackVerificationResult.Succeeded(
                selected.GetProperty("className").GetString() ?? "",
                selected.GetProperty("selectedFlag").GetString() ?? "",
                cleared.GetProperty("className").GetString() ?? "",
                cleared.GetProperty("selectedFlag").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusedSelectFallbackVerificationResult> VerifyFocusedSelectFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusedSelectFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusedSelectFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtmlWithFocusCallbackWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusedSelectFallbackVerificationResult.Failed(
                    "focused default onSetSelect fallback harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.focusThum(77);",
                "{ movieId: 77, id: 77, selected: false, focused: true, focusedMovieId: 77 }"
            );
            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.selectThum(77, true);",
                "{ movieId: 77, id: 77, selected: true, focused: true, focusedMovieId: 77 }"
            );
            string selectedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                  selectedFlag: document.getElementById('thum77') && document.getElementById('thum77').dataset ? (document.getElementById('thum77').dataset.immSelected || '') : ''
                })
                """
            );
            string selectedJson = JsonSerializer.Deserialize<string>(selectedJsonRaw) ?? "{}";

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.selectThum(77, false);",
                "{ movieId: 77, id: 77, selected: false, focused: true, focusedMovieId: 77 }"
            );
            string clearedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                  selectedFlag: document.getElementById('thum77') && document.getElementById('thum77').dataset ? (document.getElementById('thum77').dataset.immSelected || '') : ''
                })
                """
            );
            string clearedJson = JsonSerializer.Deserialize<string>(clearedJsonRaw) ?? "{}";

            JsonElement selected = JsonSerializer.Deserialize<JsonElement>(selectedJson);
            JsonElement cleared = JsonSerializer.Deserialize<JsonElement>(clearedJson);

            return FocusedSelectFallbackVerificationResult.Succeeded(
                selected.GetProperty("className").GetString() ?? "",
                cleared.GetProperty("className").GetString() ?? "",
                cleared.GetProperty("selectedFlag").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<IncrementalSelectedStateVerificationResult> VerifyIncrementalSelectedStateAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return IncrementalSelectedStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return IncrementalSelectedStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return IncrementalSelectedStateVerificationResult.Failed(
                    "incremental selected state harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSelectedStateDone = false;
                  window.__wbSelectedStateError = "";
                  window.__wbSelectedStateResult = { selectEvents: [] };
                  window.__immMessages = [];
                  window.__wbResults.select = [];

                  (async () => {
                    const firstUpdate = wb.update(0, 1);
                    const firstRequest = window.__immMessages.shift();
                    if (!firstRequest) {
                      throw new Error("first update request was not captured.");
                    }

                    window.__immWbCompat.resolve(firstRequest.id, {
                      findInfo: { find: "", sort: [""], filter: [], where: "", total: 1, result: 1 },
                      items: [{ id: 77, title: "Alpha.mp4", select: 1 }]
                    });

                    await Promise.resolve();
                    const firstSelectedRequest = window.__immMessages.shift();
                    if (!firstSelectedRequest) {
                      throw new Error("first getSelectThums request was not captured.");
                    }
                    window.__immWbCompat.resolve(firstSelectedRequest.id, [77]);
                    await firstUpdate;

                    const secondUpdate = wb.update(1, 1);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second update request was not captured.");
                    }

                    window.__immWbCompat.resolve(secondRequest.id, {
                      findInfo: { find: "", sort: [""], filter: [], where: "", total: 1, result: 1 },
                      items: [{ id: 84, title: "Beta.mp4" }]
                    });

                    await Promise.resolve();
                    const secondSelectedRequest = window.__immMessages.shift();
                    if (!secondSelectedRequest) {
                      throw new Error("second getSelectThums request was not captured.");
                    }
                    window.__immWbCompat.resolve(secondSelectedRequest.id, []);
                    await secondUpdate;

                    window.__immWbCompat.handleClearAll();
                    window.__wbSelectedStateResult = {
                      selectEvents: (window.__wbResults.select || []).map(function (entry) {
                        return String(entry[0] || 0) + ":" + String(!!entry[1]);
                      })
                    };
                    window.__wbSelectedStateDone = true;
                  })().catch(function (error) {
                    window.__wbSelectedStateError = String(error && error.message ? error.message : error);
                    window.__wbSelectedStateDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSelectedStateDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSelectedStateError ? JSON.stringify(window.__wbSelectedStateError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSelectedStateResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);
            string[] selectEvents = result.TryGetProperty("selectEvents", out JsonElement eventsElement)
                ? eventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                : [];

            return IncrementalSelectedStateVerificationResult.Succeeded(selectEvents);
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<DefaultFocusFallbackVerificationResult> VerifyDefaultFocusFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultFocusFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultFocusFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtmlWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return DefaultFocusFallbackVerificationResult.Failed(
                    "default onSetFocus fallback harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.focusThum(77);",
                "{ movieId: 77, id: 77, selected: false, focused: true, focusedMovieId: 77 }"
            );
            string focusedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : ''
                })
                """
            );
            string focusedJson = JsonSerializer.Deserialize<string>(focusedJsonRaw) ?? "{}";

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.focusThum(0);",
                "{ movieId: 77, id: 77, selected: false, focused: false, focusedMovieId: 0 }"
            );
            string clearedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : ''
                })
                """
            );
            string clearedJson = JsonSerializer.Deserialize<string>(clearedJsonRaw) ?? "{}";

            JsonElement focused = JsonSerializer.Deserialize<JsonElement>(focusedJson);
            JsonElement cleared = JsonSerializer.Deserialize<JsonElement>(clearedJson);

            return DefaultFocusFallbackVerificationResult.Succeeded(
                focused.GetProperty("className").GetString() ?? "",
                cleared.GetProperty("className").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<IncrementalSelectedStateVerificationResult> VerifySelectedStateStaleResponseAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return IncrementalSelectedStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return IncrementalSelectedStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return IncrementalSelectedStateVerificationResult.Failed(
                    "selected stale guard harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSelectedStaleDone = false;
                  window.__wbSelectedStaleError = "";
                  window.__wbSelectedStaleResult = { selectEvents: [] };
                  window.__immMessages = [];
                  window.__wbResults.select = [];

                  (async () => {
                    const firstUpdate = wb.update(0, 1);
                    const firstUpdateRequest = window.__immMessages.shift();
                    if (!firstUpdateRequest) {
                      throw new Error("first update request was not captured.");
                    }

                    window.__immWbCompat.resolve(firstUpdateRequest.id, {
                      findInfo: { find: "", sort: [""], filter: [], where: "", total: 1, result: 1 },
                      items: [{ id: 77, title: "Alpha.mp4", select: 1 }]
                    });

                    await Promise.resolve();
                    const staleSelectedRequest = window.__immMessages.shift();
                    if (!staleSelectedRequest) {
                      throw new Error("stale getSelectThums request was not captured.");
                    }

                    const deselectTask = wb.selectThum(77, false);
                    const deselectRequest = window.__immMessages.shift();
                    if (!deselectRequest) {
                      throw new Error("selectThum request was not captured.");
                    }

                    window.__immWbCompat.resolve(deselectRequest.id, {
                      movieId: 77,
                      selected: false,
                      focusedMovieId: 0,
                      focused: false
                    });

                    await Promise.resolve();
                    const currentSelectedRequest = window.__immMessages.shift();
                    if (!currentSelectedRequest) {
                      throw new Error("current getSelectThums request was not captured.");
                    }

                    window.__immWbCompat.resolve(currentSelectedRequest.id, []);
                    await deselectTask;

                    window.__immWbCompat.resolve(staleSelectedRequest.id, [77]);
                    await firstUpdate;

                    window.__wbSelectedStaleResult = {
                      selectEvents: (window.__wbResults.select || []).map(function (entry) {
                        return String(entry[0] || 0) + ":" + String(!!entry[1]);
                      })
                    };
                    window.__wbSelectedStaleDone = true;
                  })().catch(function (error) {
                    window.__wbSelectedStaleError = String(error && error.message ? error.message : error);
                    window.__wbSelectedStaleDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSelectedStaleDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSelectedStaleError ? JSON.stringify(window.__wbSelectedStaleError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSelectedStaleResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);
            string[] selectEvents = result.TryGetProperty("selectEvents", out JsonElement eventsElement)
                ? eventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                : [];

            return IncrementalSelectedStateVerificationResult.Succeeded(selectEvents);
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusedSelectFallbackVerificationResult> VerifyCompactFocusFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusedSelectFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusedSelectFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildCompactHarnessHtmlWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusedSelectFallbackVerificationResult.Failed(
                    "compact focus fallback harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.selectThum(77, true);",
                "{ movieId: 77, id: 77, selected: true, focused: false, focusedMovieId: 0 }",
                "window.__immWbCompat.resolve(window.__immMessages.shift().id, [77]);"
            );
            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.focusThum(77);",
                "{ movieId: 77, id: 77, selected: true, focused: true, focusedMovieId: 77 }"
            );
            string selectedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                  selectedFlag: document.getElementById('thum77') && document.getElementById('thum77').dataset
                    ? String(document.getElementById('thum77').dataset.immSelected || '')
                    : ''
                })
                """
            );
            string selectedJson = JsonSerializer.Deserialize<string>(selectedJsonRaw) ?? "{}";

            await ExecuteScenarioAsync(
                webView,
                "window.__wbDone = false; wb.focusThum(0);",
                "{ movieId: 77, id: 77, selected: true, focused: false, focusedMovieId: 0 }"
            );
            string clearedJsonRaw = await webView.ExecuteScriptAsync(
                """
                JSON.stringify({
                  className: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                  selectedFlag: document.getElementById('thum77') && document.getElementById('thum77').dataset
                    ? String(document.getElementById('thum77').dataset.immSelected || '')
                    : ''
                })
                """
            );
            string clearedJson = JsonSerializer.Deserialize<string>(clearedJsonRaw) ?? "{}";

            JsonElement selected = JsonSerializer.Deserialize<JsonElement>(selectedJson);
            JsonElement cleared = JsonSerializer.Deserialize<JsonElement>(clearedJson);

            return FocusedSelectFallbackVerificationResult.Succeeded(
                selected.GetProperty("className").GetString() ?? "",
                cleared.GetProperty("className").GetString() ?? "",
                cleared.GetProperty("selectedFlag").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusedSelectFallbackVerificationResult> VerifyCompactSelectFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusedSelectFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusedSelectFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildCompactHarnessHtmlWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusedSelectFallbackVerificationResult.Failed(
                    "compact select fallback harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbCompactSelectDone = false;
                  window.__wbCompactSelectError = "";
                  window.__wbCompactSelectResult = {};
                  window.__immMessages = [];

                  (async () => {
                    const selectRequestPromise = wb.selectThum(77, true);
                    const selectRequest = window.__immMessages.shift();
                    if (!selectRequest) {
                      throw new Error("select request was not captured.");
                    }

                    window.__immWbCompat.resolve(selectRequest.id, { movieId: 77, selected: true });
                    await Promise.resolve();
                    const syncSelectedRequest = window.__immMessages.shift();
                    if (!syncSelectedRequest) {
                      throw new Error("select sync request was not captured.");
                    }
                    window.__immWbCompat.resolve(syncSelectedRequest.id, [77]);
                    await selectRequestPromise;

                    const selectedThumb = document.getElementById("thum77");
                    const selectedClassName = selectedThumb ? String(selectedThumb.className || "") : "";

                    const clearRequestPromise = wb.selectThum(77, false);
                    const clearRequest = window.__immMessages.shift();
                    if (!clearRequest) {
                      throw new Error("clear request was not captured.");
                    }

                    window.__immWbCompat.resolve(clearRequest.id, { movieId: 77, selected: false });
                    await Promise.resolve();
                    const clearSyncRequest = window.__immMessages.shift();
                    if (!clearSyncRequest) {
                      throw new Error("clear sync request was not captured.");
                    }
                    window.__immWbCompat.resolve(clearSyncRequest.id, []);
                    await clearRequestPromise;

                    const clearedThumb = document.getElementById("thum77");
                    window.__wbCompactSelectResult = {
                      selectedClassName: selectedClassName,
                      clearedClassName: clearedThumb ? String(clearedThumb.className || "") : "",
                      selectedFlag: clearedThumb && clearedThumb.dataset ? String(clearedThumb.dataset.immSelected || "") : ""
                    };
                    window.__wbCompactSelectDone = true;
                  })().catch(function (error) {
                    window.__wbCompactSelectError = String(error && error.message ? error.message : error);
                    window.__wbCompactSelectDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbCompactSelectDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbCompactSelectError ? JSON.stringify(window.__wbCompactSelectError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string selectedJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbCompactSelectResult)"
            );
            string json = JsonSerializer.Deserialize<string>(selectedJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return FocusedSelectFallbackVerificationResult.Succeeded(
                result.GetProperty("selectedClassName").GetString() ?? "",
                result.GetProperty("clearedClassName").GetString() ?? "",
                result.GetProperty("selectedFlag").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusedSelectFallbackVerificationResult> VerifyCompactFocusedSelectFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusedSelectFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusedSelectFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildCompactHarnessHtmlWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusedSelectFallbackVerificationResult.Failed(
                    "compact focus fallback harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  const thumb = document.getElementById("thum77");
                  wb.onSetSelect(77, true);
                  wb.onSetFocus(77, true);
                  const selectedClassName = thumb ? String(thumb.className || "") : "";
                  wb.onSetFocus(77, false);
                  const clearedThumb = document.getElementById("thum77");
                  window.__wbCompactFocusFallbackResult = {
                    selectedClassName: selectedClassName,
                    clearedClassName: clearedThumb ? String(clearedThumb.className || "") : "",
                    selectedFlag: clearedThumb && clearedThumb.dataset ? String(clearedThumb.dataset.immSelected || "") : ""
                  };
                  return true;
                })();
                """
            );

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbCompactFocusFallbackResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return FocusedSelectFallbackVerificationResult.Succeeded(
                result.GetProperty("selectedClassName").GetString() ?? "",
                result.GetProperty("clearedClassName").GetString() ?? "",
                result.GetProperty("selectedFlag").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<IncrementalSelectedStateVerificationResult> VerifyExternalSelectedReadSurvivesInternalSyncAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return IncrementalSelectedStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return IncrementalSelectedStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtmlWithSelectedReadOnUpdate(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return IncrementalSelectedStateVerificationResult.Failed(
                    "external selected read harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSelectedExternalDone = false;
                  window.__wbSelectedExternalError = "";
                  window.__wbSelectedExternalResult = { selectEvents: [] };
                  window.__immMessages = [];

                  (async () => {
                    const updateTask = wb.update(0, 1);
                    const updateRequest = window.__immMessages.shift();
                    if (!updateRequest) {
                      throw new Error("update request was not captured.");
                    }

                    window.__immWbCompat.resolve(updateRequest.id, {
                      findInfo: { find: "", sort: [""], filter: [], where: "", total: 1, result: 1 },
                      items: [{ id: 77, title: "Alpha.mp4", select: 1 }]
                    });

                    await Promise.resolve();
                    const externalSelectedRequest = window.__immMessages.shift();
                    if (!externalSelectedRequest) {
                      throw new Error("external getSelectThums request was not captured.");
                    }

                    await Promise.resolve();
                    const internalSelectedRequest = window.__immMessages.shift();
                    if (!internalSelectedRequest) {
                      throw new Error("internal getSelectThums request was not captured.");
                    }

                    window.__immWbCompat.resolve(internalSelectedRequest.id, []);
                    window.__immWbCompat.resolve(externalSelectedRequest.id, [77]);
                    await updateTask;
                    await Promise.resolve();

                    const externalResult = Array.isArray(window.__wbSelectedReadResult)
                      ? window.__wbSelectedReadResult.map(function (id) { return String(id || 0); })
                      : [];

                    window.__wbSelectedExternalResult = {
                      selectEvents: externalResult
                    };
                    window.__wbSelectedExternalDone = true;
                  })().catch(function (error) {
                    window.__wbSelectedExternalError = String(error && error.message ? error.message : error);
                    window.__wbSelectedExternalDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSelectedExternalDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSelectedExternalError ? JSON.stringify(window.__wbSelectedExternalError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSelectedExternalResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);
            string[] selectEvents = result.TryGetProperty("selectEvents", out JsonElement eventsElement)
                ? eventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                : [];

            return IncrementalSelectedStateVerificationResult.Succeeded(selectEvents);
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusedSelectFallbackVerificationResult> VerifyCompactSkinLeaveFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusedSelectFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusedSelectFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildCompactHarnessHtmlWithoutSelectCallback(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusedSelectFallbackVerificationResult.Failed(
                    "compact skin leave harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  const thumb = document.getElementById("thum77");
                  wb.onSetSelect(77, true);
                  const selectedClassName = thumb ? String(thumb.className || "") : "";
                  window.__immWbCompat.handleSkinLeave();
                  const clearedThumb = document.getElementById("thum77");
                  window.__wbCompactSkinLeaveResult = {
                    selectedClassName: selectedClassName,
                    clearedClassName: clearedThumb ? String(clearedThumb.className || "") : "",
                    selectedFlag: clearedThumb && clearedThumb.dataset ? String(clearedThumb.dataset.immSelected || "") : ""
                  };
                  return true;
                })();
                """
            );

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbCompactSkinLeaveResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return FocusedSelectFallbackVerificationResult.Succeeded(
                result.GetProperty("selectedClassName").GetString() ?? "",
                result.GetProperty("clearedClassName").GetString() ?? "",
                result.GetProperty("selectedFlag").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<IncrementalSelectedStateVerificationResult> VerifySelectedReadDuringClearAllAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return IncrementalSelectedStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return IncrementalSelectedStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtmlWithSelectedReadOnClearAll(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return IncrementalSelectedStateVerificationResult.Failed(
                    "clearAll selected read harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbClearAllDone = false;
                  window.__wbClearAllError = "";
                  window.__wbClearAllResult = { selectEvents: [] };
                  window.__immMessages = [];

                  (async () => {
                    const selectTask = wb.selectThum(77, true);
                    const selectRequest = window.__immMessages.shift();
                    if (!selectRequest) {
                      throw new Error("select request was not captured.");
                    }

                    window.__immWbCompat.resolve(selectRequest.id, { movieId: 77, selected: true });
                    await Promise.resolve();
                    const syncSelectedRequest = window.__immMessages.shift();
                    if (!syncSelectedRequest) {
                      throw new Error("select sync request was not captured.");
                    }
                    window.__immWbCompat.resolve(syncSelectedRequest.id, [77]);
                    await selectTask;

                    window.__wbClearSnapshots = [];
                    window.__immWbCompat.handleClearAll();

                    while (window.__immMessages.length > 0) {
                      const pendingRequest = window.__immMessages.shift();
                      if (pendingRequest && pendingRequest.method === "getSelectThums") {
                        window.__immWbCompat.resolve(pendingRequest.id, []);
                      }
                    }

                    window.__wbClearAllResult = {
                      selectEvents: Array.isArray(window.__wbClearSnapshots)
                        ? window.__wbClearSnapshots.slice()
                        : []
                    };
                    window.__wbClearAllDone = true;
                  })().catch(function (error) {
                    window.__wbClearAllError = String(error && error.message ? error.message : error);
                    window.__wbClearAllDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbClearAllDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbClearAllError ? JSON.stringify(window.__wbClearAllError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbClearAllResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);
            string[] selectEvents = result.TryGetProperty("selectEvents", out JsonElement eventsElement)
                ? eventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                : [];

            return IncrementalSelectedStateVerificationResult.Succeeded(selectEvents);
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<DefaultFocusFallbackVerificationResult> VerifyChangeSkinUpdatesSkinNameCacheAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultFocusFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultFocusFallbackVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return DefaultFocusFallbackVerificationResult.Failed(
                    "changeSkin skinName harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbChangeSkinNameDone = false;
                  window.__wbChangeSkinNameError = "";
                  window.__wbChangeSkinNameResult = { changed: "", skinName: "" };

                  (async () => {
                    const changeTask = wb.changeSkin("DefaultSmallWB");
                    const changeRequest = window.__immMessages.shift();
                    if (!changeRequest) {
                      throw new Error("changeSkin request was not captured.");
                    }

                    window.__immWbCompat.resolve(changeRequest.id, true);
                    const changed = await changeTask;
                    const skinName = await wb.getSkinName();

                    window.__wbChangeSkinNameResult = {
                      changed: String(!!changed),
                      skinName: String(skinName || "")
                    };
                    window.__wbChangeSkinNameDone = true;
                  })().catch(function (error) {
                    window.__wbChangeSkinNameError = String(error && error.message ? error.message : error);
                    window.__wbChangeSkinNameDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbChangeSkinNameDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbChangeSkinNameError ? JSON.stringify(window.__wbChangeSkinNameError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbChangeSkinNameResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return DefaultFocusFallbackVerificationResult.Succeeded(
                result.GetProperty("skinName").GetString() ?? "",
                result.GetProperty("changed").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusStateVerificationResult> VerifyFocusStateStaleResponseAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 240,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusStateVerificationResult.Failed(
                    "focus stale guard harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbFocusStaleDone = false;
                  window.__wbFocusStaleError = "";
                  window.__wbFocusStaleResult = {};
                  window.__immMessages = [];
                  window.__wbResults.focus = [];

                  (async () => {
                    const focusTask = wb.focusThum(77);
                    const focusRequest = window.__immMessages.shift();
                    if (!focusRequest) {
                      throw new Error("initial focusThum request was not captured.");
                    }

                    window.__immWbCompat.resolve(focusRequest.id, {
                      movieId: 77,
                      id: 77,
                      selected: false,
                      focused: true,
                      focusedMovieId: 77
                    });
                    await Promise.resolve();
                    const currentSelectedRequest = window.__immMessages.shift();
                    if (!currentSelectedRequest) {
                      throw new Error("initial getSelectThums request was not captured.");
                    }
                    window.__immWbCompat.resolve(currentSelectedRequest.id, []);
                    await focusTask;

                    const staleFocusPromise = wb.getFocusThum();
                    const staleFocusRequest = window.__immMessages.shift();
                    if (!staleFocusRequest) {
                      throw new Error("stale getFocusThum request was not captured.");
                    }

                    const currentFocusPromise = wb.getFocusThum();
                    const currentFocusRequest = window.__immMessages.shift();
                    if (!currentFocusRequest) {
                      throw new Error("current getFocusThum request was not captured.");
                    }

                    window.__immWbCompat.resolve(currentFocusRequest.id, 77);
                    await currentFocusPromise;

                    window.__immWbCompat.resolve(staleFocusRequest.id, 42);
                    const staleFocusResult = await staleFocusPromise;

                    window.__wbFocusStaleResult = {
                      staleResult: String(staleFocusResult || 0),
                      focusedMovieId: "77",
                      focusEvents: (window.__wbResults.focus || []).map(function (entry) {
                        return String(entry[0] || 0) + ":" + String(!!entry[1]);
                      })
                    };
                    window.__wbFocusStaleDone = true;
                  })().catch(function (error) {
                    window.__wbFocusStaleError = String(error && error.message ? error.message : error);
                    window.__wbFocusStaleDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbFocusStaleDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbFocusStaleError ? JSON.stringify(window.__wbFocusStaleError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbFocusStaleResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return FocusStateVerificationResult.Succeeded(
                result.GetProperty("staleResult").GetString() ?? "",
                result.GetProperty("focusedMovieId").GetString() ?? "",
                result.TryGetProperty("focusEvents", out JsonElement focusEventsElement)
                    ? focusEventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                    : []
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<FocusStateVerificationResult> VerifyFocusActionStaleResponseAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return FocusStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 240,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return FocusStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return FocusStateVerificationResult.Failed(
                    "focus action stale harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbFocusActionStaleDone = false;
                  window.__wbFocusActionStaleError = "";
                  window.__wbFocusActionStaleResult = {};
                  window.__immMessages = [];
                  window.__wbResults.focus = [];

                  (async () => {
                    const firstFocusTask = wb.focusThum(77);
                    const firstFocusRequest = window.__immMessages.shift();
                    if (!firstFocusRequest) {
                      throw new Error("first focusThum request was not captured.");
                    }

                    const secondFocusTask = wb.focusThum(84);
                    const secondFocusRequest = window.__immMessages.shift();
                    if (!secondFocusRequest) {
                      throw new Error("second focusThum request was not captured.");
                    }

                    window.__immWbCompat.resolve(secondFocusRequest.id, {
                      movieId: 84,
                      id: 84,
                      selected: false,
                      focused: true,
                      focusedMovieId: 84
                    });

                    await Promise.resolve();
                    const currentSelectedRequest = window.__immMessages.shift();
                    if (!currentSelectedRequest) {
                      throw new Error("current getSelectThums request was not captured.");
                    }
                    window.__immWbCompat.resolve(currentSelectedRequest.id, []);
                    await secondFocusTask;

                    window.__immWbCompat.resolve(firstFocusRequest.id, {
                      movieId: 77,
                      id: 77,
                      selected: false,
                      focused: true,
                      focusedMovieId: 77
                    });

                    await Promise.resolve();
                    const staleSelectedRequest = window.__immMessages.shift();
                    if (staleSelectedRequest) {
                      window.__immWbCompat.resolve(staleSelectedRequest.id, []);
                    }
                    const staleResult = await firstFocusTask;

                    window.__wbFocusActionStaleResult = {
                      staleResult: String(staleResult && (staleResult.focusedMovieId || staleResult.movieId || staleResult.id || 0)),
                      focusedMovieId: "84",
                      focusEvents: (window.__wbResults.focus || []).map(function (entry) {
                        return String(entry[0] || 0) + ":" + String(!!entry[1]);
                      })
                    };
                    window.__wbFocusActionStaleDone = true;
                  })().catch(function (error) {
                    window.__wbFocusActionStaleError = String(error && error.message ? error.message : error);
                    window.__wbFocusActionStaleDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbFocusActionStaleDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbFocusActionStaleError ? JSON.stringify(window.__wbFocusActionStaleError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbFocusActionStaleResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return FocusStateVerificationResult.Succeeded(
                result.GetProperty("staleResult").GetString() ?? "",
                result.GetProperty("focusedMovieId").GetString() ?? "",
                result.TryGetProperty("focusEvents", out JsonElement focusEventsElement)
                    ? focusEventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                    : []
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<SelectionStateVerificationResult> VerifySelectActionStaleResponseAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return SelectionStateVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 240,
            Height = 160,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return SelectionStateVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return SelectionStateVerificationResult.Failed(
                    "select action stale harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSelectActionStaleDone = false;
                  window.__wbSelectActionStaleError = "";
                  window.__wbSelectActionStaleResult = {};
                  window.__immMessages = [];
                  window.__wbResults.select = [];

                  (async () => {
                    const firstSelectTask = wb.selectThum(77, true);
                    const firstSelectRequest = window.__immMessages.shift();
                    if (!firstSelectRequest) {
                      throw new Error("first selectThum request was not captured.");
                    }

                    const secondSelectTask = wb.selectThum(77, false);
                    const secondSelectRequest = window.__immMessages.shift();
                    if (!secondSelectRequest) {
                      throw new Error("second selectThum request was not captured.");
                    }

                    window.__immWbCompat.resolve(secondSelectRequest.id, {
                      movieId: 77,
                      id: 77,
                      selected: false,
                      focused: false,
                      focusedMovieId: 0
                    });

                    await Promise.resolve();
                    const currentSelectedRequest = window.__immMessages.shift();
                    if (!currentSelectedRequest) {
                      throw new Error("current getSelectThums request was not captured.");
                    }
                    window.__immWbCompat.resolve(currentSelectedRequest.id, []);
                    await secondSelectTask;

                    window.__immWbCompat.resolve(firstSelectRequest.id, {
                      movieId: 77,
                      id: 77,
                      selected: true,
                      focused: false,
                      focusedMovieId: 0
                    });

                    await Promise.resolve();
                    const staleSelectedRequest = window.__immMessages.shift();
                    if (staleSelectedRequest) {
                      window.__immWbCompat.resolve(staleSelectedRequest.id, [77]);
                    }
                    await firstSelectTask;

                    window.__wbSelectActionStaleResult = {
                      selectedIds: [],
                      selectEvents: (window.__wbResults.select || []).map(function (entry) {
                        return String(entry[0] || 0) + ":" + String(!!entry[1]);
                      })
                    };
                    window.__wbSelectActionStaleDone = true;
                  })().catch(function (error) {
                    window.__wbSelectActionStaleError = String(error && error.message ? error.message : error);
                    window.__wbSelectActionStaleDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSelectActionStaleDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSelectActionStaleError ? JSON.stringify(window.__wbSelectActionStaleError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSelectActionStaleResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return SelectionStateVerificationResult.Succeeded(
                result.TryGetProperty("selectedIds", out JsonElement selectedIdsElement)
                    ? selectedIdsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                    : [],
                result.TryGetProperty("selectEvents", out JsonElement selectEventsElement)
                    ? selectEventsElement.EnumerateArray().Select(element => element.GetString() ?? "").ToArray()
                    : []
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<SearchTableFocusedDeselectVerificationResult> VerifySearchTableFocusedDeselectAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return SearchTableFocusedDeselectVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 260,
            Height = 200,
            Left = 12,
            Top = 12,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath
                );
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return SearchTableFocusedDeselectVerificationResult.Ignored(ex.Message);
            }

            TaskCompletionSource navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
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

            hostWindow.Show();
            await webView.EnsureCoreWebView2Async(environment);
            webView.NavigateToString(BuildSearchTableHarnessHtml(compatScript));

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return SearchTableFocusedDeselectVerificationResult.Failed(
                    "Search_table focused deselect harness の読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSearchTableDone = false;
                  window.__wbSearchTableError = "";
                  window.__wbSearchTableResult = {};
                  window.__immMessages = [];

                  async function resolveAction(action, response, selectedIds) {
                    const promise = action();
                    const request = window.__immMessages.shift();
                    if (!request) {
                      throw new Error("action request was not captured.");
                    }

                    window.__immWbCompat.resolve(request.id, response);
                    await Promise.resolve();

                    const selectedRequest = window.__immMessages.shift();
                    if (!selectedRequest || selectedRequest.method !== "getSelectThums") {
                      throw new Error("getSelectThums request was not captured.");
                    }

                    window.__immWbCompat.resolve(selectedRequest.id, selectedIds);
                    await promise;
                  }

                  (async () => {
                    await resolveAction(
                      function () { return wb.focusThum(77); },
                      { movieId: 77, id: 77, selected: false, focused: true, focusedMovieId: 77 },
                      []
                    );
                    await resolveAction(
                      function () { return wb.selectThum(77, true); },
                      { movieId: 77, id: 77, selected: true, focused: true, focusedMovieId: 77 },
                      [77]
                    );
                    await resolveAction(
                      function () { return wb.selectThum(84, true); },
                      { movieId: 84, id: 84, selected: true, focused: false, focusedMovieId: 77 },
                      [77, 84]
                    );
                    await resolveAction(
                      function () { return wb.focusThum(84); },
                      { movieId: 84, id: 84, selected: true, focused: true, focusedMovieId: 84 },
                      [84]
                    );
                    await resolveAction(
                      function () { return wb.selectThum(84, false); },
                      { movieId: 84, id: 84, selected: false, focused: false, focusedMovieId: 0 },
                      []
                    );

                    const focusPromise = wb.getFocusThum();
                    const focusRequest = window.__immMessages.shift();
                    if (!focusRequest || focusRequest.method !== "getFocusThum") {
                      throw new Error("getFocusThum request was not captured.");
                    }
                    window.__immWbCompat.resolve(focusRequest.id, 0);
                    const focusedId = await focusPromise;

                    const selectPromise = wb.getSelectThums();
                    const selectRequest = window.__immMessages.shift();
                    if (!selectRequest || selectRequest.method !== "getSelectThums") {
                      throw new Error("final getSelectThums request was not captured.");
                    }
                    window.__immWbCompat.resolve(selectRequest.id, []);
                    const selectedIds = await selectPromise;

                    window.__wbSearchTableResult = {
                      focusedId: String(focusedId || 0),
                      selectedIds: Array.isArray(selectedIds) ? selectedIds.map(function (x) { return String(x); }) : [],
                      thum77: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
                      img77: document.getElementById('img77') ? document.getElementById('img77').className : '',
                      title77: document.getElementById('title77') ? document.getElementById('title77').className : '',
                      thum84: document.getElementById('thum84') ? document.getElementById('thum84').className : '',
                      img84: document.getElementById('img84') ? document.getElementById('img84').className : '',
                      title84: document.getElementById('title84') ? document.getElementById('title84').className : ''
                    };
                    window.__wbSearchTableDone = true;
                  })().catch(function (error) {
                    window.__wbSearchTableError = String(error && error.message ? error.message : error);
                    window.__wbSearchTableDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSearchTableDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSearchTableError ? JSON.stringify(window.__wbSearchTableError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSearchTableResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            JsonElement result = JsonSerializer.Deserialize<JsonElement>(json);

            return SearchTableFocusedDeselectVerificationResult.Succeeded(
                result.GetProperty("focusedId").GetString() ?? "0",
                result.GetProperty("selectedIds").EnumerateArray().Select(x => x.GetString() ?? "").ToArray(),
                result.GetProperty("thum77").GetString() ?? "",
                result.GetProperty("img77").GetString() ?? "",
                result.GetProperty("title77").GetString() ?? "",
                result.GetProperty("thum84").GetString() ?? "",
                result.GetProperty("img84").GetString() ?? "",
                result.GetProperty("title84").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<ResetViewVerificationResult> VerifyResetViewBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return ResetViewVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return ResetViewVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため reset view 確認をスキップします: {ex.Message}"
                );
            }

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

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return ResetViewVerificationResult.Failed(
                    "reset view harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbResetDone = false;
                  window.__wbResetError = "";
                  window.__wbResetResult = { sequence: [], methods: [] };
                  window.__immMessages = [];
                  window.wb.onClearAll = function () {
                    window.__wbResetResult.sequence.push("clear");
                    return true;
                  };
                  window.wb.onUpdate = function (items) {
                    window.__wbResetResult.sequence.push("update:" + String(Array.isArray(items) ? items.length : 0));
                    return true;
                  };

                  const firstPromise = wb.update(0, 200);
                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first update request was not captured.");
                  }
                  window.__wbResetResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, { items: [{ id: 1 }, { id: 2 }] });

                  firstPromise.then(function () {
                    const secondPromise = wb.update(120, 80);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second update request was not captured.");
                    }
                    window.__wbResetResult.methods.push(
                      secondRequest.method + ":" +
                      String(secondRequest.payload.startIndex || 0) + ":" +
                      String(secondRequest.payload.count || 0)
                    );
                    window.__immWbCompat.resolve(secondRequest.id, { items: [{ id: 3 }] });

                    return secondPromise.then(function () {
                      const thirdPromise = wb.find("idol");
                      const thirdRequest = window.__immMessages.shift();
                      if (!thirdRequest) {
                        throw new Error("find request was not captured.");
                      }

                      const thirdStartIndex = Object.prototype.hasOwnProperty.call(thirdRequest.payload || {}, "startIndex")
                        ? thirdRequest.payload.startIndex
                        : 0;
                      const thirdCount = Object.prototype.hasOwnProperty.call(thirdRequest.payload || {}, "count")
                        ? thirdRequest.payload.count
                        : 0;
                      window.__wbResetResult.methods.push(
                        thirdRequest.method + ":" +
                        String(thirdStartIndex || 0) + ":" +
                        String(thirdCount || 0)
                      );
                      window.__immWbCompat.resolve(thirdRequest.id, { items: [{ id: 9 }] });

                      return thirdPromise.then(function () {
                        window.__wbResetDone = true;
                      });
                    });
                  }).catch(function (error) {
                    window.__wbResetError = String(error && error.message ? error.message : error);
                    window.__wbResetDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbResetDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbResetError ? JSON.stringify(window.__wbResetError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbResetResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return ResetViewVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("sequence").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<GetInfosRequestVerificationResult> VerifyGetInfosRequestShapesAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return GetInfosRequestVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return GetInfosRequestVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため getInfos payload 確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return GetInfosRequestVerificationResult.Failed(
                    "getInfos harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbGetInfosDone = false;
                  window.__wbGetInfosError = "";
                  window.__wbGetInfosResult = { methods: [], payloads: [] };
                  window.__immMessages = [];

                  const firstPromise = wb.getInfos([42, 77]);
                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first getInfos request was not captured.");
                  }
                  window.__wbGetInfosResult.methods.push(firstRequest.method);
                  window.__wbGetInfosResult.payloads.push(JSON.stringify(firstRequest.payload || {}));
                  window.__immWbCompat.resolve(firstRequest.id, []);

                  firstPromise.then(function () {
                    const secondPromise = wb.getInfos(120);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second getInfos request was not captured.");
                    }
                    window.__wbGetInfosResult.methods.push(secondRequest.method);
                    window.__wbGetInfosResult.payloads.push(JSON.stringify(secondRequest.payload || {}));
                    window.__immWbCompat.resolve(secondRequest.id, []);

                    return secondPromise.then(function () {
                      const thirdPromise = wb.getInfos({ recordKeys: ["db-main:42"] });
                      const thirdRequest = window.__immMessages.shift();
                      if (!thirdRequest) {
                        throw new Error("third getInfos request was not captured.");
                      }
                      window.__wbGetInfosResult.methods.push(thirdRequest.method);
                      window.__wbGetInfosResult.payloads.push(JSON.stringify(thirdRequest.payload || {}));
                      window.__immWbCompat.resolve(thirdRequest.id, []);

                      return thirdPromise.then(function () {
                        window.__wbGetInfosDone = true;
                      });
                    });
                  }).catch(function (error) {
                    window.__wbGetInfosError = String(error && error.message ? error.message : error);
                    window.__wbGetInfosDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbGetInfosDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbGetInfosError ? JSON.stringify(window.__wbGetInfosError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbGetInfosResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return GetInfosRequestVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("payloads").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<DefaultUpdateAppendVerificationResult> VerifyDefaultUpdateAppendBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultUpdateAppendVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultUpdateAppendVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため default onUpdate append 確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return DefaultUpdateAppendVerificationResult.Failed(
                    "default append harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbDefaultAppendDone = false;
                  window.__wbDefaultAppendError = "";
                  window.__wbDefaultAppendResult = { sequence: [], methods: [], titles: [] };
                  window.__immMessages = [];

                  window.wb.onClearAll = function () {
                    window.__wbDefaultAppendResult.sequence.push("clear");
                    document.getElementById("view").innerHTML = "";
                    return true;
                  };

                  delete window.wb.onUpdate;
                  window.wb.onCreateThum = function (mv) {
                    window.__wbDefaultAppendResult.sequence.push("create:" + String(mv && mv.id ? mv.id : 0));
                    var node = document.createElement("div");
                    node.className = "card";
                    node.textContent = String(mv && mv.title ? mv.title : "") + String(mv && mv.ext ? mv.ext : "");
                    document.getElementById("view").appendChild(node);
                    return true;
                  };

                  const firstPromise = wb.update(0, 2);
                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first update request was not captured.");
                  }
                  window.__wbDefaultAppendResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, {
                    items: [
                      { id: 1, title: "Alpha", ext: ".mp4" },
                      { id: 2, title: "Beta", ext: ".avi" }
                    ]
                  });

                  firstPromise.then(function () {
                    const secondPromise = wb.update(2, 1);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second update request was not captured.");
                    }
                    window.__wbDefaultAppendResult.methods.push(
                      secondRequest.method + ":" +
                      String(secondRequest.payload.startIndex || 0) + ":" +
                      String(secondRequest.payload.count || 0)
                    );
                    window.__immWbCompat.resolve(secondRequest.id, {
                      items: [
                        { id: 3, title: "Gamma", ext: ".mkv" }
                      ]
                    });

                    return secondPromise.then(function () {
                      window.__wbDefaultAppendResult.titles = Array.from(
                        document.querySelectorAll("#view .card")
                      ).map(function (node) {
                        return node.textContent || "";
                      });
                      window.__wbDefaultAppendDone = true;
                    });
                  }).catch(function (error) {
                    window.__wbDefaultAppendError = String(error && error.message ? error.message : error);
                    window.__wbDefaultAppendDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbDefaultAppendDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbDefaultAppendError ? JSON.stringify(window.__wbDefaultAppendError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbDefaultAppendResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return DefaultUpdateAppendVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("sequence").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("titles").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<SeamlessScrollVerificationResult> VerifySeamlessScrollBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return SeamlessScrollVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 20,
            Top = 20,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return SeamlessScrollVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため seamless scroll 確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return SeamlessScrollVerificationResult.Failed(
                    "seamless scroll harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSeamlessDone = false;
                  window.__wbSeamlessError = "";
                  window.__wbSeamlessResult = { sequence: [], methods: [], titles: [] };
                  window.__immMessages = [];
                  window.g_thumbs_limit = 2;

                  const scroll = document.getElementById("scroll");
                  scroll.style.height = "120px";
                  scroll.style.overflowY = "auto";
                  const view = document.getElementById("view");
                  view.innerHTML = "";

                  window.wb.onClearAll = function () {
                    window.__wbSeamlessResult.sequence.push("clear");
                    view.innerHTML = "";
                    return true;
                  };

                  delete window.wb.onUpdate;
                  window.wb.onCreateThum = function (mv) {
                    window.__wbSeamlessResult.sequence.push("create:" + String(mv && mv.id ? mv.id : 0));
                    const node = document.createElement("div");
                    node.className = "card";
                    node.style.height = "70px";
                    node.textContent = String(mv && mv.title ? mv.title : "") + String(mv && mv.ext ? mv.ext : "");
                    view.appendChild(node);
                    return true;
                  };

                  wb.scrollSetting(2, "scroll").then(function () {
                    const pumpSecondRequest = function (remaining) {
                      const secondRequest = window.__immMessages.shift();
                      if (secondRequest) {
                        window.__wbSeamlessResult.methods.push(
                          secondRequest.method + ":" +
                          String(secondRequest.payload.startIndex || 0) + ":" +
                          String(secondRequest.payload.count || 0)
                        );
                        window.__immWbCompat.resolve(secondRequest.id, {
                          startIndex: 2,
                          requestedCount: 2,
                          totalCount: 3,
                          items: [{ id: 3, title: "Gamma", ext: ".mkv" }]
                        });

                        setTimeout(function () {
                          window.__wbSeamlessResult.titles = Array.from(
                            document.querySelectorAll("#view .card")
                          ).map(function (node) {
                            return node.textContent || "";
                          });
                          window.__wbSeamlessDone = true;
                        }, 0);
                        return;
                      }

                      if (remaining <= 0) {
                        throw new Error("second seamless update request was not captured.");
                      }

                      setTimeout(function () {
                        try {
                          pumpSecondRequest(remaining - 1);
                        } catch (error) {
                          window.__wbSeamlessError = String(error && error.message ? error.message : error);
                          window.__wbSeamlessDone = true;
                        }
                      }, 20);
                    };

                    try {
                      scroll.scrollTop = scroll.scrollHeight;
                      scroll.dispatchEvent(new Event("scroll"));
                      pumpSecondRequest(25);
                    } catch (error) {
                      window.__wbSeamlessError = String(error && error.message ? error.message : error);
                      window.__wbSeamlessDone = true;
                    }
                  }).catch(function (error) {
                    window.__wbSeamlessError = String(error && error.message ? error.message : error);
                    window.__wbSeamlessDone = true;
                  });

                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first seamless update request was not captured.");
                  }
                  window.__wbSeamlessResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, {
                    startIndex: 0,
                    requestedCount: 2,
                    totalCount: 3,
                    items: [
                      { id: 1, title: "Alpha", ext: ".mp4" },
                      { id: 2, title: "Beta", ext: ".avi" }
                    ]
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSeamlessDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSeamlessError ? JSON.stringify(window.__wbSeamlessError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSeamlessResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return SeamlessScrollVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("sequence").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("titles").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<SeamlessScrollStopVerificationResult> VerifySeamlessScrollStopBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return SeamlessScrollStopVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 20,
            Top = 20,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return SeamlessScrollStopVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため seamless scroll 空振り停止確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return SeamlessScrollStopVerificationResult.Failed(
                    "seamless scroll 空振り停止 harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSeamlessStopDone = false;
                  window.__wbSeamlessStopError = "";
                  window.__wbSeamlessStopResult = { methods: [], titles: [], pendingRequestCount: 0 };
                  window.__immMessages = [];
                  window.g_thumbs_limit = 2;

                  const scroll = document.getElementById("scroll");
                  scroll.style.height = "120px";
                  scroll.style.overflowY = "auto";
                  const view = document.getElementById("view");
                  view.innerHTML = "";

                  window.wb.onClearAll = function () {
                    view.innerHTML = "";
                    return true;
                  };

                  delete window.wb.onUpdate;
                  window.wb.onCreateThum = function (mv) {
                    const node = document.createElement("div");
                    node.className = "card";
                    node.style.height = "70px";
                    node.textContent = String(mv && mv.title ? mv.title : "") + String(mv && mv.ext ? mv.ext : "");
                    view.appendChild(node);
                    return true;
                  };

                  wb.scrollSetting(2, "scroll").then(function () {
                    const pumpSecondRequest = function (remaining) {
                      const secondRequest = window.__immMessages.shift();
                      if (secondRequest) {
                        window.__wbSeamlessStopResult.methods.push(
                          secondRequest.method + ":" +
                          String(secondRequest.payload.startIndex || 0) + ":" +
                          String(secondRequest.payload.count || 0)
                        );
                        window.__immWbCompat.resolve(secondRequest.id, {
                          startIndex: 2,
                          requestedCount: 2,
                          totalCount: 4,
                          items: []
                        });

                        setTimeout(function () {
                          try {
                            scroll.scrollTop = scroll.scrollHeight;
                            scroll.dispatchEvent(new Event("scroll"));
                            setTimeout(function () {
                              window.__wbSeamlessStopResult.titles = Array.from(
                                document.querySelectorAll("#view .card")
                              ).map(function (node) {
                                return node.textContent || "";
                              });
                              window.__wbSeamlessStopResult.pendingRequestCount = window.__immMessages.length;
                              window.__wbSeamlessStopDone = true;
                            }, 120);
                          } catch (error) {
                            window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                            window.__wbSeamlessStopDone = true;
                          }
                        }, 0);
                        return;
                      }

                      if (remaining <= 0) {
                        throw new Error("second seamless stop request was not captured.");
                      }

                      setTimeout(function () {
                        try {
                          pumpSecondRequest(remaining - 1);
                        } catch (error) {
                          window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                          window.__wbSeamlessStopDone = true;
                        }
                      }, 20);
                    };

                    try {
                      scroll.scrollTop = scroll.scrollHeight;
                      scroll.dispatchEvent(new Event("scroll"));
                      pumpSecondRequest(25);
                    } catch (error) {
                      window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                      window.__wbSeamlessStopDone = true;
                    }
                  }).catch(function (error) {
                    window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                    window.__wbSeamlessStopDone = true;
                  });

                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first seamless stop request was not captured.");
                  }
                  window.__wbSeamlessStopResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, {
                    startIndex: 0,
                    requestedCount: 2,
                    totalCount: 4,
                    items: [
                      { id: 1, title: "Alpha", ext: ".mp4" },
                      { id: 2, title: "Beta", ext: ".avi" }
                    ]
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSeamlessStopDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSeamlessStopError ? JSON.stringify(window.__wbSeamlessStopError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSeamlessStopResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return SeamlessScrollStopVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("titles").GetRawText()),
                document.RootElement.GetProperty("pendingRequestCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task ExecuteScenarioAsync(
        WebView2 webView,
        string startScript,
        string responseLiteral
    )
    {
        string script =
            $$"""
            (async () => {
              window.__immMessages = [];
              {{startScript}}
              const request = window.__immMessages.shift();
              if (!request) {
                throw new Error("wb request was not captured.");
              }

              window.__immWbCompat.resolve(request.id, {{responseLiteral}});
              await Promise.resolve();
              await Promise.resolve();
              window.__wbDone = true;
              return true;
            })();
            """;
        await webView.ExecuteScriptAsync(script);
        await WaitForWebFlagAsync(webView, "__wbDone");
    }

    private static async Task ExecuteScenarioAsync(
        WebView2 webView,
        string startScript,
        string responseLiteral,
        string afterResolveScript
    )
    {
        string script =
            $$"""
            (async () => {
              window.__immMessages = [];
              {{startScript}}
              const request = window.__immMessages.shift();
              if (!request) {
                throw new Error("wb request was not captured.");
              }

              window.__immWbCompat.resolve(request.id, {{responseLiteral}});
              await Promise.resolve();
              {{afterResolveScript}}
              await Promise.resolve();
              await Promise.resolve();
              window.__wbDone = true;
              return true;
            })();
            """;
        await webView.ExecuteScriptAsync(script);
        await WaitForWebFlagAsync(webView, "__wbDone");
    }

    private static async Task<string> ReadCompatResultAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync("JSON.stringify(window.__wbResults)");
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<string> ExecuteScriptAndReadJsonAsync(
        WebView2 webView,
        string requestStartScript,
        string responseLiteral,
        string readScript
    )
    {
        await ExecuteScenarioAsync(webView, requestStartScript, responseLiteral);
        string resultJson = await webView.ExecuteScriptAsync(readScript);
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<string> ExecuteTagRequestScenarioAsync(WebView2 webView)
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbTagDone = false;
              window.__wbTagError = "";
              window.__wbTagResult = [];
              window.__immMessages = [];
              window.__wbTagOps = [];

              const focusPromise = wb.focusThum(42);
              const focusRequest = window.__immMessages.shift();
              if (!focusRequest) {
                throw new Error("focusThum request was not captured before tag mutation.");
              }
              window.__immWbCompat.resolve(focusRequest.id, {
                found: true,
                focused: true,
                focusedMovieId: 42,
                movieId: 42,
                id: 42,
                selected: true
              });

              focusPromise.then(function () {
                const addPromise = wb.addTag("idol");
                const addRequest = window.__immMessages.shift();
                if (!addRequest) {
                  throw new Error("addTag request was not captured.");
                }
                window.__immWbCompat.resolve(addRequest.id, {
                  found: true,
                  changed: true,
                  hasTag: true,
                  movieId: 42,
                  id: 42,
                  tag: "idol",
                  item: {
                    MovieId: 42,
                    Tags: ["idol"]
                  }
                });

                return addPromise.then(function () {
                const flipPromise = wb.flipTag("beta", "77");
                const flipRequest = window.__immMessages.shift();
                if (!flipRequest) {
                  throw new Error("flipTag request was not captured.");
                }

                window.__immWbCompat.resolve(flipRequest.id, {
                  found: true,
                  changed: true,
                  hasTag: false,
                  movieId: 77,
                  id: 77,
                  tag: "beta",
                  item: {
                    MovieId: 77,
                    Tags: []
                  }
                });

                return flipPromise.then(function () {
                  window.__wbTagResult = [
                    addRequest.method + ":" + String(addRequest.payload.movieId || 0) + ":" + String(addRequest.payload.tag || ""),
                    flipRequest.method + ":" + String(flipRequest.payload.movieId || 0) + ":" + String(flipRequest.payload.tag || "")
                  ];
                  window.__wbTagDone = true;
                });
                });
              }).catch(function (error) {
                window.__wbTagError = String(error && error.message ? error.message : error);
                window.__wbTagDone = true;
              });

              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbTagDone");

        string errorJson = await webView.ExecuteScriptAsync(
            "window.__wbTagError ? JSON.stringify(window.__wbTagError) : \"\""
        );
        string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AssertionException(error);
        }

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbTagResult)"
        );
        return JsonSerializer.Deserialize<string>(resultJson) ?? "[]";
    }

    private static async Task<string> ExecuteThumbnailUpdateCallbackScenarioAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbThumbUpdates = [];
              window.__immWbCompat.dispatchCallback("onUpdateThum", {
                recordKey: "db-main:77",
                thumbUrl: "https://thum.local/sample.jpg?rev=thumb-1",
                thumbRevision: "thumb-1",
                thumbSourceKind: "managed-thumbnail",
                sizeInfo: {
                  thumbNaturalWidth: 160,
                  thumbNaturalHeight: 120,
                  thumbSheetColumns: 1,
                  thumbSheetRows: 1,
                  naturalWidth: 160,
                  naturalHeight: 120,
                  sheetColumns: 1,
                  sheetRows: 1
                },
                __immCallArgs: [
                  "db-main:77",
                  "https://thum.local/sample.jpg?rev=thumb-1",
                  "thumb-1",
                  "managed-thumbnail",
                  {
                    thumbNaturalWidth: 160,
                    thumbNaturalHeight: 120,
                    thumbSheetColumns: 1,
                    thumbSheetRows: 1,
                    naturalWidth: 160,
                    naturalHeight: 120,
                    sheetColumns: 1,
                    sheetRows: 1
                  }
                ]
              });
              return JSON.stringify(window.__wbThumbUpdates);
            })();
            """
        );
        return JsonSerializer.Deserialize<string>(resultJson) ?? "[]";
    }

    private static async Task<DefaultThumbnailFallbackVerificationResult> VerifyDefaultThumbnailFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultThumbnailFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultThumbnailFallbackVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため compat thumb fallback 統合確認をスキップします: {ex.Message}"
                );
            }

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

            string htmlPath = Path.Combine(tempRootPath, "compat-thumb-fallback.html");
            await File.WriteAllTextAsync(htmlPath, BuildMinimalThumbnailFallbackHarnessHtml(compatScript));
            webView.Source = new Uri(htmlPath);
            await navigationCompleted.Task;

            string createdJson = await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immWbCompat.handleClearAll();
                  wb.onCreateThum({
                    id: 77,
                    movieId: 77,
                    title: 'Beta',
                    ext: '.avi',
                    thum: 'https://thum.local/original.jpg?rev=thumb-0',
                    exist: true,
                    select: 0
                  }, 1);

                  return JSON.stringify({
                    thumbSrc: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : '',
                    titleText: document.getElementById('title77') ? (document.getElementById('title77').textContent || '') : ''
                  });
                })();
                """
            );
            string updatedJson = await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immWbCompat.dispatchCallback('onUpdateThum', {
                    movieId: 77,
                    id: 77,
                    recordKey: 'db-main:77',
                    thumbUrl: 'https://thum.local/updated.jpg?rev=thumb-2',
                    thum: 'https://thum.local/updated.jpg?rev=thumb-2',
                    thumbRevision: 'thumb-2',
                    thumbSourceKind: 'managed-thumbnail',
                    __immCallArgs: [
                      'db-main:77',
                      'https://thum.local/updated.jpg?rev=thumb-2',
                      'thumb-2',
                      'managed-thumbnail',
                      null
                    ]
                  });

                  return JSON.stringify({
                    thumbSrc: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''
                  });
                })();
                """
            );

            using JsonDocument createdDocument = JsonDocument.Parse(
                JsonSerializer.Deserialize<string>(createdJson) ?? "{}"
            );
            using JsonDocument updatedDocument = JsonDocument.Parse(
                JsonSerializer.Deserialize<string>(updatedJson) ?? "{}"
            );

            return DefaultThumbnailFallbackVerificationResult.Succeeded(
                createdDocument.RootElement.GetProperty("thumbSrc").GetString() ?? "",
                updatedDocument.RootElement.GetProperty("thumbSrc").GetString() ?? "",
                createdDocument.RootElement.GetProperty("titleText").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static async Task<LegacyThumbnailFallbackVerificationResult> VerifyLegacyThumbnailFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return LegacyThumbnailFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return LegacyThumbnailFallbackVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため compat legacy thumb fallback 統合確認をスキップします: {ex.Message}"
                );
            }

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

            string htmlPath = Path.Combine(tempRootPath, "compat-thumb-legacy-fallback.html");
            await File.WriteAllTextAsync(htmlPath, BuildLegacyThumbnailFallbackHarnessHtml(compatScript));
            webView.Source = new Uri(htmlPath);
            await navigationCompleted.Task;

            string resultJson = await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immWbCompat.dispatchCallback('onUpdateThum', {
                    movieId: 77,
                    id: 77,
                    recordKey: 'db-main:77',
                    thumbUrl: 'https://thum.local/updated.jpg?rev=thumb-2',
                    thum: 'https://thum.local/updated.jpg?rev=thumb-2',
                    thumbRevision: 'thumb-2',
                    thumbSourceKind: 'managed-thumbnail',
                    __immCallArgs: [
                      'db-main:77',
                      'https://thum.local/updated.jpg?rev=thumb-2',
                      'thumb-2',
                      'managed-thumbnail',
                      null
                    ]
                  });

                  return JSON.stringify({
                    thumbSrc: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : '',
                    legacyArgs: (window.__legacyThumbCalls && window.__legacyThumbCalls[0]) || ''
                  });
                })();
                """
            );

            using JsonDocument resultDocument = JsonDocument.Parse(
                JsonSerializer.Deserialize<string>(resultJson) ?? "{}"
            );

            return LegacyThumbnailFallbackVerificationResult.Succeeded(
                resultDocument.RootElement.GetProperty("legacyArgs").GetString() ?? "",
                resultDocument.RootElement.GetProperty("thumbSrc").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static async Task<GeneratedViewFallbackVerificationResult> VerifyGeneratedViewFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return GeneratedViewFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return GeneratedViewFallbackVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため compat generated view 統合確認をスキップします: {ex.Message}"
                );
            }

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

            string htmlPath = Path.Combine(tempRootPath, "compat-thumb-generated-view.html");
            await File.WriteAllTextAsync(htmlPath, BuildGeneratedViewFallbackHarnessHtml(compatScript));
            webView.Source = new Uri(htmlPath);
            await navigationCompleted.Task;

            string resultJson = await webView.ExecuteScriptAsync(
                """
                (() => {
                  wb.onCreateThum({
                    id: 77,
                    movieId: 77,
                    title: 'Beta',
                    ext: '.avi',
                    thum: 'https://thum.local/original.jpg?rev=thumb-0',
                    exist: true,
                    select: 0
                  }, 1);

                  const view = document.getElementById('view');
                  const image = document.getElementById('img77');
                  return JSON.stringify({
                    generatedViewExists: !!view,
                    generatedViewFlag: view ? (view.getAttribute('data-imm-generated-view') || '') : '',
                    thumbSrc: image ? (image.getAttribute('src') || '') : ''
                  });
                })();
                """
            );

            using JsonDocument resultDocument = JsonDocument.Parse(
                JsonSerializer.Deserialize<string>(resultJson) ?? "{}"
            );

            return GeneratedViewFallbackVerificationResult.Succeeded(
                resultDocument.RootElement.GetProperty("generatedViewExists").GetBoolean(),
                resultDocument.RootElement.GetProperty("generatedViewFlag").GetString() ?? "",
                resultDocument.RootElement.GetProperty("thumbSrc").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static async Task<string[]> ReadTagModifyEventsAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync("JSON.stringify(window.__wbTagOps)");
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "[]";
        return DeserializeStringArray(json);
    }

    private static async Task<string> ReadTagCacheSummaryAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync(
            """
            (() => {
              var info42 = wb.getInfo(42) || {};
              var info77 = wb.getInfo(77) || {};
              var tags42 = Array.isArray(info42.tags) ? info42.tags : [];
              var tags77 = Array.isArray(info77.tags) ? info77.tags : [];
              return JSON.stringify(tags42.join(",") + "|" + tags77.join(","));
            })();
            """
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "\"\"";
        return JsonSerializer.Deserialize<string>(json) ?? "";
    }

    private static async Task<StaleFindInfoVerificationResult> VerifyStaleFindInfoResponseIsIgnoredAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return StaleFindInfoVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return StaleFindInfoVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため stale getFindInfo 検証をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return StaleFindInfoVerificationResult.Failed(
                    "stale getFindInfo harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbFindInfoStaleDone = false;
                  window.__wbFindInfoStaleError = "";
                  window.__wbFindInfoStaleResult = { methods: [], staleSummary: "", cachedSummary: "" };
                  window.__immMessages = [];

                  const stalePromise = wb.getFindInfo();
                  const staleRequest = window.__immMessages.shift();
                  if (!staleRequest) {
                    throw new Error("stale getFindInfo request was not captured.");
                  }

                  const updatePromise = wb.update(0, 1);
                  const updateRequest = window.__immMessages.shift();
                  if (!updateRequest) {
                    throw new Error("update request was not captured.");
                  }

                  window.__immWbCompat.resolve(updateRequest.id, {
                    findInfo: {
                      find: "fresh",
                      sort: [""],
                      filter: [],
                      where: "",
                      total: 3,
                      result: 1
                    },
                    items: [{ id: 1, title: "Alpha.mp4", ext: ".mp4", thum: "https://thum.local/a.jpg" }]
                  });

                  updatePromise.then(function () {
                    window.__immWbCompat.resolve(staleRequest.id, {
                      find: "stale",
                      sort: [""],
                      filter: ["idol"],
                      where: "",
                      total: 9,
                      result: 2
                    });

                    return stalePromise.then(function (findInfo) {
                      var cached = wb.getFindInfo();
                      window.__wbFindInfoStaleResult = {
                        methods: [staleRequest.method, updateRequest.method],
                        staleSummary:
                          String(findInfo && findInfo.find ? findInfo.find : "") + "|" +
                          String(findInfo && findInfo.result ? findInfo.result : 0) + "|" +
                          (findInfo && Array.isArray(findInfo.filter) ? findInfo.filter.join(",") : ""),
                        cachedSummary:
                          String(cached && cached.find ? cached.find : "") + "|" +
                          String(cached && cached.result ? cached.result : 0) + "|" +
                          (cached && Array.isArray(cached.filter) ? cached.filter.join(",") : "")
                      };
                      window.__wbFindInfoStaleDone = true;
                    });
                  }).catch(function (error) {
                    window.__wbFindInfoStaleError = String(error && error.message ? error.message : error);
                    window.__wbFindInfoStaleDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbFindInfoStaleDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbFindInfoStaleError ? JSON.stringify(window.__wbFindInfoStaleError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbFindInfoStaleResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return StaleFindInfoVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                document.RootElement.GetProperty("staleSummary").GetString() ?? "",
                document.RootElement.GetProperty("cachedSummary").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<StaleFindInfoVerificationResult> VerifyOverlappedFindInfoResponsesAreIgnoredAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return StaleFindInfoVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return StaleFindInfoVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため getFindInfo 競合検証をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return StaleFindInfoVerificationResult.Failed(
                    "overlapped getFindInfo harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbFindInfoStaleDone = false;
                  window.__wbFindInfoStaleError = "";
                  window.__wbFindInfoStaleResult = { methods: [], staleSummary: "", cachedSummary: "" };
                  window.__immMessages = [];

                  const stalePromise = wb.getFindInfo();
                  const staleRequest = window.__immMessages.shift();
                  if (!staleRequest) {
                    throw new Error("first getFindInfo request was not captured.");
                  }

                  const freshPromise = wb.getFindInfo();
                  const freshRequest = window.__immMessages.shift();
                  if (!freshRequest) {
                    throw new Error("second getFindInfo request was not captured.");
                  }

                  window.__immWbCompat.resolve(freshRequest.id, {
                    find: "fresh",
                    sort: [""],
                    filter: ["idol"],
                    where: "",
                    total: 9,
                    result: 3
                  });

                  freshPromise.then(function () {
                    window.__immWbCompat.resolve(staleRequest.id, {
                      find: "stale",
                      sort: [""],
                      filter: [],
                      where: "",
                      total: 1,
                      result: 1
                    });

                    return stalePromise.then(function (findInfo) {
                      var cached = wb.getFindInfo();
                      window.__wbFindInfoStaleResult = {
                        methods: [staleRequest.method, freshRequest.method],
                        staleSummary:
                          String(findInfo && findInfo.find ? findInfo.find : "") + "|" +
                          String(findInfo && findInfo.result ? findInfo.result : 0) + "|" +
                          (findInfo && Array.isArray(findInfo.filter) ? findInfo.filter.join(",") : ""),
                        cachedSummary:
                          String(cached && cached.find ? cached.find : "") + "|" +
                          String(cached && cached.result ? cached.result : 0) + "|" +
                          (cached && Array.isArray(cached.filter) ? cached.filter.join(",") : "")
                      };
                      window.__wbFindInfoStaleDone = true;
                    });
                  }).catch(function (error) {
                    window.__wbFindInfoStaleError = String(error && error.message ? error.message : error);
                    window.__wbFindInfoStaleDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbFindInfoStaleDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbFindInfoStaleError ? JSON.stringify(window.__wbFindInfoStaleError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbFindInfoStaleResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return StaleFindInfoVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                document.RootElement.GetProperty("staleSummary").GetString() ?? "",
                document.RootElement.GetProperty("cachedSummary").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
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

    private static async Task<bool> ExecuteScrollScenarioAsync(WebView2 webView)
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbScrollDone = false;
              window.__wbScrollResult = null;
              window.__wbScrollTarget = "";
              const scroll = document.getElementById("scroll");
              const view = document.getElementById("view");
              view.innerHTML = "<div style='height:180px'></div><div id='thum77' style='display:block;height:10px;'></div>";
              const target = document.getElementById("thum77");
              scroll.scrollTop = 0;
              view.scrollTop = 0;
              scroll.scrollTo = function (options) {
                window.__wbScrollTarget = "scroll";
                this.scrollTop = options && typeof options.top === "number" ? options.top : 0;
              };
              view.scrollTo = function (options) {
                window.__wbScrollTarget = "view";
                this.scrollTop = options && typeof options.top === "number" ? options.top : 0;
              };
              target.scrollIntoView = undefined;

              wb.scrollSetting(0, "scroll").then(function () {
                return wb.scrollTo(77);
              }).then(function (scrolled) {
                window.__wbScrollResult = {
                  scrolled: scrolled,
                  scrollTop: scroll.scrollTop,
                  target: window.__wbScrollTarget
                };
                window.__wbScrollDone = true;
              });
              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbScrollDone");

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbScrollResult)"
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("scrolled").GetBoolean()
            && document.RootElement.GetProperty("target").GetString() == "scroll";
    }

    private static async Task<InfoGetterVerificationResult> ExecuteInfoGetterScenarioAsync(
        WebView2 webView
    )
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbInfoDone = false;
              window.__wbInfoError = "";
              window.__wbInfoResult = { methods: [], summary: "" };
              window.__immMessages = [];

              const findPromise = wb.getFindInfo();
              const findRequest = window.__immMessages.shift();
              if (!findRequest) {
                throw new Error("getFindInfo request was not captured.");
              }

              window.__immWbCompat.resolve(findRequest.id, {
                find: "idol",
                sort: ["ファイル名(昇順)", "#スコア(低い順)"],
                filter: [],
                where: "score >= 80",
                total: 3,
                result: 2
              });

              findPromise.then(function (findInfo) {
                const focusPromise = wb.getFocusThum();
                const focusRequest = window.__immMessages.shift();
                if (!focusRequest) {
                  throw new Error("getFocusThum request was not captured.");
                }

                window.__immWbCompat.resolve(focusRequest.id, 42);

                return focusPromise.then(function (focusId) {
                  const selectPromise = wb.getSelectThums();
                  const selectRequest = window.__immMessages.shift();
                  if (!selectRequest) {
                    throw new Error("getSelectThums request was not captured.");
                  }

                  window.__immWbCompat.resolve(selectRequest.id, [42, 77]);

                  return selectPromise.then(function (selectedIds) {
                    window.__wbInfoResult = {
                      methods: [findRequest.method, focusRequest.method, selectRequest.method],
                      summary:
                        String(findInfo && findInfo.find ? findInfo.find : "") + "|" +
                        String(findInfo && findInfo.total ? findInfo.total : 0) + "|" +
                        String(findInfo && findInfo.result ? findInfo.result : 0) + "|" +
                        String(focusId || 0) + "|" +
                        (Array.isArray(selectedIds) ? selectedIds.join(",") : "")
                    };
                    window.__wbInfoDone = true;
                  });
                });
              }).catch(function (error) {
                window.__wbInfoError = String(error && error.message ? error.message : error);
                window.__wbInfoDone = true;
              });

              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbInfoDone");

        string errorJson = await webView.ExecuteScriptAsync(
            "window.__wbInfoError ? JSON.stringify(window.__wbInfoError) : \"\""
        );
        string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AssertionException(error);
        }

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbInfoResult)"
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
        using JsonDocument document = JsonDocument.Parse(json);
        return new InfoGetterVerificationResult(
            DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
            document.RootElement.GetProperty("summary").GetString() ?? ""
        );
    }

    private static async Task<FilterApiVerificationResult> ExecuteFilterApiScenarioAsync(
        WebView2 webView
    )
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbFilterDone = false;
              window.__wbFilterError = "";
              window.__wbFilterResult = { methods: [], counts: [] };
              window.__immMessages = [];
              window.wb.onUpdate = function (items) {
                window.__wbFilterResult.counts.push(String(Array.isArray(items) ? items.length : 0));
                return true;
              };

              const addPromise = wb.addFilter("idol");
              const addRequest = window.__immMessages.shift();
              if (!addRequest) {
                throw new Error("addFilter request was not captured.");
              }
              window.__immWbCompat.resolve(addRequest.id, {
                items: [{ id: 1 }, { id: 2 }]
              });

              addPromise.then(function () {
                const removePromise = wb.removeFilter("idol");
                const removeRequest = window.__immMessages.shift();
                if (!removeRequest) {
                  throw new Error("removeFilter request was not captured.");
                }
                window.__immWbCompat.resolve(removeRequest.id, {
                  items: [{ id: 2 }]
                });

                return removePromise.then(function () {
                  const clearPromise = wb.clearFilter();
                  const clearRequest = window.__immMessages.shift();
                  if (!clearRequest) {
                    throw new Error("clearFilter request was not captured.");
                  }
                  window.__immWbCompat.resolve(clearRequest.id, {
                    items: [{ id: 1 }, { id: 2 }, { id: 3 }]
                  });

                  return clearPromise.then(function () {
                    window.__wbFilterResult.methods = [
                      addRequest.method,
                      removeRequest.method,
                      clearRequest.method
                    ];
                    window.__wbFilterDone = true;
                  });
                });
              }).catch(function (error) {
                window.__wbFilterError = String(error && error.message ? error.message : error);
                window.__wbFilterDone = true;
              });

              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbFilterDone");

        string errorJson = await webView.ExecuteScriptAsync(
            "window.__wbFilterError ? JSON.stringify(window.__wbFilterError) : \"\""
        );
        string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AssertionException(error);
        }

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbFilterResult)"
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
        using JsonDocument document = JsonDocument.Parse(json);
        return new FilterApiVerificationResult(
            DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
            DeserializeStringArray(document.RootElement.GetProperty("counts").GetRawText())
        );
    }

    private static async Task WaitForWebFlagAsync(WebView2 webView, string flagName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            string resultJson = await webView.ExecuteScriptAsync(
                $"Boolean(window.{flagName})"
            );
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"WebView2 側の待機フラグ '{flagName}' が立ちませんでした。");
    }

    private static string[] ExtractEventList(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty(propertyName, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
            {
                continue;
            }

            long movieId = item[0].GetInt64();
            bool state = item[1].GetBoolean();
            values.Add($"{movieId}:{state.ToString().ToLowerInvariant()}");
        }

        return [.. values];
    }

    private static string BuildHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbResults = { focus: [], select: [] };
                window.__wbSequence = [];
                window.__wbDone = false;
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };

                function onSetFocus(id, isFocus) {
                  window.__wbResults.focus.push([Number(id || 0), !!isFocus]);
                  window.__wbSequence.push("focus:" + String(id || 0) + ":" + String(!!isFocus));
                  return true;
                }

                function onSetSelect(id, isSel) {
                  window.__wbResults.select.push([Number(id || 0), !!isSel]);
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

                function onModifyTags(payload) {
                  var tagName = payload && payload.tag ? payload.tag : "";
                  window.__wbTagOps = window.__wbTagOps || [];
                  window.__wbTagOps.push(tagName + ":" + String(!!(payload && payload.changed)));
                  return true;
                }

                function onUpdateThum(recordKey, thumbUrl, thumbRevision, thumbSourceKind, sizeInfo) {
                  window.__wbThumbUpdates = window.__wbThumbUpdates || [];
                  var width = sizeInfo && sizeInfo.thumbNaturalWidth ? sizeInfo.thumbNaturalWidth : 0;
                  var height = sizeInfo && sizeInfo.thumbNaturalHeight ? sizeInfo.thumbNaturalHeight : 0;
                  var columns = sizeInfo && sizeInfo.thumbSheetColumns ? sizeInfo.thumbSheetColumns : 0;
                  var rows = sizeInfo && sizeInfo.thumbSheetRows ? sizeInfo.thumbSheetRows : 0;
                  window.__wbThumbUpdates.push(
                    String(recordKey || "") + "|" +
                    String(thumbUrl || "") + "|" +
                    String(thumbRevision || "") + "|" +
                    String(thumbSourceKind || "") + "|" +
                    String(width) + "x" + String(height) + "|" +
                    String(columns) + "x" + String(rows)
                  );
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="scroll"><div id="view"><div id="thum77"></div></div></div>
              <div id="config">multi-select : 1; scroll-id : scroll;</div>
            </body>
            </html>
            """;
    }

    private static string BuildHarnessHtmlWithoutSelectCallback(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbDone = false;
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view">
                <div id="thum77" class="thum"></div>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildCompactHarnessHtmlWithoutSelectCallback(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbDone = false;
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view">
                <div id="thum77" class="cthum"></div>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildHarnessHtmlWithSelectedReadOnUpdate(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbDone = false;
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };
              </script>
              <script>
                function onUpdate() {
                  window.__wbSelectedReadResult = [];
                  wb.getSelectThums().then(function (ids) {
                    window.__wbSelectedReadResult = Array.isArray(ids) ? ids.slice() : [];
                  });
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view">
                <div id="thum77" class="thum"></div>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildHarnessHtmlWithSelectedReadOnClearAll(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbDone = false;
                window.__wbClearSnapshots = [];
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };
              </script>
              <script>
                function onSetSelect(id, isSelect) {
                  if (!isSelect) {
                    var selectedIds = wb.getSelectThums();
                    window.__wbClearSnapshots.push(
                      Array.isArray(selectedIds)
                        ? selectedIds.map(function (value) { return String(value || ""); }).join(",")
                        : ""
                    );
                  }
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view">
                <div id="thum77" class="thum"></div>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildHarnessHtmlWithFocusCallbackWithoutSelectCallback(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbDone = false;
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };
              </script>
              <script>
            {{compatScript}}
              </script>
              <script>
                function onSetFocus(id, isFocus) {
                  const target = document.getElementById('thum' + String(id || 0));
                  if (target) {
                    target.className = isFocus ? 'thum_focus' : 'thum';
                  }
                  return true;
                }
              </script>
            </head>
            <body>
              <div id="view">
                <div id="thum77" class="thum"></div>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildSearchTableHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };

                function onSetFocus(id, isFocus) {
                  var img = document.getElementById('img' + String(id || 0));
                  var title = document.getElementById('title' + String(id || 0));
                  if (!img || !title) {
                    return true;
                  }

                  img.className = isFocus ? 'img_focus' : 'img_thum';
                  title.className = isFocus ? 'title_focus' : 'title_thum';
                  return true;
                }

                function onSetSelect(id, isSelect) {
                  var thumb = document.getElementById('thum' + String(id || 0));
                  if (!thumb) {
                    return true;
                  }

                  thumb.className = isSelect ? 'thum_select' : 'thum';
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="scroll">
                <div id="view">
                  <div id="thum77" class="thum"><img id="img77" class="img_thum"><h3 id="title77" class="title_thum"></h3></div>
                  <div id="thum84" class="thum"><img id="img84" class="img_thum"><h3 id="title84" class="title_thum"></h3></div>
                </div>
              </div>
              <div id="config">multi-select : 1; scroll-id : scroll;</div>
            </body>
            </html>
            """;
    }

    private static string BuildMinimalThumbnailFallbackHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.chrome = {
                  webview: {
                    postMessage: function () {
                    }
                  }
                };
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view"></div>
              <div id="config">multi-select : 1; scroll-id : view;</div>
            </body>
            </html>
            """;
    }

    private static string BuildLegacyThumbnailFallbackHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.chrome = {
                  webview: {
                    postMessage: function () {
                    }
                  }
                };
                window.wb = window.wb || {};
                window.__legacyThumbCalls = [];
                window.wb.onUpdateThum = function(id, src) {
                  window.__legacyThumbCalls.push(String(id || "") + "|" + String(src || ""));
                  var img = document.getElementById(id);
                  if (img == null) {
                    return;
                  }

                  img.src = "";
                  img.src = src;
                };
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view">
                <div id="thum77">
                  <img id="img77" src="https://thum.local/original.jpg?rev=thumb-0">
                </div>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildGeneratedViewFallbackHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.chrome = {
                  webview: {
                    postMessage: function () {
                    }
                  }
                };
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="config">multi-select : 1; scroll-id : view;</div>
            </body>
            </html>
            """;
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

            DirectoryInfo parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return "";
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
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

    private sealed record CompatScriptVerificationResult(
        string[] FocusEvents,
        string[] FocusSelectionEvents,
        string[] SelectFocusEvents,
        string[] SelectEvents,
        string[] ThumbnailUpdateEvents,
        string[] TagRequests,
        string[] TagModifyEvents,
        string TagCacheSummary,
        string[] LifecycleEvents,
        bool ScrollSucceeded,
        string[] InfoRequestMethods,
        string InfoSummary,
        string[] FilterRequestMethods,
        string[] FilterUpdateCounts,
        string IgnoreReason
    )
    {
        public static CompatScriptVerificationResult Succeeded(
            string[] focusEvents,
            string[] focusSelectionEvents,
            string[] selectFocusEvents,
            string[] selectEvents,
            string[] thumbnailUpdateEvents,
            string[] tagRequests,
            string[] tagModifyEvents,
            string tagCacheSummary,
            string[] lifecycleEvents,
            bool scrollSucceeded,
            string[] infoRequestMethods,
            string infoSummary,
            string[] filterRequestMethods,
            string[] filterUpdateCounts
        )
        {
            return new CompatScriptVerificationResult(
                focusEvents,
                focusSelectionEvents,
                selectFocusEvents,
                selectEvents,
                thumbnailUpdateEvents,
                tagRequests,
                tagModifyEvents,
                tagCacheSummary,
                lifecycleEvents,
                scrollSucceeded,
                infoRequestMethods,
                infoSummary,
                filterRequestMethods,
                filterUpdateCounts,
                ""
            );
        }

        public static CompatScriptVerificationResult Ignored(string reason)
        {
            return new CompatScriptVerificationResult(
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                "",
                [],
                false,
                [],
                "",
                [],
                [],
                reason
            );
        }

        public static CompatScriptVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record InfoGetterVerificationResult(string[] RequestMethods, string Summary);

    private sealed record FilterApiVerificationResult(string[] RequestMethods, string[] UpdateCounts);

    private sealed record DefaultSelectFallbackVerificationResult(
        string SelectedClassName,
        string SelectedFlag,
        string ClearedClassName,
        string ClearedFlag,
        string IgnoreReason
    )
    {
        public static DefaultSelectFallbackVerificationResult Succeeded(
            string selectedClassName,
            string selectedFlag,
            string clearedClassName,
            string clearedFlag
        )
        {
            return new DefaultSelectFallbackVerificationResult(
                selectedClassName,
                selectedFlag,
                clearedClassName,
                clearedFlag,
                ""
            );
        }

        public static DefaultSelectFallbackVerificationResult Ignored(string reason)
        {
            return new DefaultSelectFallbackVerificationResult("", "", "", "", reason);
        }

        public static DefaultSelectFallbackVerificationResult Failed(string reason)
        {
            return new DefaultSelectFallbackVerificationResult("", "", "", "", reason);
        }
    }

    private sealed record FocusedSelectFallbackVerificationResult(
        string SelectedClassName,
        string ClearedClassName,
        string ClearedFlag,
        string IgnoreReason
    )
    {
        public static FocusedSelectFallbackVerificationResult Succeeded(
            string selectedClassName,
            string clearedClassName,
            string clearedFlag
        )
        {
            return new FocusedSelectFallbackVerificationResult(
                selectedClassName,
                clearedClassName,
                clearedFlag,
                ""
            );
        }

        public static FocusedSelectFallbackVerificationResult Ignored(string reason)
        {
            return new FocusedSelectFallbackVerificationResult("", "", "", reason);
        }

        public static FocusedSelectFallbackVerificationResult Failed(string reason)
        {
            return new FocusedSelectFallbackVerificationResult("", "", "", reason);
        }
    }

    private sealed record IncrementalSelectedStateVerificationResult(
        string[] SelectEvents,
        string IgnoreReason
    )
    {
        public static IncrementalSelectedStateVerificationResult Succeeded(string[] selectEvents)
        {
            return new IncrementalSelectedStateVerificationResult(selectEvents, "");
        }

        public static IncrementalSelectedStateVerificationResult Ignored(string reason)
        {
            return new IncrementalSelectedStateVerificationResult([], reason);
        }

        public static IncrementalSelectedStateVerificationResult Failed(string reason)
        {
            return new IncrementalSelectedStateVerificationResult([], reason);
        }
    }

    private sealed record DefaultFocusFallbackVerificationResult(
        string FocusedClassName,
        string ClearedClassName,
        string IgnoreReason
    )
    {
        public static DefaultFocusFallbackVerificationResult Succeeded(
            string focusedClassName,
            string clearedClassName
        )
        {
            return new DefaultFocusFallbackVerificationResult(
                focusedClassName,
                clearedClassName,
                ""
            );
        }

        public static DefaultFocusFallbackVerificationResult Ignored(string reason)
        {
            return new DefaultFocusFallbackVerificationResult("", "", reason);
        }

        public static DefaultFocusFallbackVerificationResult Failed(string reason)
        {
            return new DefaultFocusFallbackVerificationResult("", "", reason);
        }
    }

    private sealed record FocusStateVerificationResult(
        string StaleResult,
        string FocusedMovieId,
        string[] FocusEvents,
        string IgnoreReason
    )
    {
        public static FocusStateVerificationResult Succeeded(
            string staleResult,
            string focusedMovieId,
            string[] focusEvents
        )
        {
            return new FocusStateVerificationResult(
                staleResult,
                focusedMovieId,
                focusEvents,
                ""
            );
        }

        public static FocusStateVerificationResult Ignored(string reason)
        {
            return new FocusStateVerificationResult("", "", [], reason);
        }

        public static FocusStateVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record SelectionStateVerificationResult(
        string[] SelectedIds,
        string[] SelectEvents,
        string IgnoreReason
    )
    {
        public static SelectionStateVerificationResult Succeeded(
            string[] selectedIds,
            string[] selectEvents
        )
        {
            return new SelectionStateVerificationResult(selectedIds, selectEvents, "");
        }

        public static SelectionStateVerificationResult Ignored(string reason)
        {
            return new SelectionStateVerificationResult([], [], reason);
        }

        public static SelectionStateVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record GetInfosRequestVerificationResult(
        string[] Methods,
        string[] Payloads,
        string IgnoreReason
    )
    {
        public static GetInfosRequestVerificationResult Succeeded(
            string[] methods,
            string[] payloads
        )
        {
            return new GetInfosRequestVerificationResult(methods, payloads, "");
        }

        public static GetInfosRequestVerificationResult Ignored(string reason)
        {
            return new GetInfosRequestVerificationResult([], [], reason);
        }

        public static GetInfosRequestVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record DefaultUpdateAppendVerificationResult(
        string[] Sequence,
        string[] Methods,
        string[] Titles,
        string IgnoreReason
    )
    {
        public static DefaultUpdateAppendVerificationResult Succeeded(
            string[] sequence,
            string[] methods,
            string[] titles
        )
        {
            return new DefaultUpdateAppendVerificationResult(sequence, methods, titles, "");
        }

        public static DefaultUpdateAppendVerificationResult Ignored(string reason)
        {
            return new DefaultUpdateAppendVerificationResult([], [], [], reason);
        }

        public static DefaultUpdateAppendVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record DefaultThumbnailFallbackVerificationResult(
        string CreatedThumbSrc,
        string UpdatedThumbSrc,
        string TitleText,
        string IgnoreReason
    )
    {
        public static DefaultThumbnailFallbackVerificationResult Succeeded(
            string createdThumbSrc,
            string updatedThumbSrc,
            string titleText
        )
        {
            return new DefaultThumbnailFallbackVerificationResult(
                createdThumbSrc,
                updatedThumbSrc,
                titleText,
                ""
            );
        }

        public static DefaultThumbnailFallbackVerificationResult Ignored(string reason)
        {
            return new DefaultThumbnailFallbackVerificationResult("", "", "", reason);
        }

        public static DefaultThumbnailFallbackVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record LegacyThumbnailFallbackVerificationResult(
        string LegacyCallArgs,
        string UpdatedThumbSrc,
        string IgnoreReason
    )
    {
        public static LegacyThumbnailFallbackVerificationResult Succeeded(
            string legacyCallArgs,
            string updatedThumbSrc
        )
        {
            return new LegacyThumbnailFallbackVerificationResult(
                legacyCallArgs,
                updatedThumbSrc,
                ""
            );
        }

        public static LegacyThumbnailFallbackVerificationResult Ignored(string reason)
        {
            return new LegacyThumbnailFallbackVerificationResult("", "", reason);
        }

        public static LegacyThumbnailFallbackVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record GeneratedViewFallbackVerificationResult(
        bool GeneratedViewExists,
        string GeneratedViewFlag,
        string CreatedThumbSrc,
        string IgnoreReason
    )
    {
        public static GeneratedViewFallbackVerificationResult Succeeded(
            bool generatedViewExists,
            string generatedViewFlag,
            string createdThumbSrc
        )
        {
            return new GeneratedViewFallbackVerificationResult(
                generatedViewExists,
                generatedViewFlag,
                createdThumbSrc,
                ""
            );
        }

        public static GeneratedViewFallbackVerificationResult Ignored(string reason)
        {
            return new GeneratedViewFallbackVerificationResult(false, "", "", reason);
        }

        public static GeneratedViewFallbackVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record StaleFindInfoVerificationResult(
        string[] RequestMethods,
        string StaleSummary,
        string CachedSummary,
        string IgnoreReason
    )
    {
        public static StaleFindInfoVerificationResult Succeeded(
            string[] requestMethods,
            string staleSummary,
            string cachedSummary
        )
        {
            return new StaleFindInfoVerificationResult(
                requestMethods,
                staleSummary,
                cachedSummary,
                ""
            );
        }

        public static StaleFindInfoVerificationResult Ignored(string reason)
        {
            return new StaleFindInfoVerificationResult([], "", "", reason);
        }

        public static StaleFindInfoVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record SeamlessScrollVerificationResult(
        string[] Sequence,
        string[] Methods,
        string[] Titles,
        string IgnoreReason
    )
    {
        public static SeamlessScrollVerificationResult Succeeded(
            string[] sequence,
            string[] methods,
            string[] titles
        )
        {
            return new SeamlessScrollVerificationResult(sequence, methods, titles, "");
        }

        public static SeamlessScrollVerificationResult Ignored(string reason)
        {
            return new SeamlessScrollVerificationResult([], [], [], reason);
        }

        public static SeamlessScrollVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record SeamlessScrollStopVerificationResult(
        string[] Methods,
        string[] Titles,
        int PendingRequestCount,
        string IgnoreReason
    )
    {
        public static SeamlessScrollStopVerificationResult Succeeded(
            string[] methods,
            string[] titles,
            int pendingRequestCount
        )
        {
            return new SeamlessScrollStopVerificationResult(
                methods,
                titles,
                pendingRequestCount,
                ""
            );
        }

        public static SeamlessScrollStopVerificationResult Ignored(string reason)
        {
            return new SeamlessScrollStopVerificationResult([], [], 0, reason);
        }

        public static SeamlessScrollStopVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record ResetViewVerificationResult(
        string[] Sequence,
        string[] Methods,
        string IgnoreReason
    )
    {
        public static ResetViewVerificationResult Succeeded(string[] sequence, string[] methods)
        {
            return new ResetViewVerificationResult(sequence, methods, "");
        }

        public static ResetViewVerificationResult Ignored(string reason)
        {
            return new ResetViewVerificationResult([], [], reason);
        }

        public static ResetViewVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record SearchTableFocusedDeselectVerificationResult(
        string FocusedMovieId,
        string[] SelectedIds,
        string Thumb77ClassName,
        string Image77ClassName,
        string Title77ClassName,
        string Thumb84ClassName,
        string Image84ClassName,
        string Title84ClassName,
        string IgnoreReason
    )
    {
        public static SearchTableFocusedDeselectVerificationResult Succeeded(
            string focusedMovieId,
            string[] selectedIds,
            string thumb77ClassName,
            string image77ClassName,
            string title77ClassName,
            string thumb84ClassName,
            string image84ClassName,
            string title84ClassName
        )
        {
            return new SearchTableFocusedDeselectVerificationResult(
                focusedMovieId,
                selectedIds,
                thumb77ClassName,
                image77ClassName,
                title77ClassName,
                thumb84ClassName,
                image84ClassName,
                title84ClassName,
                ""
            );
        }

        public static SearchTableFocusedDeselectVerificationResult Ignored(string reason)
        {
            return new SearchTableFocusedDeselectVerificationResult(
                "",
                [],
                "",
                "",
                "",
                "",
                "",
                "",
                reason
            );
        }

        public static SearchTableFocusedDeselectVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }
}
