> 徹底プランニング完了！ 🔥
>
> 既存資料3本を読み込み + コードの深掘り調査を行った結果、3フェーズの段階的改善計画を作った！
>
> ## 要点サマリ
>
> **調査で判明**: `ScanFolderWithStrategyInBackground` は **`Task.Run` で保護済み**なので、Everything IPC 自体は UI 詰まりの原因ではなかった。
>
> **本当の犯人は3つ**:
>
> 1. 🔥 `EverythingLiteProvider.QueryIndexedItems` の `.GetAwaiter().GetResult()` — lock 内で秒単位ブロック
> 2. ⚠️ 単発追加時の `Path.Exists` × 12回（タブ数 × 現行/旧命名）
> 3. ⚠️ `EverythingLiteProvider.CollectThumbnailBodies` が Everything を使わず `Directory.EnumerateFiles`
>
> **3フェーズ計画**:
>
> - **フェーズ1**: EverythingLite BG タイマ化（UI ブロック根絶）
> - **フェーズ2**: ThumbnailIndexDb（SQLite キャッシュ）導入で `Path.Exists` 根絶
> - **フェーズ3**: ThumbFolder 直引き統合 + identity SQL 共通化

# Implementation Plan: サムネパス取得最適化 + 新DB作成 fast path + Everything連携 (2026-03-18)

最終更新日: 2026-03-18

変更概要:
- 動画/サムネパス取得の一元化を、起動テンポ改善と監視高速化まで接続した再計画として整理
- 新DB作成時の初回起動と初期設定の fast path を追加
- Everything / EverythingLite の使い分けと、UI を詰まらせない実装順を明記

## 1. 結論

今回の本命は、単なる「サムネパス helper 化」ではない。

やるべきことは次の 3 本を同時に揃えることだ。

1. 動画パスとサムネパスの解決責務を分離して一元化する
2. 新DB作成直後を `空DB fast path` として最短表示へ寄せる
3. 監視と欠損サムネ探索は Everything 優先で回し、同期 I/O を UI 主経路から外す

この 3 本をバラバラにやると、部分最適だけ増えて構造がまた散る。
逆にここをまとめて設計すると、`新規作成`、`DB切替`、`Watcher`、`サムネ表示`、`救済` が同じ前提で動く。

## 2. 前提

- WhiteBrowser の DB (`*.wb`) は変更しない
- `workthree` はユーザー体感テンポ最優先
- 既存の本線は「常駐タスクはアプリ寿命で生かし、MainDB の参照先だけ切り替える」思想
- Everything 連携モードは DB ではなく User Settings 側管理
- `system.thum` 未設定時は runtime で既定 thumb root へ補完する

## 3. 今回の対象

### 3.1 対象に含める

- 動画パスの比較・DB照会・identity 補完
- サムネ根の決定
- タブ別サムネ出力先の決定
- 既存サムネ探索
- 新DB作成直後の初回表示
- Everything / EverythingLite の利用戦略
- Watcher での動画/サムネ候補収集

### 3.2 対象に含めない

- Bookmark 独自画像の命名規則統合
- `MovieRecords` の全面置換
- WhiteBrowser DB の index 追加やスキーマ変更
- 救済 worker の別リポジトリ化そのもの

## 4. 現状の問題を一本にまとめると何か

問題は 1 個ずつ独立していない。
実際には次の連鎖で起きている。

1. `CreateDatabase(...)` は最小テーブル作成だけで、system 初期値が空
2. `OpenDatafile(...)` 後は runtime 補完で動けるが、各所が `ThumbFolder` を直参照して分岐する
3. 一覧生成時のサムネ探索、詳細サムネ探索、ERROR マーカー探索が別実装
4. Watcher/救済/一覧がそれぞれ独自に `movie_path` を引き直す
5. EverythingLite に同期ブロックと同期 `Directory.EnumerateFiles` が残る
6. 結果として「空DB」「大DB」「Everything fallback」「旧命名互換」が全部別経路で複雑化する

要するに、path 解決と起動経路の境界が曖昧なのが本質だ。

