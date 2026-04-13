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
            Movie_Path = @"E:\incoming\movie.mp4",
            Movie_Length = "00:10:00",
            Movie_Size = 12345,
            Score = 88,
            Tags = $"tagA{Environment.NewLine}tagB",
            Artist = "sample-artist",
            Kana = "さんぷる",
            Container = "MP4",
            Video = "1920x1080&nbsp;60fps",
            Audio = "AAC&nbsp;128kbps",
            Extra = "chapter=1",
            File_Date = "2026-04-12 12:34:56",
            Comment1 = "memo-a",
            Comment2 = "memo-b",
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
            Assert.That(dto.artist, Is.EqualTo("sample-artist"));
            Assert.That(dto.drive, Is.EqualTo("E:"));
            Assert.That(dto.dir, Is.EqualTo(@"\incoming\"));
            Assert.That(dto.ext, Is.EqualTo(".mp4"));
            Assert.That(dto.kana, Is.EqualTo("さんぷる"));
            Assert.That(dto.tags, Is.EqualTo(new[] { "tagA", "tagB" }));
            Assert.That(dto.container, Is.EqualTo("MP4"));
            Assert.That(dto.video, Is.EqualTo("1920x1080&nbsp;60fps"));
            Assert.That(dto.audio, Is.EqualTo("AAC&nbsp;128kbps"));
            Assert.That(dto.extra, Is.EqualTo("chapter=1"));
            Assert.That(dto.fileDate, Is.EqualTo("2026-04-12 12:34:56"));
            Assert.That(dto.comments, Is.EqualTo("memo-a\nmemo-b"));
            Assert.That(dto.lenSec, Is.EqualTo("600"));
            Assert.That(dto.offset, Is.EqualTo(1));
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
    public async Task HandleGetInfos_startIndexとcountで必要範囲だけ返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-getinfos-range");
        MovieRecords movieA = new()
        {
            Movie_Id = 31,
            Movie_Name = "alpha.mp4",
            Movie_Path = Path.Combine(root, "alpha.mp4"),
        };
        MovieRecords movieB = new()
        {
            Movie_Id = 32,
            Movie_Name = "beta.mp4",
            Movie_Path = Path.Combine(root, "beta.mp4"),
        };
        MovieRecords movieC = new()
        {
            Movie_Id = 33,
            Movie_Name = "gamma.mp4",
            Movie_Path = Path.Combine(root, "gamma.mp4"),
        };

        WhiteBrowserSkinApiService service = CreateService(
            [movieA, movieB, movieC],
            selectedMovie: movieB,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "getInfos",
            JsonDocument.Parse("""{"startIndex":1,"count":1}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        WhiteBrowserSkinMovieDto[] payload = result.Payload as WhiteBrowserSkinMovieDto[];
        Assert.That(payload, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(payload.Length, Is.EqualTo(1));
            Assert.That(payload[0].MovieId, Is.EqualTo(32));
            Assert.That(payload[0].Selected, Is.True);
        });
    }

    [Test]
    public async Task HandleGetInfos_recordKeys指定で要求順を維持して返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-getinfos-recordkeys");
        string dbPath = Path.Combine(root, "main.wb");
        string dbIdentity = WhiteBrowserSkinDbIdentity.Build(dbPath);
        MovieRecords movieA = new()
        {
            Movie_Id = 41,
            Movie_Name = "alpha.mp4",
            Movie_Path = Path.Combine(root, "alpha.mp4"),
        };
        MovieRecords movieB = new()
        {
            Movie_Id = 42,
            Movie_Name = "beta.mp4",
            Movie_Path = Path.Combine(root, "beta.mp4"),
        };

        WhiteBrowserSkinApiService service = CreateService(
            [movieA, movieB],
            selectedMovie: null,
            dbFullPath: dbPath,
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        string payloadJson =
            $$"""
            {"recordKeys":["{{WhiteBrowserSkinDbIdentity.BuildRecordKey(dbIdentity, 42)}}","{{WhiteBrowserSkinDbIdentity.BuildRecordKey(dbIdentity, 41)}}"]}
            """;
        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "getInfos",
            JsonDocument.Parse(payloadJson).RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        WhiteBrowserSkinMovieDto[] payload = result.Payload as WhiteBrowserSkinMovieDto[];
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload.Select(x => x.MovieId), Is.EqualTo(new[] { 42L, 41L }));
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
    public async Task HandleAddWhere_SQL風条件を現在結果へ重ねて空文字でクリアできる()
    {
        string root = CreateTempDirectory("imm-webview-api-addwhere");
        MovieRecords movieA = new()
        {
            Movie_Id = 51,
            Movie_Name = "alpha.mp4",
            Movie_Path = Path.Combine(root, "idol-alpha.mp4"),
            Score = 90,
        };
        MovieRecords movieB = new()
        {
            Movie_Id = 52,
            Movie_Name = "beta.mp4",
            Movie_Path = Path.Combine(root, "beta.mp4"),
            Score = 40,
        };

        WhiteBrowserSkinApiService service = CreateService(
            [movieA, movieB],
            selectedMovie: null,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        WhiteBrowserSkinApiInvocationResult addWhereResult = await service.HandleAsync(
            "addWhere",
            JsonDocument.Parse("""{"where":"movie_path like '%idol%' and score >= 80"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult clearResult = await service.HandleAsync(
            "addWhere",
            JsonDocument.Parse("""{"where":""}""").RootElement
        );

        Assert.That(addWhereResult.Succeeded, Is.True);
        Assert.That(addWhereResult.Payload, Is.TypeOf<WhiteBrowserSkinUpdateResponse>());
        WhiteBrowserSkinUpdateResponse addWherePayload =
            (WhiteBrowserSkinUpdateResponse)addWhereResult.Payload;
        Assert.That(addWherePayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 51L }));

        Assert.That(clearResult.Succeeded, Is.True);
        WhiteBrowserSkinUpdateResponse clearPayload =
            (WhiteBrowserSkinUpdateResponse)clearResult.Payload;
        Assert.That(clearPayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 51L, 52L }));
    }

    [Test]
    public async Task HandleAddOrder_override指定で追加並びとsort後クリアを制御できる()
    {
        string root = CreateTempDirectory("imm-webview-api-addorder");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 61,
                Movie_Name = "zeta.mp4",
                Movie_Path = Path.Combine(root, "zeta.mp4"),
                Score = 1,
            },
            new MovieRecords
            {
                Movie_Id = 62,
                Movie_Name = "alpha.mp4",
                Movie_Path = Path.Combine(root, "alpha.mp4"),
                Score = 1,
            },
            new MovieRecords
            {
                Movie_Id = 63,
                Movie_Name = "beta.mp4",
                Movie_Path = Path.Combine(root, "beta.mp4"),
                Score = 3,
            },
        ];

        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            currentSortId: "7",
            executeSortAsync: _ => Task.FromResult(true),
            resolveSortId: sortKey => string.Equals(sortKey, "ファイル名(昇順)", StringComparison.Ordinal)
                ? "12"
                : sortKey
        );

        WhiteBrowserSkinApiInvocationResult secondaryOrderResult = await service.HandleAsync(
            "addOrder",
            JsonDocument.Parse("""{"order":"{movie_name asc}","override":0}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult overrideOrderResult = await service.HandleAsync(
            "addOrder",
            JsonDocument.Parse("""{"order":"ファイル名(昇順)","override":1}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult sortResult = await service.HandleAsync(
            "sort",
            JsonDocument.Parse("""{"sortId":"7"}""").RootElement
        );

        Assert.That(secondaryOrderResult.Succeeded, Is.True);
        WhiteBrowserSkinUpdateResponse secondaryOrderPayload =
            (WhiteBrowserSkinUpdateResponse)secondaryOrderResult.Payload;
        Assert.That(
            secondaryOrderPayload.Items.Select(x => x.MovieId),
            Is.EqualTo(new[] { 62L, 61L, 63L })
        );

        Assert.That(overrideOrderResult.Succeeded, Is.True);
        WhiteBrowserSkinUpdateResponse overrideOrderPayload =
            (WhiteBrowserSkinUpdateResponse)overrideOrderResult.Payload;
        Assert.That(
            overrideOrderPayload.Items.Select(x => x.MovieId),
            Is.EqualTo(new[] { 62L, 63L, 61L })
        );

        Assert.That(sortResult.Succeeded, Is.True);
        WhiteBrowserSkinUpdateResponse sortPayload =
            (WhiteBrowserSkinUpdateResponse)sortResult.Payload;
        Assert.That(sortPayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 61L, 62L, 63L }));
    }

    [Test]
    public async Task HandleGetFindInfo_検索状態とoverlay状態をまとめて返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-findinfo");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 71,
                Movie_Name = "idol-a.mp4",
                Movie_Path = Path.Combine(root, "idol-a.mp4"),
                Score = 90,
            },
            new MovieRecords
            {
                Movie_Id = 72,
                Movie_Name = "idol-b.mp4",
                Movie_Path = Path.Combine(root, "idol-b.mp4"),
                Score = 80,
            },
            new MovieRecords
            {
                Movie_Id = 73,
                Movie_Name = "other.mp4",
                Movie_Path = Path.Combine(root, "other.mp4"),
                Score = 10,
            },
        ];

        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            currentSortId: "12",
            currentSortName: "ファイル名(昇順)",
            currentSearchKeyword: "idol",
            registeredMovieCount: 10,
            resolveSortId: sortKey => string.Equals(sortKey, "スコア(低い順)", StringComparison.Ordinal)
                ? "7"
                : sortKey
        );

        WhiteBrowserSkinApiInvocationResult addWhereResult = await service.HandleAsync(
            "addWhere",
            JsonDocument.Parse("""{"where":"score >= 80"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult addFilterResult = await service.HandleAsync(
            "addFilter",
            JsonDocument.Parse("""{"filter":"idol"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult addOrderResult = await service.HandleAsync(
            "addOrder",
            JsonDocument.Parse("""{"order":"スコア(低い順)","override":1}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult getFindInfoResult = await service.HandleAsync(
            "getFindInfo",
            JsonDocument.Parse("""{}""").RootElement
        );

        Assert.That(addWhereResult.Succeeded, Is.True);
        Assert.That(addFilterResult.Succeeded, Is.True);
        Assert.That(addOrderResult.Succeeded, Is.True);
        Assert.That(getFindInfoResult.Succeeded, Is.True);

        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(getFindInfoResult.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("find").GetString(), Is.EqualTo("idol"));
            Assert.That(payload.RootElement.GetProperty("sort")[0].GetString(), Is.EqualTo("ファイル名(昇順)"));
            Assert.That(payload.RootElement.GetProperty("sort")[1].GetString(), Is.EqualTo("#スコア(低い順)"));
            Assert.That(payload.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(1));
            Assert.That(payload.RootElement.GetProperty("filter")[0].GetString(), Is.EqualTo("idol"));
            Assert.That(payload.RootElement.GetProperty("where").GetString(), Is.EqualTo("score >= 80"));
            Assert.That(payload.RootElement.GetProperty("total").GetInt32(), Is.EqualTo(10));
            Assert.That(payload.RootElement.GetProperty("result").GetInt32(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleGetFindInfo_登録件数未同期時はtotalを0で返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-findinfo-total");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 74,
                Movie_Name = "sample.mp4",
                Movie_Path = Path.Combine(root, "sample.mp4"),
            },
        ];

        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            currentSearchKeyword: "sample",
            registeredMovieCount: 0
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "getFindInfo",
            JsonDocument.Parse("""{}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);

        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("total").GetInt32(), Is.EqualTo(0));
            Assert.That(payload.RootElement.GetProperty("result").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleGetRelation_タイトル近傍とタグを返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-relation");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 81,
                Movie_Name = "Idol Live 2025.mp4",
                Movie_Path = Path.Combine(root, "idol-live-2025.mp4"),
                Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine(["idol", "live", "concert"]),
            },
            new MovieRecords
            {
                Movie_Id = 82,
                Movie_Name = "Idol Talk 2025.mp4",
                Movie_Path = Path.Combine(root, "idol-talk-2025.mp4"),
                Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine(["idol", "talk"]),
            },
            new MovieRecords
            {
                Movie_Id = 83,
                Movie_Name = "Travel Vlog.mp4",
                Movie_Path = Path.Combine(root, "travel-vlog.mp4"),
                Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine(["travel"]),
            },
        ];

        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "getRelation",
            JsonDocument.Parse("""{"title":"Idol Live 2025","limit":2}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);

        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(payload.RootElement.GetArrayLength(), Is.EqualTo(2));
            Assert.That(payload.RootElement[0].GetProperty("id").GetInt64(), Is.EqualTo(82));
            Assert.That(payload.RootElement[0].GetProperty("title").GetString(), Is.EqualTo("Idol Talk 2025"));
            Assert.That(payload.RootElement[0].GetProperty("tags")[0].GetString(), Is.EqualTo("idol"));
            Assert.That(payload.RootElement[1].GetProperty("id").GetInt64(), Is.EqualTo(83));
        });
    }

    [Test]
    public async Task ResetTransientState_addWhereとaddOrderをまとめてクリアできる()
    {
        string root = CreateTempDirectory("imm-webview-api-reset-state");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 75,
                Movie_Name = "a.mp4",
                Movie_Path = Path.Combine(root, "a.mp4"),
                Score = 100,
            },
            new MovieRecords
            {
                Movie_Id = 76,
                Movie_Name = "b.mp4",
                Movie_Path = Path.Combine(root, "b.mp4"),
                Score = 10,
            },
        ];

        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            currentSortId: "12",
            currentSortName: "ファイル名(昇順)",
            resolveSortId: sortKey => string.Equals(sortKey, "スコア(低い順)", StringComparison.Ordinal)
                ? "7"
                : sortKey
        );

        await service.HandleAsync(
            "addFilter",
            JsonDocument.Parse("""{"filter":"a"}""").RootElement
        );
        await service.HandleAsync(
            "addWhere",
            JsonDocument.Parse("""{"where":"score >= 80"}""").RootElement
        );
        await service.HandleAsync(
            "addOrder",
            JsonDocument.Parse("""{"order":"スコア(低い順)","override":1}""").RootElement
        );

        service.ResetTransientState();

        WhiteBrowserSkinApiInvocationResult getFindInfoResult = await service.HandleAsync(
            "getFindInfo",
            JsonDocument.Parse("""{}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult updateResult = await service.HandleAsync(
            "update",
            JsonDocument.Parse("""{"startIndex":0,"count":10}""").RootElement
        );

        Assert.That(getFindInfoResult.Succeeded, Is.True);
        Assert.That(updateResult.Succeeded, Is.True);

        using JsonDocument findInfoPayload = JsonDocument.Parse(
            JsonSerializer.Serialize(getFindInfoResult.Payload)
        );
        WhiteBrowserSkinUpdateResponse updatePayload = (WhiteBrowserSkinUpdateResponse)updateResult.Payload;
        Assert.Multiple(() =>
        {
            Assert.That(findInfoPayload.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(0));
            Assert.That(findInfoPayload.RootElement.GetProperty("where").GetString(), Is.EqualTo(""));
            Assert.That(findInfoPayload.RootElement.GetProperty("sort")[1].GetString(), Is.EqualTo(""));
            Assert.That(findInfoPayload.RootElement.GetProperty("result").GetInt32(), Is.EqualTo(2));
            Assert.That(updatePayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 75L, 76L }));
        });
    }

    [Test]
    public async Task HandleAddFilter_remove_clearでoverlay検索を重ねて制御できる()
    {
        string root = CreateTempDirectory("imm-webview-api-addfilter");
        IReadOnlyList<MovieRecords> visibleMovies =
        [
            new MovieRecords
            {
                Movie_Id = 77,
                Movie_Name = "idol-a.mp4",
                Movie_Path = Path.Combine(root, "idol-a.mp4"),
                Tags = "fav",
            },
            new MovieRecords
            {
                Movie_Id = 78,
                Movie_Name = "idol-b.mp4",
                Movie_Path = Path.Combine(root, "idol-b.mp4"),
                Tags = "skip",
            },
            new MovieRecords
            {
                Movie_Id = 79,
                Movie_Name = "other.mp4",
                Movie_Path = Path.Combine(root, "other.mp4"),
                Tags = "fav",
            },
        ];

        WhiteBrowserSkinApiService service = CreateService(
            () => visibleMovies,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        WhiteBrowserSkinApiInvocationResult addFirstFilterResult = await service.HandleAsync(
            "addFilter",
            JsonDocument.Parse("""{"filter":"idol"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult addSecondFilterResult = await service.HandleAsync(
            "addFilter",
            JsonDocument.Parse("""{"filter":"skip"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult removeFilterResult = await service.HandleAsync(
            "removeFilter",
            JsonDocument.Parse("""{"filter":"skip"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult clearFilterResult = await service.HandleAsync(
            "clearFilter",
            JsonDocument.Parse("""{}""").RootElement
        );

        Assert.That(addFirstFilterResult.Succeeded, Is.True);
        Assert.That(addSecondFilterResult.Succeeded, Is.True);
        Assert.That(removeFilterResult.Succeeded, Is.True);
        Assert.That(clearFilterResult.Succeeded, Is.True);

        WhiteBrowserSkinUpdateResponse firstPayload =
            (WhiteBrowserSkinUpdateResponse)addFirstFilterResult.Payload;
        WhiteBrowserSkinUpdateResponse secondPayload =
            (WhiteBrowserSkinUpdateResponse)addSecondFilterResult.Payload;
        WhiteBrowserSkinUpdateResponse removePayload =
            (WhiteBrowserSkinUpdateResponse)removeFilterResult.Payload;
        WhiteBrowserSkinUpdateResponse clearPayload =
            (WhiteBrowserSkinUpdateResponse)clearFilterResult.Payload;

        Assert.Multiple(() =>
        {
            Assert.That(firstPayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 77L, 78L }));
            Assert.That(secondPayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 78L }));
            Assert.That(removePayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 77L, 78L }));
            Assert.That(clearPayload.Items.Select(x => x.MovieId), Is.EqualTo(new[] { 77L, 78L, 79L }));
        });
    }

    [Test]
    public async Task HandleGetFocusThum_現在フォーカス中のIDを返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-getfocus");
        MovieRecords movie = new()
        {
            Movie_Id = 81,
            Movie_Name = "focused.mp4",
            Movie_Path = Path.Combine(root, "focused.mp4"),
        };

        MovieRecords? currentSelectedMovie = null;
        WhiteBrowserSkinApiService service = CreateService(
            [],
            selectedMovie: null,
            getCurrentSelectedMovie: () => currentSelectedMovie,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum")
        );

        WhiteBrowserSkinApiInvocationResult emptyResult = await service.HandleAsync(
            "getFocusThum",
            JsonDocument.Parse("""{}""").RootElement
        );
        currentSelectedMovie = movie;
        WhiteBrowserSkinApiInvocationResult focusedResult = await service.HandleAsync(
            "getFocusThum",
            JsonDocument.Parse("""{}""").RootElement
        );

        Assert.Multiple(() =>
        {
            Assert.That(emptyResult.Succeeded, Is.True);
            Assert.That(emptyResult.Payload, Is.EqualTo(0L));
            Assert.That(focusedResult.Succeeded, Is.True);
            Assert.That(focusedResult.Payload, Is.EqualTo(81L));
        });
    }

    [Test]
    public async Task HandleGetSelectThums_複数選択のID一覧を返せる()
    {
        string root = CreateTempDirectory("imm-webview-api-getselect");
        MovieRecords movieA = new()
        {
            Movie_Id = 91,
            Movie_Name = "a.mp4",
            Movie_Path = Path.Combine(root, "a.mp4"),
        };
        MovieRecords movieB = new()
        {
            Movie_Id = 92,
            Movie_Name = "b.mp4",
            Movie_Path = Path.Combine(root, "b.mp4"),
        };

        IReadOnlyList<MovieRecords> selectedMovies = [movieB, movieA, movieB];
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
            "getSelectThums",
            JsonDocument.Parse("""{}""").RootElement
        );

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.EqualTo(new[] { 92L, 91L }));
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

    [Test]
    public async Task HandleTagApis_タグ更新delegateへ委譲できる()
    {
        string root = CreateTempDirectory("imm-webview-api-tags");
        MovieRecords movie = new()
        {
            Movie_Id = 41,
            Movie_Name = "tagged.mp4",
            Movie_Path = Path.Combine(root, "tagged.mp4"),
            Tag = ["alpha"],
            Tags = "alpha",
        };

        List<WhiteBrowserSkinTagMutationMode> invokedModes = [];
        WhiteBrowserSkinApiService service = CreateService(
            [movie],
            selectedMovie: movie,
            dbFullPath: Path.Combine(root, "main.wb"),
            dbName: "main",
            skinName: "SampleSkin",
            thumbRoot: Path.Combine(root, "thum"),
            mutateMovieTagAsync: (record, tagName, mutationMode) =>
            {
                invokedModes.Add(mutationMode);
                List<string> currentTags = record.Tag?.ToList() ?? [];
                bool exists = currentTags.Any(x =>
                    string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
                );

                switch (mutationMode)
                {
                    case WhiteBrowserSkinTagMutationMode.Add:
                        if (!exists)
                        {
                            currentTags.Add(tagName);
                        }
                        break;
                    case WhiteBrowserSkinTagMutationMode.Remove:
                        currentTags.RemoveAll(x =>
                            string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
                        );
                        break;
                    case WhiteBrowserSkinTagMutationMode.Flip:
                        if (exists)
                        {
                            currentTags.RemoveAll(x =>
                                string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
                            );
                        }
                        else
                        {
                            currentTags.Add(tagName);
                        }
                        break;
                }

                record.Tag = currentTags
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                record.Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. record.Tag]);
                bool hasTag = record.Tag.Any(x =>
                    string.Equals(x, tagName, StringComparison.CurrentCultureIgnoreCase)
                );
                return Task.FromResult(new WhiteBrowserSkinTagMutationResult(true, hasTag));
            }
        );

        WhiteBrowserSkinApiInvocationResult addResult = await service.HandleAsync(
            "addTag",
            JsonDocument.Parse("""{"tagName":"beta"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult removeResult = await service.HandleAsync(
            "removeTag",
            JsonDocument.Parse("""{"movieId":41,"tag":"alpha"}""").RootElement
        );
        WhiteBrowserSkinApiInvocationResult flipResult = await service.HandleAsync(
            "flipTag",
            JsonDocument.Parse("""{"movieId":41,"name":"beta"}""").RootElement
        );

        Assert.That(invokedModes, Is.EqualTo(
            new[]
            {
                WhiteBrowserSkinTagMutationMode.Add,
                WhiteBrowserSkinTagMutationMode.Remove,
                WhiteBrowserSkinTagMutationMode.Flip,
            }
        ));

        using JsonDocument addPayload = JsonDocument.Parse(JsonSerializer.Serialize(addResult.Payload));
        using JsonDocument removePayload = JsonDocument.Parse(JsonSerializer.Serialize(removeResult.Payload));
        using JsonDocument flipPayload = JsonDocument.Parse(JsonSerializer.Serialize(flipResult.Payload));

        Assert.Multiple(() =>
        {
            Assert.That(addResult.Succeeded, Is.True);
            Assert.That(addPayload.RootElement.GetProperty("changed").GetBoolean(), Is.True);
            Assert.That(addPayload.RootElement.GetProperty("hasTag").GetBoolean(), Is.True);
            Assert.That(addPayload.RootElement.GetProperty("tag").GetString(), Is.EqualTo("beta"));
            Assert.That(addPayload.RootElement.GetProperty("movieId").GetInt64(), Is.EqualTo(41));
            Assert.That(
                addPayload.RootElement.GetProperty("item").GetProperty("Tags")[1].GetString(),
                Is.EqualTo("beta")
            );

            Assert.That(removeResult.Succeeded, Is.True);
            Assert.That(removePayload.RootElement.GetProperty("changed").GetBoolean(), Is.True);
            Assert.That(removePayload.RootElement.GetProperty("hasTag").GetBoolean(), Is.False);
            Assert.That(removePayload.RootElement.GetProperty("tag").GetString(), Is.EqualTo("alpha"));

            Assert.That(flipResult.Succeeded, Is.True);
            Assert.That(flipPayload.RootElement.GetProperty("changed").GetBoolean(), Is.True);
            Assert.That(flipPayload.RootElement.GetProperty("hasTag").GetBoolean(), Is.False);
            Assert.That(
                flipPayload.RootElement.GetProperty("item").GetProperty("Tags").GetArrayLength(),
                Is.EqualTo(0)
            );
            Assert.That(movie.Tags, Is.EqualTo(""));
            Assert.That(movie.Tag, Is.Empty);
        });
    }

    [Test]
    public void ThumbnailUpdateCallbackPayload_互換引数とnamed_payloadを同時に持てる()
    {
        WhiteBrowserSkinThumbnailContractDto contract = new()
        {
            DbIdentity = "db-a",
            MovieId = 77,
            RecordKey = "db-a:77",
            MoviePath = @"C:\movies\sample.mp4",
            ThumbUrl = "https://thum.local/sample.jpg?rev=rev-1",
            ThumbRevision = "rev-1",
            ThumbSourceKind = WhiteBrowserSkinThumbnailSourceKinds.ManagedThumbnail,
            ThumbNaturalWidth = 320,
            ThumbNaturalHeight = 180,
            ThumbSheetColumns = 2,
            ThumbSheetRows = 3,
        };

        WhiteBrowserSkinThumbnailUpdateCallbackPayload payload =
            WhiteBrowserSkinThumbnailUpdateCallbackPayload.Create(contract);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        Assert.Multiple(() =>
        {
            Assert.That(payload.RecordKey, Is.EqualTo("db-a:77"));
            Assert.That(payload.CompatCallArgs, Has.Length.EqualTo(5));
            Assert.That(document.RootElement.GetProperty("recordKey").GetString(), Is.EqualTo("db-a:77"));
            Assert.That(document.RootElement.GetProperty("thumbRevision").GetString(), Is.EqualTo("rev-1"));
            Assert.That(
                document.RootElement.GetProperty("sizeInfo").GetProperty("thumbNaturalWidth").GetInt32(),
                Is.EqualTo(320)
            );
            Assert.That(
                document.RootElement.GetProperty("sizeInfo").GetProperty("sheetRows").GetInt32(),
                Is.EqualTo(3)
            );
            Assert.That(
                document.RootElement.GetProperty("__immCallArgs")[0].GetString(),
                Is.EqualTo("db-a:77")
            );
            Assert.That(
                document.RootElement.GetProperty("__immCallArgs")[1].GetString(),
                Is.EqualTo("https://thum.local/sample.jpg?rev=rev-1")
            );
            Assert.That(
                document.RootElement.GetProperty("__immCallArgs")[4].GetProperty("thumbSheetColumns").GetInt32(),
                Is.EqualTo(2)
            );
        });
    }

    private static WhiteBrowserSkinApiService CreateService(
        IReadOnlyList<MovieRecords> visibleMovies,
        MovieRecords? selectedMovie,
        string dbFullPath,
        string dbName,
        string skinName,
        string thumbRoot,
        string currentSortId = "",
        string currentSortName = "",
        string currentSearchKeyword = "",
        int registeredMovieCount = 0,
        Func<IReadOnlyList<MovieRecords>>? getCurrentSelectedMovies = null,
        Func<MovieRecords?>? getCurrentSelectedMovie = null,
        Func<MovieRecords, Task<bool>>? focusMovieAsync = null,
        Func<MovieRecords, bool, Task<bool>>? setMovieSelectionAsync = null,
        Func<MovieRecords, string, WhiteBrowserSkinTagMutationMode, Task<WhiteBrowserSkinTagMutationResult>>? mutateMovieTagAsync = null,
        Func<string, Task<bool>>? executeSearchAsync = null,
        Func<string, Task<bool>>? executeSortAsync = null,
        Func<string, string>? resolveSortId = null,
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
            currentSortId: currentSortId,
            currentSortName: currentSortName,
            currentSearchKeyword: currentSearchKeyword,
            registeredMovieCount: registeredMovieCount,
            getCurrentSelectedMovie: getCurrentSelectedMovie ?? (() => selectedMovie),
            getCurrentSelectedMovies:
                getCurrentSelectedMovies
                ?? (() => selectedMovie is null ? Array.Empty<MovieRecords>() : [selectedMovie]),
            focusMovieAsync: focusMovieAsync,
            setMovieSelectionAsync: setMovieSelectionAsync,
            mutateMovieTagAsync: mutateMovieTagAsync,
            executeSearchAsync: executeSearchAsync,
            executeSortAsync: executeSortAsync,
            resolveSortId: resolveSortId,
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
        string currentSortId = "",
        string currentSortName = "",
        string currentSearchKeyword = "",
        int registeredMovieCount = 0,
        Func<MovieRecords?>? getCurrentSelectedMovie = null,
        Func<IReadOnlyList<MovieRecords>>? getCurrentSelectedMovies = null,
        Func<MovieRecords, Task<bool>>? focusMovieAsync = null,
        Func<MovieRecords, bool, Task<bool>>? setMovieSelectionAsync = null,
        Func<MovieRecords, string, WhiteBrowserSkinTagMutationMode, Task<WhiteBrowserSkinTagMutationResult>>? mutateMovieTagAsync = null,
        Func<string, Task<bool>>? executeSearchAsync = null,
        Func<string, Task<bool>>? executeSortAsync = null,
        Func<string, string>? resolveSortId = null,
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
                GetCurrentSortId = () => currentSortId,
                GetCurrentSortName = () => string.IsNullOrWhiteSpace(currentSortName) ? currentSortId : currentSortName,
                GetCurrentSearchKeyword = () => currentSearchKeyword,
                GetRegisteredMovieCount = () => registeredMovieCount,
                GetCurrentThumbFolder = () => thumbRoot,
                GetCurrentSelectedMovie = getCurrentSelectedMovie ?? (() => null),
                GetCurrentSelectedMovies =
                    getCurrentSelectedMovies ?? (() => Array.Empty<MovieRecords>()),
                FocusMovieAsync = focusMovieAsync ?? (_ => Task.FromResult(false)),
                SetMovieSelectionAsync =
                    setMovieSelectionAsync ?? ((_, _) => Task.FromResult(false)),
                MutateMovieTagAsync =
                    mutateMovieTagAsync
                    ?? ((_, _, _) => Task.FromResult(new WhiteBrowserSkinTagMutationResult(false, false))),
                ExecuteSearchAsync = executeSearchAsync ?? (_ => Task.FromResult(false)),
                ExecuteSortAsync = executeSortAsync ?? (_ => Task.FromResult(false)),
                ResolveSortId = resolveSortId ?? (sortKey => sortKey ?? ""),
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
