# 手動回帰チェック手順（Phase2: サムネイルキュー別プロジェクト化 2026-03-04）

## 1. 目的
- Phase2（キュー処理の別プロジェクト化）後に、投入・処理・進捗表示の導線が退行していないことを確認する。
- 特に「通常キュー」「再投入（実運用上の手動再試行）」「進捗タブ更新」の3点を固定手順で確認する。

## 2. 事前準備
1. 本体をビルドする。  
   `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /m`
2. テストを確認する。  
   `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --no-build`

## 3. 手動チェック手順（6ステップ）
1. 手動回帰ログ採取スクリプトを起動する。  
   `pwsh -File .\Thumbnail\Test\run_queue_e2e_manual.ps1`
2. アプリを起動し、任意タブで通常キュー投入を実行する。  
   例: `等間隔サムネイル作成` または `全ファイルサムネイル再作成`
3. `サムネイル進捗` タブを開き、以下が更新されることを確認する。  
   `作成キュー`, `DB保留件数`, `Thread`, Workerパネル、CPU/GPU/HDDメーター
4. 処理中に再投入を実行し、処理が継続することを確認する。  
   例: 同じタブで `全ファイルサムネイル再作成` を再実行
5. エラーダイアログや即時クラッシュがないことを確認する。
6. スクリプト側へ戻って Enter を押し、`logs\queue-e2e-manual\<timestamp>` に `before_/after_` ログとQueueDBスナップショットが保存されることを確認する。

## 4. 合格条件
- アプリが処理中に停止しない。
- キュー投入後、進捗タブの数値・ワーカーパネルが更新される。
- 再投入後もキュー処理が継続し、サムネイル作成が進む。
- `debug-runtime.log` に `queue-db` / `queue-consumer` / `queue` のログが継続出力される。

## 5. 実施ログ（記入欄）
- [x] 手順1: スクリプト起動
- [x] 手順2: 通常キュー投入
- [x] 手順3: 進捗タブ更新確認
- [x] 手順4: 再投入確認
- [x] 手順5: 安定動作確認
- [x] 手順6: ログ退避確認

### メモ
- 実施日時: 2026-03-04
- 実施者: ユーザー
- 対象MainDB: 実運用DB（ユーザー環境）
- 結果: 合格
- 補足: 通常キュー投入・再投入・進捗タブ更新を確認。
