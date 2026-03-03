# Implementation Plan_FileIndexProvider_UI切替_AB差分テスト_2026-03-03

## 1. 目的
- 共通設定UIに `FileIndexProvider` 設定を追加し、`everything` / `everythinglite` を手動切替可能にする。
- `everything` と `everythinglite` のA/B差分テストを追加し、`件数` / `reason` / `strategy` の互換性を継続検証できる状態にする。

## 2. スコープ
- 対象: `IndigoMovieManager_fork`
- 対象機能:
  - 設定画面（`CommonSettingsWindow`）へのプロバイダ選択UI追加
  - 設定保存ロジックへの `FileIndexProvider` 反映
  - Provider差分テスト（NUnit）追加
- 非対象:
  - `EverythingLiteProvider` の性能最適化
  - `FileIndexProvider` の動的切替（再起動なし即時反映）

## 3. 実装計画（タスクリスト）
### 3.1 UI切替（設定画面）
- [x] `CommonSettingsWindow.xaml` に「検索プロバイダ」ComboBoxを追加する。
- [x] 選択肢を `everything` / `everythinglite` の2値に固定する。
- [x] ヘルプ文言に「変更は次回監視開始時に有効（再起動推奨）」を明記する。

### 3.2 設定保存ロジック
- [x] `CommonSettingsWindow.xaml.cs` の `OnClosing` で `FileIndexProvider` を保存する。
- [x] 未選択/未知値は `everything` に丸める。
- [x] 保存後に `Properties.Settings.Default.Save()` で永続化する。

### 3.3 Provider選択責務の明確化
- [x] `Watcher/FileIndexProviderFactory.cs` に正規化ロジックを公開メソッド化する。
- [x] UI側の丸めとFactory側の丸めを同一ルールに統一する。
- [x] `everything` を既定値として後方互換を維持する。

### 3.4 A/B差分テスト追加
- [x] `Tests/IndigoMovieManager_fork.Tests` に差分テストファイルを追加する。
- [x] 比較観点1: `CollectMoviePaths` の返却件数差分（許容差を定義）を検証する。
- [x] 比較観点2: `reason` をカテゴリ比較（`ok:*`, `*_error:*`, `*_not_available`）で検証する。
- [x] 比較観点3: `IndexProviderFacade` 経由の `strategy` が期待値（`everything` or `filesystem`）と一致することを検証する。
- [x] `Everything` 実行環境がない場合は `Inconclusive/Skip` で判定不能を明示する。

### 3.5 ドキュメント更新
- [x] `Watcher/Everything_reason_code契約_2026-03-03.md` に差分テスト観点との対応を追記する。
- [x] `MyLab/docs/EverythingLite_汎用スイッチ可能化プラン_2026-03-03.md` に本計画への参照を追記する。

## 4. 受け入れ基準
- 設定画面で `FileIndexProvider` を選択・保存できる。
- 保存した値が次回起動時に `FileIndexProviderFactory` へ反映される。
- A/B差分テストが追加され、ローカル実行で成功または妥当なスキップ理由を返す。
- 既存テストを壊さない。

## 5. 実行順序（推奨）
1. UI追加
2. 保存ロジック追加
3. Factory正規化の共通化
4. A/B差分テスト追加
5. ドキュメント更新
6. ビルド・テスト実行

## 6. 実行コマンド
- ビルド:
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" .\IndigoMovieManager_fork.sln /restore /t:Build /p:Configuration=Debug /m`
- テスト:
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" .\Tests\IndigoMovieManager_fork.Tests\bin\Debug\net8.0-windows\IndigoMovieManager_fork.Tests.dll`

## 7. リスクと対策
- リスク: `Everything` 非導入環境でA/Bテストが不安定化する。
- 対策: `CheckAvailability()` を前提に `Skip/Inconclusive` へ分岐する。
- リスク: UI保存値の揺れで未知値が混入する。
- 対策: UI保存時とFactory読込時の両方で `everything` へ丸める。

## 8. 実施ログ（2026-03-04）
### 8.1 完了
- `CommonSettingsWindow.xaml` に `FileIndexProvider` 選択UIを追加した。
- `CommonSettingsWindow.xaml.cs` で `FileIndexProvider` の初期表示と保存処理を追加した。
- `Watcher/FileIndexProviderFactory.cs` に `NormalizeProviderKey` を追加し、UI保存時とFactory読込時の丸めルールを共通化した。
- `Tests/IndigoMovieManager_fork.Tests/FileIndexProviderAbDiffTests.cs` を追加し、A/B比較の `件数` / `reasonカテゴリ` / `strategy` を検証可能にした。

### 8.2 未完了
- なし

### 8.3 検証結果
- ビルド: 成功（警告0 / エラー0）
- テスト: 合計36件中、成功34 / スキップ2 / 失敗0
  - スキップ内訳:
    - `IMM_BENCH_INPUT` 未設定ベンチ
    - `Everything` 側件数が環境依存で比較不能なA/B件数比較

### 8.4 追試結果（2026-03-04）
- ビルド:
  - `IndigoMovieManager_fork.sln` を `/restore /t:Build /p:Configuration=Debug /m` で実行し成功（エラー0）
  - 警告:
    - `MSB3026`（`testhost` によるDLLロックのためコピー再試行）
    - `NETSDK1206`（`SQLitePCLRaw.lib.e_sqlite3` のRID警告）
- テスト:
  - `dotnet test --filter "FullyQualifiedName~EverythingLiteProviderTests|FullyQualifiedName~FileIndexProviderAbDiffTests"` を実行
  - 結果: 成功4 / スキップ1 / 失敗0

### 8.5 CI組み込み（2026-03-04）
- `scripts/run_fileindex_ab_ci.ps1` を追加し、A/B差分のビルド+テストを1コマンドで実行可能にした。
  - 実行内容:
    - MSBuildで `IndigoMovieManager_fork.sln` を `Debug|x64` ビルド
    - `dotnet test` で以下フィルタを実行
      - `EverythingLiteProviderTests`
      - `FileIndexProviderAbDiffTests`
      - `FileIndexReasonTableTests`
- `.github/workflows/fileindex-ab-tests.yml` を追加し、PR/手動実行でA/B差分テストを実行するCIを定義した。
  - `IndigoMovieManager_fork` と `MyLab` を同時checkoutして、既存 `ProjectReference (..\MyLab\EverythingLite)` 構成を維持。
