# ThumbnailCreationService host別 factory化 実装計画

最終更新日: 2026-03-19

## 目的

- `MainWindow` と `RescueWorker` に残っている service 組み立て責務を、巨大クラス本体から外す
- host 固有の runtime / process log writer / provider / logger の組み合わせを、名前付き factory へ閉じ込める

## 今回の実装

- `Thumbnail/AppThumbnailCreationServiceFactory.cs` を追加した
  - アプリ本体向けの `DefaultThumbnailCreationHostRuntime`
  - `DefaultThumbnailCreateProcessLogWriter`
  - `AppVideoMetadataProvider`
  - `AppThumbnailLogger`
  をまとめて組み立てる
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerThumbnailCreationServiceFactory.cs`
  を追加した
  - rescue worker 向けの host runtime と process log writer をまとめて組み立てる
- `Views/Main/MainWindow.xaml.cs`
  と `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  から private な `CreateThumbnailCreationService()` を削除した

## 効果

- 巨大クラス側は「使う」ことに集中でき、組み立て責務が薄くなる
- host 固有の違いが factory 名で読める
- `Factory + Interface + Args` の公開面整理を、host ごとの composition root にも反映できる
