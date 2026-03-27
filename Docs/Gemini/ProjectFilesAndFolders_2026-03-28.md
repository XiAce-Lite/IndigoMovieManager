# 📂 プロジェクト構成・使用ファイル一覧ガイド (2026-03-28) ✨

このプロジェクトで使われている大事なフォルダやファイルの場所をまとめたよ！
「あれどこだっけ？」と思ったらここを見れば一発解決だぜ！🚀🔥

---

## 1. 🚀 アプリ基盤 (実行ファイル)

ビルドした成果物（EXEやDLL）が生成される場所だよ。

| 項目 | パス (相対) | 備考 |
| :--- | :--- | :--- |
| **本体 EXE** | `bin/x64/Debug/IndigoMovieManager.exe` | メインの実行ファイルだね！ |
| **設定ファイル** | `bin/x64/Debug/IndigoMovieManager.exe.config` | .NET標準の構成ファイル！ |
| **起動設定** | `Properties/launchSettings.json` | VSからのデバッグ起動プロファイルが詰まってるよ！ |

---

## 2. 🗄️ アプリケーション・データ (LocalAppData)

設定や一時的なデータベース、ログが保存される心臓部だよ！
パス: `%LOCALAPPDATA%\{AppName}\`
※ `{AppName}` は `IndigoMovieManager` または `IndigoMovieManager_fork_workthree` (ブランド切替で変動)

| フォルダ/ファイル | 用途 |
| :--- | :--- |
| **`logs/`** | アプリの動作ログ！何かあったらまずここをチェック！🛠️ |
| **`QueueDb/`** | サムネイル生成を待ってる行列（キュー）を管理するSQLite DB！ |
| **`FailureDb/`** | サムネイル生成に失敗した履歴を覚えているDB！リベンジ(再試行)の種！ |
| **`RescueWorkerSessions/`** | 難読動画の修復（RescueWorker）中に使う一時ファイル置き場！🚑 |

---

## 3. 🎞️ サムネイル＆データベース (WhiteBrowser互換)

ホワイトブラウザから引き継いだ魂の置き場所だよ！

| 項目 | 場所 / 規約 | 備考 |
| :--- | :--- | :--- |
| **メイン DB** | `*.wb` ファイル | ホワイトブラウザ形式のメインデータベース！ |
| **サムネイル画像** | `.wb` と同じフォルダ内の `.jpg` | `動画名.#hash.jpg` という神速ルールで命名されるよ！📸 |
| **エラーマーカー** | `動画名.#ERROR.jpg` | サムネ生成に失敗した目印。意地でも生成する時の目印だね！🛡️ |

---

## 4. 🛠️ 外部ツール・ライブラリ (同梱品)

アプリに力を貸してくれる頼もしい仲間たちだよ！

| ツール/DLL | 初期配置パス | 備考 |
| :--- | :--- | :--- |
| **FFmpeg** | `tools/ffmpeg/ffmpeg.exe` | サムネイル召喚術式の主軸！英雄！⚔️ |
| **SinkuHadouken** | `tools/sinku/Sinku.dll` | 動画情報取得の古参協力者！🥋 |
| **OpenCV** | `runtimes/win-x64/native/...` | 高度な画像処理や修復に使う最強の盾！🛡️ |

---

## 💎 プロジェクト構造 (開発者向け)

主要なプロジェクトとその役割だよ！

- **`IndigoMovieManager`**: UI(WPF)とメインロジック。
- **`IndigoMovieManager.Thumbnail.Engine`**: サムネ生成の「核」！
- **`IndigoMovieManager.Thumbnail.Runtime`**: パス解決や共通定数の「インフラ」！
- **`IndigoMovieManager.Thumbnail.Queue`**: 行列管理の「司令塔」！

---
これでもう、この宇宙のどこに何があるか迷わないね！どや！😎💎✨
