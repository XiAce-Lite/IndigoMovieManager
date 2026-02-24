using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;

namespace IndigoMovieManager.Thumbnail.Test
{
    // ダミークラス (テストで実装の動きを模倣)
    public class ThumbnailQueuePersister
    {
        private readonly QueueDbService _dbService;
        private readonly ChannelReader<QueueRequest> _reader;

        public ThumbnailQueuePersister(QueueDbService dbService, ChannelReader<QueueRequest> reader)
        {
            _dbService = dbService;
            _reader = reader;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var batch = new List<QueueRequest>();

                while (await _reader.WaitToReadAsync(cancellationToken))
                {
                    while (_reader.TryRead(out var req))
                    {
                        batch.Add(req);
                    }

                    if (batch.Count > 0)
                    {
                        _dbService.UpsertBatch(batch);
                        batch.Clear();
                    }

                    // 短い待機でバッチの単位を形成
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Instant Exit: Cancelled expectedly.
            }
        }
    }

    [TestFixture]
    public class ThumbnailQueuePersisterTests
    {
        private string testDbPath;
        private string connectionString;
        private QueueDbService dbService;

        [SetUp]
        public void Setup()
        {
            testDbPath = Path.Combine(Path.GetTempPath(), $"TestPersisterDb_{Guid.NewGuid()}.db");
            connectionString = $"Data Source={testDbPath};Version=3;";
            QueueDbSchema.EnsureSchemaCreated(connectionString);
            dbService = new QueueDbService(connectionString);
        }

        [TearDown]
        public void Teardown()
        {
            if (File.Exists(testDbPath))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    File.Delete(testDbPath);
                }
                catch
                { /* Ignore */
                }
            }
        }

        [Test]
        public async Task StartAsync_DrainsChannel_AndWritesToDb()
        {
            // Arrange (Phase 2)
            var channel = Channel.CreateUnbounded<QueueRequest>();
            var persister = new ThumbnailQueuePersister(dbService, channel.Reader);
            var cts = new CancellationTokenSource();

            // 1. プロデューサがチャンネルに素早く書き込む (No Blocking)
            channel.Writer.TryWrite(
                new QueueRequest
                {
                    MainDbPath = "DB",
                    MoviePath = "A",
                    TabIndex = 1,
                }
            );
            channel.Writer.TryWrite(
                new QueueRequest
                {
                    MainDbPath = "DB",
                    MoviePath = "B",
                    TabIndex = 2,
                }
            );

            // Act
            // 2. Persister をバックグラウンドで開始
            var persisterTask = persister.StartAsync(cts.Token);

            // 3. バッチ処理が完了する程度の短い時間待機 (100ms周期 + 余裕分)
            await Task.Delay(300);

            // 4. 即停止（即終了ポリシーのテストも兼ねる）
            cts.Cancel();
            await persisterTask;

            // Assert
            // 5. 書き込まれたか DB をチェック
            using (var conn = new System.Data.SQLite.SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM ThumbnailQueue;";
                    long count = (long)cmd.ExecuteScalar();
                    Assert.That(count, Is.EqualTo(2));
                }
            }
        }
    }
}
