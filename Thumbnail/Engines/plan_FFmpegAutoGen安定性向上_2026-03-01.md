# FFmpeg.AutoGen 安定性向上 計画書（2026-03-01）

対象:
- `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs`
- `Thumbnail/ThumbnailCreationService.cs`
- `Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs`
- `Thumbnail/Test/AutogenRegressionTests.cs`

## 1. コードレビュー結果（重大度順）

### [High] 初期化失敗がコンストラクタ例外で伝播し、エンジン生成自体が落ちる
- 箇所: `FfmpegAutoGenThumbnailGenerationEngine.cs` L25-L28, L30-L59
- 症状:
  - `EnsureFfmpegInitialized()` がコンストラクタで即実行される。
  - DLL不整合や不足時に例外が上位へ伝播し、`ThumbnailCreationService` の生成まで巻き込む。
- 影響:
  - フォールバック以前にサービス初期化が失敗し、可用性が下がる。

### [High] `EAGAIN` の扱いが誤りで、デコード継続できず失敗率を上げる
- 箇所: `FfmpegAutoGenThumbnailGenerationEngine.cs` L289-L295, L465-L471
- 症状:
  - `avcodec_receive_frame` が `EAGAIN` を返すと `break` で内側ループを抜ける。
  - 本来は「追加パケット投入が必要」なので継続処理が必要。
- 影響:
  - `No frames decoded` の誤失敗が増える。

### [Medium] `Bitmap` の解放が正常系寄りで、例外時リーク余地がある
- 箇所: `FfmpegAutoGenThumbnailGenerationEngine.cs` L248-L327, L338-L345
- 症状:
  - `bitmaps` の `Dispose()` が成功経路の末尾に集中。
  - 途中例外時に未解放が残る。
- 影響:
  - GDIリソース圧迫、長時間運転で不安定化。

### [Medium] `CreateBookmarkInternal` が `OperationCanceledException` を握り潰す
- 箇所: `FfmpegAutoGenThumbnailGenerationEngine.cs` L489-L492
- 症状:
  - `catch` で全例外を `false` 化。
  - キャンセル伝播の契約を壊す。
- 影響:
  - 中断制御が効かず、上位のキャンセル設計と不整合。

### [Low] 使われない変数が残存
- 箇所: `FfmpegAutoGenThumbnailGenerationEngine.cs` L263-L285
- 症状:
  - `frameGot` が参照されない。
- 影響:
  - 可読性低下、警告ノイズ。

## 2. 修正方針

### 方針A: 初期化を「安全な遅延初期化 + 失敗キャッシュ」に変更
- コンストラクタで初期化しない。
- `CreateAsync` / `CreateBookmarkAsync` の入口で `EnsureFfmpegInitializedSafe()` を呼ぶ。
- 初期化失敗時は:
  - 失敗フラグと失敗理由を保持。
  - 以後は即 `failed result` を返し、毎回の再初期化を抑止。
- `CanHandle()` は初期化状態を反映（使えない時は `false`）。

### 方針B: デコードループを FFmpeg セオリーに合わせる
- `avcodec_send_packet` 後は `avcodec_receive_frame` を `while` でドレイン。
- `receive == EAGAIN` は「次パケット投入へ継続（break先を調整）」。
- `receive == EOF` は対象秒失敗として次秒へ。
- `send == EAGAIN` もドレイン不足のシグナルとして扱う。

### 方針C: リソース解放の一本化
- `List<Bitmap> bitmaps` を `try` 外で宣言し、`finally` で全 `Dispose`。
- `ConvertFrameToBitmap` の `LockBits/UnlockBits` を `try/finally` 化。
- `SwsContext` 取得失敗時の明示的エラー化を追加。

### 方針D: キャンセル契約の修正
- `CreateBookmarkInternal` で `OperationCanceledException` は再スロー。
- それ以外のみ `false` へ変換。

### 方針E: ノイズ削減
- 未使用変数 `frameGot` を削除。

## 3. 実装タスク

- [x] T1: 初期化処理を遅延化し、失敗キャッシュ（フラグ+理由）を追加
- [x] T2: `CanHandle()` を実行可能状態判定に変更
- [x] T3: `CreateInternal` の decode loop を `EAGAIN` 正常対応へ改修
- [x] T4: `CreateBookmarkInternal` の decode loop も同方針で改修
- [x] T5: `bitmaps` と `LockBits` の解放保証を `finally` ベースへ改修
- [x] T6: `OperationCanceledException` の再スロー対応
- [x] T7: 未使用変数除去とメッセージ整備

## 4. 回帰テスト計画

### 4.1 既存テストの維持
- `AutogenExecutionFlowTests`:
  - autogen成功時にフォールバックしない
  - autogen失敗時にffmediatoolkitへフォールバック

### 4.2 追加テスト（今回追加対象）
- [x] `AutogenInitializationFailure_IsCached_AndFallsBack`
  - 初回失敗後、同プロセスで再初期化を繰り返さないこと。
- [x] `Autogen_CreateBookmarkAsync_Cancellation_Propagates`
  - ブックマーク生成でキャンセルが上位に伝播すること。
- [x] `Autogen_CanHandle_ReturnsFalse_WhenInitUnavailable`
  - 失敗状態でルーター選定対象から外れること。

### 4.3 実行コマンド
- ビルド:
  - `dotnet build IndigoMovieManager_fork.sln -c Debug`（COM参照で失敗時はMSBuild）
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /p:Configuration=Debug /p:Platform="Any CPU"`
- テスト:
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj --no-build`

## 5. 完了条件（DoD）

- autogen初期化失敗でサービス全体が落ちない。
- `EAGAIN` を含む動画で `No frames decoded` の誤失敗率が下がる。
- 長時間バッチでメモリ/GDIハンドルの増加が頭打ちになる。
- キャンセルが仕様どおり上位へ伝播する。
- 既存回帰 + 追加回帰がすべて通る。
