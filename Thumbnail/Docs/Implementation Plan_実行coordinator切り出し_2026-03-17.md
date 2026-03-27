# ThumbnailCreationService 実行coordinator切り出し

更新日: 2026-03-17

## 背景

- `ThumbnailCreationService` の中央には engine 実行ループが残っていた
- ここには次の判断が混在していた
  - engine 順次実行
  - `ffmpeg1pass` skip
  - `autogen` retry
  - near-black reject
  - engine error 蓄積

service 本体の責務としては重すぎるため、実行ループを coordinator 化する

## 今回の方針

1. 実行ループを `ThumbnailEngineExecutionCoordinator` へ移す
2. 戻り値は `result / processEngineId / engineErrorMessages` をまとめた outcome にする
3. `ThumbnailCreationService` は
   - context 構築
   - placeholder / marker / cache 後処理
   を主に持つ形へ寄せる

## 変更点

### 1. `ThumbnailEngineExecutionCoordinator`

- engine order を順に実行
- `ffmpeg1pass` skip を判断
- `autogen` retry を実行
- near-black jpg を reject
- engine error を蓄積

をここへ集約した

### 2. `ThumbnailEngineExecutionOutcome`

- `Result`
- `ProcessEngineId`
- `EngineErrorMessages`

をまとめて返す専用型を追加した

### 3. `ThumbnailCreationService`

- engine ループ本体を削除
- coordinator 呼び出しに差し替え
- placeholder / marker / duration cache 後処理は既存位置に残した

## テスト

- `ThumbnailEngineExecutionCoordinatorTests`
  - 既知破損シグネチャ後の `ffmpeg1pass` skip
  - `autogen` transient failure の統計記録と失敗返却

## 残り

- `ThumbnailCreationService` にはまだ
  - precheck 群
  - context 構築
  - placeholder / marker 後処理
  が残っている
- 次段では `context builder` か `result finalizer` を外すと、service はさらに読みやすくなる