## 5. 目標アーキテクチャ

## 5.1 `MoviePathService`

責務:

- 比較用正規化
- 比較キー生成
- 同一動画判定
- `dbFullPath + moviePath` から `movie_id/hash` の取得

最小 API:

```csharp
string Normalize(string path);
string CreateKey(string path);
bool IsSamePath(string left, string right);
bool TryGetMovieIdentity(string dbFullPath, string moviePath, out long movieId, out string hash);
```

方針:

- `QueueDbPathResolver.NormalizePathForCompare(...)` を土台にする
- UI / Watcher / Failure / Queue の path 比較を同じ規則へ寄せる
- `lower(movie_path) = lower(...)` の SQL を 1 か所へ閉じ込める

## 5.2 `ThumbnailLocationService`

責務:

- thumb root 解決
- tabIndex から out path 解決
- 正常サムネ path 生成
- ERROR marker path 生成
- 既存ファイル探索
- 旧命名フォールバック探索

最小 API:

```csharp
string ResolveThumbRoot(string dbFullPath, string dbName, string thumbFolder);
string ResolveOutPath(string dbFullPath, string dbName, string thumbFolder, int tabIndex);
string BuildSuccessPath(string outPath, string moviePath, string hash);
string BuildErrorMarkerPath(string outPath, string moviePath);
bool TryResolveExistingDisplayPath(
    string outPath,
    string moviePath,
    string movieName,
    string hash,
    out string path);
```

方針:

- `ThumbRootResolver`
- `ThumbnailLayoutProfileResolver`
- `ThumbnailPathResolver`

この 3 つを内側で使う façade として置く。

## 5.3 `EmptyDbFastPath`

責務:

- 新DB作成直後や movie 0 件DB で、起動主経路を極小化する

条件:

- `movie` 件数 0
- `watch` 件数 0
- queue/failure sidecar も未作成または空

動き:

1. `OpenDatafile(...)` 成功
2. first-page 読み込みは即空配列で確定
3. 登録件数は 0 を即表示
4. Watcher 作成は watch 件数 0 のため短絡
5. サムネ consumer / Everything poll は起動しても実仕事なし、ただし UI 主経路で待たない

狙い:

- 新規作成後の「空DBなのに重い」を消す
- `新規作成 -> すぐ監視フォルダ設定` の体感を軽くする

## 5.4 Everything の役割整理

役割を明確に分ける。

1. 動画候補収集
   - `EverythingProvider` または `EverythingLiteProvider`
2. サムネボディ収集
   - 既存 jpg 群の body 集合取得
3. fallback 理由の可視化
   - UI通知とログ文言を共通化

ここで大事なのは、
Everything を「高速化オプション」ではなく「監視・探索の標準経路」として整理し、
利用不能時だけ filesystem fallback へ落とすことだ。

## 6. 新DB作成の最適化方針

## 6.1 現状

- `BtnNew_Click` は `CreateDatabase(...)` してすぐ `TrySwitchMainDb(...)`
- `CreateDatabase(...)` はテーブル作成だけ
- `system` は空
- runtime 側が `skin/sort/thum/bookmark` の空を補完して動いている

## 6.2 改善方針

新DB作成直後に、最小限の system 値だけ埋める。

初期投入候補:

- `skin`
  - 既定タブの明示
- `sort`
  - 起動時 sort の明示
- `thum`
  - 空でも動くが、runtime 解決結果を保存すると分岐が減る
- `bookmark`
  - 既定 bookmark root
- `keepHistory`
  - 既定件数
- `playerPrg`
  - 空のまま可
- `playerParam`
  - 空のまま可

推奨:

- 最初の一手では `skin`, `sort`, `thum`, `bookmark`, `keepHistory` を seed
- `playerPrg`, `playerParam` は現状どおり空でもよい

理由:

- `GetSystemTable(...)` 以降の runtime 補完分岐が減る
- 新DB作成直後に「空だから runtime 補完」「設定保存後にようやく DB 値が入る」という二段構えを減らせる
- それでも WhiteBrowser DB 互換を壊さない

