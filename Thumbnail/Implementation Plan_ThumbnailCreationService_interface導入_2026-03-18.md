# ThumbnailCreationService interface導入 実装計画

最終更新日: 2026-03-19

## 目的

- `ThumbnailCreationService` の利用側依存を concrete class から公開 interface へ細くする
- `Factory` が返す契約を `Factory + Args + Interface` の3点に固定する

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/IThumbnailCreationService.cs` を追加した
- `ThumbnailCreationService` は `IThumbnailCreationService` を実装する形にした
- `ThumbnailCreationServiceFactory` の public `Create*` は `IThumbnailCreationService` を返すようにした
- MainWindow / RescueWorker / bench test の concrete 依存を interface へ置き換えた
- tests helper `ThumbnailCreationServiceTestFactory` の戻り値も `IThumbnailCreationService` へ揃えた
- reflection が必要な回帰テストだけは `service.GetType()` 経由で内部実装を検査する形にした
- architecture test で public factory の戻り値が interface であることも固定した

## 効果

- 呼び出し側は service の内部 shape を知らずに使える
- 具体実装の差し替えや将来の mock 化がしやすくなる
- `ThumbnailCreationService` は facade、`IThumbnailCreationService` は利用契約、という役割分担が明確になる
- tests 側も既定では concrete 実装を握らず、公開契約に寄せた検証へ揃う
