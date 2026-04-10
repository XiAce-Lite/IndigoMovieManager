using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndigoMovieManager.Skin.Runtime;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinApiServiceTests
{
    [Test]
    public void BuildDbIdentity_引用符とスラッシュ差を吸収して同じ値を返す()
    {
        string root = CreateTempDirectory("imm-webview-api-db");
        string dbPathA = Path.Combine(root, "db", "..", "main.wb");
        string dbPathB =
            $"\"{Path.Combine(root, "main.wb").Replace('\\', '/').ToUpperInvariant()}\"";

        string identityA = WhiteBrowserSkinDbIdentity.Build(dbPathA);
        string identityB = WhiteBrowserSkinDbIdentity.Build(dbPathB);

        Assert.Multiple(() =>
        {
            Assert.That(identityA, Is.Not.Empty);
            Assert.That(identityA, Is.EqualTo(identityB));
            Assert.That(
                WhiteBrowserSkinDbIdentity.BuildRecordKey(identityA, 42),
                Is.EqualTo($"{identityA}:42")
            );
        });
    }

    [Test]
    public void BuildContractIdentity_空値ケースを固定する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WhiteBrowserSkinDbIdentity.Build(""), Is.EqualTo(""));
            Assert.That(WhiteBrowserSkinDbIdentity.BuildRecordKey("", 42), Is.EqualTo(""));
            Assert.That(
                WhiteBrowserSkinThumbnailContractService.BuildThumbRevision("", "managed-thumbnail"),
                Is.EqualTo("0")
            );
        });
    }

    [Test]
    public async Task HandleUpdate_契約必須項目を埋めて返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-update");
        string thumbRoot = Path.Combine(root, "thum");
        string thumbPath = Path.Combine(thumbRoot, "movie.#abc123.jpg");
        Directory.CreateDirectory(thumbRoot);
        File.WriteAllBytes(thumbPath, Encoding.UTF8.GetBytes("not-a-real-jpeg"));
        DateTime revisionTime = new(2026, 4, 1, 12, 34, 56, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(thumbPath, revisionTime);

        MovieRecords movie = new()
        {
            Movie_Id = 7,
            Movie_Name = "movie.mp4",
            Movie_Path = Path.Combine(root, "movie.mp4"),
            Movie_Length = "00:10:00",
            Movie_Size = 12345,
            Score = 88,
            Tags = $"tagA{Environment.NewLine}tagB",
            ThumbPathGrid = thumbPath,
            IsExists = true,
        };

        WhiteBrowserSkinApiService service = CreateService(
            [movie],
            selectedMovie: movie,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: thumbRoot
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "update",
            JsonDocument.Parse("""{"startIndex":0,"count":10}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        WhiteBrowserSkinUpdateResponse payload = result.Payload as WhiteBrowserSkinUpdateResponse;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload.TotalCount, Is.EqualTo(1));
        Assert.That(payload.Items.Length, Is.EqualTo(1));

        WhiteBrowserSkinMovieDto dto = payload.Items[0];
        string expectedRevision = ComputeExpectedThumbRevision(
            thumbPath,
            WhiteBrowserSkinThumbnailSourceKinds.ManagedThumbnail
        );
        Assert.Multiple(() =>
        {
            Assert.That(dto.DbIdentity, Is.Not.Empty);
            Assert.That(dto.RecordKey, Is.EqualTo($"{dto.DbIdentity}:7"));
            Assert.That(dto.ThumbRevision, Is.EqualTo(expectedRevision));
            Assert.That(
                dto.ThumbUrl,
                Is.EqualTo("https://thum.local/movie.%23abc123.jpg?rev=" + expectedRevision)
            );
            Assert.That(
                dto.ThumbSourceKind,
                Is.EqualTo(WhiteBrowserSkinThumbnailSourceKinds.ManagedThumbnail)
            );
            Assert.That(dto.ThumbNaturalWidth, Is.EqualTo(160));
            Assert.That(dto.ThumbNaturalHeight, Is.EqualTo(120));
            Assert.That(dto.ThumbSheetColumns, Is.EqualTo(1));
            Assert.That(dto.ThumbSheetRows, Is.EqualTo(1));
            Assert.That(dto.Selected, Is.True);
            Assert.That(dto.Tags, Is.EqualTo(new[] { "tagA", "tagB" }));
            Assert.That(dto.id, Is.EqualTo(7));
            Assert.That(dto.title, Is.EqualTo("movie"));
            Assert.That(dto.ext, Is.EqualTo(".mp4"));
            Assert.That(dto.path, Is.EqualTo(movie.Movie_Path));
            Assert.That(dto.thum, Is.EqualTo(dto.ThumbUrl));
            Assert.That(dto.len, Is.EqualTo("00:10:00"));
            Assert.That(dto.size, Is.EqualTo(12345));
            Assert.That(dto.score, Is.EqualTo(88));
            Assert.That(dto.exist, Is.True);
            Assert.That(dto.select, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleGetInfo_WBメタ付きJpegから寸法を復元できる()
    {
        string root = CreateTempDirectory("imm-webview-api-info");
        string thumbRoot = Path.Combine(root, "thum");
        string thumbPath = Path.Combine(thumbRoot, "sample.jpg");
        Directory.CreateDirectory(thumbRoot);
        CreateSampleJpeg(thumbPath);
        WhiteBrowserThumbInfoSerializer.AppendToJpeg(
            thumbPath,
            new ThumbnailSheetSpec
            {
                ThumbCount = 4,
                ThumbWidth = 160,
                ThumbHeight = 90,
                ThumbColumns = 2,
                ThumbRows = 2,
                CaptureSeconds = [10, 20, 30, 40],
            }
        );

        MovieRecords movie = new()
        {
            Movie_Id = 9,
            Movie_Name = "sample.mp4",
            Movie_Path = Path.Combine(root, "sample.mp4"),
            ThumbPathGrid = thumbPath,
            IsExists = true,
        };

        WhiteBrowserSkinApiService service = CreateService(
            [movie],
            selectedMovie: null,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: thumbRoot
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "getInfo",
            JsonDocument.Parse("""{"movieId":9}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        WhiteBrowserSkinMovieDto dto = result.Payload as WhiteBrowserSkinMovieDto;
        Assert.That(dto, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dto.MovieId, Is.EqualTo(9));
            Assert.That(dto.ThumbNaturalWidth, Is.EqualTo(320));
            Assert.That(dto.ThumbNaturalHeight, Is.EqualTo(240));
            Assert.That(dto.ThumbSheetColumns, Is.EqualTo(2));
            Assert.That(dto.ThumbSheetRows, Is.EqualTo(2));
            Assert.That(
                dto.ThumbRevision,
                Is.EqualTo(
                    ComputeExpectedThumbRevision(
                        thumbPath,
                        WhiteBrowserSkinThumbnailSourceKinds.ManagedThumbnail
                    )
                )
            );
        });
    }

    [Test]
    public async Task HandleFocusThum_既存delegateへ委譲できる()
    {
        string root = CreateTempDirectory("imm-webview-api-focus");
        MovieRecords movie = new()
        {
            Movie_Id = 11,
            Movie_Name = "focus.mp4",
            Movie_Path = Path.Combine(root, "focus.mp4"),
        };

        MovieRecords focused = null;
        MovieRecords currentSelectedMovie = null;
        IReadOnlyList<MovieRecords> currentSelectedMovies = [];
        WhiteBrowserSkinApiService service = CreateService(
            [movie],
            selectedMovie: null,
            getCurrentSelectedMovie: () => currentSelectedMovie,
            getCurrentSelectedMovies: () => currentSelectedMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            focusMovieAsync: record =>
            {
                focused = record;
                currentSelectedMovie = record;
                currentSelectedMovies = [record];
                return Task.FromResult(true);
            }
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "focusThum",
            JsonDocument.Parse("""{"movieId":11}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        Assert.That(focused, Is.SameAs(movie));

        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("found").GetBoolean(), Is.True);
            Assert.That(payload.RootElement.GetProperty("focused").GetBoolean(), Is.True);
            Assert.That(payload.RootElement.GetProperty("focusedMovieId").GetInt64(), Is.EqualTo(11));
            Assert.That(payload.RootElement.GetProperty("movieId").GetInt64(), Is.EqualTo(11));
            Assert.That(payload.RootElement.GetProperty("id").GetInt64(), Is.EqualTo(11));
            Assert.That(payload.RootElement.GetProperty("selected").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task HandleUpdate_複数選択をSelectedへ反映できる()
    {
        string root = CreateTempDirectory("imm-webview-api-multiselect");
        MovieRecords movieA = new()
        {
            Movie_Id = 21,
            Movie_Name = "a.mp4",
            Movie_Path = Path.Combine(root, "a.mp4"),
        };
        MovieRecords movieB = new()
        {
            Movie_Id = 22,
            Movie_Name = "b.mp4",
            Movie_Path = Path.Combine(root, "b.mp4"),
        };

        IReadOnlyList<MovieRecords> selectedMovies = [movieA, movieB];
        WhiteBrowserSkinApiService service = CreateService(
            [movieA, movieB],
            selectedMovie: movieA,
            getCurrentSelectedMovies: () => selectedMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "update",
            JsonDocument.Parse("""{"startIndex":0,"count":10}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        WhiteBrowserSkinUpdateResponse payload = result.Payload as WhiteBrowserSkinUpdateResponse;
        Assert.That(payload, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(payload.Items.Length, Is.EqualTo(2));
            Assert.That(payload.Items[0].MovieId, Is.EqualTo(21));
            Assert.That(payload.Items[0].Selected, Is.True);
            Assert.That(payload.Items[0].select, Is.EqualTo(1));
            Assert.That(payload.Items[1].MovieId, Is.EqualTo(22));
            Assert.That(payload.Items[1].Selected, Is.True);
            Assert.That(payload.Items[1].select, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleTrace_委譲してtrueを返す()
    {
        string traced = "";
        WhiteBrowserSkinApiService service = CreateService(
            [],
            selectedMovie: null,
            dbFullPath: "",
            dbName: "",
            skinName: "",
            thumbRoot: "",
            trace: message => traced = message
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "trace",
            JsonDocument.Parse("""{"message":"hello"}""").RootElement
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Payload, Is.EqualTo(true));
            Assert.That(traced, Is.EqualTo("hello"));
        });
    }

    [Test]
    public async Task HandleFind_非同期delegate完了後のupdate結果を返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-find");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 1,
                Movie_Name = "before.mp4",
                Movie_Path = Path.Combine(root, "before.mp4"),
            },
        ];

        string searchedKeyword = "";
        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            getCurrentSelectedMovie: () => null,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            executeSearchAsync: async keyword =>
            {
                await Task.Delay(20);
                searchedKeyword = keyword;
                visibleMovies =
                [
                    new MovieRecords
                    {
                        Movie_Id = 9,
                        Movie_Name = "after.mp4",
                        Movie_Path = Path.Combine(root, "after.mp4"),
                    },
                ];
                return true;
            }
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "find",
            JsonDocument.Parse("""{"keyword":"tag:test"}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.TypeOf<WhiteBrowserSkinUpdateResponse>());
        WhiteBrowserSkinUpdateResponse payload = (WhiteBrowserSkinUpdateResponse)result.Payload;
        Assert.Multiple(() =>
        {
            Assert.That(searchedKeyword, Is.EqualTo("tag:test"));
            Assert.That(payload.TotalCount, Is.EqualTo(1));
            Assert.That(payload.Items.Length, Is.EqualTo(1));
            Assert.That(payload.Items[0].MovieId, Is.EqualTo(9));
            Assert.That(payload.Items[0].MovieName, Is.EqualTo("after.mp4"));
        });
    }

    [Test]
    public async Task HandleSort_数値sortIdでもdelegate完了後のupdate結果を返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-sort");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 1,
                Movie_Name = "before.mp4",
                Movie_Path = Path.Combine(root, "before.mp4"),
            },
        ];

        string sortedKey = "";
        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            getCurrentSelectedMovie: () => null,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            executeSortAsync: async sortId =>
            {
                await Task.Delay(20);
                sortedKey = sortId;
                visibleMovies =
                [
                    new MovieRecords
                    {
                        Movie_Id = 4,
                        Movie_Name = "sorted.mp4",
                        Movie_Path = Path.Combine(root, "sorted.mp4"),
                    },
                ];
                return true;
            }
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "sort",
            JsonDocument.Parse("""{"sortId":7}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.TypeOf<WhiteBrowserSkinUpdateResponse>());
        WhiteBrowserSkinUpdateResponse payload = (WhiteBrowserSkinUpdateResponse)result.Payload;
        Assert.Multiple(() =>
        {
            Assert.That(sortedKey, Is.EqualTo("7"));
            Assert.That(payload.TotalCount, Is.EqualTo(1));
            Assert.That(payload.Items.Length, Is.EqualTo(1));
            Assert.That(payload.Items[0].MovieId, Is.EqualTo(4));
            Assert.That(payload.Items[0].MovieName, Is.EqualTo("sorted.mp4"));
        });
    }

    [Test]
    public async Task HandleProfileAndSkinApis_delegateへ委譲できる()
    {
        string root = CreateTempDirectory("imm-webview-api-profile");
        string readKey = "";
        string writeKey = "";
        string writeValue = "";
        string changedSkinName = "";
        WhiteBrowserSkinApiService service = CreateService(
            [],
            selectedMovie: null,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            getProfileValueAsync: key =>
            {
                readKey = key;
                return Task.FromResult("remembered");
            },
            writeProfileValueAsync: (key, value) =>
            {
                writeKey = key;
                writeValue = value;
                return Task.FromResult(true);
            },
            changeSkinAsync: skinName =>
            {
                changedSkinName = skinName;
                return Task.FromResult(true);
            }
        );

        WhiteBrowserSkinApiInvocationResult getProfileResult = await service.HandleAsync(
            "getProfile",
            JsonDocument.Parse("""{"key":"grid.columns"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult writeProfileResult = await service.HandleAsync(
            "writeProfile",
            JsonDocument.Parse("""{"key":"grid.columns","value":"4"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult changeSkinResult = await service.HandleAsync(
            "changeSkin",
            JsonDocument.Parse("""{"skinName":"WhiteBrowserDefaultGrid"}""").RootElement
        );

        Assert.Multiple(() =>
        {
            Assert.That(getProfileResult.Succeeded, Is.True);
            Assert.That(getProfileResult.Payload, Is.EqualTo("remembered"));
            Assert.That(readKey, Is.EqualTo("grid.columns"));

            Assert.That(writeProfileResult.Succeeded, Is.True);
            Assert.That(writeProfileResult.Payload, Is.EqualTo(true));
            Assert.That(writeKey, Is.EqualTo("grid.columns"));
            Assert.That(writeValue, Is.EqualTo("4"));

            Assert.That(changeSkinResult.Succeeded, Is.True);
            Assert.That(changeSkinResult.Payload, Is.EqualTo(true));
            Assert.That(changedSkinName, Is.EqualTo("WhiteBrowserDefaultGrid"));
        });
    }

    [Test]
    public async Task HandleSelectThum_独立した選択delegateへ委譲できる()
    {
        string root = CreateTempDirectory("imm-webview-api-select");
        MovieRecords focusedMovie = new()
        {
            Movie_Id = 14,
            Movie_Name = "focused.mp4",
            Movie_Path = Path.Combine(root, "focused.mp4"),
        };
        MovieRecords movie = new()
        {
            Movie_Id = 15,
            Movie_Name = "select.mp4",
            Movie_Path = Path.Combine(root, "select.mp4"),
        };

        bool focusCalled = false;
        IReadOnlyList<MovieRecords> selectedMovies = [focusedMovie];
        WhiteBrowserSkinApiService service = CreateService(
            [focusedMovie, movie],
            selectedMovie: focusedMovie,
            getCurrentSelectedMovies: () => selectedMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            focusMovieAsync: record =>
            {
                focusCalled = true;
                return Task.FromResult(true);
            },
            setMovieSelectionAsync: (record, isSelected) =>
            {
                if (isSelected)
                {
                    selectedMovies = [focusedMovie, record];
                }
                else
                {
                    selectedMovies = [focusedMovie];
                }

                return Task.FromResult(true);
            }
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "selectThum",
            JsonDocument.Parse("""{"movieId":15,"selected":true}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        Assert.That(focusCalled, Is.False);

        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("found").GetBoolean(), Is.True);
            Assert.That(payload.RootElement.GetProperty("focused").GetBoolean(), Is.False);
            Assert.That(payload.RootElement.GetProperty("focusedMovieId").GetInt64(), Is.EqualTo(14));
            Assert.That(payload.RootElement.GetProperty("selected").GetBoolean(), Is.True);
            Assert.That(payload.RootElement.GetProperty("movieId").GetInt64(), Is.EqualTo(15));
            Assert.That(payload.RootElement.GetProperty("id").GetInt64(), Is.EqualTo(15));
        });
    }

    [Test]
    public async Task HandleSelectThum_選択解除後の現在フォーカスを返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-select-shift");
        MovieRecords focusedMovie = new()
        {
            Movie_Id = 31,
            Movie_Name = "focused.mp4",
            Movie_Path = Path.Combine(root, "focused.mp4"),
        };
        MovieRecords remainedMovie = new()
        {
            Movie_Id = 32,
            Movie_Name = "remained.mp4",
            Movie_Path = Path.Combine(root, "remained.mp4"),
        };

        MovieRecords currentSelectedMovie = focusedMovie;
        IReadOnlyList<MovieRecords> selectedMovies = [focusedMovie, remainedMovie];
        WhiteBrowserSkinApiService service = CreateService(
            [focusedMovie, remainedMovie],
            selectedMovie: focusedMovie,
            getCurrentSelectedMovie: () => currentSelectedMovie,
            getCurrentSelectedMovies: () => selectedMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            setMovieSelectionAsync: (record, isSelected) =>
            {
                if (!isSelected && record?.Movie_Id == focusedMovie.Movie_Id)
                {
                    currentSelectedMovie = remainedMovie;
                    selectedMovies = [remainedMovie];
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "selectThum",
            JsonDocument.Parse("""{"movieId":31,"selected":false}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);

        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("found").GetBoolean(), Is.True);
            Assert.That(payload.RootElement.GetProperty("selectionChanged").GetBoolean(), Is.True);
            Assert.That(payload.RootElement.GetProperty("focused").GetBoolean(), Is.False);
            Assert.That(payload.RootElement.GetProperty("focusedMovieId").GetInt64(), Is.EqualTo(32));
            Assert.That(payload.RootElement.GetProperty("selected").GetBoolean(), Is.False);
            Assert.That(payload.RootElement.GetProperty("movieId").GetInt64(), Is.EqualTo(31));
        });
    }

    private static WhiteBrowserSkinApiService CreateService(
        IReadOnlyList<MovieRecords> visibleMovies,
        MovieRecords? selectedMovie,
        string dbFullPath,
        string dbName,
        string skinName,
        string thumbRoot,
        Func<IReadOnlyList<MovieRecords>>? getCurrentSelectedMovies = null,
        Func<MovieRecords?>? getCurrentSelectedMovie = null,
        Func<MovieRecords, Task<bool>>? focusMovieAsync = null,
        Func<MovieRecords, bool, Task<bool>>? setMovieSelectionAsync = null,
        Func<string, Task<bool>>? executeSearchAsync = null,
        Func<string, Task<bool>>? executeSortAsync = null,
        Func<string, Task<string>>? getProfileValueAsync = null,
        Func<string, string, Task<bool>>? writeProfileValueAsync = null,
        Func<string, Task<bool>>? changeSkinAsync = null,
        Action<string>? trace = null
    )
    {
        return CreateService(
            () => visibleMovies,
            dbFullPath,
            dbName,
            skinName,
            thumbRoot,
            getCurrentSelectedMovie: getCurrentSelectedMovie ?? (() => selectedMovie),
            getCurrentSelectedMovies:
                getCurrentSelectedMovies
                ?? (() => selectedMovie is null ? Array.Empty<MovieRecords>() : [selectedMovie]),
            focusMovieAsync: focusMovieAsync,
            setMovieSelectionAsync: setMovieSelectionAsync,
            executeSearchAsync: executeSearchAsync,
            executeSortAsync: executeSortAsync,
            getProfileValueAsync: getProfileValueAsync,
            writeProfileValueAsync: writeProfileValueAsync,
            changeSkinAsync: changeSkinAsync,
            trace: trace
        );
    }

    private static WhiteBrowserSkinApiService CreateService(
        Func<IReadOnlyList<MovieRecords>> getVisibleMovies,
        string dbFullPath,
        string dbName,
        string skinName,
        string thumbRoot,
        Func<MovieRecords?>? getCurrentSelectedMovie = null,
        Func<IReadOnlyList<MovieRecords>>? getCurrentSelectedMovies = null,
        Func<MovieRecords, Task<bool>>? focusMovieAsync = null,
        Func<MovieRecords, bool, Task<bool>>? setMovieSelectionAsync = null,
        Func<string, Task<bool>>? executeSearchAsync = null,
        Func<string, Task<bool>>? executeSortAsync = null,
        Func<string, Task<string>>? getProfileValueAsync = null,
        Func<string, string, Task<bool>>? writeProfileValueAsync = null,
        Func<string, Task<bool>>? changeSkinAsync = null,
        Action<string>? trace = null
    )
    {
        return new WhiteBrowserSkinApiService(
            new WhiteBrowserSkinApiServiceDependencies
            {
                GetVisibleMovies = getVisibleMovies,
                GetCurrentTabIndex = () => 2,
                GetCurrentDbFullPath = () => dbFullPath,
                GetCurrentDbName = () => dbName,
                GetCurrentSkinName = () => skinName,
                GetCurrentThumbFolder = () => thumbRoot,
                GetCurrentSelectedMovie = getCurrentSelectedMovie ?? (() => null),
                GetCurrentSelectedMovies =
                    getCurrentSelectedMovies ?? (() => Array.Empty<MovieRecords>()),
                FocusMovieAsync = focusMovieAsync ?? (_ => Task.FromResult(false)),
                SetMovieSelectionAsync =
                    setMovieSelectionAsync ?? ((_, _) => Task.FromResult(false)),
                ExecuteSearchAsync = executeSearchAsync ?? (_ => Task.FromResult(false)),
                ExecuteSortAsync = executeSortAsync ?? (_ => Task.FromResult(false)),
                GetProfileValueAsync = getProfileValueAsync ?? (_ => Task.FromResult("")),
                WriteProfileValueAsync =
                    writeProfileValueAsync ?? ((_, _) => Task.FromResult(false)),
                ChangeSkinAsync = changeSkinAsync ?? (_ => Task.FromResult(false)),
                ResolveThumbUrl = _ => "",
                Trace = trace ?? (_ => { }),
            }
        );
    }

    private static void CreateSampleJpeg(string jpgPath)
    {
        using Bitmap bitmap = new(320, 240);
        using Graphics graphics = Graphics.FromImage(bitmap);

        for (int y = 0; y < bitmap.Height; y++)
        {
            using Pen pen = new(Color.FromArgb((y * 3) % 255, (y * 5) % 255, (y * 7) % 255));
            graphics.DrawLine(pen, 0, y, bitmap.Width - 1, y);
        }

        bitmap.Save(jpgPath, ImageFormat.Jpeg);
    }

    private static string ComputeExpectedThumbRevision(string thumbPath, string sourceKind)
    {
        string normalizedPath = NormalizePath(thumbPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
        {
            return "0";
        }

        FileInfo fileInfo = new(normalizedPath);
        string fingerprint =
            $"{sourceKind ?? ""}|{normalizedPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"')).Replace('/', '\\').ToLowerInvariant();
        }
        catch
        {
            return path.Trim().Trim('"').Replace('/', '\\').ToLowerInvariant();
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }
}
