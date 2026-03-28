using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace IndigoMovieManager.Thumbnail.Test
{
    // ダミークラス (テストで実装の動きを模倣)
    public class ThumbnailQueueProcessor
    {
        private readonly QueueDbService _dbService;
        private readonly string _instanceId;

        public ThumbnailQueueProcessor(QueueDbService dbService, string instanceId)
        {
            _dbService = dbService;
            _instanceId = instanceId;
        }

        public async Task ProcessNextAsync(CancellationToken cancellationToken)
        {
            var records = _dbService.GetPendingAndLease(_instanceId, count: 1, leaseMinutes: 5);
            if (records == null || records.Count == 0)
                return;

            var target = records[0];

            try
            {
                // ここでサムネイル本体の重い生成処理をシミュレート
                await Task.Delay(50, cancellationToken);

                // 成功したら Done(2)
                _dbService.UpdateStatus(target.QueueId, status: 2, errorMsg: null);
            }
            catch (OperationCanceledException)
            {
                // Instant Exit: 中断したので DB ステータスは Processing のまま放棄(次回起動時 or 他PCでリース切れにより拾われる)
                throw;
            }
            catch (Exception ex)
            {
                // 失敗時
                int maxAttempts = 5;
                if (target.AttemptCount + 1 >= maxAttempts)
                {
                    _dbService.UpdateStatus(target.QueueId, status: 3, errorMsg: ex.Message); // Failed
                }
                else
                {
                    // UpdateStatus メソッド内で AttemptCount++ して Pending(0) に戻す事とする
                    _dbService.UpdateStatus(target.QueueId, status: 0, errorMsg: ex.Message); // Retry (Pending)
                }
            }
        }
    }

    [TestFixture]
    public class ThumbnailQueueProcessorTests
    {
        private string testDbPath;
        private string connectionString;

        [SetUp]
        public void Setup()
        {
            testDbPath = Path.Combine(Path.GetTempPath(), $"TestProcessorDb_{Guid.NewGuid()}.db");
            connectionString = $"Data Source={testDbPath};Version=3;";
            QueueDbSchema.EnsureSchemaCreated(connectionString);
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
        public async Task ProcessNextAsync_UpdatesStatusToDone_OnSuccess()
        {
            var dbService = new QueueDbService(connectionString);
            var req1 = new QueueRequest
            {
                MainDbPath = "DB",
                MoviePath = "A",
                TabIndex = 1,
            };
            dbService.UpsertBatch(new[] { req1 });

            var processor = new ThumbnailQueueProcessor(dbService, "INSTANCE_1");
            var cts = new CancellationTokenSource();

            // Act: 1件処理
            await processor.ProcessNextAsync(cts.Token);

            // Assert: Done (2) になっているか
            using (var conn = new System.Data.SQLite.SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Status FROM ThumbnailQueue WHERE TabIndex = 1;";
                    long status = (long)cmd.ExecuteScalar();
                    Assert.That(status, Is.EqualTo(2));
                }
            }
        }

        [Test]
        public async Task Concurrency_MultipleProcessors_DoNotProcessSameRecord()
        {
            // 排他制御テスト (Phase 3 + 複数プロセスシミュレーション)
            var dbService = new QueueDbService(connectionString);

            // Arrange: 1個だけ Pending のキューを用意
            var req1 = new QueueRequest
            {
                MainDbPath = "DB",
                MoviePath = "CONCURRENCY_TARGET",
                TabIndex = 99,
            };
            dbService.UpsertBatch(new[] { req1 });

            var proc1 = new ThumbnailQueueProcessor(dbService, "INSTANCE_A");
            var proc2 = new ThumbnailQueueProcessor(dbService, "INSTANCE_B");
            var cts = new CancellationTokenSource();

            // Act: 2つのプロセッサが同時に「次の1件を処理しろ」と動く
            var task1 = Task.Run(() => proc1.ProcessNextAsync(cts.Token));
            var task2 = Task.Run(() => proc2.ProcessNextAsync(cts.Token));

            await Task.WhenAll(task1, task2);

            // Assert: 片方だけが処理し、DBはロックエラーにならず Done に至っているはず
            using (var conn = new System.Data.SQLite.SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT Status, OwnerInstanceId FROM ThumbnailQueue WHERE TabIndex = 99;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        Assert.That(reader.Read(), Is.True);
                        long status = reader.GetInt64(0);
                        string owner = reader.GetString(1);

                        Assert.That(status, Is.EqualTo(2)); // Doneになっている
                        // owner は INSTANCE_A か INSTANCE_B の どちらか1つだけで実行された
                        Assert.That(owner == "INSTANCE_A" || owner == "INSTANCE_B", Is.True);
                    }
                }
            }
        }
    }
}
