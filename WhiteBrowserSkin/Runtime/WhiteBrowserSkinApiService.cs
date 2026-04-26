using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;

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
        private readonly object queryOverlaySync = new();
        private string[] addedFilters = [];
        private Func<MovieRecords, bool> addedWherePredicate = static _ => true;
        private bool hasAddedWhere;
        private string addedWhereText = "";
        private string addedNamedOrderSortId = "";
        private string addedOrderText = "";
        private WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder addedSqlOrder =
            WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder.Empty;
        private bool addedOrderOverride;

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
                    case "addWhere":
                        return HandleAddWhere(payload);
                    case "addOrder":
                        return HandleAddOrder(payload);
                    case "addFilter":
                        return await HandleAddFilterAsync(payload, cancellationToken);
                    case "removeFilter":
                        return await HandleRemoveFilterAsync(payload, cancellationToken);
                    case "clearFilter":
                        return await HandleClearFilterAsync(payload, cancellationToken);
                    case "getInfo":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetInfo(payload));
                    case "getInfos":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetInfos(payload));
                    case "getRelation":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetRelation(payload));
                    case "getFindInfo":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetFindInfo());
                    case "getFocusThum":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetFocusThum());
                    case "getSelectThums":
                        return WhiteBrowserSkinApiInvocationResult.Success(HandleGetSelectThums());
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
                    case "addTag":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleTagMutationAsync(
                                payload,
                                WhiteBrowserSkinTagMutationMode.Add,
                                cancellationToken
                            )
                        );
                    case "removeTag":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleTagMutationAsync(
                                payload,
                                WhiteBrowserSkinTagMutationMode.Remove,
                                cancellationToken
                            )
                        );
                    case "flipTag":
                        return WhiteBrowserSkinApiInvocationResult.Success(
                            await HandleTagMutationAsync(
                                payload,
                                WhiteBrowserSkinTagMutationMode.Flip,
                                cancellationToken
                            )
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

        public void ResetTransientState()
        {
            ClearAddedFilters();
            ClearAddedWhere();
            ClearAddedOrder();
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

            ClearAddedOrder();
            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private WhiteBrowserSkinApiInvocationResult HandleAddWhere(JsonElement payload)
        {
            string whereClause = ResolveWhereClause(payload);
            if (string.IsNullOrWhiteSpace(whereClause))
            {
                ClearAddedWhere();
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            if (
                !WhiteBrowserSkinQueryOverlayCompiler.TryCompileWhere(
                    whereClause,
                    out Func<MovieRecords, bool> predicate,
                    out string errorMessage
                )
            )
            {
                return WhiteBrowserSkinApiInvocationResult.Failure(
                    $"Unsupported addWhere clause: {errorMessage}"
                );
            }

            lock (queryOverlaySync)
            {
                addedWherePredicate = predicate;
                hasAddedWhere = true;
                addedWhereText = whereClause.Trim();
            }

            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private WhiteBrowserSkinApiInvocationResult HandleAddOrder(JsonElement payload)
        {
            string orderText = ResolveOrderText(payload);
            bool overrideCurrentOrder = GetBoolean(payload, "override", false);
            if (string.IsNullOrWhiteSpace(orderText))
            {
                ClearAddedOrder();
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            string normalizedOrderText = orderText.Trim();
            if (IsSqlOrderText(normalizedOrderText))
            {
                if (
                    !WhiteBrowserSkinQueryOverlayCompiler.TryCompileOrder(
                        normalizedOrderText,
                        out WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder compiledOrder,
                        out string errorMessage
                    )
                )
                {
                    return WhiteBrowserSkinApiInvocationResult.Failure(
                        $"Unsupported addOrder clause: {errorMessage}"
                    );
                }

                lock (queryOverlaySync)
                {
                    addedNamedOrderSortId = "";
                    addedOrderText = normalizedOrderText;
                    addedSqlOrder = compiledOrder;
                    addedOrderOverride = overrideCurrentOrder;
                }

                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            string resolvedSortId = dependencies.ResolveSortId(normalizedOrderText) ?? "";
            if (string.IsNullOrWhiteSpace(resolvedSortId))
            {
                return WhiteBrowserSkinApiInvocationResult.Failure(
                    $"Unsupported addOrder sort: {normalizedOrderText}"
                );
            }

            lock (queryOverlaySync)
            {
                addedNamedOrderSortId = resolvedSortId;
                addedOrderText = normalizedOrderText;
                addedSqlOrder = WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder.Empty;
                addedOrderOverride = overrideCurrentOrder;
            }

            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private async Task<WhiteBrowserSkinApiInvocationResult> HandleAddFilterAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string filterText = NormalizeFilterText(ResolveFilterText(payload));
            if (string.IsNullOrWhiteSpace(filterText))
            {
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            string[] currentFilters = ResolveCurrentFilterTokens();
            string[] nextFilters = currentFilters.Contains(
                filterText,
                StringComparer.CurrentCultureIgnoreCase
            )
                ? currentFilters
                : [.. currentFilters, filterText];
            bool appliedToMainSearch = await TryApplyFilterTokensAsync(nextFilters, cancellationToken);
            if (appliedToMainSearch)
            {
                ClearAddedFilters();
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            lock (queryOverlaySync)
            {
                if (
                    !addedFilters.Contains(
                        filterText,
                        StringComparer.CurrentCultureIgnoreCase
                    )
                )
                {
                    addedFilters = [.. addedFilters, filterText];
                }
            }

            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private async Task<WhiteBrowserSkinApiInvocationResult> HandleRemoveFilterAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            string filterText = NormalizeFilterText(ResolveFilterText(payload));
            if (string.IsNullOrWhiteSpace(filterText))
            {
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            string[] currentFilters = ResolveCurrentFilterTokens();
            string[] nextFilters = currentFilters
                .Where(x => !string.Equals(x, filterText, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();
            bool appliedToMainSearch = await TryApplyFilterTokensAsync(nextFilters, cancellationToken);
            if (appliedToMainSearch)
            {
                ClearAddedFilters();
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
            }

            lock (queryOverlaySync)
            {
                addedFilters = addedFilters
                    .Where(x =>
                        !string.Equals(x, filterText, StringComparison.CurrentCultureIgnoreCase)
                    )
                    .ToArray();
            }

            return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
        }

        private async Task<WhiteBrowserSkinApiInvocationResult> HandleClearFilterAsync(
            JsonElement payload,
            CancellationToken cancellationToken
        )
        {
            bool appliedToMainSearch = await TryApplyFilterTokensAsync(
                Array.Empty<string>(),
                cancellationToken
            );
            ClearAddedFilters();
            if (appliedToMainSearch)
            {
                return WhiteBrowserSkinApiInvocationResult.Success(HandleUpdate(payload));
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

            if (results.Count > 0)
            {
                return [.. results];
            }

            // ID 指定が無い時は、update と同じ startIndex/count で必要範囲だけ返せるようにする。
            int startIndex = Math.Max(0, GetInt32(payload, "startIndex", 0));
            int requestedCount = GetInt32(payload, "count", visibleMovies.Count);
            if (requestedCount < 0)
            {
                requestedCount = 0;
            }

            return BuildDtos(visibleMovies, startIndex, requestedCount, selectionSnapshot);
        }

        private object HandleGetFindInfo()
        {
            QueryOverlayState overlayState = CaptureQueryOverlayState();
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();

            // WB 互換では、検索語と追加条件をひとまとめのスナップショットとして返す。
            return new
            {
                find = dependencies.GetCurrentSearchKeyword() ?? "",
                sort = new[]
                {
                    dependencies.GetCurrentSortName() ?? dependencies.GetCurrentSortId() ?? "",
                    BuildAdditionalOrderText(overlayState),
                },
                filter = ResolveCurrentFilterTokens(overlayState),
                where = overlayState.WhereText ?? "",
                total = Math.Max(0, dependencies.GetRegisteredMovieCount()),
                result = visibleMovies.Count,
            };
        }

        private object[] HandleGetRelation(JsonElement payload)
        {
            string title = GetString(payload, "title", GetString(payload, "query", ""));
            int limit = Math.Max(1, GetInt32(payload, "limit", 20));
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();

            string normalizedQuery = NormalizeRelationText(title);
            string[] queryTokens = SplitRelationTokens(normalizedQuery);

            return visibleMovies
                .Where(movie => movie != null)
                .Select(movie => new
                {
                    Movie = movie,
                    Title = Path.GetFileNameWithoutExtension(movie.Movie_Name ?? "") ?? "",
                    Tags = TagTextParser.SplitDistinct(movie.Tags, StringComparer.CurrentCultureIgnoreCase),
                })
                .Where(x => x.Tags.Length > 0)
                .Select(x => new
                {
                    x.Movie,
                    x.Title,
                    x.Tags,
                    Score = ScoreRelationCandidate(normalizedQuery, queryTokens, x.Title, x.Tags),
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Movie.Movie_Id)
                .Take(limit)
                .Select(x => (object)new
                {
                    id = x.Movie.Movie_Id,
                    title = x.Title,
                    tags = x.Tags,
                })
                .ToArray();
        }

        private static int ScoreRelationCandidate(
            string normalizedQuery,
            string[] queryTokens,
            string title,
            string[] tags
        )
        {
            string normalizedTitle = NormalizeRelationText(title);
            string[] titleTokens = SplitRelationTokens(normalizedTitle);
            int score = 0;

            if (!string.IsNullOrWhiteSpace(normalizedQuery))
            {
                if (string.Equals(normalizedTitle, normalizedQuery, StringComparison.Ordinal))
                {
                    return 0;
                }

                if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
                {
                    score += 100;
                }
            }

            if (queryTokens.Length > 0)
            {
                score += queryTokens.Count(token =>
                    titleTokens.Contains(token, StringComparer.Ordinal)
                    || tags.Any(tag => NormalizeRelationText(tag).Contains(token, StringComparison.Ordinal))
                ) * 10;
            }

            score += Math.Min(tags.Length, 5);
            return score;
        }

        private static string NormalizeRelationText(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }

        private static string[] SplitRelationTokens(string value)
        {
            return (value ?? "")
                .Split(
                    [' ', '\t', '\r', '\n', '-', '_', '.', ',', '(', ')', '[', ']', '/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries
                )
                .Distinct(StringComparer.Ordinal)
                .ToArray();
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
            if (changed)
            {
                ClearAddedFilters();
                ClearAddedWhere();
                ClearAddedOrder();
            }

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

        private long HandleGetFocusThum()
        {
            return dependencies.GetCurrentSelectedMovie()?.Movie_Id ?? 0;
        }

        private long[] HandleGetSelectThums()
        {
            return (dependencies.GetCurrentSelectedMovies() ?? Array.Empty<MovieRecords>())
                .Where(movie => movie != null)
                .Select(movie => movie.Movie_Id)
                .Distinct()
                .ToArray();
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

        private async Task<object> HandleTagMutationAsync(
            JsonElement payload,
            WhiteBrowserSkinTagMutationMode mutationMode,
            CancellationToken cancellationToken
        )
        {
            IReadOnlyList<MovieRecords> visibleMovies = GetVisibleMoviesSnapshot();
            MovieRecords movie = FindMovieRecord(visibleMovies, payload)
                ?? dependencies.GetCurrentSelectedMovie();
            if (movie == null)
            {
                return new { found = false };
            }

            string tagName = GetTagName(payload);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return new
                {
                    found = true,
                    changed = false,
                    hasTag = ResolveTags(movie).Contains(tagName ?? "", StringComparer.CurrentCultureIgnoreCase),
                    movieId = movie.Movie_Id,
                    id = movie.Movie_Id,
                    tag = tagName ?? "",
                };
            }

            WhiteBrowserSkinTagMutationResult mutationResult = await dependencies.MutateMovieTagAsync(
                movie,
                tagName,
                mutationMode
            );
            cancellationToken.ThrowIfCancellationRequested();
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot = CreateSelectionSnapshot();
            WhiteBrowserSkinMovieDto item = BuildMovieDto(movie, selectionSnapshot);

            return new
            {
                found = true,
                changed = mutationResult.Changed,
                hasTag = mutationResult.HasTag,
                focused = selectionSnapshot.FocusedMovie?.Movie_Id == movie.Movie_Id,
                focusedMovieId = selectionSnapshot.FocusedMovie?.Movie_Id ?? 0,
                selected = selectionSnapshot.SelectedMovieIds.Contains(movie.Movie_Id),
                movieId = movie.Movie_Id,
                id = movie.Movie_Id,
                tag = tagName,
                tags = item.Tags,
                item,
                recordKey = item.RecordKey,
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
                items[index] = BuildMovieDto(
                    visibleMovies[startIndex + index],
                    selectionSnapshot,
                    startIndex + index + 1
                );
            }

            return items;
        }

        private WhiteBrowserSkinMovieDto BuildMovieDto(
            MovieRecords movie,
            WhiteBrowserSkinSelectionSnapshot selectionSnapshot,
            int offset = 0
        )
        {
            (string legacyDrive, string legacyDir) = ResolveLegacyDriveAndDirectory(movie);
            string[] legacyTags = ResolveTags(movie);

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
                Tags = legacyTags,
                Score = movie?.Score ?? 0,
                Exists = movie?.IsExists ?? false,
                Selected = thumbnailContract.Selected,
                id = movie?.Movie_Id ?? 0,
                title = ResolveLegacyTitle(movie),
                artist = movie?.Artist ?? "",
                drive = legacyDrive,
                dir = legacyDir,
                ext = ResolveLegacyExtension(movie),
                kana = movie?.Kana ?? "",
                tags = legacyTags,
                container = movie?.Container ?? "",
                video = movie?.Video ?? "",
                audio = movie?.Audio ?? "",
                extra = movie?.Extra ?? "",
                accessDate = movie?.Last_Date ?? "",
                fileDate = movie?.File_Date ?? "",
                registDate = movie?.Regist_Date ?? "",
                comments = ResolveLegacyComments(movie),
                viewCount = movie?.View_Count ?? 0,
                lenSec = ResolveLegacyLengthSeconds(movie),
                offset = offset,
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
            IReadOnlyList<MovieRecords> visibleMovies = dependencies.GetVisibleMovies()
                ?? Array.Empty<MovieRecords>();
            return ApplyQueryOverlays(visibleMovies);
        }

        private IReadOnlyList<MovieRecords> ApplyQueryOverlays(
            IReadOnlyList<MovieRecords> visibleMovies
        )
        {
            List<MovieRecords> items = visibleMovies?.Where(movie => movie != null).ToList() ?? [];
            QueryOverlayState overlayState = CaptureQueryOverlayState();
            if (
                items.Count < 2
                && overlayState.Filters.Length < 1
                && !overlayState.HasWhere
                && !overlayState.HasOrder
            )
            {
                return items;
            }

            IEnumerable<MovieRecords> filtered = items;
            if (overlayState.Filters.Length > 0)
            {
                foreach (string filterText in overlayState.Filters)
                {
                    // 既存検索 service を段階適用して、WB filter の AND 的な重ね掛けに寄せる。
                    filtered = SearchService.FilterMovies(filtered, filterText);
                }
            }

            if (overlayState.HasWhere)
            {
                filtered = filtered.Where(movie => overlayState.WherePredicate(movie));
            }

            List<MovieRecords> filteredItems = filtered.ToList();

            if (filteredItems.Count < 2 || !overlayState.HasOrder)
            {
                return filteredItems;
            }

            return ApplyOrderOverlay(filteredItems, overlayState);
        }

        private IReadOnlyList<MovieRecords> ApplyOrderOverlay(
            IReadOnlyList<MovieRecords> items,
            QueryOverlayState overlayState
        )
        {
            if (!overlayState.HasOrder || items.Count < 2)
            {
                return items;
            }

            if (!string.IsNullOrWhiteSpace(overlayState.NamedSortId))
            {
                if (overlayState.OverrideCurrentOrder)
                {
                    return ApplyNamedSort(items, overlayState.NamedSortId).ToArray();
                }

                IOrderedEnumerable<MovieRecords> ordered = ApplyNamedSortOrKeepInputOrder(
                    items,
                    dependencies.GetCurrentSortId()
                );
                return ApplyThenByNamedSort(ordered, overlayState.NamedSortId).ToArray();
            }

            if (!overlayState.SqlOrder.HasTerms)
            {
                return items;
            }

            if (overlayState.OverrideCurrentOrder)
            {
                return ApplyCompiledOrder(items, overlayState.SqlOrder).ToArray();
            }

            IOrderedEnumerable<MovieRecords> baseOrdered = ApplyNamedSortOrKeepInputOrder(
                items,
                dependencies.GetCurrentSortId()
            );
            return ApplyThenByCompiledOrder(baseOrdered, overlayState.SqlOrder).ToArray();
        }

        private QueryOverlayState CaptureQueryOverlayState()
        {
            lock (queryOverlaySync)
            {
                return new QueryOverlayState(
                    [.. addedFilters],
                    hasAddedWhere,
                    addedWherePredicate,
                    addedWhereText,
                    !string.IsNullOrWhiteSpace(addedNamedOrderSortId) || addedSqlOrder.HasTerms,
                    addedNamedOrderSortId,
                    addedOrderText,
                    addedSqlOrder,
                    addedOrderOverride
                );
            }
        }

        private void ClearAddedFilters()
        {
            lock (queryOverlaySync)
            {
                addedFilters = [];
            }
        }

        private string[] ResolveCurrentFilterTokens(QueryOverlayState overlayState = default)
        {
            string[] currentFilters = (dependencies.GetCurrentFilterTokens() ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (currentFilters.Length > 0)
            {
                return currentFilters;
            }

            if (EqualityComparer<QueryOverlayState>.Default.Equals(overlayState, default))
            {
                overlayState = CaptureQueryOverlayState();
            }

            return overlayState.Filters ?? [];
        }

        private async Task<bool> TryApplyFilterTokensAsync(
            IReadOnlyList<string> nextFilters,
            CancellationToken cancellationToken
        )
        {
            bool applied = await dependencies.ApplyFilterTokensAsync(nextFilters ?? Array.Empty<string>());
            cancellationToken.ThrowIfCancellationRequested();
            return applied;
        }

        private void ClearAddedWhere()
        {
            lock (queryOverlaySync)
            {
                addedWherePredicate = static _ => true;
                hasAddedWhere = false;
                addedWhereText = "";
            }
        }

        private void ClearAddedOrder()
        {
            lock (queryOverlaySync)
            {
                addedNamedOrderSortId = "";
                addedOrderText = "";
                addedSqlOrder = WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder.Empty;
                addedOrderOverride = false;
            }
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

        private static string GetTagName(JsonElement payload)
        {
            string tagName = GetString(payload, "tag", "");
            if (!string.IsNullOrWhiteSpace(tagName))
            {
                return tagName.Trim();
            }

            tagName = GetString(payload, "tagName", "");
            if (!string.IsNullOrWhiteSpace(tagName))
            {
                return tagName.Trim();
            }

            tagName = GetString(payload, "value", "");
            if (!string.IsNullOrWhiteSpace(tagName))
            {
                return tagName.Trim();
            }

            tagName = GetString(payload, "name", "");
            return tagName?.Trim() ?? "";
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

        private static (string Drive, string DirectoryPath) ResolveLegacyDriveAndDirectory(
            MovieRecords movie
        )
        {
            string moviePath = movie?.Movie_Path ?? "";
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return ("", "");
            }

            string drive = Path.GetPathRoot(moviePath) ?? "";
            if (
                drive.EndsWith(Path.DirectorySeparatorChar)
                || drive.EndsWith(Path.AltDirectorySeparatorChar)
            )
            {
                drive = drive[..^1];
            }

            string directoryPath = Path.GetDirectoryName(moviePath) ?? "";
            if (!string.IsNullOrWhiteSpace(drive)
                && directoryPath.StartsWith(drive, StringComparison.OrdinalIgnoreCase))
            {
                directoryPath = directoryPath[drive.Length..];
            }

            directoryPath = directoryPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(directoryPath)
                && !directoryPath.StartsWith(Path.DirectorySeparatorChar))
            {
                directoryPath = Path.DirectorySeparatorChar + directoryPath;
            }

            if (!string.IsNullOrWhiteSpace(directoryPath)
                && !directoryPath.EndsWith(Path.DirectorySeparatorChar))
            {
                directoryPath += Path.DirectorySeparatorChar;
            }

            return (drive, directoryPath);
        }

        private static string ResolveLegacyComments(MovieRecords movie)
        {
            string[] parts =
            [
                movie?.Comment1 ?? "",
                movie?.Comment2 ?? "",
                movie?.Comment3 ?? "",
            ];

            return string.Join(
                "\n",
                parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            );
        }

        private static string ResolveLegacyLengthSeconds(MovieRecords movie)
        {
            if (TimeSpan.TryParse(movie?.Movie_Length ?? "", out TimeSpan parsed))
            {
                return ((long)parsed.TotalSeconds).ToString();
            }

            return "";
        }

        private static string ResolveWhereClause(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.String)
            {
                return payload.GetString() ?? "";
            }

            return GetString(
                payload,
                "where",
                GetString(payload, "clause", GetString(payload, "condition", ""))
            );
        }

        private static string ResolveOrderText(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.String)
            {
                return payload.GetString() ?? "";
            }

            return GetString(payload, "order", GetString(payload, "value", ""));
        }

        private static string ResolveFilterText(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.String)
            {
                return payload.GetString() ?? "";
            }

            return GetString(payload, "filter", GetString(payload, "value", ""));
        }

        private static bool IsSqlOrderText(string orderText)
        {
            return !string.IsNullOrWhiteSpace(orderText)
                && orderText.TrimStart().StartsWith('{')
                && orderText.TrimEnd().EndsWith('}');
        }

        private static string BuildAdditionalOrderText(QueryOverlayState overlayState)
        {
            string orderText = overlayState.OrderText ?? "";
            if (string.IsNullOrWhiteSpace(orderText))
            {
                return "";
            }

            // override 指定は先頭 `#` で表す、という旧 WB 互換へ合わせる。
            return overlayState.OverrideCurrentOrder && !orderText.StartsWith("#", StringComparison.Ordinal)
                ? $"#{orderText}"
                : orderText;
        }

        private static string NormalizeFilterText(string filterText)
        {
            string normalized = (filterText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            // タグバーの複数行検索は OR として扱われるため、overlay でも寄せておく。
            normalized = normalized.Replace("\r\n", "\n", StringComparison.Ordinal);
            string[] lines = normalized
                .Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length > 1 ? string.Join(" | ", lines) : normalized;
        }

        private static IOrderedEnumerable<MovieRecords> ApplyCompiledOrder(
            IEnumerable<MovieRecords> source,
            WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder compiledOrder
        )
        {
            IOrderedEnumerable<MovieRecords> ordered = null;
            foreach (WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrderTerm term in compiledOrder.Terms)
            {
                if (ordered == null)
                {
                    ordered = term.Descending
                        ? source.OrderByDescending(
                            movie => term.Selector(movie),
                            WhiteBrowserSkinQueryOverlayCompiler.SqlValueComparer.Instance
                        )
                        : source.OrderBy(
                            movie => term.Selector(movie),
                            WhiteBrowserSkinQueryOverlayCompiler.SqlValueComparer.Instance
                        );
                }
                else
                {
                    ordered = term.Descending
                        ? ordered.ThenByDescending(
                            movie => term.Selector(movie),
                            WhiteBrowserSkinQueryOverlayCompiler.SqlValueComparer.Instance
                        )
                        : ordered.ThenBy(
                            movie => term.Selector(movie),
                            WhiteBrowserSkinQueryOverlayCompiler.SqlValueComparer.Instance
                        );
                }
            }

            return ordered ?? source.OrderBy(static _ => 0);
        }

        private static IOrderedEnumerable<MovieRecords> ApplyThenByCompiledOrder(
            IOrderedEnumerable<MovieRecords> source,
            WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder compiledOrder
        )
        {
            IOrderedEnumerable<MovieRecords> ordered = source;
            foreach (WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrderTerm term in compiledOrder.Terms)
            {
                ordered = term.Descending
                    ? ordered.ThenByDescending(
                        movie => term.Selector(movie),
                        WhiteBrowserSkinQueryOverlayCompiler.SqlValueComparer.Instance
                    )
                    : ordered.ThenBy(
                        movie => term.Selector(movie),
                        WhiteBrowserSkinQueryOverlayCompiler.SqlValueComparer.Instance
                    );
            }

            return ordered;
        }

        private static IOrderedEnumerable<MovieRecords> ApplyNamedSortOrKeepInputOrder(
            IEnumerable<MovieRecords> source,
            string sortId
        )
        {
            return string.IsNullOrWhiteSpace(sortId)
                ? source.OrderBy(static _ => 0)
                : ApplyNamedSort(source, sortId);
        }

        private static IOrderedEnumerable<MovieRecords> ApplyNamedSort(
            IEnumerable<MovieRecords> source,
            string sortId
        )
        {
            IEnumerable<MovieRecords> query = source ?? Array.Empty<MovieRecords>();
            return sortId switch
            {
                "0" => query.OrderByDescending(x => x.Last_Date),
                "1" => query.OrderBy(x => x.Last_Date),
                "2" => query.OrderByDescending(x => x.File_Date),
                "3" => query.OrderBy(x => x.File_Date),
                "6" => query.OrderByDescending(x => x.Score),
                "7" => query.OrderBy(x => x.Score),
                "8" => query.OrderByDescending(x => x.View_Count),
                "9" => query.OrderBy(x => x.View_Count),
                "10" => query.OrderBy(x => x.Kana),
                "11" => query.OrderByDescending(x => x.Kana),
                "12" => query.OrderBy(x => x.Movie_Name),
                "13" => query.OrderByDescending(x => x.Movie_Name),
                "14" => query.OrderBy(x => x.Movie_Path),
                "15" => query.OrderByDescending(x => x.Movie_Path),
                "16" => query.OrderByDescending(x => x.Movie_Size),
                "17" => query.OrderBy(x => x.Movie_Size),
                "18" => query.OrderByDescending(x => x.Regist_Date),
                "19" => query.OrderBy(x => x.Regist_Date),
                "20" => query.OrderByDescending(x => x.Movie_Length),
                "21" => query.OrderBy(x => x.Movie_Length),
                "22" => query.OrderBy(x => x.Comment1),
                "23" => query.OrderByDescending(x => x.Comment1),
                "24" => query.OrderBy(x => x.Comment2),
                "25" => query.OrderByDescending(x => x.Comment2),
                "26" => query.OrderBy(x => x.Comment3),
                "27" => query.OrderByDescending(x => x.Comment3),
                "28" => query
                    .OrderByDescending(ResolveThumbnailErrorSortCount)
                    .ThenBy(x => x.Movie_Name ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Movie_Path ?? "", StringComparer.CurrentCultureIgnoreCase),
                _ => query.OrderBy(static _ => 0),
            };
        }

        private static IOrderedEnumerable<MovieRecords> ApplyThenByNamedSort(
            IOrderedEnumerable<MovieRecords> source,
            string sortId
        )
        {
            return sortId switch
            {
                "0" => source.ThenByDescending(x => x.Last_Date),
                "1" => source.ThenBy(x => x.Last_Date),
                "2" => source.ThenByDescending(x => x.File_Date),
                "3" => source.ThenBy(x => x.File_Date),
                "6" => source.ThenByDescending(x => x.Score),
                "7" => source.ThenBy(x => x.Score),
                "8" => source.ThenByDescending(x => x.View_Count),
                "9" => source.ThenBy(x => x.View_Count),
                "10" => source.ThenBy(x => x.Kana),
                "11" => source.ThenByDescending(x => x.Kana),
                "12" => source.ThenBy(x => x.Movie_Name),
                "13" => source.ThenByDescending(x => x.Movie_Name),
                "14" => source.ThenBy(x => x.Movie_Path),
                "15" => source.ThenByDescending(x => x.Movie_Path),
                "16" => source.ThenByDescending(x => x.Movie_Size),
                "17" => source.ThenBy(x => x.Movie_Size),
                "18" => source.ThenByDescending(x => x.Regist_Date),
                "19" => source.ThenBy(x => x.Regist_Date),
                "20" => source.ThenByDescending(x => x.Movie_Length),
                "21" => source.ThenBy(x => x.Movie_Length),
                "22" => source.ThenBy(x => x.Comment1),
                "23" => source.ThenByDescending(x => x.Comment1),
                "24" => source.ThenBy(x => x.Comment2),
                "25" => source.ThenByDescending(x => x.Comment2),
                "26" => source.ThenBy(x => x.Comment3),
                "27" => source.ThenByDescending(x => x.Comment3),
                "28" => source
                    .ThenByDescending(ResolveThumbnailErrorSortCount)
                    .ThenBy(x => x.Movie_Name ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Movie_Path ?? "", StringComparer.CurrentCultureIgnoreCase),
                _ => source,
            };
        }

        private static int ResolveThumbnailErrorSortCount(MovieRecords movie)
        {
            if (movie == null)
            {
                return 0;
            }

            return Math.Max(
                ThumbnailErrorPlaceholderHelper.CountPlaceholders(movie),
                movie.ThumbnailErrorMarkerCount
            );
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

        private readonly record struct QueryOverlayState(
            string[] Filters,
            bool HasWhere,
            Func<MovieRecords, bool> WherePredicate,
            string WhereText,
            bool HasOrder,
            string NamedSortId,
            string OrderText,
            WhiteBrowserSkinQueryOverlayCompiler.WhiteBrowserSkinCompiledOrder SqlOrder,
            bool OverrideCurrentOrder
        );

        private readonly record struct ThumbnailMetadata(int Width, int Height, int Columns, int Rows);

        private readonly record struct WhiteBrowserSkinSelectionSnapshot(
            MovieRecords FocusedMovie,
            HashSet<long> SelectedMovieIds
        );
    }
}
