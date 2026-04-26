# Implementation Plan: 下部タブ Logタブ追加

最終更新日: 2026-04-11

変更概要:
- Debug 限定の `Log` 下部タブを追加した
- `DebugRuntimeLog` にカテゴリ別の出力スイッチを追加した
- チェックボックス変更を `Properties.Settings` へ即保存する流れを追加した

## 目的

- Debug 中だけ、ログ出力の粒度を UI から素早く切り替えられるようにする
- `debug-runtime.log` の末尾確認と、ログ出力先確認を 1 タブにまとめる
- 既存の `Debug` タブは DB / Queue / FailureDB 操作へ寄せ、ログ制御を分離する

## 実装方針

1. `MainWindow.xaml` の下部タブへ `Log` を追加する
2. `EvaluateShowDebugTab()` を使って Debug 限定表示に揃える
3. `Properties.Settings` にカテゴリ別 boolean を追加する
4. `DebugRuntimeLog.ShouldWrite(...)` の入口でカテゴリ別 switch を判定する
5. `LogTabView` のチェック変更は TwoWay バインド済み値をそのまま `Save()` する

## カテゴリ設計

- `Watcher`
  - `watch*`
- `Queue`
  - `queue*`
- `Thumbnail`
  - `thumbnail*`
- `UI・起動`
  - `ui-*`, `layout`, `lifecycle`, `player`, `task*`, `kana`, `overlay`
- `Skin`
  - `skin*`
- `Debug操作`
  - `debug*`, `log-tab`
- `DB・外部`
  - `db*`, `sinku`
- `その他`
  - 上記に当てはまらないカテゴリ

## 保存方針

- チェックボックスは `Properties.Settings.Default` へ TwoWay バインドする
- `Click` 時に `Properties.Settings.Default.Save()` を呼ぶ
- 保存後は `Log` タブのサマリ文言を即更新する

## 保守メモ

- 新しいログカテゴリを増やしたら、`Infrastructure/DebugRuntimeLog.cs` の `ResolveToggleGroup(...)` を更新する
- Debug 限定タブを増やす場合は、`ToolLog` と同じく `ValidateDockLayoutText(...)` の必須タブ判定へ追加する
