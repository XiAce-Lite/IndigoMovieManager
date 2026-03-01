# DB切り替え時の安全対策 — 前DBの未処理混入防止設計

> **✨ Designed by Opus ✨**

## 概要

メインDBを切り替えた直後に、**前のDBで未処理だった内容が新DBへ混入する**という問題がございます。
Everything導入後の非同期処理フロー（`CheckFolderAsync`）において顕在化いたしました。

---

## 原因分析

調査の結果、以下の3つの原因を特定いたしました。

### 原因1: `FileSystemWatcher` が正しく解放されていない

**該当箇所**: `MainWindow.xaml.cs` → `OpenDatafile`

```csharp
fileWatchers?.Clear();  // リストから除外するのみで、Dispose() が呼ばれていない
```

- 旧DBの Watcher イベント（`FileChanged`）が**発火し続け**、新DBの `MainVM.DbInfo.DBFullPath` に対して INSERT が実行されます
- 結果として、前DBの監視フォルダに到着した新規ファイルが新DBに登録されてしまいます

### 原因2: `CheckFolderAsync` のレースコンディション

**該当箇所**: `MainWindow.Watcher.cs` → `CheckFolderAsync`

```
旧DB open → CheckFolderAsync(旧) 開始（非同期）
          → 新DB open → MainVM.DbInfo が新DBへ切り替わる
          → 旧DBのスキャン続行中に InsertMovieTable(新DB, 旧DBの動画)
```

- `CheckFolderAsync` 内で `MainVM.DbInfo.*` を**都度参照**しているため、DB切り替えが発生すると参照先が変わります
- 開始時にDBパスのスナップショットを取得していないことが根本原因でございます

### 原因3: `CancellationToken` が未実装

- 旧DBのスキャンを**途中で停止する手段が存在しない**状態でした
- `_checkFolderRunLock` による排他制御はあるものの、実行中タスクのキャンセル機構が欠けておりました

---

## 修正方針

段階的に、確実な対策を講じてまいります。

### 修正1: `FileSystemWatcher` の適切な解放

`OpenDatafile` 内の `fileWatchers.Clear()` を `StopAndClearFileWatchers()` へ差し替えます。

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

### 修正2: `CheckFolderAsync` へのDBスナップショット導入と切り替え検知ガード

`CheckFolderAsync` の開始時にDBパスをスナップショットとして保持し、ループ内でDB切り替えを検知した場合は即座に処理を打ち切ります。
内部の `MainVM.DbInfo.*` 直接参照を、すべてスナップショット変数へ置き換えます。

### 修正3: `CancellationToken` による旧スキャンの即時キャンセル（次フェーズ候補）

`_checkFolderCts` を追加し、`OpenDatafile` 呼び出し時にキャンセルを発火させます。

### 修正4: `OpenDatafile` の2フェーズ分離

```csharp
private void OpenDatafile(string dbFullPath)
{
    ShutdownCurrentDb();   // Phase 1: 旧DBの完全シャットダウン
    BootNewDb(dbFullPath); // Phase 2: 新DBの起動
}
```

旧DBの後始末と新DBの起動を明確に分離することで、状態の混在を構造的に防止いたします。

---

## 将来のアーキテクチャ方針

> 💡 中央管理層（`DbSessionManager`）の導入は、MVVM化を本格的に進めるタイミングで併せて実施するのが最も合理的と考えております。
> 現段階では「安全な2フェーズ分離」に集中し、まず確実にバグを解消することを優先いたします。

### 現時点で `DbSessionManager` を導入しない理由

- 影響範囲が `MainWindow` のコードビハインド全体に及ぶため、リスクが大きいこと
- MVVM化が未完の状態で中途半端なDI/サービス層を導入すると、かえって複雑化を招くこと
- 2フェーズ分離だけでも十分な安全性を確保でき、将来 `DbSessionManager` へ昇格させやすい土台となること

### DBセッション切り替えの依存関係

```
DB切り替え ──→ 監視フォルダ再読込 ──→ FileSystemWatcher再起動
    │                                      │
    ├──→ ThumbFolder変更 ──→ Everything照会先変更
    │                                      │
    ├──→ タブ切り替え ──→ CurrentTabIndex ──→ QueueObj.Tabindex
    │
    └──→ サムネイルキュー全クリア
```

起点は必ず「DB切り替え」の1箇所でございます。タブ切り替え・監視フォルダ変更はすべてDBの子イベントとして位置づけられます。

---

## タスクリスト

### Phase 1（✅ 実装完了 — 2026-03-01）
- [x] 修正1: `StopAndClearFileWatchers()` ヘルパー追加 + `OpenDatafile` 差し替え
- [x] 修正2: `CheckFolderAsync` のDBスナップショット + 切り替え検知ガード（全11箇所差し替え）
- [x] 修正4: `OpenDatafile` を `ShutdownCurrentDb` / `BootNewDb` に2フェーズ分離
- [x] ビルド確認（コンパイルエラーゼロ）
- [ ] 手動テスト（DB切り替え混入テスト） — Codex側のテスト作業完了後に実施予定

### Phase 2（MVVM化の際に実施）
- [ ] 修正3: `CancellationToken` による旧スキャン即時キャンセル
- [ ] `DbSessionManager` クラスの導入
- [ ] DI/サービス層の整備

---

## 検証プラン

Codex側のテスト作業が完了次第、以下の手動テストを実施いたします。

1. **DB切り替え混入テスト** — DB-Aを開きスキャン進行中にDB-Bへ切り替え → 混入がないことを確認
2. **Watcher解放テスト** — DB切り替え後、旧監視フォルダにファイルを追加 → 新DBに混入しないことを確認
3. **ログ確認** — `"abort scan: db switched"` のログが出力されることを確認

