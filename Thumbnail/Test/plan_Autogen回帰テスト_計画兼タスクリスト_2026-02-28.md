# Autogen回帰テスト 計画兼タスクリスト（2026-02-28）

対象:
- `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs`
- `Thumbnail/Engines/ThumbnailEngineRouter.cs`
- `Thumbnail/ThumbnailCreationService.cs`

目的:
- `autogen` を通常サムネイルの第一候補にした変更で、既存挙動が壊れていないことを確認する。
- 失敗時フォールバック経路の退行を防ぐ。
- VS2026環境で本格テストへ移行できるよう、段階的な作業を明確化する。

---

## 1. テスト方針

1. まずは現行の `NUnitMocks` 前提で「回帰ポイントをコード化してビルドで維持」する。
2. 次に、VS2026で本物の `NUnit` 実行環境へ段階移行する。
3. ネイティブDLL依存があるため、`Unit` と `E2E` を分離して安定運用する。

---

## 2. 回帰観点（必須）

1. 通常サムネイルで `autogen` が最初に選択される。
2. `manual` は従来どおり `ffmediatoolkit` 優先を維持する。
3. 環境変数 `IMM_THUMB_ENGINE` の強制指定が最優先される。
4. `autogen` 失敗時のフォールバック順が維持される。
   - 期待順: `autogen -> ffmediatoolkit -> ffmpeg1pass -> opencv`
5. ログ形式（`thumbnail-create-process.csv`）が崩れない。

---

## 3. テストレベル別の実施範囲

### 3.1 Unit（軽量・常時）
- ルーター選択ロジック（`ThumbnailEngineRouter`）
- フォールバック順ロジック（`ThumbnailCreationService` の順序生成）

### 3.2 Integration（中量・PR前）
- `ThumbnailCreationService` を通した1件生成（テスト動画1本）
- `IMM_THUMB_ENGINE=autogen` 強制時の成功/失敗時挙動

### 3.3 E2E（重め・リリース前）
- 実動画セット（絵文字パス含む）での一括生成
- 失敗ケースでのフォールバック確認
- 既存CSVログの列互換確認

### 3.4 実装済みUnitテスト内容（2026-02-28時点）
- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
- `Router_Default_UsesAutogenFirst_ForNormalThumbnail`
  - 通常サムネイル時に `autogen` が第一候補になることを確認する。
- `Router_Manual_TimeSpecified_UsesFfMediaToolkitFirst`
  - 手動サムネイル時は `ffmediatoolkit` が優先されることを確認する。
- `Router_ForcedEngineEnv_Wins`
  - 環境変数 `IMM_THUMB_ENGINE` で強制指定したエンジンが最優先されることを確認する。
- `Service_AutogenSelected_FallbackOrder_IsStable`
  - `autogen -> ffmediatoolkit -> ffmpeg1pass -> opencv` のフォールバック順を維持していることを確認する。

---

## 4. 完了条件（DoD）

1. Unit回帰テストがビルドで通る。
2. Integrationで `autogen` 強制時に少なくとも1件成功する。
3. `autogen` 故障時にフォールバック成功を確認できる。
4. `manual` サムネイルが従来どおり成功する。
5. テスト実行手順をドキュメント化し、再現できる。

---

## 5. タスクリスト

## Phase A: 現行運用（NUnitMocks前提）
- [x] `autogen` 回帰テストコードを追加（選択優先・強制指定・フォールバック順）
  - `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
- [x] テストケースごとの意図コメントを補足（将来の本物NUnit移行用）
- [x] `manual` の期待挙動（時間指定優先）をテスト名に明示

## Phase B: 本格テスト化（VS2026）
- [x] `IndigoMovieManager_fork.Tests` を新規作成
- [x] NuGet導入
  - `Microsoft.NET.Test.Sdk`
  - `NUnit`
  - `NUnit3TestAdapter`
  - `coverlet.collector`（任意）
- [x] `NUnitMocks.cs` を廃止または本番ビルド対象外化
- [x] `AutogenRegressionTests` を新規テストプロジェクトへ移設

## Phase C: Integration/E2E拡張
- [ ] テスト動画（短尺/長尺/絵文字パス）を固定化
- [x] `autogen` 成功ケースの自動検証を追加
  - `Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs`
- [x] `autogen` 初期化失敗を擬似注入してフォールバック検証を追加
  - `Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs`
- [x] `thumbnail-create-process.csv` の列互換テストを追加
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateProcessCsvFormatTests.cs`

