# FfmpegAutoGenThumbnailGenerationEngine 修正プラン

本事象は、複数スレッド（並列処理）からの同時利用時にリソース枯渇や巻き込みクラッシュを引き起こす可能性が高いため、以下の変更により安全にフォールバック・リソース解放・フレーム取得ができるように修正します。

## 変更の目的とコンテキスト
AutoGenによるエンジン実行時の安定性向上と、メモリリークの解消を行います。特にサムネイル生成は「複数ファイルに対する並列実行」が前提となるため、スレッドセーフな初期化状態の共有と確実なリソース解放に重点を置きます。

## Proposed Changes

### [MODIFY] FfmpegAutoGenThumbnailGenerationEngine.cs
`Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs` を以下の方針で修正します。

1. **[High] 初期化の安全化と遅延評価 (並列スレッド保護)**
   - **内容**: 内部に静的フラグ `_initFailed` を追加し、`EnsureFfmpegInitialized()` 内部で `Ffmpeg.AutoGen` の初期化（依存DLLロード等）を `try-catch` で保護します。
   - **並列処理の考慮**: 万が一どこかのスレッドでロードに失敗した場合、初期化時例外でそのスレッドごとクラッシュするのを防ぐとともに、以降の他スレッドからのアクセスでも「初期化失敗済み」として速やかに諦める（無駄なロックや例外発生を回避する）ようにします。
2. **[Medium] `CanHandle` での事前回避**
   - **内容**: `CanHandle(ThumbnailJobContext)` が呼ばれた際、未初期化であれば `EnsureFfmpegInitialized()` を実行。成功したか（または既に成功しているか）をチェックし、`_initFailed` が `true` の場合は常に `false` を返します。
   - **効果**: エンジンが無効な状態での無駄なルーター経由の実行と実行時エラーの発生を事前回避し、正常な機能（FFMediaToolkit等）へのフォールバックを高速化します。
3. **[High] デコードループの `EAGAIN` 判定修正**
   - **内容**: `CreateInternal` および `CreateBookmarkInternal` 内において、`avcodec_receive_frame` が `EAGAIN` を返した際の `break;` を削除（実質 `continue` 扱い）します。
   - **効果**: パケット不足時に読み取りを早期打ち切りせず、次のパケットを供給して正常にフレームがデコードされるまで継続します。
4. **[Medium] リソース（`Bitmap`等）の確実な解放**
   - **内容**: 取得済みの `Bitmap` を保持する `List<Bitmap> bitmaps` について、`try-finally` ブロックへスコープを広げるか、処理中常に監視できる構造にします。例外やキャンセルで処理が中断された場合でも、未保存の `Bitmap` を全て確実に `Dispose()` します。
   - **効果**: 並列処理中に予期せぬ中断が多数発生した場合の、GDI+ ハンドル枯渇（メモリリーク）をシャットアウトします。
5. **[Low] 未使用変数の削除**
   - **内容**: `frameGot` などの不要なローカル変数を削除し、コードを整理します。

## Verification Plan

### Automated Tests
- コンパイルエラーが発生しないこと（`/turbo-all` または `dotnet build` による確認）。
- `CSharpier` によるフォーマットが維持されているかの確認。

### Manual Verification
- 実機での動画読み込みにて、`autogen` エンジンが優先で生成された際、問題なくサムネイルができあがるかをログと出力ファイルで確認。
- 意図的に FFmpeg の共有 DLL をリネーム等してロードを失敗させた際、アプリが落ちずに `CanHandle = false` となり、スムーズに他のエンジンにフォールバックすることを確認。
