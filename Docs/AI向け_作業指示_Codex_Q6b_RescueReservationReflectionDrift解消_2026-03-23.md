# AI向け 作業指示 Codex Q6b RescueReservationReflectionDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `MissingThumbnailRescuePolicyTests` が、既に削除された private method `ReleaseMissingThumbnailRescueWindowReservation` を reflection で掴みに行って失敗している
- 現行 runtime は `TryReserveMissingThumbnailRescueWindow(...)` の時刻ベース throttle 契約へ寄っているため、source を戻さず test を再整合する

## 1. 目的

- failing test 2 件を、現行 watcher 契約に沿って解消する
- `ReleaseMissingThumbnailRescueWindowReservation` を source へ戻さず、test を現仕様へ寄せる

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`
- 参照だけ
  - `Watcher/MainWindow.Watcher.cs`

## 3. 現状の根拠

- 現在の source には `ReleaseMissingThumbnailRescueWindowReservation` は存在しない
- `Watcher/MainWindow.Watcher.cs` には
  - `TryReserveMissingThumbnailRescueWindow(string scopeKey, DateTime nowUtc, out TimeSpan nextIn)`
  だけが残っている
- `12dea5e Watch coordinator の stale guard と deferred 制御を整える` で
  - `ReleaseMissingThumbnailRescueWindowReservation(...)` 本体
  - それを呼ぶ `defer/drop` 時の巻き戻し
  が削除されている
- 現行契約は「最終実行時刻で throttle する」であり、「予約を巻き戻して即再試行する」ではない

## 4. 守ること

1. source に `ReleaseMissingThumbnailRescueWindowReservation` を戻さない
2. `Watcher/MainWindow.Watcher.cs` は触らない
3. 変更は `MissingThumbnailRescuePolicyTests.cs` 1 ファイルに閉じる
4. reflection 対象は現行 private method `TryReserveMissingThumbnailRescueWindow` に揃える

## 5. 着地イメージ

- 旧 2 テストは、例えば次のような現行契約テストへ置き換える
  - 同一 scope は最小間隔内なら再予約できず `nextIn > 0`
  - 最小間隔を超えたら再予約できる
- `Release...` という test 名、コメント、reflection 呼び出しは削除する
- 可能なら `MissingThumbnailRescueMinInterval` を跨ぐ時刻差で意図が読めるようにする

## 6. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~MissingThumbnailRescuePolicyTests"`
- `git diff --check`

## 7. 禁止

- private method の名前合わせだけのために runtime を戻すこと
- `Watcher` の挙動変更
- manual rescue popup 側の close reservation と watch rescue throttle を混同すること
