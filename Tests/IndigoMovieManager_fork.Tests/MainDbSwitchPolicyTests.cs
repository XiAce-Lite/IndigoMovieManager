using NUnit.Framework;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.QueuePipeline;

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
    }
}
