# 🛡️ DB切り替え時の安全対策 — 前DBの未処理混入を防ぐ

> **✨ Designed by Opus ✨**

## 概要

メインDBを切り替えた直後、**前のDBで未処理だった内容が新DBに混ざり込む**問題が見つかりました。
Everything導入後の非同期フロー（`CheckFolderAsync`）で表面化しています。

---

## 🔬 原因分析 — 3つの原因を突き止めました

### 🐛 原因1: `FileSystemWatcher` が Dispose されていない

**場所**: `MainWindow.xaml.cs` → `OpenDatafile`

```csharp
fileWatchers?.Clear();  // リストから外すだけで Dispose() が呼ばれていない
```

- 旧DBの Watcher イベント（`FileChanged`）が**発火し続け**、新DBの `MainVM.DbInfo.DBFullPath` に INSERT が走る
- 前DBの監視フォルダに来た新規ファイルが、新DBに登録されてしまう 💥

### 🐛 原因2: `CheckFolderAsync` のレースコンディション

**場所**: `MainWindow.Watcher.cs` → `CheckFolderAsync`

```
旧DB open → CheckFolderAsync(旧) 開始（非同期）
          → 新DB open → MainVM.DbInfo が新DBに切り替わる
          → 旧DBスキャン続行中に InsertMovieTable(新DB, 旧DBの動画) 💥
```

- `CheckFolderAsync` 内で `MainVM.DbInfo.*` を**都度参照**しているので、DB切り替えが起きると参照先が変わってしまう
- 開始時にDBパスのスナップショットを取っていなかったのが根本原因

### 🐛 原因3: `CancellationToken` がない

- 旧DBのスキャンを**途中で止める手段がない**状態だった
- `_checkFolderRunLock` で排他はしているものの、実行中タスクのキャンセルができなかった

---

## ✅ 修正方針 — シンプルに3段構えでいきます

### 修正1: `FileSystemWatcher` をちゃんと Dispose する

`OpenDatafile` 内の `fileWatchers.Clear()` を `StopAndClearFileWatchers()` に差し替えました。

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

### 修正2: `CheckFolderAsync` にDBスナップショット + 切り替え検知ガード

`CheckFolderAsync` の開始時にDBパスをスナップショットして保持。ループ内でDB切り替えを検知したら即座に打ち切ります。
`MainVM.DbInfo.*` の直接参照は全てスナップショット変数に置き換え済みです。

### 修正3: `CancellationToken` で旧スキャンを即キャンセル（次フェーズ候補）

`_checkFolderCts` を追加して、`OpenDatafile` の呼び出し時にキャンセル発火させる構想です。

### 修正4: `OpenDatafile` の2フェーズ分離

```csharp
private void OpenDatafile(string dbFullPath)
{
    ShutdownCurrentDb();   // Phase 1: 旧DBの完全シャットダウン
    BootNewDb(dbFullPath); // Phase 2: 新DBの起動
}
```

旧DBの後始末と新DBの起動をくっきり分けることで、状態の混在を構造的に防ぎます。

---

## 🏗️ 将来のアーキテクチャ方針

> 💡 中央管理層（`DbSessionManager`）は、MVVM化に本腰を入れるタイミングで一緒にやるのが一番効率的です。
> 今は「安全な2フェーズ分離」だけやって、まずバグを確実に潰しましょう 💪

### 今 `DbSessionManager` を作らない理由

- 影響範囲が `MainWindow` のコードビハインド全体に及ぶのでリスクが大きい
- MVVM未完のまま中途半端にDI/サービス層を入れると、逆に複雑になる
- 2フェーズ分離だけで十分安全だし、将来 `DbSessionManager` に昇格させやすい土台になる

### 📌 DBセッション切り替えの依存関係

```
DB切り替え ──→ 監視フォルダ再読込 ──→ FileSystemWatcher再起動
    │                                      │
    ├──→ ThumbFolder変更 ──→ Everything照会先変更
    │                                      │
    ├──→ タブ切り替え ──→ CurrentTabIndex ──→ QueueObj.Tabindex
    │
    └──→ サムネイルキュー全クリア
```

起点は必ず「DB切り替え」の1箇所。タブ切り替えや監視フォルダ変更は全部DBの子イベントです。

---

## 📋 タスクリスト

### Phase 1（✅ 実装完了！ 2026-03-01）
- [x] 修正1: `StopAndClearFileWatchers()` ヘルパー追加 + `OpenDatafile` 差し替え
- [x] 修正2: `CheckFolderAsync` のDBスナップショット + 切り替え検知ガード（全11箇所）
- [x] 修正4: `OpenDatafile` を `ShutdownCurrentDb` / `BootNewDb` に2フェーズ分離
- [x] ビルド確認（コンパイルエラーゼロ ✅）
- [ ] 手動テスト（DB切り替え混入テスト）— Codexがテスト中なので後日実施 ⏳

### Phase 2（MVVM化と一緒にやる）
- [ ] 修正3: `CancellationToken` で旧スキャン即キャンセル
- [ ] `DbSessionManager` クラスの導入
- [ ] DI/サービス層の整備

---

## 🧪 検証プラン

Codexのテストが終わり次第、以下を手動で確認します。

1. 🔄 **DB切り替え混入テスト** — DB-Aを開いてスキャン中にDB-Bへ切り替え → 混入なしを確認
2. 🗑️ **Watcher解放テスト** — DB切り替え後、旧監視フォルダにファイル追加 → 新DBに混入しないことを確認
3. 📝 **ログ確認** — `"abort scan: db switched"` が出ればOK

