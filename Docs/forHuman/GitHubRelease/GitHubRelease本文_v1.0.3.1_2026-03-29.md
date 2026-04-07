# IndigoMovieManager v1.0.3.1

アセンブリ version は `1.0.3.1` です。

## 主な更新

- `*.wb` をメイン画面へドロップして、DB を開けるようにしました
- DB 未選択時に動画フォルダをドロップすると、新しい DB 作成へ進めるようにしました
- Release Assets から使い始めるまでの簡単マニュアルを追加しました
- README に動作風景動画への導線を追加しました
- 配布 ZIP から `*.pdb` を除外し、利用者向けの内容に整理しました

## 使い始め方

- Release の `Assets` には、同梱版のアプリ本体 ZIP を載せます
- `rescue-worker` はアプリ本体 ZIP の中に入っています
- ZIP を展開して、`IndigoMovieManager.exe` を起動してください
- 利用だけなら `.NET 8 Desktop Runtime` が必要です
- 開発者は `.NET 8 SDK` が入っていれば、Runtime も含まれています

## 使い方

### WhiteBrowser から移行する場合

- 使いたい `*.wb` をコピーします
- アプリ起動後、コピーした `*.wb` を画面にドロップすると開きます

### 新規開始

- 画面に動画フォルダをドロップします
- 新しい DB 作成ダイアログが開きます

## 注意

- 元の WhiteBrowser DB を直接開かず、必ずコピーした `*.wb` を使ってください
- `rescue-worker` はアプリ本体 ZIP の `rescue-worker` フォルダへ同梱されています
- 起動しない時は、まず `.NET 8 Desktop Runtime` の有無を確認してください

## 参考

- フォーク版 Releases:
  https://github.com/T-Hamada0101/IndigoMovieManager_fork/releases
- かんたんマニュアル:
  https://github.com/T-Hamada0101/IndigoMovieManager_fork/blob/master/Docs/forHuman/%E7%B0%A1%E5%8D%98%E3%83%9E%E3%83%8B%E3%83%A5%E3%82%A2%E3%83%AB_ReleaseAssets%E3%81%8B%E3%82%89%E3%83%80%E3%82%A6%E3%83%B3%E3%83%AD%E3%83%BC%E3%83%89%E3%81%97%E3%81%A6%E4%BD%BF%E3%81%84%E5%A7%8B%E3%82%81%E3%82%8B%E3%81%BE%E3%81%A7_2026-03-29.md
- 動作風景動画:
  https://youtu.be/kvj_862G4iI
- .NET 8 ダウンロード:
  https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0
