# 📸 サムネイル処理のすべて（爆速サムネ職人の流儀） 🎥

やっほー！このドキュメントは、IndigoMovieManager の心臓部とも言える「サムネイル生成処理」の裏側を、サクッと楽しく理解するためのガイドだよ！✨
これさえ読めば、どうやってあの爆速でサムネが量産されていくのかが丸わかりだぜ！😎

## 1. 今のトレンド（2026-02-25時点の最強スタンス）🔥

- **デコードの主役**: `FFMediaToolkit` を標準採用！こいつがめちゃくちゃ速い！🏎️
- **一発勝負**: フレームを抜くときは「1つの動画につき、開く(`MediaFile.Open`)のは1回だけ」！何度もガチャガチャ開かないのがプロの流儀！🎯
- **GPUの力解放**: 環境変数 `IMM_THUMB_GPU_DECODE=cuda` の時だけ、FFmpegのデコーダオプションをバリバリに有効化！
- **デフォルトON**: GPUデコードは最初から「ON」！余力は全部使っていくスタイル！💪
- **自由な切り替え**: GPUを使いたくない時は、共通設定画面からサクッとOFFにできる親切設計！
- **並列処理の鬼**: 同時に走るスレッド数は共通設定で `1〜24` まで自由自在（デフォは8）！PCの限界まで突き詰めろ！🌋
- **ffmpegコマンド封印**: 速度重視のため、外部プロセスの `ffmpeg` は極力使わない縛りプレイ！

---

## 2. 🧩 役者たち（主要コンポーネント）

- **キューを束ねる者**: `Thumbnail/MainWindow.ThumbnailQueue.cs`
- **容赦なき並列バッチャー**: `Thumbnail/ThumbnailQueueProcessor.cs`
- **サムネを生み出す神**: `Thumbnail/ThumbnailCreationService.cs`
- **メイン画面との架け橋**:
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `MainWindow.xaml.cs`

---

## 3. 🌊 爆速生成フローの全貌

1. 何かしらのキッカケ（トリガー）で `QueueObj` っていうお仕事リストが作成されて、キューに放り込まれる！📦
2. 影の立役者、常駐タスク `CheckThumbAsync` がキューをじーっと見張ってる。👁️
3. 仕事が来たら、`ThumbnailQueueProcessor.RunAsync` がまとめて「ヨシ、並列で一気にやるぞ！」と号令をかける！🗣️
4. それぞれのジョブで `ThumbnailCreationService.CreateThumbAsync`（サムネの神）が降臨！ゴリゴリ生成する！✨
5. 出来上がった画像のパスを `MovieRecords`（DB）にバシッと反映！📝
6. ついでに動画の長さ（Duration）もDBに教えてあげる親切設計！

---

## 4. 🧨 サムネイルのスイッチ（生成トリガー）

### 4.1 キューに乗せるやつ（基本ルート）
- **メニューからの全件再生成**: `MainWindow.MenuActions.cs`
- **ツールボタンからの全件再生成**: `MainWindow.MenuActions.cs`
- **エラーからの復活（選択変更）**: `ThumbDetail` が `error` の時（`MainWindow.Selection.cs`）
- **フォルダ監視で新顔を見つけた時**: `MainWindow.Watcher.cs` 👀
- **手動チェックで新顔を見つけた時**: `MainWindow.Watcher.cs`
- **一定間隔でサムネを再生成する時**: `Thumbnail/MainWindow.ThumbnailCreation.cs`

### 4.2 直通特急（キューを通さず即生成！）
- **プレイヤー画面で「今ここをサムネにして！」って言われた時**: `MainWindow.Player.cs`
- **ブックマーク用にちょっとサムネが欲しい時**: `MainWindow.Player.cs`

---

## 5. 🛡️ 並列の暴力と、無駄な仕事を防ぐ盾

- **並列への渇望 (Parallel Execution)**
  - `Parallel.ForEachAsync` で複数の動画を一気に捌く！
  - 並列数 `ThumbnailParallelism` (1〜24、デフォは8) でCPUを使い切れ！！🔥
- **無駄撃ち禁止 (重複抑止)**
  - キューDBの無敵の鍵 `MoviePathKey + TabIndex` のおかげで、同じ動画の仕事が何個も降ってくるのをブロック！✋
- **ファイル衝突回避**
  - 同じ出力パスに対して同時に書き込もうとして爆発しないように、`SemaphoreSlim` がちゃんと順番待ちさせてるよ！🚦

---

## 6. 🎨 サムネ創世録（CreateThumbAsyncの錬金術）

1. まず保存先を決めて、動画ファイルが本当にあるかチェック！🔍
2. `FFMediaToolkit` 先生が動画を**1回だけ**慎重に開く。
3. 動画の全体時間を測る！（まずは `mediaFile.Info.Duration` に聞き、ダメなら奥の手のShellに頼る！）⏱️
4. 並べるパネルの数（縦×横）に合わせて、**「何秒ごとの場面を切り取るか」**を超正確に計算！🧮
5. その秒数の場面（フレーム）を `TryGetFrame` で引っ張り出し、縦横比を綺麗に整えてリサイズ！🖼️
6. 集めたフレームたちをメモリの上で超高速にくっつけて、1枚の巨大なJPEGとして出力！💥
7. 最後に、画像のお尻に `ThumbInfo` という秘密のメタデータをそっと埋め込む…（これで後から色んな情報がわかる！）🤫

---

## 7. 🎮 GPUの力を使いこなせ！

### 7.1 どうなってるの？（既定値）
- **設定名**: `ThumbnailGpuDecodeEnabled`
- **デフォルト**: `True`（最初から全力のGPUデコード有効！）💪

### 7.2 スイッチはここだ！
- 共通設定画面でいつでもON/OFFを選べるよ！気分に合わせて切り替えてね！
- 裏側は `CommonSettingsWindow.xaml.cs` で制御してるよ。

### 7.3 いつ力が解放されるの？
- アプリ起動時に今の設定を読み取って、環境変数 `IMM_THUMB_GPU_DECODE` に注ぎ込む！
- 共通設定画面を閉じた瞬間にも、新しい設定を即座に再反映！⚡

### 7.4 FFMediaToolkit 先生へのお願い
- `IMM_THUMB_GPU_DECODE=cuda` の時だけ、FFMediaToolkit 先生の `DecoderOptions` に `hwaccel=cuda` と `hwaccel_output_format=cuda` っていう魔法の呪文を追加して、GPUの力を呼び覚ますんだ！✨

---

## 8. 📝 ログと戦いの記録（計測）

- バッチ処理がひと段落するたびに、結果サマリーをログに書き残すよ！
- **ログの見た目**:
  `thumb queue summary: gpu=..., parallel=..., batch_count=..., batch_ms=..., total_count=..., total_ms=...`
- もし `IMM_THUMB_FILE_LOG=1` がセットされてたら、`%LOCALAPPDATA%\IndigoMovieManager_fork\logs\thumb_decode.log` にもこっそり追記してるから、後からじっくり戦果を確認できるぞ！🕵️‍♂️

---

## 9. 💡 現場からの運用メモ

- GPUを使うと、劇的に速くなるわけじゃなくても**CPUの負荷（悲鳴）を下げる効果がめっちゃある**からオススメ！🖥️💦
- 並列数はPCによって全然違うから、とりあえず `8` か `12` あたりをベースにして遊んでみてね！
- もっとガチな速度比較データが見たいマニアな君は、`Docs/サムネイル生成速度比較.txt` をチェックしてくれ！！🚀
