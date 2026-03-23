# AI向け レビュー結果 Q6b RescueReservationReflectionDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `MissingThumbnailRescuePolicyTests` の旧 `ReleaseMissingThumbnailRescueWindowReservation` reflection 依存を外し、現行 `TryReserveMissingThumbnailRescueWindow(...)` 契約へ寄せた
- clean worktree では対象テスト `62` 件成功、レビュー専任役 `findings なし` を確認した
- main 側の同一ファイルは dirty だったが、accepted blob だけを index に載せて本線 commit `ec26b36b4597f9a15941b38e79c48d8030998c71` で取り込んだ

## 1. 対象

- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`

## 2. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q6b-rescue-reservation`

## 3. 着地

- 旧テスト
  - `ReleaseMissingThumbnailRescueWindowReservation_defer_drop時は同じ予約だけ巻き戻せる`
  - `ReleaseMissingThumbnailRescueWindowReservation_新しい予約は巻き戻さない`
  を削除した
- 新テスト
  - `TryReserveMissingThumbnailRescueWindow_同一scopeは最小間隔内なら再予約できずnextInを返す`
  - `TryReserveMissingThumbnailRescueWindow_最小間隔を超えたら再予約できる`
  へ置き換えた
- `InvokeVoid(...)` は不要になったため削除した

## 4. 根拠

- `Watcher/MainWindow.Watcher.cs`
  - `TryReserveMissingThumbnailRescueWindow(...)` は存在する
  - `ReleaseMissingThumbnailRescueWindowReservation(...)` は存在しない
- `12dea5e Watch coordinator の stale guard と deferred 制御を整える`
  - release 本体と defer/drop 時の巻き戻し呼び出しが削除されている
- 現仕様は `MissingThumbnailRescueMinInterval = 60秒` の時刻ベース throttle

## 5. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MissingThumbnailRescuePolicyTests"`
  - 成功
  - `62` 件合格
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~MissingThumbnailRescuePolicyTests"`
  - 成功
  - `62` 件合格
- `git diff --check`
  - 成功

## 6. レビュー結果

- レビュー専任役
  - `findings なし`
- 調整役判断
  - 受け入れ

## 7. 本線取り込み結果

- clean accepted commit
  - `dabfd9f3fc6fca551904c4e19082c690b69832bf`
  - `rescue reservation回帰テストを現仕様へ寄せる`
- 本線 commit
  - `ec26b36b4597f9a15941b38e79c48d8030998c71`
  - `rescue reservation回帰テストを現仕様へ寄せる`
- 取り込み方法
  - main 側の同一ファイルは dirty だったため、accepted blob だけを index に載せて index-only でコミットした

## 8. 残留注意

- main worktree には同じ `MissingThumbnailRescuePolicyTests.cs` の後続 dirty がまだ残っている
- これは `Q6b` とは別帯であり、今回の commit には混ぜていない
