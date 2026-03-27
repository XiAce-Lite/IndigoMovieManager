using System.Data.SQLite;
using System.Globalization;
using System.Threading.Channels;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public class ThumbnailQueuePersisterSessionTests
{
    [Test]
    public async Task Persister_古いセッション印の要求を破棄する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-persister-session-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        Channel<QueueRequest> channel = Channel.CreateUnbounded<QueueRequest>();
        ThumbnailQueuePersister persister = new(
            channel.Reader,
            batchWindowMs: 100,
            shouldPersistRequest: request =>
                request != null && request.MainDbSessionStamp == 2
        );

        try
        {
            channel.Writer.TryWrite(
                new QueueRequest
                {
                    MainDbFullPath = mainDbPath,
                    MainDbSessionStamp = 1,
                    MoviePath = @"C:\movie\old.mp4",
                    MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\old.mp4"),
                    TabIndex = 0,
                }
            );
            channel.Writer.TryWrite(
                new QueueRequest
                {
                    MainDbFullPath = mainDbPath,
                    MainDbSessionStamp = 2,
                    MoviePath = @"C:\movie\current.mp4",
                    MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\current.mp4"),
                    TabIndex = 0,
                }
            );

            using CancellationTokenSource cts = new();
            Task runTask = persister.RunAsync(cts.Token);
            await Task.Delay(300);
            cts.Cancel();
            await runTask;

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM ThumbnailQueue
WHERE MoviePath = @MoviePath;";
            command.Parameters.AddWithValue("@MoviePath", @"C:\movie\current.mp4");
            int count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            Assert.That(count, Is.EqualTo(1));

            command.Parameters.Clear();
            command.CommandText = @"
SELECT COUNT(1)
FROM ThumbnailQueue
WHERE MoviePath = @MoviePath;";
            command.Parameters.AddWithValue("@MoviePath", @"C:\movie\old.mp4");
            int oldCount = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            Assert.That(oldCount, Is.EqualTo(0));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 一時DB削除失敗はテスト結果に影響しないため握る。
        }
    }
}
