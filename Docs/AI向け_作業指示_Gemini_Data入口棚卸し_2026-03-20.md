# AI向け 作業指示 Gemini Data入口棚卸し 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは実装役ではない
- 今回は `Lane B: Data 入口集約` の棚卸しと論点整理だけを担当する
- コード変更はしない

## 2. 目的

- MainDB へ直接触っている入口を洗い出し、`Data DLL` へ寄せる順番を決める
- 実装役が迷わないように、read / write / special case を分けて短く整理する

## 3. 主に見る場所

- `DB\SQLite.cs`
- `Startup\StartupDbPageReader.cs`
- `Views\Main\MainWindow.xaml.cs`
- `Watcher\MainWindow.WatcherMainDbWriter.cs`
- `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`

## 4. 出してほしい成果物

- 新規ドキュメント 1 本
- 内容は次を含める
  - MainDB read 入口一覧
  - MainDB write 入口一覧
  - UI 直叩きの箇所
  - watch 専用 writer 化しやすい箇所
  - worker 側の read-only 化候補
  - 最初に facade 化すべき 3 入口

## 5. 制約

- コードは変更しない
- `*.wb` schema の話へ広げない
- queue DB と failure DB の全面設計へ脱線しない
- 1 回の棚卸しで全件完全網羅を目指さず、初動 3 着手に必要な粒度で止める

## 6. 完了条件

1. read / write / special case が分かれている
2. 実装優先順が 1 位から 3 位まで書かれている
3. 実装役が「どこを facade にするか」を追加調査なしで判断できる

## 7. 次へ渡す相手

- 調整役 Codex