## 6.3 `新規作成` 専用受け入れ条件

- 作成直後に空一覧が即表示される
- 登録件数 0 が即時表示される
- `ThumbFolder` と `BookmarkFolder` が runtime 補完と DB 初期値で一致する
- `新規作成 -> 閉じる -> 再起動` で見た目と sort が崩れない

## 7. Everything 利用の最適化方針

## 7.1 優先ルール

1. `AUTO`
   - Everything 使用可能なら Everything
   - 不可なら filesystem fallback
2. `ON`
   - Everything を必ず試す
   - 失敗時は fallback するが、理由を明示
3. `OFF`
   - 常に filesystem

この方針自体は現状維持でよい。
問題は実装のブロッキングだけだ。

## 7.2 先に潰すべき問題

### A. `EverythingLiteProvider.QueryIndexedItems(...)` の同期ブロック

現状:

- `RebuildIndexAsync(...).GetAwaiter().GetResult()` がある

方針:

- `CollectMoviePaths` 実行中に rebuild しない
- バックグラウンド再構築へ寄せる

推奨案:

- ルート単位 cache は維持
- `RebuildCooldown` 経過後は専用 BG 更新タスクを予約
- 取得側は「直近の完成済み index」だけ読む

これで UI 主経路から同期ブロックを外せる。

### B. `EverythingLiteProvider.CollectThumbnailBodies(...)` の同期列挙

現状:

- `Directory.EnumerateFiles(thumbFolder, "*.jpg")`

方針:

- Lite index 側から jpg を抽出して body 集合を作る
- 少なくとも UI 主経路では `Directory.EnumerateFiles` を直に打たない

### C. IPC Everything 呼び出し元の thread 安全確認

方針:

- `EverythingProvider.CollectMoviePaths(...)`
- `EverythingProvider.CollectThumbnailBodies(...)`

これらの呼び出し元が UI thread 直実行になっていないことを保証する。

受け入れ条件:

- 呼び出し元は `async` バックグラウンド文脈
- 1.5 秒 timeout が UI フリーズとして見えない

## 8. 実装フェーズ

## Phase 0: 計測固定

目的:

- 変更前後で、どこが速くなったかを見失わないようにする

追加観測:

- `new-db create begin/end`
- `new-db seed begin/end`
- `new-db first-open first-page shown`
- `everything-lite rebuild scheduled/start/end`
- `thumbnail-body-collect provider=... elapsed_ms=...`
- `movie-identity lookup provider=ui/db elapsed_ms=...`

完了条件:

- 新DB作成直後
- watch folder 設定後初回 scan
- 大DB起動後の first-page

この 3 ケースで比較ログが取れる

## Phase 1: Path 責務の分離

目的:

- 呼び出し側の散乱を止める

実施:

- `MoviePathService` 追加
- `ThumbnailLocationService` 追加
- `TryResolveMovieIdentityFromDb(...)` を外出し
- `ResolveThumbnailOutPath(...)` 重複を façade へ寄せる

完了条件:

- app / engine / rescue worker が同じ out path 規則を使う
- path 比較の正規化規則が UI/Queue/Watcher で揃う

## Phase 2: 表示用サムネ探索の共通化

目的:

- 一覧 / 起動 / 詳細 / ERROR タブの判断差をなくす

実施:

- `TryResolveExistingDisplayPath(...)` 追加
- `movie_path` 優先
- `movie_name` / `movie_body` は legacy fallback
- success / ERROR / placeholder の優先順を固定

完了条件:

- `MainWindow.xaml.cs`
- `MainWindow.Startup.cs`
- `Extension.DetailThumbnail`
- `ThumbnailFailedTab`

で探索ロジックが共有される

## Phase 3: 新DB作成 fast path

目的:

- `新規作成` 後の体感を最短にする

実施:

- `CreateDatabase(...)` 後に system seed を入れる helper 追加
- seed 値は runtime 解決結果と整合する既定値を使う
- empty DB 判定で first-page を即確定
- watch 0 件なら watcher 作成を短絡
- sidecar/queue/failure は必要時生成のまま維持

