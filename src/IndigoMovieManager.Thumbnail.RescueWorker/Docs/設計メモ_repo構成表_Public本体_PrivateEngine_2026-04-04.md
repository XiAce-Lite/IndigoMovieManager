# 設計メモ repo構成表 Public本体 PrivateEngine 2026-04-04

最終更新日: 2026-04-04

## 1. 目的

外だし後の repo 構成を、人名・アカウント名・URL を含めずに固定するための正本である。

この文書では、次を明確にする。

1. Public repo に残す責務
2. Private repo へ出す責務
3. 両 repo の接続境界
4. release / artifact / 運用の役割分担

## 2. 先に結論

repo は 2 つに分ける。

- Public repo: 本体アプリ
- Private repo: 外だし engine / worker

最初の分離単位は次である。

- Public repo
  - `IndigoMovieManager`
- Private repo
  - `IndigoMovieEngine`

ここでは repo owner、organization、実 URL は書かない。
個人情報と運用先情報は別管理にする。

## 3. repo構成表

| 区分 | repo 名 | 公開範囲 | 主責務 | 主な中身 | release の見え方 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 本体 | `IndigoMovieManager` | Public | app に機能を追加し、配る | WPF、本体設定、worker 起動 client、app package | 利用者向け app ZIP を公開 |
| engine | `IndigoMovieEngine` | Private | サムネ作成 engine、救済 worker、FailureDb、artifact 生成 | `Contracts`、`Engine`、`FailureDb`、`RescueWorker`、engine tests | worker package / preview artifact を内部運用 |

## 4. Public repo の情報

### 4.1 目的

app に機能を追加し、利用者へ安定して配る。

### 4.2 含めるもの

- WPF UI
- 設定画面
- tab / list / player / overlay
- launcher
- app への機能追加
- worker 起動設定
- app release workflow
- app package
- worker lock file 読取
- compatibilityVersion fail-fast

### 4.3 含めないもの

- worker の実処理本体
- repair / rescue の重い実装詳細
- engine 側の実験用 CI
- 捨て tag 前提の試験 release

### 4.4 Public repo の利用者向け成果物

- app package
- app release notes

利用者が通常取得するのは app package だけとする。

## 5. Private repo の情報

### 5.1 目的

engine / worker を閉じた環境で高速改善し、preview 検証を安全に回す。

### 5.2 含めるもの

- `Contracts`
- `Engine`
- `FailureDb`
- `RescueWorker`
- worker artifact 生成
- contract tests
- engine 単体 tests
- preview package / preview artifact 発行

### 5.3 含めないもの

- WPF UI
- app 固有の View / ViewModel
- 本体 release asset
- 利用者向け画面文言の最終責務

### 5.4 Private repo の運用成果物

- worker artifact
- engine package
- contract preview
- 内部検証用 release

## 6. 接続境界

Public repo と Private repo の正式境界は次である。

- `CLI + JSON`
- `compatibilityVersion`
- worker artifact 配置規約
- `rescue-worker.lock.json`

v1 では `RescueWorker` main mode だけを外部契約にする。

```powershell
indigo-engine rescue --job-json <path> --result-json <path>
```

この時の `job.json` 正本フィールドは次である。

- `contractVersion`
- `mode`
- `requestId`
- `mainDbFullPath`
- `thumbFolderOverride`
- `logDirectoryPath`
- `failureDbDirectoryPath`
- `requestedFailureId`

## 7. ディレクトリ構成の目安

### 7.1 Public repo

```text
IndigoMovieManager/
  Views/
  ViewModels/
  BottomTabs/
  UpperTabs/
  Thumbnail/
  Watcher/
  scripts/
  Docs/
```

### 7.2 Private repo

```text
IndigoMovieEngine/
  src/
    IndigoMovieManager.Thumbnail.Contracts/
    IndigoMovieManager.Thumbnail.Engine/
    IndigoMovieManager.Thumbnail.FailureDb/
    IndigoMovieManager.Thumbnail.RescueWorker/
  tests/
  scripts/
  docs/
  .github/workflows/
```

## 8. release / artifact 分担

| 項目 | Public repo | Private repo |
| :--- | :--- | :--- |
| app release | 正本 | 非担当 |
| worker artifact | lock file で参照 | 正本 |
| compatibilityVersion | 読取と fail-fast | 定義と bump |
| 利用者向け asset | app ZIP | 原則出さない |
| preview 検証 | 最小限 | 主担当 |

## 9. 個人情報を入れないルール

この repo 構成表には次を書かない。

- GitHub の owner 名
- organization 名
- 個人名
- メールアドレス
- 個人用 URL
- ローカルユーザー名入り絶対 path

必要な時は、別の非公開運用資料で持つ。

## 10. 実務判断

この構成の狙いは、Public repo を「app に機能を追加し、配る」責務へ集中させつつ、Private repo で engine を攻めて改善できるようにすることである。

大事なのは次の 3 点である。

1. Public repo は app に機能を追加し、配る責務に集中する
2. Private repo は engine / worker の改善速度を優先する
3. 両者の間は `CLI + JSON + compatibilityVersion` に閉じ込める

2026-04-04 時点では、

- `scripts/bootstrap_private_engine_repo.ps1`
- sibling `IndigoMovieEngine`

を使った source / docs / assets / solution 同期と、Private repo 単体 build / worker artifact publish のローカル確認まで通っている。
さらに local では `git init` と workflow seed 配置まで完了しており、残るのは remote 接続と CI 実走である。

## 11. 参照先

- `scripts/bootstrap_private_engine_repo.ps1`
  - Private repo の初期フォルダ構成と docs 同期を作る入口
- `Thumbnail/Docs/Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_RescueWorker_v1契約_PrivateRepo前提_2026-04-04.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/TASK-007_外部repo最小構成とCI最小フロー_2026-04-03.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/TASK-008_main repo残置責務とexternal worker運用_2026-04-03.md`
