using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using IndigoMovieManager.Skin.Host;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private WhiteBrowserSkinHostControl _externalSkinHostApiAttachedControl;
        private WhiteBrowserSkinApiService _externalSkinApiService;
        private bool _suppressSortComboSelectionChangedHandling;

        private void AttachExternalSkinHostApiBridge(WhiteBrowserSkinHostControl hostControl)
        {
            if (hostControl == null)
            {
                return;
            }

            if (ReferenceEquals(_externalSkinHostApiAttachedControl, hostControl))
            {
                return;
            }

            DetachExternalSkinHostApiBridge(_externalSkinHostApiAttachedControl);
            hostControl.WebMessageReceived += ExternalSkinHostControl_WebMessageReceived;
            _externalSkinHostApiAttachedControl = hostControl;
        }

        private void DetachExternalSkinHostApiBridge(WhiteBrowserSkinHostControl hostControl)
        {
            if (hostControl == null)
            {
                return;
            }

            hostControl.WebMessageReceived -= ExternalSkinHostControl_WebMessageReceived;
            if (ReferenceEquals(_externalSkinHostApiAttachedControl, hostControl))
            {
                _externalSkinHostApiAttachedControl = null;
            }
        }

        private async void ExternalSkinHostControl_WebMessageReceived(
            object sender,
            WhiteBrowserSkinWebMessageReceivedEventArgs e
        )
        {
            WhiteBrowserSkinHostControl hostControl =
                sender as WhiteBrowserSkinHostControl ?? _externalSkinHostControl;
            if (hostControl == null || e == null)
            {
                return;
            }

            await HandleExternalSkinHostWebMessageAsync(hostControl, e);
        }

        private async Task HandleExternalSkinHostWebMessageAsync(
            WhiteBrowserSkinHostControl hostControl,
            WhiteBrowserSkinWebMessageReceivedEventArgs message
        )
        {
            if (hostControl == null || message == null || string.IsNullOrWhiteSpace(message.MessageId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message.Method))
            {
                await hostControl.RejectRequestAsync(message.MessageId, "Missing wb method.");
                return;
            }

            try
            {
                WhiteBrowserSkinApiInvocationResult result = await GetOrCreateExternalSkinApiService()
                    .HandleAsync(message.Method, message.Payload);

                if (result?.Succeeded == true)
                {
                    await hostControl.ResolveRequestAsync(message.MessageId, result.Payload);
                    return;
                }

                await hostControl.RejectRequestAsync(
                    message.MessageId,
                    result?.ErrorMessage ?? $"Unsupported wb method: {message.Method}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"api bridge failed: method='{message.Method}' err='{ex.GetType().Name}: {ex.Message}'"
                );
                await hostControl.RejectRequestAsync(message.MessageId, ex.Message);
            }
        }

        private WhiteBrowserSkinApiService GetOrCreateExternalSkinApiService()
        {
            _externalSkinApiService ??= new WhiteBrowserSkinApiService(
                BuildExternalSkinApiServiceDependencies(),
                new WhiteBrowserSkinApiServiceOptions
                {
                    ThumbnailBaseUri = WhiteBrowserSkinHostPaths.BuildThumbnailBaseUri(),
                }
            );
            return _externalSkinApiService;
        }

        // MainWindow は UI 状態の読み書きだけを持ち、DTO 組み立ては runtime service へ寄せる。
        private WhiteBrowserSkinApiServiceDependencies BuildExternalSkinApiServiceDependencies()
        {
            return new WhiteBrowserSkinApiServiceDependencies
            {
                GetVisibleMovies = () =>
                    ReadExternalSkinUiState(
                        () =>
                            MainVM?.FilteredMovieRecs?.Where(x => x != null).ToArray()
                            ?? Array.Empty<MovieRecords>(),
                        Array.Empty<MovieRecords>()
                    ),
                GetCurrentTabIndex = () =>
                    ReadExternalSkinUiState(
                        ResolveExternalSkinApiTabIndexOnUiThread,
                        UpperTabGridFixedIndex
                    ),
                GetCurrentDbFullPath = () =>
                    ReadExternalSkinUiState(() => MainVM?.DbInfo?.DBFullPath ?? "", ""),
                GetCurrentDbName = () =>
                    ReadExternalSkinUiState(() => MainVM?.DbInfo?.DBName ?? "", ""),
                GetCurrentSkinName = () =>
                    ReadExternalSkinUiState(() => MainVM?.DbInfo?.Skin ?? "", ""),
                GetCurrentThumbFolder = () =>
                    ReadExternalSkinUiState(() => MainVM?.DbInfo?.ThumbFolder ?? "", ""),
                GetCurrentSelectedMovie = () =>
                    ReadExternalSkinUiState(() => GetSelectedItemByTabIndex(), null),
                GetCurrentSelectedMovies = () =>
                    ReadExternalSkinUiState(
                        () =>
                            GetSelectedItemsByTabIndex()?.Where(x => x != null).Distinct().ToArray()
                            ?? Array.Empty<MovieRecords>(),
                        Array.Empty<MovieRecords>()
                    ),
                FocusMovieAsync = FocusExternalSkinMovieAsync,
                SetMovieSelectionAsync = SetExternalSkinMovieSelectionAsync,
                ExecuteSearchAsync = SearchExternalSkinAsync,
                ExecuteSortAsync = SortExternalSkinAsync,
                ChangeSkinAsync = ChangeExternalSkinAsync,
                GetProfileValueAsync = GetExternalSkinProfileValueAsync,
                WriteProfileValueAsync = WriteExternalSkinProfileValueAsync,
                ResolveThumbUrl = ResolveExternalSkinThumbUrl,
                Trace = message =>
                    DebugRuntimeLog.Write("skin-webview", $"js trace: {message ?? ""}"),
            };
        }

        private async Task<bool> FocusExternalSkinMovieAsync(MovieRecords movie)
        {
            if (movie == null)
            {
                return false;
            }

            return await InvokeExternalSkinUiActionAsync(
                () =>
                {
                    SelectCurrentUpperTabMovieRecord(movie);
                    return true;
                },
                false
            );
        }

        private async Task<bool> SetExternalSkinMovieSelectionAsync(
            MovieRecords movie,
            bool isSelected
        )
        {
            if (movie == null)
            {
                return false;
            }

            return await InvokeExternalSkinUiActionAsync(
                () => SetCurrentUpperTabMovieSelection(movie, isSelected),
                false
            );
        }

        private async Task<bool> SearchExternalSkinAsync(string keyword)
        {
            return await InvokeExternalSkinUiTaskAsync(
                () => ExecuteExternalSkinSearchAsync(keyword),
                false
            );
        }

        private async Task<bool> SortExternalSkinAsync(string sortKey)
        {
            return await InvokeExternalSkinUiActionAsync(
                () =>
                {
                    string resolvedSortId = ResolveExternalSkinSortIdOnUiThread(sortKey);
                    if (string.IsNullOrWhiteSpace(resolvedSortId))
                    {
                        return false;
                    }

                    _suppressSortComboSelectionChangedHandling = true;
                    try
                    {
                        if (ComboSort != null)
                        {
                            ComboSort.SelectedValue = resolvedSortId;
                        }
                    }
                    finally
                    {
                        _suppressSortComboSelectionChangedHandling = false;
                    }

                    if (MainVM?.DbInfo == null)
                    {
                        return false;
                    }

                    MainVM.DbInfo.Sort = resolvedSortId;
                    if (IsStartupFeedPartialActive)
                    {
                        FilterAndSort(resolvedSortId, true);
                    }
                    else
                    {
                        SortData(resolvedSortId);
                    }

                    SelectFirstItem();
                    return true;
                },
                false
            );
        }

        private async Task<bool> ChangeExternalSkinAsync(string skinName)
        {
            return await InvokeExternalSkinUiActionAsync(
                () => ApplySkinByName(skinName, persistToCurrentDb: true),
                false
            );
        }

        private Task<string> GetExternalSkinProfileValueAsync(string key)
        {
            return InvokeExternalSkinUiTaskAsync(
                () =>
                    Task.FromResult(
                        ReadExternalSkinUiState(
                            () =>
                            {
                                string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
                                string skinName = MainVM?.DbInfo?.Skin ?? "";
                                if (
                                    string.IsNullOrWhiteSpace(dbFullPath)
                                    || string.IsNullOrWhiteSpace(skinName)
                                    || string.IsNullOrWhiteSpace(key)
                                )
                                {
                                    return "";
                                }

                                return DB.SQLite.SelectProfileValue(dbFullPath, skinName, key);
                            },
                            ""
                        )
                    ),
                ""
            );
        }

        private async Task<bool> WriteExternalSkinProfileValueAsync(string key, string value)
        {
            return await InvokeExternalSkinUiActionAsync(
                () =>
                {
                    string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
                    string skinName = MainVM?.DbInfo?.Skin ?? "";
                    if (
                        string.IsNullOrWhiteSpace(dbFullPath)
                        || string.IsNullOrWhiteSpace(skinName)
                        || string.IsNullOrWhiteSpace(key)
                    )
                    {
                        return false;
                    }

                    DB.SQLite.UpsertProfileTable(dbFullPath, skinName, key, value ?? "");
                    return true;
                },
                false
            );
        }

        private int ResolveExternalSkinApiTabIndexOnUiThread()
        {
            int tabIndex = MainVM?.DbInfo?.CurrentTabIndex ?? UpperTabGridFixedIndex;
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => UpperTabSmallFixedIndex,
                UpperTabBigFixedIndex => UpperTabBigFixedIndex,
                UpperTabGridFixedIndex => UpperTabGridFixedIndex,
                UpperTabListFixedIndex => UpperTabListFixedIndex,
                UpperTabBig10FixedIndex => UpperTabBig10FixedIndex,
                _ => UpperTabGridFixedIndex,
            };
        }

        private string ResolveExternalSkinSortIdOnUiThread(string sortKey)
        {
            string normalizedSortKey = sortKey?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedSortKey))
            {
                return MainVM?.DbInfo?.Sort ?? "";
            }

            if (MainVM?.SortLists == null || MainVM.SortLists.Count < 1)
            {
                return normalizedSortKey;
            }

            ViewModels.MainWindowViewModel.SortItem exactById = MainVM.SortLists.FirstOrDefault(x =>
                string.Equals(x?.Id, normalizedSortKey, StringComparison.OrdinalIgnoreCase)
            );
            if (exactById != null)
            {
                return exactById.Id ?? "";
            }

            ViewModels.MainWindowViewModel.SortItem exactByName = MainVM.SortLists.FirstOrDefault(x =>
                string.Equals(x?.Name, normalizedSortKey, StringComparison.CurrentCultureIgnoreCase)
            );
            return exactByName?.Id ?? "";
        }

        private string ResolveExternalSkinThumbUrl(string thumbPath)
        {
            return ReadExternalSkinUiState(
                () =>
                {
                    string thumbRootPath = MainVM?.DbInfo?.ThumbFolder ?? "";
                    string thumbUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                        thumbPath,
                        thumbRootPath,
                        ""
                    );
                    if (
                        string.IsNullOrWhiteSpace(thumbUrl)
                        || !IsExternalSkinExternalThumbUrl(thumbUrl)
                    )
                    {
                        return thumbUrl;
                    }

                    WhiteBrowserSkinHostControl hostControl =
                        _externalSkinHostApiAttachedControl ?? _externalSkinHostControl;
                    hostControl?.RegisterExternalThumbnailPath(thumbPath);
                    return thumbUrl;
                },
                ""
            );
        }

        // 外部 skin からの複数選択要求は、現在前面の通常タブの SelectedItems へだけ反映する。
        private bool SetCurrentUpperTabMovieSelection(MovieRecords movie, bool isSelected)
        {
            if (movie == null)
            {
                return false;
            }

            return GetCurrentUpperTabFixedIndex() switch
            {
                UpperTabSmallFixedIndex => SetExternalSkinListSelection(
                    GetUpperTabSmallList(),
                    movie,
                    isSelected
                ),
                UpperTabBigFixedIndex => SetExternalSkinListSelection(
                    GetUpperTabBigList(),
                    movie,
                    isSelected
                ),
                UpperTabGridFixedIndex => SetExternalSkinListSelection(
                    GetUpperTabGridList(),
                    movie,
                    isSelected
                ),
                UpperTabListFixedIndex => SetExternalSkinGridSelection(
                    GetUpperTabListDataGrid(),
                    movie,
                    isSelected
                ),
                UpperTabBig10FixedIndex => SetExternalSkinListSelection(
                    GetUpperTabBig10List(),
                    movie,
                    isSelected
                ),
                _ => false,
            };
        }

        private static bool SetExternalSkinListSelection(
            ListView listView,
            MovieRecords movie,
            bool isSelected
        )
        {
            if (listView?.SelectedItems == null || movie == null || !listView.Items.Contains(movie))
            {
                return false;
            }

            if (isSelected)
            {
                if (!listView.SelectedItems.Contains(movie))
                {
                    listView.SelectedItems.Add(movie);
                }
            }
            else
            {
                if (listView.SelectedItems.Contains(movie))
                {
                    listView.SelectedItems.Remove(movie);
                }

                if (ReferenceEquals(listView.SelectedItem, movie))
                {
                    listView.SelectedItem = listView.SelectedItems.Count > 0
                        ? listView.SelectedItems[0]
                        : null;
                }
            }

            return listView.SelectedItems.Contains(movie) == isSelected;
        }

        private static bool SetExternalSkinGridSelection(
            DataGrid dataGrid,
            MovieRecords movie,
            bool isSelected
        )
        {
            if (dataGrid?.SelectedItems == null || movie == null || !dataGrid.Items.Contains(movie))
            {
                return false;
            }

            if (isSelected)
            {
                if (!dataGrid.SelectedItems.Contains(movie))
                {
                    dataGrid.SelectedItems.Add(movie);
                }
            }
            else
            {
                if (dataGrid.SelectedItems.Contains(movie))
                {
                    dataGrid.SelectedItems.Remove(movie);
                }

                if (ReferenceEquals(dataGrid.SelectedItem, movie))
                {
                    dataGrid.SelectedItem = dataGrid.SelectedItems.Count > 0
                        ? dataGrid.SelectedItems[0]
                        : null;
                }
            }

            return dataGrid.SelectedItems.Contains(movie) == isSelected;
        }

        private T ReadExternalSkinUiState<T>(Func<T> reader, T fallback)
        {
            if (reader == null)
            {
                return fallback;
            }

            try
            {
                if (Dispatcher == null || Dispatcher.CheckAccess())
                {
                    return reader();
                }

                return Dispatcher.Invoke(reader);
            }
            catch
            {
                return fallback;
            }
        }

        private Task<T> InvokeExternalSkinUiActionAsync<T>(Func<T> action, T fallback)
        {
            if (action == null)
            {
                return Task.FromResult(fallback);
            }

            try
            {
                if (Dispatcher == null || Dispatcher.CheckAccess())
                {
                    return Task.FromResult(action());
                }

                return Dispatcher.InvokeAsync(action).Task;
            }
            catch
            {
                return Task.FromResult(fallback);
            }
        }

        private Task<T> InvokeExternalSkinUiTaskAsync<T>(Func<Task<T>> action, T fallback)
        {
            if (action == null)
            {
                return Task.FromResult(fallback);
            }

            try
            {
                if (Dispatcher == null || Dispatcher.CheckAccess())
                {
                    return action();
                }

                return Dispatcher.InvokeAsync(action).Task.Unwrap();
            }
            catch
            {
                return Task.FromResult(fallback);
            }
        }

        private static bool IsExternalSkinExternalThumbUrl(string thumbUrl)
        {
            if (!Uri.TryCreate(thumbUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            if (
                !string.Equals(
                    uri.Host,
                    WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            string relativePath = uri.AbsolutePath.Trim('/');
            return relativePath.StartsWith(
                $"{WhiteBrowserSkinThumbnailUrlCodec.ExternalRoutePrefix}/",
                StringComparison.OrdinalIgnoreCase
            );
        }
    }
}
