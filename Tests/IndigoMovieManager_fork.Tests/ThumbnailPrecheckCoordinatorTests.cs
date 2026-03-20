using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailPrecheckCoordinatorTests
{
    [Test]
    public void Run_manual更新で既存サムネが無ければ即失敗する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out _,
                out _
            );
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, [0x01, 0x02]);

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 1,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = Path.Combine(tempRoot, "thumb", "missing.jpg"),
                    IsResizeThumb = true,
                    IsManual = true,
                    KnownDurationSec = 10,
                    CacheMeta = new CachedMovieMeta("hash1", 10, false, ""),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.True);
                Assert.That(actual.ImmediateResult.IsSuccess, Is.False);
                Assert.That(
                    actual.ImmediateResult.ErrorMessage,
                    Is.EqualTo("manual target thumbnail does not exist")
                );
                Assert.That(actual.ImmediateResult.ProcessEngineId, Is.EqualTo("precheck"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Run_欠損動画ならplaceholderをコピーして成功を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out string placeholderPath
            );
            string moviePath = Path.Combine(tempRoot, "missing.mp4");
            string savePath = Path.Combine(tempRoot, "thumb", "out.jpg");

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 2,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = savePath,
                    IsResizeThumb = true,
                    IsManual = false,
                    KnownDurationSec = 12,
                    CacheMeta = new CachedMovieMeta("hash2", 12, false, ""),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.True);
                Assert.That(actual.ImmediateResult.IsSuccess, Is.True);
                Assert.That(actual.ImmediateResult.ProcessEngineId, Is.EqualTo("missing-movie"));
                Assert.That(File.ReadAllBytes(savePath), Is.EqualTo(File.ReadAllBytes(placeholderPath)));
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("missing-movie"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Run_DRM疑いならplaceholder_drm_precheckで成功を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out _
            );
            string moviePath = Path.Combine(tempRoot, "movie.wmv");
            string savePath = Path.Combine(tempRoot, "thumb", "drm.jpg");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03]);

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 3,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = savePath,
                    IsResizeThumb = true,
                    IsManual = false,
                    KnownDurationSec = 15,
                    CacheMeta = new CachedMovieMeta("hash3", 15, true, "playready"),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.True);
                Assert.That(actual.ImmediateResult.IsSuccess, Is.True);
                Assert.That(
                    actual.ImmediateResult.ProcessEngineId,
                    Is.EqualTo("placeholder-drm-precheck")
                );
                Assert.That(File.Exists(savePath), Is.True);
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("placeholder-drm-precheck"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Run_0バイト動画ならplaceholder_no_dataで成功を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out _
            );
            string moviePath = Path.Combine(tempRoot, "empty.mp4");
            string savePath = Path.Combine(tempRoot, "thumb", "empty.jpg");
            File.WriteAllBytes(moviePath, []);

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 31,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = savePath,
                    IsResizeThumb = true,
                    IsManual = false,
                    KnownDurationSec = 0,
                    CacheMeta = new CachedMovieMeta("hash31", 0, false, ""),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.True);
                Assert.That(actual.ImmediateResult.IsSuccess, Is.True);
                Assert.That(actual.ImmediateResult.ProcessEngineId, Is.EqualTo("placeholder-no-data"));
                Assert.That(File.Exists(savePath), Is.True);
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("placeholder-no-data"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Run_AppleDoubleならplaceholder_appledoubleで成功を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out _
            );
            string moviePath = Path.Combine(tempRoot, "._movie.mp4");
            string savePath = Path.Combine(tempRoot, "thumb", "appledouble.jpg");
            File.WriteAllBytes(moviePath, [0x00, 0x05, 0x16, 0x07, 0x00, 0x02, 0x00, 0x00]);

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 32,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = savePath,
                    IsResizeThumb = true,
                    IsManual = false,
                    KnownDurationSec = null,
                    CacheMeta = new CachedMovieMeta("hash32", null, false, ""),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.True);
                Assert.That(actual.ImmediateResult.IsSuccess, Is.True);
                Assert.That(actual.ImmediateResult.ProcessEngineId, Is.EqualTo("placeholder-appledouble"));
                Assert.That(File.Exists(savePath), Is.True);
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("placeholder-appledouble"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Run_動画シグネチャ無しならplaceholder_not_movieで成功を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out _
            );
            string moviePath = Path.Combine(tempRoot, "sample.dat");
            string savePath = Path.Combine(tempRoot, "thumb", "not-movie.jpg");
            File.WriteAllBytes(
                moviePath,
                [0x54, 0x45, 0x58, 0x54, 0x2D, 0x46, 0x49, 0x4C, 0x45, 0x2D, 0x44, 0x41, 0x54, 0x41, 0x2D, 0x58]
            );

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 33,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = savePath,
                    IsResizeThumb = true,
                    IsManual = false,
                    KnownDurationSec = null,
                    CacheMeta = new CachedMovieMeta("hash33", null, false, ""),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.True);
                Assert.That(actual.ImmediateResult.IsSuccess, Is.True);
                Assert.That(actual.ImmediateResult.ProcessEngineId, Is.EqualTo("placeholder-not-movie"));
                Assert.That(File.Exists(savePath), Is.True);
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("placeholder-not-movie"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Run_通常経路なら継続しfileSizeを返してRequestへ反映する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailPrecheckCoordinator coordinator = CreateCoordinator(
                tempRoot,
                out _,
                out _
            );
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03, 0x04, 0x05]);
            ThumbnailRequest request = new()
            {
                MovieId = 4,
                TabIndex = 0,
                MovieFullPath = moviePath,
            };

            ThumbnailPrecheckOutcome actual = coordinator.Run(
                new ThumbnailPrecheckRequest
                {
                    Request = request,
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = Path.Combine(tempRoot, "thumb"),
                    MovieFullPath = moviePath,
                    SourceMovieFullPath = moviePath,
                    SaveThumbFileName = Path.Combine(tempRoot, "thumb", "normal.jpg"),
                    IsResizeThumb = true,
                    IsManual = false,
                    KnownDurationSec = null,
                    CacheMeta = new CachedMovieMeta("hash4", null, false, ""),
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.HasImmediateResult, Is.False);
                Assert.That(actual.FileSizeBytes, Is.EqualTo(5));
                Assert.That(request.MovieSizeBytes, Is.EqualTo(5));
                Assert.That(Directory.Exists(Path.Combine(tempRoot, "thumb")), Is.True);
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static ThumbnailPrecheckCoordinator CreateCoordinator(
        string tempRoot,
        out RecordingProcessLogWriter writer,
        out string placeholderPath
    )
    {
        placeholderPath = Path.Combine(tempRoot, "no-file.jpg");
        File.WriteAllBytes(placeholderPath, [0x10, 0x20, 0x30]);

        writer = new RecordingProcessLogWriter();
        TestHostRuntime hostRuntime = new(placeholderPath);
        ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider(""));
        ThumbnailJobContextBuilder jobContextBuilder = new(resolver);
        ThumbnailCreateResultFinalizer finalizer = new(writer, resolver);
        return new ThumbnailPrecheckCoordinator(hostRuntime, resolver, jobContextBuilder, finalizer);
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // 一時ディレクトリ削除失敗はテスト本体より優先しない。
        }
    }

    private sealed class RecordingProcessLogWriter : IThumbnailCreateProcessLogWriter
    {
        public List<ThumbnailCreateProcessLogEntry> Entries { get; } = [];

        public void Write(ThumbnailCreateProcessLogEntry entry)
        {
            Entries.Add(entry);
        }
    }

    private sealed class TestHostRuntime : IThumbnailCreationHostRuntime
    {
        private readonly string placeholderPath;

        public TestHostRuntime(string placeholderPath)
        {
            this.placeholderPath = placeholderPath;
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            return placeholderPath;
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return Path.Combine(Path.GetTempPath(), fileName ?? "");
        }
    }

    private sealed class FakeVideoMetadataProvider : IVideoMetadataProvider
    {
        private readonly string codec;

        public FakeVideoMetadataProvider(string codec)
        {
            this.codec = codec;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = this.codec;
            return !string.IsNullOrWhiteSpace(codec);
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            return false;
        }
    }
}
