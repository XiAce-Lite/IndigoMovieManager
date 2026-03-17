# Implementation Plan ThumbInfo互換serializer化 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `ThumbnailSheetSpec` を追加し、WB互換メタへ流す純データを分離した
- `WhiteBrowserThumbInfoSerializer` を追加し、JPEG末尾メタの encode / decode を集約した
- `ThumbInfo` は互換 facade として残し、内部のバイナリ組み立てと復元を serializer 経由へ寄せた
- `ThumbnailCreationService` / `RescueWorkerApplication` / 生成エンジンのメタ追記を serializer 経由へ統一した

## 1. 目的

`ThumbInfo` に混在していた

- 秒配列と寸法を持つ DTO 的責務
- WhiteBrowser 互換バイナリを組み立てる serializer 的責務

を分ける。

今回は公開API を壊さず、`ThumbInfo` を互換 facade として残したまま、WB互換仕様の中心を `ThumbnailSheetSpec` + `WhiteBrowserThumbInfoSerializer` へ移す。

## 2. 今回やったこと

1. `ThumbnailSheetSpec` を追加した
2. `WhiteBrowserThumbInfoSerializer` を追加した
3. `ThumbInfo.NewThumbInfo()` は serializer に委譲する形へ変更した
4. `ThumbInfo.GetThumbInfo()` は serializer の decode 結果を facade へ反映する形へ変更した
5. JPEG 末尾への WB メタ追記は serializer 経由へ統一した
6. 互換テストに `ThumbnailSheetSpec` / serializer の確認を追加した

## 3. 判断

- WhiteBrowser 互換は「仕様」であり、`ThumbInfo` という1クラスに固定する必要はない
- 末尾メタの encode / decode を外へ出すと、`ThumbnailCreationService` と `RescueWorkerApplication` の責務が薄くなる
- facade を先に残すことで、既存 `ThumbInfo` 呼び出しを一気に壊さずに移行できる

## 4. 今回やらないこと

1. `ThumbInfo` public API の即時削除
2. `ThumbnailJobContext` の入力型を一気に `ThumbnailSheetSpec` へ変えること
3. WB互換フォーマット自体の変更

## 5. 次の候補

1. `ThumbnailJobContext` の入力を `ThumbnailSheetSpec` 基準へ寄せる
2. `RescueWorkerApplication` の `BuildExplicitThumbInfo` 周辺を spec 中心へ寄せる
3. `Tools` の CRC32 / 画像結合責務を分解する
