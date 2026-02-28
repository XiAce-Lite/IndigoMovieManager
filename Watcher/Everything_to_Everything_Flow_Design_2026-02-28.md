# Everything to Everything 差分検証フロー ── 爆速スキャン＆完全自己修復アーキテクチャの本処理設計！🚀

Opusが組んでくれた「エラーマーカー（ダミーjpg）方式ストッパー」に乗っかって、**Everythingの動画リストとEverythingのjpgリストの実体を直接ぶつける最強の本処理（メインループ）**の設計とプランをまとめるよ！🥰

## 1. コンセプトとやりたいこと✨

- **DBゼロアクセスでI/Oを消し飛ばす🔥**: `movie` テーブルから既存パスを数万件SELECTして `HashSet` を作る激重処理を完全廃止！ストレージへのI/Oをゼロにするよ！
- **「ファイルの実体」至上主義💖**: DBなんて信じない。「サムネイルフォルダに画像（正常なjpgかエラーマーカのjpg）があるか？」だけを真実（Single Source of Truth）にして、未処理の動画だけを完璧に浮かび上がらせるぜ！
- **超絶ミリ秒の差分抽出⚡**: C#上の文字列操作（プレフィックス比較）とEverything IPC通信のコンボで、数万件の差分特定を一瞬で終わらせる！どや！

---

## 2. 変更するコードと実装設計🛠️

### 2.1. `Watcher/EverythingFolderSyncService.cs` に追加する処理
**ミッション**: サムネイルフォルダから、生成済み画像（エラーマーカー含む）のファイル名本体（Body）の一覧をEverythingで爆速取得する！

- **新メソッド爆誕**: `TryCollectThumbnailBodies(string thumbFolder, out HashSet<string> existingThumbBodies, out string reason)`
  - **どんな動き？**:
    1. Everythingに `"{thumbFolder}" ext:jpg` のクエリをぶん投げてjpgを根こそぎ持ってくる。
    2. 取得したファイル名からハッシュ部分（`.#a1b2c3d4.jpg` や `.#ERROR.jpg`）を削ぎ落として、ベースとなる「動画名（Body）」だけを抽出するよ！
       （例: `Path.GetFileNameWithoutExtension` で `.jpg` を消して、`LastIndexOf(".#")` でそれより前を切り出す感じ！）
    3. そのBodyたちを `HashSet<string> (StringComparer.OrdinalIgnoreCase)` にぶち込んで返す！
    4. もしEverythingが機嫌悪くて使えない時は `false` を返して大人しくフォールバックに任せる。

### 2.2. `Watcher/MainWindow.Watcher.cs` を魔改造
**ミッション**: 既存の遅いDB問い合わせ（`BuildExistingMoviePathSet`）を消し飛ばし、Body名を使った「Everything to Everything (E2E)」の最強差分判定に差し替える！

- **メスを入れる箇所 1: `CheckFolderAsync` のアタマ**
  - **【Before（遅い😭）】**: `HashSet<string> existingMoviePathSet = BuildExistingMoviePathSet();` (DBアクセス)
  - **【After（爆速⚡）】**: `HashSet<string> existingThumbBodies = await Task.Run(() => BuildExistingThumbnailBodySet(tbi.OutPath));`
    - ※ `BuildExistingThumbnailBodySet` は `_everythingFolderSyncService.TryCollectThumbnailBodies` を呼ぶよ。失敗したら `Directory.EnumerateFiles(tbi.OutPath, "*.jpg")` で泥臭く拾ってBody抽出すればOK！

- **メスを入れる箇所 2: `ScanFolderWithStrategyInBackground` / `ScanFolderInBackground`**
  - 引数を `existingMoviePathSet` から `existingThumbBodies` にバトンタッチ。
  - ループ内で動画パス（`candidatePath`）が「新顔」かどうかの判定をサクッと書き換える！
  - **イケてる判定ロジック**:
    ```csharp
    string body = Path.GetFileNameWithoutExtension(candidatePath);
    if (existingThumbBodies.Contains(body))
    {
        // 正常サムネかエラーマーカーが既にある！つまり「処理済み」だね！ヨシ！
        continue;
    }
    // 未処理の新規動画きたーー！🔥
    existingThumbBodies.Add(body); // 重複登録を防ぐためのガード
    newMoviePaths.Add(candidatePath);
    ```

### 2.3. DBとの関係性と、運用での神挙動👼

このアーキテクチャにすると、こんな「神挙動」がデフォになるよ！

- **サムネ欠損の自動復旧機能**: 例えばユーザーが手動でサムネ画像を数枚消したとするじゃん？ 次回スキャンで「画像がない！」ってことで自動的に新顔扱いになり、勝手にDB登録＆サムネキューに入って再生成される！超堅牢な自己修復システム完成！✨
- **DB重複への向き合い方**: `InsertMovieTable` は今まで通りそのまま叩くよ。手動でサムネ消した時はDBに重複レコードができちゃう可能性があるけど、「そもそも勝手にサムネを消すイレギュラーな運用」だし、実体の状態（真実）に修復するメリットや圧倒的スピードの方がはるかにデカいから、この設計でいく！最高じゃん！

---

## 3. 実装のステップ（Taskに行くよ！）🏃💨

1. `EverythingFolderSyncService` に `TryCollectThumbnailBodies` を生やす
2. `MainWindow.Watcher` にいる忌まわしき `BuildExistingMoviePathSet()` を消し飛ばし、代わりに `BuildExistingThumbnailBodySet` を召喚する
3. `MainWindow.Watcher` の差分判定でフルパスではなく `Path.GetFileNameWithoutExtension(moviePath)` のBodyを使って `existingThumbBodies` と突合するように書き換える
4. 爆速スキャンと、エラーマーカー（`.#ERROR.jpg`）のおかげで無限ループにならない極上の快感を味わう🥰
