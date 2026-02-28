# FfmpegAutoGenThumbnailGenerationEngine コードレビュー

対象ファイル: `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs`

## 指摘事項（重大度順）

### [High] 初期化失敗でエンジン生成時に即例外化し、全体処理を巻き込むリスク
* **箇所**: `FfmpegAutoGenThumbnailGenerationEngine` コンストラクタ (L25), `EnsureFfmpegInitialized()` (L30)
* **詳細**: エンジンのインスタンス生成時（コンストラクタ実行時）に、同期的に `EnsureFfmpegInitialized()` を実行しています。ここで FFmpeg の DLL ロード等に失敗して例外がスローされると、インスタンス生成自体が失敗し、呼び出し元（ルーターの初期化全体等）を巻き込んでクラッシュするリスクが高くなっています。
* **対応案**: 初期化処理を初回の利用時まで遅延させる（Lazy化）、もしくは例外ハンドリングを追加して、「初期化失敗状態」としてエンジンを生成し使用不可のステータスを持たせる構成が安全です。

### [High] デコードループの EAGAIN 扱いでフレーム取得失敗を増やす実装
* **箇所**: `CreateInternal` デコードループ (L290), `CreateBookmarkInternal` デコードループ (L466)
* **詳細**: `avcodec_receive_frame` が返す値が `ffmpeg.AVERROR(ffmpeg.EAGAIN)` の場合、現在の実装では `break` によりストリームのフレーム読み込みを終了し、当該秒数の取得試行を打ち切ってしまっています。`EAGAIN` は本来「パケットが不足しているため、新しいパケットを `avcodec_send_packet` で供給してほしい」という意味です。そのため、途中で `break` するとフレーム完了が阻害され、`No frames decoded` に陥る原因になります。
* **対応案**: `EAGAIN` 時はループ内の `break` ではなく、後続の `av_read_frame` やパケット供給を継続できるようにフローを修正（`continue` 扱いに相当）する必要があります。

### [Medium] 取得済み Bitmap の解放漏れリスク
* **箇所**: `CreateInternal` 内の `bitmaps` 管理 (L248, L324)
* **詳細**: サムネイルとして取得した各 `Bitmap` を `bitmaps` リストに保持していますが、これを `Dispose` しているのはメソッドで正常に全ての処理が完了する直前のみです（L324）。途中、たとえば `SaveCombinedThumbnail` （L306）内部等で例外が発生し `catch` に処理が飛んだ場合、すでに取得済みの `Bitmap` インスタンスが解放されず、メモリ（GDI+ リソース）のリークに繋がります。
* **対応案**: `finally` ブロックへ `bitmaps` に格納された全画像の `Dispose()` 処理を移動するか、常に確実に破棄される設計に変更する必要があります。

### [Medium] CanHandle が常に true で、実行不能状態を事前回避できない
* **箇所**: `CanHandle` (L61)
* **詳細**: 初期化の失敗・DLLの不在などによりエンジンが内部的に利用不可能な状態に陥っていても、`CanHandle` が常に `true` を返します。そのため、ルーターからは「実行可能なエンジン」と見なされて渡され、結局内部処理の例外やエラーで失敗するという無駄なフォールバックフローを毎回経由することになります。
* **対応案**: `EnsureFfmpegInitialized` で初期化できたかを示すフラグ（`_isInitialized` かつ正常な状態であるか）を `CanHandle` から返すようにし、無効時は前段で選択されないようにするべきです。

### [Low] 使われない変数が残存
* **箇所**: `CreateInternal` 内の `frameGot` 変数 (L263)
* **詳細**: 変数 `frameGot` に対して `true` の代入を行っています（L284）が、値の参照がどこにもありません。
* **対応案**: ゴミとして残っているだけの変数のため、削除して見通しをよくします。

---

## 補足と今後のステップ
* **テストの必要性**: autogen側の処理はアンマネージド領域のメモリ操作やデコードループを伴うため、回帰テストが薄い現状では実際の動画を通した動作確認が不可欠です。デコードループ修正の後は、複数の動画で抜けなくサムネイルが作成されるか実環境テストが必要です。
