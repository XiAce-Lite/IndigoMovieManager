using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.Skin.Host;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private WhiteBrowserSkinHostControl _externalSkinHostApiAttachedControl;
        private WhiteBrowserSkinApiService _externalSkinApiService;

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
                FocusMovieAsync = FocusExternalSkinMovieAsync,
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
