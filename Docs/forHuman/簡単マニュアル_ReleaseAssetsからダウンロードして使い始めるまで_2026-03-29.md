# IndigoMovieManager かんたんマニュアル

最終更新日: 2026-03-29

## 1. この文書の役割

この文書は、GitHub Releases の `Assets` から配布 ZIP をダウンロードして、最初の起動確認まで進めるための短い手順書です。

## 2. はじめに

- このアプリは ZIP を展開して使います
- `exe` 単体ではなく、展開したフォルダごと扱ってください
- WhiteBrowser の DB を使う場合は、元の DB を直接開かず、必ずコピーした `*.wb` を使ってください
- 同じフォルダ内にコピーを作って、そのコピー側を開けば大丈夫です

## 3. 使い方

- GitHub の Release ページを開きます
- `Assets` を開きます
- フォーク版の Release ページ:
  https://github.com/T-Hamada0101/IndigoMovieManager_fork/releases
- アプリ本体の ZIP ファイルをダウンロードします
- ダウンロードした ZIP を任意のフォルダに展開します
- 展開したフォルダ内の `IndigoMovieManager.exe` を起動します
- ここから先は、使い始め方で 2 つに分かれます

### 3.1 WhiteBrowser から移行する場合

- 使いたい `*.wb` をコピーします
- アプリ起動後、コピーした `*.wb` を画面にドロップすると開きます
- 動画一覧が表示されることを確認します

### 3.2 新規開始

- 画面に動画フォルダをドロップします
- 新しい DB 作成ダイアログが開きます
- 作成した `*.wb` を保存します
- フォルダ解析が開始されます
- 動画一覧が表示されることを確認します
- サムネイルが作成されることを確認します

## 5. 注意

- 元の WhiteBrowser DB を直接試さないでください

## 6. 起動しない時

- まず `.NET 8 Desktop Runtime` の有無を確認してください

- `Windows の設定` から確認する場合
- `設定`
- `アプリ`
- `インストールされているアプリ`
- 一覧で `.NET` と検索する
- `.NET 8 Desktop Runtime` が入っているか確認する

- コマンドで確認する場合
- PowerShell を開く
- 次を実行します
```powershell
dotnet --list-runtimes
```

- 表示結果に `Microsoft.WindowsDesktop.App 8.x.x` があれば OK です
```text
Microsoft.WindowsDesktop.App 8.x.x [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
```

- 見つからない場合
- `.NET 8 Desktop Runtime` をインストールしてください(最新版でOK)
- 公式ダウンロードページ:
  https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0
- 直リンク：
  https://dotnet.microsoft.com/ja-jp/download/dotnet/thank-you/runtime-8.0.25-windows-x64-installer
- `.NET デスクトップ ランタイム` の`Windows x64 インストーラー` を選びます
- インストール後に、もう一度アプリを起動してください

- 開発者向け補足
- `.NET 8 SDK` が入っていれば、Runtime も含まれています

## 7. 困った時

- 問題が出た時はログを確認してください
- ログの保存先は `%LOCALAPPDATA%\IndigoMovieManager\logs\` です
