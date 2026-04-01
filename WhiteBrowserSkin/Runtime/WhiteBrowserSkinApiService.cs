using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// wb.* API の最小セットを、MainWindow 直結を増やさずに処理する service。
    /// </summary>
    public sealed class WhiteBrowserSkinApiService
    {
        private readonly WhiteBrowserSkinApiServiceDependencies dependencies;
        private readonly WhiteBrowserSkinApiServiceOptions options;
        private readonly WhiteBrowserSkinThumbnailContractService thumbnailContractService = new();

        public WhiteBrowserSkinApiService(
            WhiteBrowserSkinApiServiceDependencies dependencies,
            WhiteBrowserSkinApiServiceOptions options = null
        )
        {
            this.dependencies = dependencies ?? new WhiteBrowserSkinApiServiceDependencies();
            this.options = options ?? new WhiteBrowserSkinApiServiceOptions();
        }

        public async Task<WhiteBrowserSkinApiInvocationResult> HandleAsync(
            string method,
            JsonElement payload,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch ((method ?? "").Trim())
                {
                    case "update":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
                    case "getInfo":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetInfo(payload));
                    case "getInfos":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetInfos(payload));
                    case "getSkinName":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            dependencies.GetCurrentSkinName()
                        );
                    case "getDBName":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            dependencies.GetCurrentDbName()
                        );
                    case "getThumDir":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            options.ThumbnailBaseUri ?? WhiteBrowserSkinHostPaths.BuildThumbnailBaseUri()
                        );
                    case "trace":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleTrace(payload));
                    case "focusThum":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleFocusThumAsync(payload, cancellationToken)
                        );
                    default:
                        return WhiteBrowserSkinApiInvocationResult.Failure(
                            $"Unsupported wb method: {method}"
                        );
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return WhiteBrowserSkinApiInvocationResult.Failure(
                    $"{ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        private WhiteBrowserSkinUpdateResponse HandleUpdate(JsonElement payload)
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            int startIndex = Math.Max(0, GetInt32(payload, "startIndex", 0));
            int requestedCount = GetInt32(payload, "count", visibleMovies.Count);
            if (requestedCount < 0)
            {
                requestedCount = 0;
            }

            WhiteBrowserSkinMovieDto[] items = BuildDtos(visibleMovies, startIndex, requestedCount);
            return new WhiteBrowserSkinUpdateResponse
            {
                StartIndex = startIndex,
                RequestedCount = requestedCount,
                TotalCount = visibleMovies.Count,
                Items = items,
            };
        }

        private WhiteBrowserSkinMovieDto HandleGetInfo(JsonElement payload)
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            MovieRecords movie = FindMovieRecord(visibleMovies, payload);
            return movie == null ? null : BuildMovieDto(movie);
        }

        private WhiteBrowserSkinMovieDto[] HandleGetInfos(JsonElement payload)
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            List<WhiteBrowserSkinMovieDto> results = [];

            if (
                payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("movieIds", out JsonElement movieIdsElement)
                && movieIdsElement.ValueKind == JsonValueKind.Array
            )
            {
                foreach (JsonElement item in movieIdsElement.EnumerateArray())
                {
                    MovieRecords movie = visibleMovies.FirstOrDefault(x => x?.Movie_Id == GetInt64(item));
                    if (movie != null)
                    {
                        results.Add(BuildMovieDto(movie));
                    }
                }

                return [.. results];
            }

            if (
                payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("recordKeys", out JsonElement recordKeysElement)
                && recordKeysElement.ValueKind == JsonValueKind.Array
            )
            {
                string dbIdentity = WhiteBrowserSkinDbIdentity.Build(dependencies.GetCurrentDbFullPath());
                Dictionary<string, MovieRecords> movieByRecordKey = visibleMovies
                    .Where(x => x != null)
                    .ToDictionary(
                        x => WhiteBrowserSkinDbIdentity.BuildRecordKey(dbIdentity, x.Movie_Id),
                        x => x,
                        StringComparer.Ordinal
                    );

                foreach (JsonElement item in recordKeysElement.EnumerateArray())
                {
                    string recordKey = item.GetString() ?? "";
                    if (movieByRecordKey.TryGetValue(recordKey, out MovieRecords movie))
                    {
                        results.Add(BuildMovieDto(movie));
                    }
                }
            }

            return [.. results];
        }

        private bool HandleTrace(JsonElement payload)
        {
            string message = GetString(payload, "message", "");
            dependencies.Trace(message);
            return true;
        }

        private async Task<object> HandleFocusThumAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            MovieRecords movie = FindMovieRecord(visibleMovies, payload);
            if (movie == null)
            {
                return new { found = false };
            }

            bool focused = await dependencies.FocusMovieAsync(movie);
            cancellationToken.ThrowIfCancellationRequested();
            return new
            {
                found = true,
                focused,
                recordKey = WhiteBrowserSkinDbIdentity.BuildRecordKey(
                    WhiteBrowserSkinDbIdentity.Build(dependencies.GetCurrentDbFullPath()),
                    movie.Movie_Id
                ),
            };
        }

        private WhiteBrowserSkinMovieDto[] BuildDtos(
            IReadOnlyList<MovieRecords> visibleMovies,
            int startIndex,
            int requestedCount
        )
        {
            if (visibleMovies.Count < 1 || startIndex >= visibleMovies.Count || requestedCount == 0)
            {
                return [];
            }

            int takeCount = requestedCount <= 0
                ? visibleMovies.Count - startIndex
                : Math.Min(requestedCount, visibleMovies.Count - startIndex);

            WhiteBrowserSkinMovieDto[] items = new WhiteBrowserSkinMovieDto[takeCount];
            for (int index = 0; index < takeCount; index++)
            {
                items[index] = BuildMovieDto(visibleMovies[startIndex + index]);
            }

            return items;
        }

        private WhiteBrowserSkinMovieDto BuildMovieDto(MovieRecords movie)
        {
            MovieRecords selectedMovie = dependencies.GetCurrentSelectedMovie();
            WhiteBrowserSkinThumbnailContractDto thumbnailContract = thumbnailContractService.Create(
                movie,
                new WhiteBrowserSkinThumbnailResolveContext
                {
                    DbFullPath = dependencies.GetCurrentDbFullPath(),
                    ManagedThumbnailRootPath = dependencies.GetCurrentThumbFolder(),
                    DisplayTabIndex = dependencies.GetCurrentTabIndex(),
                    SelectedMovieId = selectedMovie?.Movie_Id,
                    ThumbUrlResolver = dependencies.ResolveThumbUrl,
                }
            );
            ThumbnailMetadata metadata = ApplyFallbackMetadata(
                thumbnailContract.ThumbNaturalWidth,
                thumbnailContract.ThumbNaturalHeight,
                thumbnailContract.ThumbSheetColumns,
                thumbnailContract.ThumbSheetRows
            );

            return new WhiteBrowserSkinMovieDto
            {
                DbIdentity = thumbnailContract.DbIdentity,
                MovieId = movie?.Movie_Id ?? 0,
                RecordKey = thumbnailContract.RecordKey,
                MovieName = movie?.Movie_Name ?? "",
                MoviePath = movie?.Movie_Path ?? "",
                ThumbUrl = thumbnailContract.ThumbUrl,
                ThumbRevision = thumbnailContract.ThumbRevision,
                ThumbSourceKind = thumbnailContract.ThumbSourceKind,
                ThumbNaturalWidth = metadata.Width,
                ThumbNaturalHeight = metadata.Height,
                ThumbSheetColumns = metadata.Columns,
                ThumbSheetRows = metadata.Rows,
                Length = movie?.Movie_Length ?? "",
                Size = movie?.Movie_Size ?? 0,
                Tags = ResolveTags(movie),
                Score = movie?.Score ?? 0,
                Exists = movie?.IsExists ?? false,
                Selected = thumbnailContract.Selected,
            };
        }

        private IReadOnlyList<MovieRecords> GetVisibleMoviesSnapshot()
        {
            IReadOnlyList<MovieRecords> visibleMovies = dependencies.GetVisibleMovies();
            return visibleMovies ?? Array.Empty<MovieRecords>();
        }

        private MovieRecords FindMovieRecord(IReadOnlyList<MovieRecords> visibleMovies, JsonElement payload)
        {
            long movieId = GetInt64(payload, "movieId", 0);
            if (movieId > 0)
            {
                return visibleMovies.FirstOrDefault(x => x?.Movie_Id == movieId);
            }

            string recordKey = GetString(payload, "recordKey", "");
            if (string.IsNullOrWhiteSpace(recordKey))
            {
                return null;
            }

            string dbIdentity = WhiteBrowserSkinDbIdentity.Build(dependencies.GetCurrentDbFullPath());
            return visibleMovies.FirstOrDefault(x =>
                string.Equals(
                    WhiteBrowserSkinThumbnailContractService.BuildRecordKey(
                        dbIdentity,
                        x?.Movie_Id ?? 0
                    ),
                    recordKey,
                    StringComparison.Ordinal
                )
            );
        }

        private ThumbnailMetadata ApplyFallbackMetadata(
            int width,
            int height,
            int columns,
            int rows
        )
        {
            return new ThumbnailMetadata(
                width > 0 ? width : Math.Max(1, options.DefaultThumbnailWidth),
                height > 0 ? height : Math.Max(1, options.DefaultThumbnailHeight),
                columns > 0 ? columns : Math.Max(1, options.DefaultThumbnailColumns),
                rows > 0 ? rows : Math.Max(1, options.DefaultThumbnailRows)
            );
        }

        private static string[] ResolveTags(MovieRecords movie)
        {
            if (movie?.Tag != null && movie.Tag.Count > 0)
            {
                return movie.Tag.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            }

            if (string.IsNullOrWhiteSpace(movie?.Tags))
            {
                return [];
            }

            return movie.Tags
                .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        private static int GetInt32(JsonElement payload, string name, int defaultValue)
        {
            long value = GetInt64(payload, name, defaultValue);
            if (value < int.MinValue || value > int.MaxValue)
            {
                return defaultValue;
            }

            return (int)value;
        }

        private static long GetInt64(JsonElement payload, string name, long defaultValue = 0)
        {
            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement element))
            {
                return GetInt64(element, defaultValue);
            }

            return defaultValue;
        }

        private static long GetInt64(JsonElement element, long defaultValue = 0)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long numericValue))
            {
                return numericValue;
            }

            if (
                element.ValueKind == JsonValueKind.String
                && long.TryParse(element.GetString(), out long stringValue)
            )
            {
                return stringValue;
            }

            return defaultValue;
        }

        private static string GetString(JsonElement payload, string name, string defaultValue)
        {
            if (
                payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out JsonElement element)
                && element.ValueKind == JsonValueKind.String
            )
            {
                return element.GetString() ?? defaultValue;
            }

            return defaultValue;
        }

        private readonly record struct ThumbnailMetadata(int Width, int Height, int Columns, int Rows);
    }
}
