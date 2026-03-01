# DB切り替え時の安全対策 — 前DBの未処理混入を消し飛ばす！🔥

> **✨ Designed by Opus ✨**

## 問題の概要

メインDBを切り替えた直後に、**前のDBで未処理だった内容が新DBに混ざり込む**。
Everything導入後の非同期処理フロー（`CheckFolderAsync`）で顕在化する。

---

## 🔬 原因分析：3つの罠を特定！

### 🐛 罠1: FileSystemWatcherがDisposeされてない

**場所**: `MainWindow.xaml.cs` → `OpenDatafile`

```csharp
fileWatchers?.Clear();  // ← リストから外すだけ！Dispose()してない！
```

- 旧DBのWatcherイベント（`FileChanged`）が**発火し続け**、新DBの `MainVM.DbInfo.DBFullPath` にINSERT
- 前DBの監視フォルダに来た新規ファイルが新DBに登録される 💀

### 🐛 罠2: 非同期CheckFolderAsyncのレースコンディション

**場所**: `MainWindow.Watcher.cs` → `CheckFolderAsync`

```
旧DB open → CheckFolderAsync(旧)開始(非同期)
          → 新DB open → MainVM.DbInfo が新DBに！
          → 旧DBのスキャン続行中に InsertMovieTable(新DB, 旧DBの動画) 💀
```

- `CheckFolderAsync` 内で `MainVM.DbInfo.*` を**都度参照**してDB操作してる
- 開始時にDBパスをスナップショットしていないため、途中で切り替わると混入する

### 🐛 罠3: CancellationTokenがない

- 旧DBのスキャンを**止める手段がない**
- `_checkFolderRunLock` で排他はしてるが、実行中タスクのキャンセルができない

---

## ✅ 修正方針 — シンプル3段構え！

### 修正1: FileSystemWatcherを正しくDisposeする

`OpenDatafile` 内の `fileWatchers.Clear()` → `StopAndClearFileWatchers()` に差し替え。

```csharp
private void StopAndClearFileWatchers()
{
    foreach (var w in fileWatchers)
    {
        w.EnableRaisingEvents = false;
        w.Dispose();
    }
    fileWatchers.Clear();
}
```

### 修正2: CheckFolderAsyncにDBスナップショット＋切り替え検知ガード

`CheckFolderAsync` の開始時にDBパスをスナップショットし、ループ内でDB切り替えを検知したら即打ち切り。
内部の `MainVM.DbInfo.*` 直接参照を全てスナップショット変数に差し替える。

### 修正3: CancellationTokenで旧スキャン即時キャンセル（次フェーズ候補）

`_checkFolderCts` を追加し、`OpenDatafile` 呼び出し時にキャンセル発火。

### 修正4: OpenDatafileの2フェーズ分離

```csharp
private void OpenDatafile(string dbFullPath)
{
    ShutdownCurrentDb();  // Phase 1: 旧DBの完全シャットダウン
    BootNewDb(dbFullPath); // Phase 2: 新DBの起動
}
```

---

## 🏗️ 将来アーキテクチャの方針

> 中央指令所（`DbSessionManager` 的なやつ）は、**MVVM化に本腰入れるタイミングで一緒にやる**のが最も効率的。
> 今は「安全な分離（2フェーズ）」だけやって、まず**バグを潰す方が勝ち**！💪🔥

### なぜ今は作らないのか

- 影響範囲が `MainWindow` コードビハインド全体に爆発する
- MVVM未完のまま中途半端なDI/サービス層を入れると逆に複雑化する
- 2フェーズ分離で十分安全、将来 `DbSessionManager` に昇格させやすい土台になる

### DBセッション切り替えの依存関係マップ

```
DB切り替え ──→ 監視フォルダ再読込 ──→ FileSystemWatcher再起動
    │                                      │
    ├──→ ThumbFolder変更 ──→ Everything照会先変更
    │                                      │
    ├──→ タブ切り替え ──→ CurrentTabIndex ──→ QueueObj.Tabindex
    │
    └──→ サムネイルキュー全クリア
```

起点は必ず「DB切り替え」の1箇所。タブ切り替え・監視フォルダ変更はDBの子イベント。

---

## 📋 タスクリスト

### Phase 1（✅ 実装完了！ 2026-03-01）
- [x] 修正1: `StopAndClearFileWatchers()` ヘルパー追加＋`OpenDatafile`差し替え
- [x] 修正2: `CheckFolderAsync` のDBスナップショット＋切り替え検知ガード（全11箇所差し替え）
- [x] 修正4: `OpenDatafile` を `ShutdownCurrentDb` / `BootNewDb` に2フェーズ分離
- [x] ビルド確認（コンパイルエラーゼロ✅）
- [ ] 手動テスト（DB切り替え混入テスト）⏳ Codexがテストぶん回し中のため後日実施

### Phase 2（MVVM化時に実施）
- [ ] 修正3: CancellationTokenで旧スキャン即時キャンセル
- [ ] `DbSessionManager` クラスの導入（中央指令所）
- [ ] DI/サービス層の整備

---

## 検証プラン

> ⏳ Codexがテストぶん回し中のため、手動テストは後日実施！

1. **DB切り替え混入テスト**: DB-A開いてスキャン進行中にDB-Bに切り替え → 混入なし確認
2. **Watcher解放テスト**: DB切り替え後、旧監視フォルダにファイル追加 → 新DBに混入しない確認
3. **ログ確認**: `"abort scan: db switched"` のログが出ること

