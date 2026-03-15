using NUnit.Framework;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using System.IO;

namespace IndigoMovieManager_fork.Tests
{
    [TestFixture]
    public class MainDbSwitchPolicyTests
    {
        [Test]
        public void 古いセッション印のQueueRequestは受け付けない()
        {
            QueueRequest request = new()
            {
                MainDbFullPath = @"C:\db\old.wb",
                MainDbSessionStamp = 3,
                MoviePath = @"C:\movie\a.mp4",
                TabIndex = 1,
            };

            bool actual = MainWindow.IsQueueRequestAcceptedForSession(request, currentSessionStamp: 4);

            Assert.That(actual, Is.False);
        }

        [Test]
        public void 現在セッション印のQueueRequestだけ受け付ける()
        {
            QueueRequest request = new()
            {
                MainDbFullPath = @"C:\db\current.wb",
                MainDbSessionStamp = 7,
                MoviePath = @"C:\movie\b.mp4",
                TabIndex = 2,
            };

            bool actual = MainWindow.IsQueueRequestAcceptedForSession(request, currentSessionStamp: 7);

            Assert.That(actual, Is.True);
        }

        [Test]
        public void タブ切替用のQueueClearでは進捗Runtimeをリセットしない()
        {
            Assert.That(
                MainWindow.ShouldResetThumbnailProgressOnQueueClear(
                    MainWindow.ThumbnailQueueClearScope.DebounceOnly
                ),
                Is.False
            );
            Assert.That(
                MainWindow.ShouldResetThumbnailProgressOnQueueClear(
                    MainWindow.ThumbnailQueueClearScope.FullReset
                ),
                Is.True
            );
        }

        [Test]
        public void UI起点の別DB切り替えでは旧表示状態を保存する()
        {
            bool actual = MainWindow.ShouldPersistCurrentDbViewStateBeforeSwitch(
                @"C:\db\a.wb",
                @"C:\db\b.wb",
                MainWindow.MainDbSwitchSource.RecentMenu
            );

            Assert.That(actual, Is.True);
        }

        [Test]
        public void 同一DBへの切り替えでは旧表示状態を保存しない()
        {
            bool actual = MainWindow.ShouldPersistCurrentDbViewStateBeforeSwitch(
                @"C:\db\a.wb",
                @"c:/db/a.wb",
                MainWindow.MainDbSwitchSource.OpenDialog
            );

            Assert.That(actual, Is.False);
        }

        [Test]
        public void 起動時自動オープンでは旧表示状態を保存しない()
        {
            bool actual = MainWindow.ShouldPersistCurrentDbViewStateBeforeSwitch(
                @"C:\db\a.wb",
                @"C:\db\b.wb",
                MainWindow.MainDbSwitchSource.StartupAutoOpen
            );

            Assert.That(actual, Is.False);
        }

        [Test]
        public void 起動時自動オープンではRecentとLastDocを更新しない()
        {
            Assert.That(
                MainWindow.ShouldUpdateRecentFilesOnSuccessfulDbSwitch(
                    MainWindow.MainDbSwitchSource.StartupAutoOpen
                ),
                Is.False
            );
            Assert.That(
                MainWindow.ShouldRememberLastDocOnSuccessfulDbSwitch(
                    MainWindow.MainDbSwitchSource.StartupAutoOpen
                ),
                Is.False
            );
        }

        [Test]
        public void UI起点ではメニューを閉じる()
        {
            Assert.That(
                MainWindow.ShouldCloseMainMenuBeforeDbSwitch(MainWindow.MainDbSwitchSource.New),
                Is.True
            );
            Assert.That(
                MainWindow.ShouldCloseMainMenuBeforeDbSwitch(
                    MainWindow.MainDbSwitchSource.OpenDialog
                ),
                Is.True
            );
            Assert.That(
                MainWindow.ShouldCloseMainMenuBeforeDbSwitch(
                    MainWindow.MainDbSwitchSource.RecentMenu
                ),
                Is.True
            );
        }

        [Test]
        public void 別DBへの切り替え成功時だけ旧Pending削除対象にする()
        {
            bool actual = MainWindow.ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
                @"C:\db\old.wb",
                @"C:\db\new.wb"
            );

            Assert.That(actual, Is.True);
        }

        [Test]
        public void 同一DBまたは空パスでは旧Pending削除対象にしない()
        {
            Assert.That(
                MainWindow.ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
                    @"C:\db\a.wb",
                    @"c:/db/a.wb"
                ),
                Is.False
            );
            Assert.That(
                MainWindow.ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
                    "",
                    @"C:\db\b.wb"
                ),
                Is.False
            );
        }

        [Test]
        public void ダイアログ初期フォルダは保存済みを最優先する()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string savedDirectory = Path.Combine(root, "saved");
            string whiteBrowserDirectory = Path.Combine(root, "wb");
            string appBaseDirectory = Path.Combine(root, "app");

            Directory.CreateDirectory(savedDirectory);
            Directory.CreateDirectory(whiteBrowserDirectory);
            Directory.CreateDirectory(appBaseDirectory);

            try
            {
                string actual = MainWindow.ResolveMainDbDialogInitialDirectory(
                    savedDirectory,
                    whiteBrowserDirectory,
                    appBaseDirectory
                );

                Assert.That(actual, Is.EqualTo(savedDirectory));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void ダイアログ初期フォルダは未保存時にWhiteBrowser相当へ寄せる()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string whiteBrowserDirectory = Path.Combine(root, "wb");
            string appBaseDirectory = Path.Combine(root, "app");

            Directory.CreateDirectory(whiteBrowserDirectory);
            Directory.CreateDirectory(appBaseDirectory);

            try
            {
                string actual = MainWindow.ResolveMainDbDialogInitialDirectory(
                    "",
                    whiteBrowserDirectory,
                    appBaseDirectory
                );

                Assert.That(actual, Is.EqualTo(whiteBrowserDirectory));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void ダイアログ初期フォルダはWhiteBrowser不在時にexe相当へ戻す()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string missingWhiteBrowserDirectory = Path.Combine(root, "missing-wb");
            string appBaseDirectory = Path.Combine(root, "app");

            Directory.CreateDirectory(appBaseDirectory);

            try
            {
                string actual = MainWindow.ResolveMainDbDialogInitialDirectory(
                    "",
                    missingWhiteBrowserDirectory,
                    appBaseDirectory
                );

                Assert.That(actual, Is.EqualTo(appBaseDirectory));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void ダイアログ保存フォルダは選択ファイルの親を返す()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string targetDirectory = Path.Combine(root, "db");
            string targetFile = Path.Combine(targetDirectory, "maimai.wb");

            Directory.CreateDirectory(targetDirectory);

            try
            {
                string actual = MainWindow.ExtractMainDbDialogDirectory(targetFile);

                Assert.That(actual, Is.EqualTo(targetDirectory));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