完了条件:

- 新DB作成から一覧表示までで不要な scan が走らない
- 初回表示が空DB前提の最短経路になる

## Phase 4: Everything の非同期化と body 集合最適化

目的:

- 大量 watch / 大量サムネでも UI 主経路を詰まらせない

実施:

- EverythingLite rebuild の BG 化
- thumbnail body collect の index 参照化
- 呼び出し元 thread の監査

完了条件:

- `EverythingLiteProvider` に `.GetAwaiter().GetResult()` が残らない
- `CollectThumbnailBodies` が UI 主経路で filesystem 列挙しない

## Phase 5: 起動段階ロードとの接続

目的:

- Phase 1〜4 を、既存の first-page / append-page 設計に自然接続する

実施:

- startup bulk build cache が `ThumbnailLocationService` を使う
- empty DB fast path を `BeginStartupDbOpen()` に接続
- heavy services 遅延起動と干渉しないよう切り分ける

完了条件:

- 大DBでも空DBでも、起動経路が 1 本の設計として説明できる

## 9. 作業順

実行順は固定する。

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3
5. Phase 4
6. Phase 5

理由:

- 先に helper だけ作っても、観測が無いと成果が見えない
- 先に Everything を大きく触ると、path 責務分離前で差分が濁る
- 新DB fast path は path 責務整理後の方が安全

## 10. 受け入れ条件

### 10.1 新DB作成

- `新規作成` 後 1 秒以内に空一覧へ到達する
- DB 作成直後に `ThumbFolder` / `BookmarkFolder` が確定する
- system seed と runtime 補完が食い違わない

### 10.2 サムネパス取得

- 一覧と詳細で同じ動画に対して同じ既存サムネを拾う
- 旧命名が残る環境でも fallback が維持される
- ERROR marker の有無判定が一覧と救済で食い違わない

### 10.3 Everything

- Everything 使用可なら watch scan は Everything 優先
- 使用不可時は理由付きで fallback
- UI スレッドで同期ブロックしない

### 10.4 回帰なし

- 通常サムネ作成
- 手動サムネ作成
- rescue
- rename/delete
- DB切替

これらで path 解決の退行を出さない

## 11. テスト観点

### 11.1 自動テスト

- `MoviePathService`
  - 大小文字違い
  - `\\?\` 接頭辞
  - `/` と `\`
- `ThumbnailLocationService`
  - 現行命名
  - legacy 命名
  - ERROR marker
  - detail tab
- empty DB fast path
  - movie 0 件
  - watch 0 件
- EverythingLite
  - rebuild 非同期化
  - body 集合取得

### 11.2 手動確認

1. 新DB作成
2. そのまま閉じて再起動
3. watch 追加なしで idle
4. watch 追加後の初回 scan
5. Everything OFF/AUTO/ON 切替
6. 旧命名サムネが残る DB を開く

## 12. リスク

- `CreateDatabase(...)` seed 追加で古い運用との期待差が出る
- path 正規化を UI 側へ広げると、比較一致が増えて副作用が出る可能性がある
- EverythingLite の rebuild 方針変更で freshness が下がる可能性がある
- out path 統合を急ぐと rescue worker 側の detail mode とズレる危険がある

## 13. 今回の判断

- Bookmark は分離維持
- Everything モードは User Settings 維持
- `system` seed は最小限だけ入れる
- WhiteBrowser DB 非変更ルールは厳守
- 本命は「path helper 化」単体ではなく、「新DB fast path と Everything 非同期化まで含めた一本化」

## 14. 次に実装へ入る時の最初の 3 タスク

1. `MoviePathService` を作り、`ThumbnailCreation` と `Watcher` の DB identity lookup を移す
2. `ThumbnailLocationService` を作り、`ResolveOutPath` 重複と表示用探索を一本化する
3. `CreateDatabase(...)` 後の system seed helper を追加し、`新規作成` を empty DB fast path に乗せる
