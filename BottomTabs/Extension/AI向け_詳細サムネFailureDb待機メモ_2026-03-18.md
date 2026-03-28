# 詳細サムネ FailureDb 待機メモ

更新日: 2026-03-18

## 背景
- 下部詳細タブは、サムネ不足時に通常キュー投入と ERROR 救済を自動で行う。
- `TryEnqueueThumbnailDisplayErrorRescueJob(...)` は救済要求の前に `#ERROR` を消すため、未完了解析が残っていても次回描画では「ただの未生成」に見えやすい。

## 現在の扱い
- `MainWindow.BottomTab.Extension.DetailThumbnail.cs` では、`FailureDb` に detail(`tab=99`) の open rescue がある間は通常キューへ戻さない。
- この間は `errorGrid.jpg` placeholder を維持し、同じ detail rescue も重複要求しない。

## 目的
- サムネ完成前に通常キューへ何度も戻るループを止める。
- `Watcher` 側で止めた `main` サムネの再投入抑止と、詳細タブ側の挙動を揃える。
