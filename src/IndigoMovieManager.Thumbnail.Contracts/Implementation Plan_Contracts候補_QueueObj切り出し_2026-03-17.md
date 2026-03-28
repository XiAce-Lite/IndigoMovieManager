# Implementation Plan Contracts候補 QueueObj切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `QueueObj` を `Thumbnail` 直下から `IndigoMovieManager.Thumbnail.Contracts` へ移した
- `App / Engine / Queue / RescueWorker / Tests` の参照を `Contracts` へ明示化した
- `QueueObj` に `Priority` を追加し、`優先 / 通常` の最小Queue入力を共有DTOへ寄せた
- `QueueObj.Priority` は通常Queueだけでなく rescue 要求入口でも再利用する整理にそろえた
- `ThumbnailRequest` を追加し、`QueueObj` を後方互換 facade として扱える形にした
- `ThumbnailCreationService` の本流入力を `ThumbnailRequest` でも受けられるようにした
- `ThumbnailJobContext` と `ThumbnailProgressRuntime` を `ThumbnailRequest` 主契約へ寄せた

## 1. 目的

`QueueObj` を、worker・queue・app が共有する最小DTOとして `Contracts` 側へ寄せる。
その上で、内部実装は `QueueObj` という legacy 名から少しずつ離し、中立な入力契約へ移す。

今回の狙いは名前変更ではない。
まず所属先を分離し、`QueueObj` が `Engine` 所有物ではない状態を作ることが先である。

## 2. 今回やったこと

1. `src/IndigoMovieManager.Thumbnail.Contracts` を新設した
2. `QueueObj.cs` を `Contracts` プロジェクトへ移した
3. `Engine.csproj` から `QueueObj.cs` のリンクコンパイルを外した
4. `Queue / RescueWorker / App / Tests` から `Contracts` を参照する形へ揃えた
5. solution へ `Contracts` プロジェクトを追加した
6. `ThumbnailRequest.cs` を追加し、生成入力の中立契約を定義した
7. `QueueObj` は `ThumbnailRequest` を内包する facade とし、既存プロパティを維持した
8. `ThumbnailCreationService` は `ThumbnailRequest` 本流 + `QueueObj` wrapper の二段入口にした
9. `QueueObj.Priority` は rescue 要求の入口でも再利用し、共有DTOを増やさずに `優先 / 通常` を通せる形へそろえた
10. `ThumbnailJobContext` は `Request` を主契約にし、`QueueObj` initializer は互換 setter として残した
11. `ThumbnailProgressRuntime` は `ThumbnailRequest` overload を追加し、進捗系の内部更新を legacy DTO から切り離し始めた

## 3. 判断

`QueueObj` は次の理由で `Contracts` 候補として適切である。

- UI ロジックを持たない
- queue / worker / app / tests で共有されている
- 動画パス、タブ番号、サイズ、ハッシュ、Queue優先度などの最小入力だけを持つ

## 4. 今回やらないこと

1. `QueueObj` の名前変更
2. Queue優先度を超える新しい業務責務を `QueueObj` に追加すること
3. `TabInfo` や `ThumbnailPathResolver` まで同時に移すこと
4. 公開API から `QueueObj` を即時削除すること

## 5. 次の候補

1. queue / worker / progress の受け渡しを `ThumbnailRequest` 基準へ順次寄せる
2. `TabInfo` を layout と path policy に分割する
3. `ThumbnailPathResolver` の `TabInfo` 依存を薄くする
4. `ThumbnailCreationService` の app runtime 依存を抜く
5. `QueueObj.Priority` の利用箇所を `優先 / 通常` の2値で固定し、設定増殖を防ぐ
6. rescue 系での `QueueObj.Priority` 利用もこの2値前提で維持し、別Priority契約を増やさない
7. `ThumbnailQueueProcessor` の callback / lease 経路も `ThumbnailRequest` 主契約へ寄せる
