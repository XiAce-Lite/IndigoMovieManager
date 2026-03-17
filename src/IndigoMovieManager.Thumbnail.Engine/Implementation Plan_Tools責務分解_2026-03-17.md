# Implementation Plan Tools責務分解 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `Tools` を薄い互換 wrapper に縮退した
- `MovieHashCalculator` / `ThumbnailTagFormatter` / `ThumbnailTempFileCleaner` / `ThumbnailSheetComposer` を追加した
- `ThumbnailCreationService` / `RescueWorkerApplication` / `MainWindow` の一部呼び出しを新クラスへ直接寄せた
- `ToolsCompatibilityTests` は wrapper と分解後実装の一致を確認する形へ広げた

## 1. 目的

`Tools` に混在していた

- ハッシュ計算
- タグ整形
- temp掃除
- 画像結合

を責務単位で分離し、`static 雑多箱` の出自を薄くする。

今回は既存公開名を残すため、`Tools` 自体は削除せず wrapper に縮退する。

## 2. 今回やったこと

1. `MovieHashCalculator` を追加した
2. `ThumbnailTagFormatter` を追加した
3. `ThumbnailTempFileCleaner` を追加した
4. `ThumbnailSheetComposer` を追加した
5. `Tools` の各 public API は新クラスへ委譲するだけにした
6. 主要呼び出しのうち、影響範囲が小さい場所から新クラスへ直接切り替えた

## 3. 判断

- `Tools` を一気に消すより、wrapper 化して呼び出しを少しずつ剥がす方が安全
- `ThumbnailCreationService` と `MainWindow` のような主経路では、新クラス名の方が責務が読みやすい
- `ConcatImages` は今後 `ThumbnailSheetComposer` 側で品質改善や差し替えをしやすくなる

## 4. 今回やらないこと

1. `Tools` public API の即時削除
2. `using static Tools` の全面廃止
3. OpenCV 依存の即時除去

## 5. 次の候補

1. 残っている `Tools` 呼び出しを新クラスへ順次寄せる
2. `ThumbnailQueueProcessor` の lease / lane / failure 責務を外へ出す
3. `ThumbnailCreationService` の policy 分割を続ける