## Phase D: CI運用
- [x] `dotnet test` 実行ターゲットを定義（Unitのみ常時）
  - `Thumbnail/Test/run_autogen_regression_tests.ps1`
- [x] 重いE2Eは手動または夜間ジョブに分離
  - `Thumbnail/Test/run_autogen_e2e_manual.ps1`（手動E2E用）
- [x] 失敗時にログ採取（`debug-runtime.log`, `thumbnail-create-process.csv`）を保存
  - `Thumbnail/Test/run_autogen_regression_tests.ps1`（失敗時 `logs/test-failures/` に退避）

---

## 6. テスト実行方法

### 6.1 事前準備
1. リポジトリルート（`IndigoMovieManager_fork`）で実行する。
2. 依存関係を復元する。
   - `dotnet restore`

### 6.2 ビルド（COM参照ありのためMSBuild推奨）
1. アプリ本体とテストプロジェクトをビルドする。
   - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.sln /restore /p:Configuration=Debug /p:Platform="Any CPU"`

### 6.3 Unitテスト（CLI）
1. Autogen回帰テストを含むテストプロジェクトを実行する。
   - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug`
2. Autogen回帰テストだけに絞って実行する場合:
   - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --filter "FullyQualifiedName~AutogenRegressionTests"`
3. 定型実行（MSBuild + 対象テスト実行）:
   - `pwsh -File .\Thumbnail\Test\run_autogen_regression_tests.ps1`
4. 期待結果:
   - 実装済み4件がすべて `Passed` になること。

### 6.4 Unitテスト（Visual Studio 2026）
1. `IndigoMovieManager_fork.sln` を開く。
2. テストエクスプローラーで `AutogenRegressionTests` を検索する。
3. クラス単位または個別ケースで実行する。
4. 期待結果:
   - 4件すべて成功。

### 6.5 Integration/E2E（手動確認の最小手順）
1. 強制モードで `autogen` を試す。
   - PowerShell: `$env:IMM_THUMB_ENGINE = "autogen"`
2. アプリを実行してサムネイル生成を行う。
3. ログ確認:
   - `logs/debug-runtime.log` にエンジン選択ログが出ること。
   - `logs/thumbnail-create-process.csv` に `status=success` 行が追加されること。
4. 検証後は環境変数を戻す。
   - PowerShell: `Remove-Item Env:IMM_THUMB_ENGINE -ErrorAction SilentlyContinue`

### 6.6 重いE2Eの分離実行（手動）
1. 手動E2Eスクリプトを実行する。
   - `pwsh -File .\Thumbnail\Test\run_autogen_e2e_manual.ps1`
2. スクリプトの案内に従ってアプリ側で重い検証（大量ファイル・絵文字パス等）を実施する。
3. 完了後、`logs/e2e-manual/` 配下に `before_*` / `after_*` ログが保存されることを確認する。

### 6.7 失敗時ログ採取
1. Unit定型スクリプトの失敗時、以下が自動保存される。
   - `logs/test-failures/<timestamp>/debug-runtime.log`
   - `logs/test-failures/<timestamp>/thumbnail-create-process.csv`
   - `logs/test-failures/<timestamp>/failure-meta.txt`

---

## 7. リスクと対策

1. ネイティブDLL差異で `autogen` が環境依存失敗する。
   - 対策: 起動時診断ログを追加し、DLLセット検査をテスト化する。
2. `manual` と通常サムネの期待挙動が混線する。
   - 対策: ルーターの分岐条件をテスト名とコメントで固定化する。
3. `NUnitMocks` のままでは「実行保証」が弱い。
   - 対策: Phase B を優先して実施する。
