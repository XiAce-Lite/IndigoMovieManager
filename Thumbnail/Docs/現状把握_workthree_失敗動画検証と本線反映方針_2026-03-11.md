# 現状把握 workthree 失敗動画検証と本線反映方針 2026-03-11

最終更新日: 2026-03-11

## 1. 目的

この文書は、`workthree` 本線で高速化を進める時に、難読動画対応をどの位置づけで扱うかを固定するための資料である。

難読動画検証の主戦場は `future` だが、`workthree` ではその結果を通常動画の高速感を壊さない形で採用する。

## 2. 現在の前提

- `workthree` は UI を含む高速化と安定化の本線である。
- `future` は難読動画検証の実験線である。
- 難読動画対応は価値があるが、通常動画の初動や一覧テンポを悪化させるなら本線へ入れない。
- 取り込み判断は、動画名ではなく一般条件で行う。

## 3. 本線で優先すること

1. UI の詰まりを解消する
2. 一覧表示、キュー投入、サムネイル作成のテンポを保つ
3. 難読動画対応は通常系へ副作用を広げない形で取り込む
4. ログとフォールバックで遅延理由を追えるようにする

## 4. `future` から持ち込む時の見方

`future` の成果は、次の 3 点が揃ったものだけ本線候補にする。

- 成功条件: どの一般条件で効いたか
- 導入位置: `MainWindow` / `Watcher` / `ThumbnailCreationService` / `ThumbnailQueueProcessor` のどこへ入れるか
- 回帰観点: UI テンポ、通常動画、フォールバック順、ログ比較のどこを見るか

## 5. 今の扱い

- UI 系改善は `workthree` で進める
- 難読動画の仮説検証は `future` で進める
- 本線へ返す時は、通常動画のテンポ悪化がないことを優先確認する
- 個別動画名依存の条件分岐は本線へ入れない

## 6. 次に見るファイル

- `MainWindow.xaml.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Thumbnail/ThumbnailCreationService.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `AI向け_ブランチ方針_future難読動画実験線_2026-03-11.md`

