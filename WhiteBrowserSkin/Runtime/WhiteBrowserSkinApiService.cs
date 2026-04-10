using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.Infrastructure;

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
                    case "find":
                        return await HandleFindAsync(payload, cancellationToken);
                    case "sort":
                        return await HandleSortAsync(payload, cancellationToken);
                    case "getInfo":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetInfo(payload));
                    case "getInfos":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetInfos(payload));
                    case "getProfile":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleGetProfileAsync(payload, cancellationToken)
                        );
                    case "writeProfile":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleWriteProfileAsync(payload, cancellationToken)
                        );
                    case "changeSkin":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleChangeSkinAsync(payload, cancellationToken)
                        );
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
                    case "selectThum":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleSelectThumAsync(payload, cancellationToken)
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
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot = CreateSelectionSnapshot();
            int startIndex = Math.Max(0, GetInt32(payload, "startIndex", 0));
            int requestedCount = GetInt32(payload, "count", visibleMovies.Count);
            if (requestedCount < 0)
            {
                requestedCount = 0;
            }

            WhiteBrowserSkinMovieDto[] items = BuildDtos(
                visibleMovies,
                startIndex,
                requestedCount,
                selectionSnapshot
            );
            return new WhiteBrowserSkinUpdateResponse
            {
                StartIndex = startIndex,
                RequestedCount = requestedCount,
                TotalCount = visibleMovies.Count,
                Items = items,
            };
        }

        private async Task<WhiteBrowserSkinApiInvocationResult> HandleFindAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string keyword = GetString(payload, "keyword", "");
            bool executed = await dependencies.ExecuteSearchAsync(keyword);
            cancellationToken.ThrowIfCancellationRequested();

            if (!executed)
            {
                return WhiteBrowserSkinApiInvocationResult.Failure(
                    "Failed to execute search."
                );
            }

            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private async Task<WhiteBrowserSkinApiInvocationResult> HandleSortAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string sortId = GetString(payload, "sortId", "");
            bool executed = await dependencies.ExecuteSortAsync(sortId);
            cancellationToken.ThrowIfCancellationRequested();

            if (!executed)
            {
                return WhiteBrowserSkinApiInvocationResult.Failure("Failed to execute sort.");
            }

            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private WhiteBrowserSkinMovieDto HandleGetInfo(JsonElement payload)
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot = CreateSelectionSnapshot();
            MovieRecords movie = FindMovieRecord(visibleMovies, payload);
            return movie == null ? null : BuildMovieDto(movie, selectionSnapshot);
        }

        private WhiteBrowserSkinMovieDto[] HandleGetInfos(JsonElement payload)
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot = CreateSelectionSnapshot();
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
                        results.Add(BuildMovieDto(movie, selectionSnapshot));
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
                        results.Add(BuildMovieDto(movie, selectionSnapshot));
                    }
                }
            }

            return [.. results];
        }

        private async Task<string> HandleGetProfileAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string key = GetString(payload, "key", "");
            string value = await dependencies.GetProfileValueAsync(key);
            cancellationToken.ThrowIfCancellationRequested();
            return value ?? "";
        }

        private async Task<bool> HandleWriteProfileAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string key = GetString(payload, "key", "");
            string value = GetString(payload, "value", "");
            bool written = await dependencies.WriteProfileValueAsync(key, value);
            cancellationToken.ThrowIfCancellationRequested();
            return written;
        }

        private async Task<bool> HandleChangeSkinAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string skinName = GetString(payload, "skinName", "");
            bool changed = await dependencies.ChangeSkinAsync(skinName);
            cancellationToken.ThrowIfCancellationRequested();
            return changed;
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
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot = CreateSelectionSnapshot();
            return new
            {
                found = true,
                focused,
                focusedMovieId = selectionSnapshot.FocusedMovie?.Movie_Id ?? 0,
                movieId = movie.Movie_Id,
                id = movie.Movie_Id,
                selected = selectionSnapshot.SelectedMovieIds.Contains(movie.Movie_Id),
                recordKey = WhiteBrowserSkinDbIdentity.BuildRecordKey(
                    WhiteBrowserSkinDbIdentity.Build(dependencies.GetCurrentDbFullPath()),
                    movie.Movie_Id
                ),
            };
        }

        private async Task<object> HandleSelectThumAsync(
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

            bool isSelected = GetBoolean(payload, "selected", GetBoolean(payload, "isSelected", true));
            bool selectionChanged = await dependencies.SetMovieSelectionAsync(movie, isSelected);
            cancellationToken.ThrowIfCancellationRequested();
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot = CreateSelectionSnapshot();

            return new
            {
                found = true,
                selectionChanged,
                focused = selectionSnapshot.FocusedMovie?.Movie_Id == movie.Movie_Id,
                focusedMovieId = selectionSnapshot.FocusedMovie?.Movie_Id ?? 0,
                movieId = movie.Movie_Id,
                id = movie.Movie_Id,
                selected = selectionSnapshot.SelectedMovieIds.Contains(movie.Movie_Id),
                recordKey = WhiteBrowserSkinDbIdentity.BuildRecordKey(
                    WhiteBrowserSkinDbIdentity.Build(dependencies.GetCurrentDbFullPath()),
                    movie.Movie_Id
                ),
            };
        }

        private WhiteBrowserSkinMovieDto[] BuildDtos(
            IReadOnlyList<MovieRecords> visibleMovies,
            int startIndex,
            int requestedCount,
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot
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
                items[index] = BuildMovieDto(visibleMovies[startIndex + index], selectionSnapshot);
            }

            return items;
        }

        private WhiteBrowserSkinMovieDto BuildMovieDto(
            MovieRecords movie,
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot
        )
        {
            WhiteBrowserSkinThumbnailContractDto thumbnailContract = thumbnailContractService.Create(
                movie,
                new WhiteBrowserSkinThumbnailResolveContext
                {
                    DbFullPath = dependencies.GetCurrentDbFullPath(),
                    ManagedThumbnailRootPath = dependencies.GetCurrentThumbFolder(),
                    DisplayTabIndex = dependencies.GetCurrentTabIndex(),
                    SelectedMovieId = selectionSnapshot.FocusedMovie?.Movie_Id,
                    SelectedMovieIds = selectionSnapshot.SelectedMovieIds,
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
                id = movie?.Movie_Id ?? 0,
                title = ResolveLegacyTitle(movie),
                ext = ResolveLegacyExtension(movie),
                path = movie?.Movie_Path ?? "",
                thum = thumbnailContract.ThumbUrl,
                len = movie?.Movie_Length ?? "",
                size = movie?.Movie_Size ?? 0,
                score = movie?.Score ?? 0,
                exist = movie?.IsExists ?? false,
                select = thumbnailContract.Selected ? 1 : 0,
            };
        }

        private IReadOnlyList<MovieRecords> GetVisibleMoviesSnapshot()
        {
            IReadOnlyList<MovieRecords> visibleMovies = dependencies.GetVisibleMovies();
            return visibleMovies ?? Array.Empty<MovieRecords>();
        }

        private WhiteBrowserSkinSelectionSnapshot CreateSelectionSnapshot()
        {
            MovieRecords focusedMovie = dependencies.GetCurrentSelectedMovie();
            IReadOnlyList<MovieRecords> selectedMovies = dependencies.GetCurrentSelectedMovies()
                ?? Array.Empty<MovieRecords>();
            HashSet<long> selectedMovieIds = selectedMovies
                .Where(movie => movie != null)
                .Select(movie => movie.Movie_Id)
                .ToHashSet();

            return new WhiteBrowserSkinSelectionSnapshot(focusedMovie, selectedMovieIds);
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
                return TagTextParser.SplitDistinct(movie.Tag, StringComparer.CurrentCultureIgnoreCase);
            }

            return TagTextParser.SplitDistinct(movie?.Tags, StringComparer.CurrentCultureIgnoreCase);
        }

        private static string ResolveLegacyTitle(MovieRecords movie)
        {
            string movieName = movie?.Movie_Name ?? "";
            string extension = ResolveLegacyExtension(movie);
            if (
                !string.IsNullOrWhiteSpace(extension)
                && movieName.EndsWith(extension, StringComparison.CurrentCultureIgnoreCase)
            )
            {
                return movieName[..^extension.Length];
            }

            return movieName;
        }

        private static string ResolveLegacyExtension(MovieRecords movie)
        {
            string moviePath = movie?.Movie_Path ?? "";
            return string.IsNullOrWhiteSpace(moviePath) ? "" : Path.GetExtension(moviePath) ?? "";
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
            )
            {
                return GetString(element, defaultValue);
            }

            return defaultValue;
        }

        private static string GetString(JsonElement element, string defaultValue)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? defaultValue,
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
                JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
                _ => defaultValue,
            };
        }

        private static bool GetBoolean(JsonElement payload, string name, bool defaultValue)
        {
            if (
                payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out JsonElement element)
            )
            {
                return GetBoolean(element, defaultValue);
            }

            return defaultValue;
        }

        private static bool GetBoolean(JsonElement element, bool defaultValue)
        {
            if (element.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long numericValue))
            {
                return numericValue != 0;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                string value = element.GetString() ?? "";
                if (bool.TryParse(value, out bool boolValue))
                {
                    return boolValue;
                }

                if (long.TryParse(value, out long parsedNumericValue))
                {
                    return parsedNumericValue != 0;
                }
            }

            return defaultValue;
        }

        private readonly record struct ThumbnailMetadata(int Width, int Height, int Columns, int Rows);

        private readonly record struct WhiteBrowserSkinSelectionSnapshot(
            MovieRecords FocusedMovie,
            HashSet<long> SelectedMovieIds
        );
    }
}
