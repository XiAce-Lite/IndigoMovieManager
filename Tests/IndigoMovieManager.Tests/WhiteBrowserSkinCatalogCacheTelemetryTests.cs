using System.IO;
using IndigoMovieManager.Skin;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinCatalogCacheTelemetryTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinCatalogService.ResetCacheForTesting();
    }

    [Test]
    public void Load_root未指定ではbuilt_in定義参照を再利用する()
    {
        IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load("");
        IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load("");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(first.Count, Is.EqualTo(5));
            Assert.That(first.Select(x => x.Name), Is.EqualTo(new[]
            {
                "DefaultSmall",
                "DefaultBig",
                "DefaultGrid",
                "DefaultList",
                "DefaultBig10",
            }));
        });
    }

    [Test]
    public void Load_同一root再読込ではcache_hitだけ増えてmissは増えない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("CacheHitSkin", "thum-width : 160;");
        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.SameAs(second));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(1));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogSignatureReusedItemCountForTesting(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_html更新後の再読込ではcache_missが増えてhitは据え置かれる()
    {
        string rootPath = CreateSkinRootWithSingleSkin("CacheReloadSkin", "thum-width : 160;");
        string htmlPath = Path.Combine(rootPath, "CacheReloadSkin", "CacheReloadSkin.htm");
        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 220;
                    thum-height : 160;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition reloaded = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "CacheReloadSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.Config.ThumbWidth, Is.EqualTo(220));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_signature_telemetryでdirectory数と経過時間を取得できる()
    {
        string rootPath = CreateSkinRootWithSingleSkin("TelemetrySkinA", "thum-width : 160;");
        string secondSkinDirectoryPath = Path.Combine(rootPath, "TelemetrySkinB");
        Directory.CreateDirectory(secondSkinDirectoryPath);
        File.WriteAllText(
            Path.Combine(secondSkinDirectoryPath, "TelemetrySkinB.htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 220;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            _ = WhiteBrowserSkinCatalogService.Load(rootPath);

            Assert.Multiple(() =>
            {
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogSignatureDirectoryCountForTesting(),
                    Is.EqualTo(2)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogSignatureReusedItemCountForTesting(),
                    Is.EqualTo(0)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogSignatureElapsedMillisecondsForTesting(),
                    Is.GreaterThanOrEqualTo(0)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_loadcore_telemetryでexternal数と経過時間を取得できる()
    {
        string rootPath = CreateSkinRootWithSingleSkin("TelemetryLoadCoreA", "thum-width : 160;");
        string secondSkinDirectoryPath = Path.Combine(rootPath, "TelemetryLoadCoreB");
        Directory.CreateDirectory(secondSkinDirectoryPath);
        File.WriteAllText(
            Path.Combine(secondSkinDirectoryPath, "TelemetryLoadCoreB.htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 220;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions = WhiteBrowserSkinCatalogService.Load(rootPath);

            Assert.Multiple(() =>
            {
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetCatalogLoadCoreCountForTesting(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreExternalDefinitionCountForTesting(),
                    Is.EqualTo(2)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreElapsedMillisecondsForTesting(),
                    Is.GreaterThanOrEqualTo(0)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, "TelemetryLoadCoreA"),
                    Is.Not.Null
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, "TelemetryLoadCoreB"),
                    Is.Not.Null
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_組み込み同名external更新ではcatalog_missを増やさない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("DefaultGrid", "thum-width : 160;");
        string secondSkinDirectoryPath = Path.Combine(rootPath, "TelemetrySkipSkin");
        string builtInHtmlPath = Path.Combine(rootPath, "DefaultGrid", "DefaultGrid.htm");
        Directory.CreateDirectory(secondSkinDirectoryPath);
        File.WriteAllText(
            Path.Combine(secondSkinDirectoryPath, "TelemetrySkipSkin.htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 220;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);

            File.WriteAllText(
                builtInHtmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 999;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(builtInHtmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition telemetrySkip =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(second, "TelemetrySkipSkin");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.SameAs(second));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(1));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetCatalogLoadCoreCountForTesting(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreExternalDefinitionCountForTesting(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreSkippedDefinitionCountForTesting(),
                    Is.EqualTo(0)
                );
                Assert.That(
                    telemetrySkip,
                    Is.Not.Null
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_html一部更新時は未変更skin定義を参照再利用する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ReuseKeepSkin", "thum-width : 160;");
        string changedSkinDirectoryPath = Path.Combine(rootPath, "ReuseChangedSkin");
        Directory.CreateDirectory(changedSkinDirectoryPath);
        string changedHtmlPath = Path.Combine(changedSkinDirectoryPath, "ReuseChangedSkin.htm");
        File.WriteAllText(
            changedHtmlPath,
            """
            <html>
            <body>
              <div id="config">
                thum-width : 200;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition firstKeep = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "ReuseKeepSkin"
            );
            WhiteBrowserSkinDefinition firstChanged = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "ReuseChangedSkin"
            );

            File.WriteAllText(
                changedHtmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 240;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(changedHtmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition secondKeep = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReuseKeepSkin"
            );
            WhiteBrowserSkinDefinition secondChanged = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReuseChangedSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(firstKeep, Is.SameAs(secondKeep));
                Assert.That(firstChanged, Is.Not.SameAs(secondChanged));
                Assert.That(secondChanged, Is.Not.Null);
                Assert.That(secondChanged.Config.ThumbWidth, Is.EqualTo(240));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogSignatureReusedItemCountForTesting(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreExternalDefinitionCountForTesting(),
                    Is.EqualTo(2)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreReusedDefinitionCountForTesting(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_html不変で付随ファイルだけ更新した時もskin定義を参照再利用する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ReuseAssetSkin", "thum-width : 160;");
        string assetPath = Path.Combine(rootPath, "ReuseAssetSkin", "theme.css");
        File.WriteAllText(assetPath, ".card { color: red; }");

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition firstDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(first, "ReuseAssetSkin");

            File.WriteAllText(assetPath, ".card { color: blue; }");
            File.SetLastWriteTimeUtc(assetPath, DateTime.UtcNow.AddSeconds(1));
            Directory.SetLastWriteTimeUtc(
                Path.Combine(rootPath, "ReuseAssetSkin"),
                DateTime.UtcNow.AddSeconds(1)
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition secondDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(second, "ReuseAssetSkin");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(firstDefinition, Is.SameAs(secondDefinition));
                Assert.That(secondDefinition, Is.Not.Null);
                Assert.That(secondDefinition.Config.ThumbWidth, Is.EqualTo(160));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(),
                    Is.EqualTo(2)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(),
                    Is.EqualTo(0)
                );
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreReusedDefinitionCountForTesting(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_非標準html名でも前回htmlPathを優先してcache_hitできる()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-catalog-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, "CustomHtmlSkin");
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, "main-view.html"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 180;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition custom = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "CustomHtmlSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.SameAs(second));
                Assert.That(custom, Is.Not.Null);
                Assert.That(custom.HtmlPath, Does.EndWith("main-view.html"));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(1));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogSignatureReusedItemCountForTesting(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_非標準html名の後で標準html名が追加されたら標準名へ切り替える()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-catalog-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, "CustomHtmlSkin");
        string customHtmlPath = Path.Combine(skinDirectoryPath, "main-view.html");
        string standardHtmlPath = Path.Combine(skinDirectoryPath, "CustomHtmlSkin.htm");
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            customHtmlPath,
            """
            <html>
            <body>
              <div id="config">
                thum-width : 180;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition firstCustom = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "CustomHtmlSkin"
            );

            File.WriteAllText(
                standardHtmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 260;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(standardHtmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition secondCustom = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "CustomHtmlSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(firstCustom, Is.Not.Null);
                Assert.That(firstCustom.HtmlPath, Does.EndWith("main-view.html"));
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(secondCustom, Is.Not.Null);
                Assert.That(secondCustom.HtmlPath, Does.EndWith("CustomHtmlSkin.htm"));
                Assert.That(secondCustom.Config.ThumbWidth, Is.EqualTo(260));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_directory時刻不変でもhtml更新は再読込できる()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-catalog-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, "TimestampStableSkin");
        string htmlPath = Path.Combine(skinDirectoryPath, "main-view.html");
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            htmlPath,
            """
            <html>
            <body>
              <div id="config">
                thum-width : 180;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition firstDefinition = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "TimestampStableSkin"
            );
            DateTime originalDirectoryWriteTimeUtc = Directory.GetLastWriteTimeUtc(skinDirectoryPath);

            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 260;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, DateTime.UtcNow.AddSeconds(1));
            Directory.SetLastWriteTimeUtc(skinDirectoryPath, originalDirectoryWriteTimeUtc);

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition secondDefinition = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "TimestampStableSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(firstDefinition, Is.Not.Null);
                Assert.That(secondDefinition, Is.Not.Null);
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(firstDefinition, Is.Not.SameAs(secondDefinition));
                Assert.That(secondDefinition.Config.ThumbWidth, Is.EqualTo(260));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_fallback_html解決でもhtm優先を維持できる()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-catalog-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, "FallbackPreferHtmSkin");
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, "main-view.html"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 180;
              </div>
            </body>
            </html>
            """
        );
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, "other-view.htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 260;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition definition = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                definitions,
                "FallbackPreferHtmSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.HtmlPath, Does.EndWith("other-view.htm"));
                Assert.That(definition.Config.ThumbWidth, Is.EqualTo(260));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void Load_非標準html名利用中にfallback_htm追加でも前回htmlPathを維持できる()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-catalog-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, "KeepCustomHtmlSkin");
        string customHtmlPath = Path.Combine(skinDirectoryPath, "main-view.html");
        string fallbackHtmPath = Path.Combine(skinDirectoryPath, "other-view.htm");
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            customHtmlPath,
            """
            <html>
            <body>
              <div id="config">
                thum-width : 180;
              </div>
            </body>
            </html>
            """
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition firstDefinition = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "KeepCustomHtmlSkin"
            );

            File.WriteAllText(
                fallbackHtmPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 260;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(fallbackHtmPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(rootPath);
            WhiteBrowserSkinDefinition secondDefinition = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "KeepCustomHtmlSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(firstDefinition, Is.Not.Null);
                Assert.That(firstDefinition.HtmlPath, Does.EndWith("main-view.html"));
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(secondDefinition, Is.Not.Null);
                Assert.That(secondDefinition.HtmlPath, Does.EndWith("main-view.html"));
                Assert.That(secondDefinition.Config.ThumbWidth, Is.EqualTo(180));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    private static string CreateSkinRootWithSingleSkin(string skinName, string configBody)
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-catalog-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, skinName);
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, $"{skinName}.htm"),
            $$"""
            <html>
            <body>
              <div id="config">
                {{configBody}}
              </div>
            </body>
            </html>
            """
        );
        return rootPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // テスト後掃除の失敗は本体判定を優先する。
        }
    }
}
