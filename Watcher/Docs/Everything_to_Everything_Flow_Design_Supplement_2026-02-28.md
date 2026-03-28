# Everything to Everything 差分検証フロー ── 本処理詳細設計・実装プラン

本ドキュメントでは、Opusが実装した「エラーマーカー（ダミーjpg）方式ストッパー」を前提とし、**Everythingの動画リストとEverythingのjpgリストの実体を突き合わせて差分抽出し、DB・キューへ登録する本処理（The Main Loop）**の詳細な設計と実装プランを定義します。

## 1. 目的とコンセプト

- **DBゼロアクセス走査の実現**: `movie` テーブルから既存パスを数万件SELECTしてHashSetに展開する処理を完全廃止し、ストレージI/Oをゼロにする。
- **ファイル（実体）至上主義**: DBではなく「サムネイルフォルダに画像（正常なjpg、またはエラーマーカー付きjpg）が存在するか」を唯一の真実（Single Source of Truth）として扱い、未処理・処理失敗の動画を浮き彫りにする。
- **超高速な差分抽出**: C#上の文字列操作（プレフィックス比較）とEverything IPC通信の組み合わせにより、差分特定を数ミリ秒〜数十ミリ秒で完了させる。

---

## 2. 実装設計（変更ファイル一覧）

### 2.1. `Watcher/EverythingFolderSyncService.cs`
**役割**: サムネイルフォルダから生成済み画像（エラーマーカー含む）のファイル名本体（Body）一覧をEverythingで超高速に取得する。

- **新規メソッド追加**: `TryCollectThumbnailBodies(string thumbFolder, out HashSet<string> existingThumbBodies, out string reason)`
  - **処理フロー**:
    1. Everythingに対して `"{thumbFolder}" ext:jpg` のクエリを投げる。
    2. ヒットしたファイルの「ファイル名」からハッシュ部分（`.#a1b2c3d4.jpg` や `.#ERROR.jpg`）を取り除き、動画本来の名前（Body）を抽出する。
       ※ 例: `Path.GetFileNameWithoutExtension(fileName)` で `.jpg` を消し、`LastIndexOf(".#")` を用いてそれより前を切り出す。
    3. 抽出した Body を `HashSet<string> (StringComparer.OrdinalIgnoreCase)` に格納し返却。
    4. Everythingが使えない環境（Auto不発時など）の場合は `false` を返し、フォールバックへ委ねる。

### 2.2. `Watcher/MainWindow.Watcher.cs`
**役割**: 既存のDB問い合わせ（`BuildExistingMoviePathSet`）を消し飛ばし、サムネイルのBody名を用いた「Everything to Everything（E2E）」での最強差分判定に差し替える。

- **変更箇所 1: `CheckFolderAsync` の冒頭**
  - **旧**: `HashSet<string> existingMoviePathSet = BuildExistingMoviePathSet();` (DBアクセス)
  - **新**: `HashSet<string> existingThumbBodies = await Task.Run(() => BuildExistingThumbnailBodySet(tbi.OutPath));`
    - ※ `BuildExistingThumbnailBodySet` は `_everythingFolderSyncService.TryCollectThumbnailBodies` を呼ぶ。失敗した場合は `Directory.EnumerateFiles(tbi.OutPath, "*.jpg")` でフォールバックし、同じように Body を抽出して HashSet を作る。

- **変更箇所 2: `ScanFolderWithStrategyInBackground` / `ScanFolderInBackground`**
  - 引数の `existingMoviePathSet` を `existingThumbBodies` に変更。
  - ループ内で動画パス（`candidatePath`）が「新顔」かどうかの判定を書き換える。
  - **判定ロジック**:
    ```csharp
    string body = Path.GetFileNameWithoutExtension(candidatePath);
    if (existingThumbBodies.Contains(body))
    {
        // 正常サムネ、またはエラーマーカーが既に存在するので「処理済み（またはスキップ対象）」
        continue;
    }
    // 未処理の新規動画！
    existingThumbBodies.Add(body); // 同名ファイルがサブフォルダにある等での重複登録を防ぐ
    newMoviePaths.Add(candidatePath);
    ```

### 2.3. DBとの関係性（運用時の挙動）

アーキテクチャの変更により、以下の挙動がデフォルトとなります。

- **サムネ欠損の自動復旧**: 例えばサムネイルフォルダの画像をユーザーが手動で数枚消した場合、次回のスキャンで「画像が無い」ため自動的に新顔とみなされ、もう一度DB登録用の `InsertMovieTable` が走り、サムネキューに入って再生成されます。（超堅牢な自己修復）
- **DBの重複登録について**: `InsertMovieTable` は基本的にそのままINSERTを投げます。ユーザーが手動でサムネを消したときなど、同一動画が再度INSERTされてDB・UI上で重複して見える可能性はありますが、「そもそもサムネを勝手に消す運用がイレギュラー」であり、DBの実体乖離を正すメリット（および圧倒的スピード）の方がはるかに大きいため、許容する設計とします。

---

## 3. 実装タスク (`task.md` への追加内容)

1. [ ] `EverythingFolderSyncService`: `TryCollectThumbnailBodies` メソッドの実装
2. [ ] `MainWindow.Watcher`: `BuildExistingMoviePathSet` 関数を破棄し、`BuildExistingThumbnailBodySet` を実装 (Everything呼び出し + 失敗時 `Directory.EnumerateFiles` のフォールバック)
3. [ ] `MainWindow.Watcher`: 差分判定でフルパスではなく `Path.GetFileNameWithoutExtension(moviePath)` を用いて `existingThumbBodies` と突合するようロジックを修正
4. [ ] 手動テスト: スキャン速度の体感テスト、およびわざと `*.#ERROR.jpg` や正常なjpgを削除して次回スキャン時に新規として拾われるか（再試行ループ）の確認
