using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const double ManualPlayerPreferredLandscapeWidth = 900d;
        private const double ManualPlayerHorizontalPadding = 96d;
        private const double ManualPlayerVerticalPadding = 120d;
        private const double ManualPlayerFallbackControllerHeight = 72d;
        private bool _isManualPlayerResizeHookRegistered;
        private bool _isTimeSliderSyncingFromPlayer;
        private bool _isTimeSliderDragging;

        public async void PlayMovie_Click(object sender, RoutedEventArgs e)
        {
            var playerPrg = SelectSystemTable("playerPrg");
            var playerParam = SelectSystemTable("playerParam");

            //設定DBごとのプレイヤーが空
            if (string.IsNullOrEmpty(playerPrg))
            {
                //全体設定のプレイヤーを設定
                playerPrg = Properties.Settings.Default.DefaultPlayerPath;
            }

            //設定DBごとのプレイヤーパラメータが空
            if (string.IsNullOrEmpty(playerParam))
            {
                //全体設定のプレイヤーパラメータを設定
                playerParam = Properties.Settings.Default.DefaultPlayerParam;
            }

            int msec = 0;
            int secPos = 0; //ここでは渡す為だけに使ってる。
            string moviePath = "";
            MovieRecords mv = new();
            bool notBookmark = true;

            if (sender is Label labelObj)
            {
                if (labelObj.Name == "LabelBookMark")
                {
                    var item = (Label)sender;
                    if (item != null)
                    {
                        notBookmark = false;
                        mv = item.DataContext as MovieRecords;
                        //実ムービーファイルのパスを取得する。Movie_Bodyに入っているファイル名の一部で検索する。
                        MovieRecords bookmarkedMv = MainVM
                            .MovieRecs.Where(x =>
                                x.Movie_Name.Contains(
                                    mv.Movie_Body,
                                    StringComparison.CurrentCultureIgnoreCase
                                )
                            )
                            .First();
                        var BookMarkedFilePath = bookmarkedMv.Movie_Path;
                        MovieInfo mvi = new(BookMarkedFilePath, true); //Hashの取得が重いのでオプション付けた。ブックマークには不要。
                        msec = (int)mv.Score / (int)mvi.FPS * 1000;
                        moviePath = $"\"{BookMarkedFilePath}\"";
                        UpdateBookmarkViewCount(MainVM.DbInfo.DBFullPath, mv.Movie_Id);
                    }
                }
            }

            if (notBookmark)
            {
                if (Tabs.SelectedItem == null)
                {
                    return;
                }

                mv = GetSelectedItemByTabIndex();
                if (mv == null)
                {
                    return;
                }

                moviePath = $"\"{mv.Movie_Path}\"";

                if (!Path.Exists(mv.Movie_Path))
                {
                    return;
                }

                if (sender is MenuItem senderObj)
                {
                    if (senderObj.Name == "PlayFromThumb")
                    {
                        msec = GetPlayPosition(GetCurrentThumbnailActionTabIndex(), mv, ref secPos);
                    }
                }
            }

            if (!string.IsNullOrEmpty(playerParam))
            {
                playerParam = playerParam.Replace("<file>", $"{mv.Movie_Path}");
                playerParam = playerParam.Replace("<ms>", $"{msec}");
            }

            var arg = $"{moviePath} {playerParam}";

            try
            {
                using Process ps1 = new();
                //設定ファイルのプログラムも既定のプログラムも空だった場合にはここのはず。
                if (string.IsNullOrEmpty(playerPrg))
                {
                    ps1.StartInfo.UseShellExecute = true;
                    ps1.StartInfo.FileName = moviePath;
                }
                else
                {
                    ps1.StartInfo.Arguments = arg;
                    ps1.StartInfo.FileName = playerPrg;
                }
                ps1.Start();

                var psName = ps1.ProcessName;
                Process ps2 = Process.GetProcessById(ps1.Id);
                foreach (Process p in Process.GetProcessesByName(psName))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (
                            p.MainWindowTitle.Contains(
                                mv.Movie_Name,
                                StringComparison.CurrentCultureIgnoreCase
                            )
                        )
                        {
                            p.Kill();
                            await p.WaitForExitAsync();
                        }
                    }
                }
                mv.View_Count += 1;
                mv.Score += 1;
                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                mv.Last_Date = result.ToString("yyyy-MM-dd HH:mm:ss");

                _mainDbMovieMutationFacade.UpdateScore(
                    MainVM.DbInfo.DBFullPath,
                    mv.Movie_Id,
                    mv.Score
                );
                _mainDbMovieMutationFacade.UpdateViewCount(
                    MainVM.DbInfo.DBFullPath,
                    mv.Movie_Id,
                    mv.View_Count
                );
                _mainDbMovieMutationFacade.UpdateLastDate(
                    MainVM.DbInfo.DBFullPath,
                    mv.Movie_Id,
                    result
                );
            }
            catch (Exception err)
            {
                MessageBox.Show(
                    err.Message,
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }
        }

        private bool IsPlaying = false;

        /// <summary>
        /// 動画再生の号砲！プレイヤーを呼び覚まし、熱い映像体験をスタートさせるぜ！▶️✨
        /// （ありがとう先人の知恵：https://resanaplaza.com/2023/06/24/%e3%80%90...MediaElement）
        /// </summary>
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            PlayerArea.Visibility = Visibility.Visible;
            PlayerController.Visibility = Visibility.Visible;
            uxVideoPlayer.Visibility = Visibility.Visible;
            uxVideoPlayer.Play();
            IsPlaying = true;
            uxTimeSlider.Value = uxVideoPlayer.Position.TotalMilliseconds;
            TryStartDispatcherTimer(timer, nameof(timer));
        }

        /// <summary>
        /// ちょい待ち！一時停止ボタンで時を止めるぜ！⏸️
        /// </summary>
        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            uxVideoPlayer.Pause();
            IsPlaying = false;
        }

        private void UxVideoPlayer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsPlaying == true)
            {
                uxVideoPlayer.Pause();
                IsPlaying = false;
            }
            else
            {
                uxVideoPlayer.Play();
                IsPlaying = true;
            }
        }

        /// <summary>
        /// 再生完全終了！ストップボタンでプレイヤーをサクッと隠し、裏方に下げるぜ！⏹️
        /// </summary>
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            CloseManualPlayerOverlay();
        }

        /// <summary>
        /// タイムラインスライダーを動かしたな！指定の秒数へ動画のポジションを即座にワープさせるぜ！🚀
        /// </summary>
        private void UxTimeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isTimeSliderSyncingFromPlayer)
            {
                return;
            }

            DateTime now = DateTime.Now;
            TimeSpan timeSinceLastUpdate = now - _lastSliderTime;

            if (timeSinceLastUpdate >= _timeSliderInterval)
            {
                uxVideoPlayer.Position = TimeSpan.FromMilliseconds(uxTimeSlider.Value);
                _lastSliderTime = now;
                uxTime.Text = uxVideoPlayer.Position.ToString()[..8];
            }
        }

        /// <summary>
        /// 動画ファイルのロード完了！再生時間の最大値をスライダーにガツンとセットするぜ！🎞️
        /// </summary>
        private void UxVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // duration 未確定の動画でも落とさず、既知の最大値だけを安全に反映する。
            uxTimeSlider.Maximum = ResolveMediaDurationMaximumMilliseconds(
                uxVideoPlayer.NaturalDuration,
                uxTimeSlider.Maximum
            );
            UpdateManualPlayerViewport();
        }

        internal static double ResolveMediaDurationMaximumMilliseconds(
            Duration naturalDuration,
            double fallbackMaximum
        )
        {
            if (naturalDuration.HasTimeSpan)
            {
                return Math.Max(0d, naturalDuration.TimeSpan.TotalMilliseconds);
            }

            if (double.IsNaN(fallbackMaximum) || double.IsInfinity(fallbackMaximum))
            {
                return 0d;
            }

            return Math.Max(0d, fallbackMaximum);
        }

        internal static Size ResolveManualPlayerViewportSize(
            double availableWidth,
            double availableHeight,
            double naturalVideoWidth,
            double naturalVideoHeight,
            double preferredLandscapeWidth = ManualPlayerPreferredLandscapeWidth
        )
        {
            double safeAvailableWidth = Math.Max(0d, availableWidth);
            double safeAvailableHeight = Math.Max(0d, availableHeight);
            if (safeAvailableWidth <= 0d || safeAvailableHeight <= 0d)
            {
                return new Size(0d, 0d);
            }

            if (naturalVideoWidth <= 0d || naturalVideoHeight <= 0d)
            {
                double fallbackWidth = Math.Min(preferredLandscapeWidth, safeAvailableWidth);
                double fallbackHeight = Math.Min(safeAvailableHeight, fallbackWidth * 9d / 16d);
                return new Size(Math.Max(0d, fallbackWidth), Math.Max(0d, fallbackHeight));
            }

            double widthLimit = safeAvailableWidth;
            if (naturalVideoWidth >= naturalVideoHeight && preferredLandscapeWidth > 0d)
            {
                widthLimit = Math.Min(widthLimit, preferredLandscapeWidth);
            }

            double scale = Math.Min(
                widthLimit / naturalVideoWidth,
                safeAvailableHeight / naturalVideoHeight
            );
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0d)
            {
                return new Size(0d, 0d);
            }

            return new Size(
                Math.Max(0d, Math.Floor(naturalVideoWidth * scale)),
                Math.Max(0d, Math.Floor(naturalVideoHeight * scale))
            );
        }

        internal static bool ShouldAttachManualPlayerResizeHook(
            bool isManualPlayerResizeHookRegistered
        )
        {
            return !isManualPlayerResizeHookRegistered;
        }

        internal static bool ShouldUpdateManualPlayerViewportOnResize(Visibility playerAreaVisibility)
        {
            return playerAreaVisibility == Visibility.Visible;
        }

        private void EnsureManualPlayerResizeHook()
        {
            if (!ShouldAttachManualPlayerResizeHook(_isManualPlayerResizeHookRegistered))
            {
                return;
            }

            // manual player を見せている間だけ resize を拾い、viewport 再計算を流す。
            SizeChanged += ManualPlayerHost_SizeChanged;
            _isManualPlayerResizeHookRegistered = true;
        }

        private void ReleaseManualPlayerResizeHook()
        {
            if (!_isManualPlayerResizeHookRegistered)
            {
                return;
            }

            SizeChanged -= ManualPlayerHost_SizeChanged;
            _isManualPlayerResizeHookRegistered = false;
        }

        private void ManualPlayerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!ShouldUpdateManualPlayerViewportOnResize(PlayerArea?.Visibility ?? Visibility.Collapsed))
            {
                return;
            }

            UpdateManualPlayerViewport();
        }

        private void UpdateManualPlayerViewport()
        {
            if (uxVideoPlayer == null || PlayerArea == null || PlayerController == null)
            {
                return;
            }

            double controllerHeight = PlayerController.ActualHeight > 1d
                ? PlayerController.ActualHeight
                : ManualPlayerFallbackControllerHeight;
            double availableWidth = Math.Max(0d, ActualWidth - ManualPlayerHorizontalPadding);
            double availableHeight = Math.Max(
                0d,
                ActualHeight - ManualPlayerVerticalPadding - controllerHeight
            );
            Size viewportSize = ResolveManualPlayerViewportSize(
                availableWidth,
                availableHeight,
                uxVideoPlayer.NaturalVideoWidth,
                uxVideoPlayer.NaturalVideoHeight
            );
            if (viewportSize.Width <= 0d || viewportSize.Height <= 0d)
            {
                return;
            }

            // 動画面と操作バーの横幅を揃え、縦動画でも画面内へ収める。
            uxVideoPlayer.Width = viewportSize.Width;
            uxVideoPlayer.Height = viewportSize.Height;
            PlayerArea.Width = viewportSize.Width;
            PlayerController.Width = viewportSize.Width;
        }

        private void CloseManualPlayerOverlay()
        {
            // overlay を閉じたら resize hook も外し、次回表示まで余計な再計算を止める。
            ReleaseManualPlayerResizeHook();
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Stop();
            IsPlaying = false;
            StopDispatcherTimerSafely(timer, nameof(timer));
        }

        private bool TryHandleManualPlayerShortcut(KeyEventArgs e)
        {
            if (
                e == null
                || e.Key != Key.Escape
                || PlayerArea?.Visibility != Visibility.Visible
            )
            {
                return false;
            }

            CloseManualPlayerOverlay();
            e.Handled = true;
            return true;
        }

        /// <summary>
        /// ボリュームスライダー調整！音量もテンションも、今の気分に合わせて自由自在だ！🔊
        /// </summary>
        private void UxVolumeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;
            if (uxVolume != null)
            {
                uxVolume.Text = ((int)(uxVideoPlayer.Volume * 100)).ToString();
            }
        }

        /// <summary>
        /// 最高の瞬間を切り取れ！キャプチャボタンで現在のフレームをバシッとサムネイル化するぜ！📸✨
        /// </summary>
        private async void Capture_Click(object sender, RoutedEventArgs e)
        {
            //QueueObj 作って、サムネ作成する。どのパネルか、秒数はどこか、差し替える画像はどれか。
            //その辺は、サムネ作成側の処理で判断。

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            StopDispatcherTimerSafely(timer, nameof(timer));
            uxVideoPlayer.Pause();

            QueueObj queueObj = new()
            {
                MovieId = mv.Movie_Id,
                MovieFullPath = mv.Movie_Path,
                Hash = mv.Hash,
                Tabindex = GetCurrentThumbnailActionTabIndex(),
                ThumbPanelPos = manualPos,
                ThumbTimePos = (int)uxVideoPlayer.Position.TotalSeconds,
            };
            CloseManualPlayerOverlay();

            try
            {
                await Task.Delay(10);
                await CreateThumbAsync(queueObj, true, default);
            }
            catch (Exception ex)
            {
                string message = ResolveManualThumbnailCaptureFailureMessage(ex);
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"manual capture failed: movie='{queueObj.MovieFullPath}', tab={queueObj.Tabindex}, reason='{message}'"
                );
                MessageBox.Show(
                    message,
                    "サムネイル取得に失敗",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        // manual 取得失敗は英語の内部理由を、そのままではなく操作に沿った文面へ寄せる。
        internal static string ResolveManualThumbnailCaptureFailureMessage(Exception ex)
        {
            string rawReason = ex switch
            {
                ThumbnailCreateFailureException failureEx
                    when !string.IsNullOrWhiteSpace(failureEx.FailureReason) =>
                    failureEx.FailureReason,
                _ => ex?.Message ?? "",
            };

            if (
                string.Equals(
                    rawReason,
                    "manual target thumbnail does not exist",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "手動サムネイル取得は既存サムネイルの差し替えです。先に通常のサムネイルを作成してください。";
            }

            if (
                string.Equals(
                    rawReason,
                    "manual source thumbnail metadata is missing",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "既存サムネイルの情報を読めないため、手動サムネイル取得を続行できませんでした。通常サムネイルを再作成してからやり直してください。";
            }

            if (ex is TimeoutException)
            {
                return "サムネイル取得が時間内に完了しませんでした。動画が重い可能性があります。";
            }

            if (!string.IsNullOrWhiteSpace(rawReason))
            {
                return $"手動サムネイル取得に失敗しました。\n{rawReason}";
            }

            return "手動サムネイル取得に失敗しました。";
        }

        private async void ManualThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            int msec = 0;
            if (sender is MenuItem senderObj)
            {
                if (senderObj.Name == "ManualThumbnail")
                {
                    msec = GetPlayPosition(GetCurrentThumbnailActionTabIndex(), mv, ref manualPos);
                }
            }

            //動画ファイルの指定
            uxVideoPlayer.Source = new Uri(mv.Movie_Path);
            await Task.Delay(1000);

            //動画の再生
            uxVideoPlayer.Volume = 0;
            uxVideoPlayer.Play();

            //再生位置の移動
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, msec);
            //uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;
            uxVideoPlayer.Pause();
            await Task.Delay(100);
            IsPlaying = false;
            EnsureManualPlayerResizeHook();
            PlayerArea.Visibility = Visibility.Visible;
            uxVideoPlayer.Visibility = Visibility.Visible;
            PlayerController.Visibility = Visibility.Visible;
            UpdateManualPlayerViewport();
            uxTimeSlider.Focus();

            TryStartDispatcherTimer(timer, nameof(timer));
        }

        private void FR_Click(object sender, RoutedEventArgs e)
        {
            var tempSlider = (int)uxTimeSlider.Value - 100;
            if (tempSlider < 0)
            {
                tempSlider = 0;
            }
            FF_FR(tempSlider);
        }

        private void FF_Click(object sender, RoutedEventArgs e)
        {
            var tempSlider = (int)uxTimeSlider.Value + 100;
            if (tempSlider > uxTimeSlider.Maximum)
            {
                tempSlider = (int)uxTimeSlider.Maximum;
            }
            FF_FR(tempSlider);
        }

        private void FF_FR(int tempSlider)
        {
            uxTimeSlider.Value = tempSlider;
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, tempSlider);
            uxTime.Text = uxVideoPlayer.Position.ToString()[..8];
        }

        /// <summary>
        /// ユーザーがスライダーを掴んでいない時は、動画の再生位置に合わせてスライダーを自動で追従させる滑らか処理！🏄‍♂️
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isTimeSliderDragging)
            {
                _isTimeSliderSyncingFromPlayer = true;
                try
                {
                    uxTimeSlider.Value = uxVideoPlayer.Position.TotalMilliseconds;
                    uxTime.Text = uxVideoPlayer.Position.ToString()[..8];
                }
                finally
                {
                    _isTimeSliderSyncingFromPlayer = false;
                }
            }
        }

        private void UxTimeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isTimeSliderDragging = true;
        }

        private void UxTimeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CommitTimeSliderSeek();
        }

        private void UxTimeSlider_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CommitTimeSliderSeek();
        }

        // スライダー操作の確定時だけ再生位置へ反映し、相互更新ループを防ぐ。
        private void CommitTimeSliderSeek()
        {
            if (!_isTimeSliderDragging)
            {
                return;
            }

            _isTimeSliderDragging = false;

            TimeSpan nextPosition = TimeSpan.FromMilliseconds(uxTimeSlider.Value);
            uxVideoPlayer.Position = nextPosition;
            uxTime.Text = nextPosition.ToString()[..8];
            _lastSliderTime = DateTime.Now;
        }
    }
}
