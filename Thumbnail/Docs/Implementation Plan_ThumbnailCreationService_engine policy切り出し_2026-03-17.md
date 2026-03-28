# Implementation Plan ThumbnailCreationService engine policy切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `ThumbnailEngineExecutionPolicy` を追加し、engine 順序・ffmpeg1pass skip・autogen retry 判定を `ThumbnailCreationService` から外した
- `ThumbnailCreationService` は engine 実行の orchestration に寄せ、policy 判断は新クラスへ委譲する形にした
- `AutogenRegressionTests` は reflection を減らし、新 policy を直接叩く形へ更新した

## 1. 目的

`ThumbnailCreationService` の中でも本家色が強く見える

- engine 順序の組み立て
- 既知失敗による ffmpeg1pass skip
- autogen retry の判定

を service 本体から外し、`CreateThumbAsync` を「実行と結果集約」に寄せる。

## 2. 今回やったこと

1. `ThumbnailEngineExecutionPolicy` を追加した
2. `BuildThumbnailEngineOrder` を policy 側へ移した
3. `ShouldSkipFfmpegOnePassByKnownInvalidInput` を policy 側へ移した
4. `IsAutogenRetryEnabled` / `ResolveAutogenRetryCount` / `ResolveAutogenRetryDelayMs` / `IsAutogenTransientRetryError` を policy 側へ移した
5. `ThumbnailAutogenRetryDecision` を追加し、retry 判定結果だけを service へ返す形にした
6. `ThumbnailCreationService` から上記 helper 群を削除した
7. `AutogenRegressionTests` を新 policy 直接呼び出しへ更新した

## 3. 判断

- engine 順序は host 依存でも bitmap 依存でもなく、純粋に execution policy の責務である
- retry 判定も I/O 実装ではなく policy 側に寄せた方が、後で `ThumbnailCreationService` の本体を薄くしやすい
- 先に reflection 依存を減らしておくと、以降の分割で private method 名に縛られにくい

## 4. 今回やらないこと

1. movie meta cache の分離
2. DRM / unsupported placeholder 描画の分離
3. bitmap utility 群の別 helper 化
4. process log writer のさらなる再設計

## 5. 次の候補

1. input normalization と movie meta 解決を専用 resolver へ寄せる
2. failure placeholder 判定と result persistence を専用 writer へ寄せる
3. `CreateThumbAsync` の precheck 分岐を段階ごとに薄い helper へ分ける
