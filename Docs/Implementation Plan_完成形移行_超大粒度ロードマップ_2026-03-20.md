# Implementation Plan 完成形移行 超大粒度ロードマップ 2026-03-20

最終更新日: 2026-03-20

変更概要:
- 完成形の責務配置へ向かうための超大粒度ロードマップを新規作成
- `Watcher` と `Thumbnail` の 2026-03-20 引き継ぎ資料を前提に、分担しやすい実装レーンへ整理
- project 名の理想像と、今すぐ触るべき境界を分けて記述し、big bang 移行を避ける方針を固定

## 1. この資料の目的

- この資料は、完成形へ向かう全体計画を AI が超大粒度で共有するための計画書である。
- 目的は「今すぐ全部作り直す」ことではない。
- 目的は、`workthree` 本線の体感テンポを守りながら、責務を完成形の置き場所へ少しずつ寄せる順番を固定することである。

## 2. 完成形の再確認

完成形では次を揃える。

1. `IndigoMovieManager.App EXE` は UI shell と起動制御へ責務を絞る
2. MainDB 入口は `IndigoMovieManager.Data DLL` へ寄せる
3. 通常サムネと rescue の判断、進行、handoff は `IndigoMovieManager.Thumbnail.Core DLL` へ寄せる
4. 動画解析とデコード実装は `IndigoMovieManager.Media DLL` に閉じる
5. `Contracts DLL` は共有契約だけを持つ
6. `RescueWorker EXE` は本体専用ロジックの複製ではなく、共通 core を使う別 host へ揃える

## 3. 固定する移行原則

1. 最優先はユーザー体感テンポであり、設計の美しさだけで hot path を重くしない
2. `Watcher` の UI 詰まり防止と rescue 実動画検証は止めない
3. project 名の全面改称は最後に回し、先に責務境界を実コードで作る
4. MainDB の `*.wb` は変えない
5. 新しい DLL を作っても、最初は facade と契約だけに留め、処理本体の丸移植を急がない

## 4. 現状から完成形への責務マッピング

| 完成形の置き場 | いま主に残っている場所 | 最初に寄せる対象 |
|---|---|---|
| App | `IndigoMovieManager_fork.csproj`、`Views/Main/`、`Watcher/MainWindow.*`、`Thumbnail/MainWindow.*` | UI event、画面状態、host 起動設定 |
| Data | `DB/SQLite.cs`、`Startup/StartupDbPageReader.cs`、`Views/Main/MainWindow.xaml.cs` の read-only DB、`Watcher/MainWindow.WatcherMainDbWriter.cs`、`RescueWorkerApplication.cs` の MainDB read | MainDB read/write facade、DB switch 支援、watch 用 writer |
| Thumbnail.Core | `src/IndigoMovieManager.Thumbnail.Queue/`、`src/IndigoMovieManager.Thumbnail.Runtime/`、`Thumbnail/MainWindow.Thumbnail*`、`RescueWorkerApplication.cs` の rescue orchestration | queue 入口、failure handoff、rescue handoff、host 非依存 coordinator |
| Media | `Thumbnail/Decoders/`、`Thumbnail/Engines/`、`Thumbnail/Engines/IndexRepair/`、`Models/MovieInfo.cs` | decoder 契約、movie probe、index repair、engine router 依存 |
| Contracts | `src/IndigoMovieManager.Thumbnail.Contracts/` と `Watcher` / `Thumbnail` 内の暗黙契約 | DTO、interface、reason code、host 契約 |
| RescueWorker host | `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs` | host 引数解釈、結果保存、core 呼び出しだけへ縮小 |

## 5. エージェント運用方針

この計画は、調整役と実装役とレビュー役を分けて進める。

### 5.1 調整役

- 当エージェントは調整役に専念する
- 役割は次に限定する
  - 全体優先順位の維持
  - 各レーンへの作業切り出し
  - 境界条件と禁止線の明文化
  - 成果物の受け入れ判断
  - レビュー指摘の再振り分け
- 原則として、調整役は大きな実装を抱え込まない

### 5.2 実装役

- 実装役は 1 回の着手で 1 レーン、または 1 サブテーマだけを担当する
- 実装役へ渡す依頼には、必ず次を含める
  - 対象レーン
  - 変更してよいファイル帯
  - 触ってはいけない境界
  - 完了条件
  - 最低限の確認手順
- 複数レーンをまたぐ変更は、調整役が分割してから渡す

### 5.3 コードレビュー専任役

- レビュー専任エージェントを必ず 1 本使う
- レビュー専任役は実装を持たず、変更後の差分だけを見る
- 主眼は次に固定する
  - バグ
  - 責務逆流
  - 体感テンポ悪化の芽
  - テスト不足
  - 将来の完成形と逆向きの依存
- レビュー結果は「重大度順の finding first」で返す

### 5.4 推奨の役割割り当て

- Codex 系
  - 調整役
  - 境界整理が必要な実装タスクの切り出し
- Gemini 系
  - 調査
  - 資料整理
  - 着手前の論点整理
  - 実装前提の短文化
- Claude / Opus 系
  - コードレビュー専任
  - 差分レビュー
  - リスク列挙
  - 回帰観点の補強

### 5.5 1 サイクルの進め方

1. 調整役が 1 テーマを 1 レーンへ切る
2. 実装役が対象レーンだけを変更する
3. レビュー専任役が finding first でレビューする
4. 調整役が修正要否を判断し、必要なら再度実装役へ返す
5. 受け入れ条件を満たしたものだけ次レーンへ進める

### 5.6 現在の調整役モード

- 現在の Codex は調整役に固定する
- 以後の大帯実装は、原則としてサブエージェントへ配る
- 調整役が自分で触るのは次に限定する
  - 運用ボード更新
  - タスク切り出し
  - 差分受け入れ
  - 部分 staging が必要な最終整理
- レビューは必ずレビュー専任役へ流す

## 6. サブエージェント分担レーン

この計画は、次の 6 レーンで並走させる。

### Lane A: App shell 薄化

- 担当範囲
  - `Views/Main/`
  - `Watcher/MainWindow.*`
  - `Thumbnail/MainWindow.*`
- 役割
  - UI event と host 起動制御だけを残す
  - `MainWindow` から DB / queue / rescue / decode の直接判断を外へ逃がす
- 直近の起点
  - `Watcher` 引き継ぎ資料で残っている `CheckFolderAsync` orchestration の薄化
  - `Thumbnail/MainWindow.*` に残る rescue 起動判断の host 化

### Lane B: Data 入口集約

- 担当範囲
  - `DB/SQLite.cs`
  - `Startup/StartupDbPageReader.cs`
  - `Watcher/MainWindow.WatcherMainDbWriter.cs`
  - `RescueWorkerApplication.cs` の MainDB read-only 部
- 役割
  - MainDB read/write の入口を `Data` 側 facade へ寄せる
  - UI や worker から SQL 直叩きを減らす
- 直近の起点
  - read-only query を先に切り出し、schema 変更なしで使い回せる形へ揃える

### Lane C: Thumbnail.Core 集約

- 担当範囲
  - `src/IndigoMovieManager.Thumbnail.Queue/`
  - `src/IndigoMovieManager.Thumbnail.Runtime/`
  - `Thumbnail/MainWindow.ThumbnailQueue.cs`
  - `Thumbnail/MainWindow.ThumbnailFailureSync.cs`
  - `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs` の orchestration 部
- 役割
  - 通常 queue と rescue queue の判断を host 非依存 coordinator に寄せる
  - App と RescueWorker の両方が同じ core 入口を使う形へ寄せる
- 直近の起点
  - 既に固定済みの `Factory + Interface + Args` を入口として守りつつ、handoff 判定を外へ出す

### Lane D: Media 実装隔離

- 担当範囲
  - `Thumbnail/Decoders/`
  - `Thumbnail/Engines/`
  - `Models/MovieInfo.cs`
- 役割
  - decode / probe / index repair を UI と queue から切り離す
  - FFmpeg / FFMediaToolkit / OpenCvSharp 依存を Media 側へ閉じる
- 直近の起点
  - `MovieInfo` と thumbnail decoder で重複する動画解析責務を棚卸しし、共通の media contract を決める

### Lane E: RescueWorker host 化

- 担当範囲
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/`
- 役割
  - `RescueWorkerApplication` を巨大オーケストレータから host へ縮める
  - 引数解釈、artifact 保存、終了コード決定だけを host に残す
- 直近の起点
  - MainDB 参照と failure/rescue 判定の core 側移管

### Lane F: Contracts と guard 固定

- 担当範囲
  - `src/IndigoMovieManager.Thumbnail.Contracts/`
  - architecture test、public request test、watch / rescue の guard test
- 役割
  - 暗黙契約を明示化し、戻してはいけない責務を test で固定する
  - project 分離の途中でも入口の破綻を防ぐ
- 直近の起点
  - `Thumbnail` で既にある architecture test の考え方を Data / Core / Media 境界にも広げる

## 7. 実施順の大マイルストーン

### M1: 境界の先行固定

- `Watcher` と `Thumbnail` の現行 handoff を崩さず、App 直結の重い判断を facade / coordinator 越しへ寄せ始める
- この段階では project 名は変えない
- 完了条件
  - `MainWindow` と `RescueWorkerApplication` の新規直書き責務が増えない

### M2: Data と Core の入口成立

- MainDB 入口を `Data` 側へまとめ始める
- 通常 queue と rescue handoff を `Core` 側の host 非依存入口へ寄せる
- 完了条件
  - App と worker が SQL 詳細を知らずに主要経路を呼べる
  - rescue 判定が UI 専用ロジックとして散らばらない

### M3: Media の独立

- decode / probe / repair を `Media` 側へ寄せる
- App / Core は engine 実装詳細ではなく契約だけを見る
- 完了条件
  - FFmpeg / FFMediaToolkit / OpenCvSharp 依存が App 直下から外れ始める

### M4: host の最終整理

- `RescueWorker` を共通 core 利用 host へ揃える
- App も同じ core 入口を使う
- project 名の整理はこの後でよい
- 完了条件
  - App と worker の重複 orchestration が説明可能な最小量まで減る

## 8. 最初の 3 連続着手

1. Lane B で MainDB read facade の棚卸しを作る
2. Lane C で queue / rescue handoff 判定の host 依存を洗う
3. Lane A で `Watcher` の `CheckFolderAsync` 残責務をさらに薄くし、Data / Core へ受け渡しやすい形へする

この 3 本は、今の `workthree` 優先順位と衝突しにくい。
特に `Watcher` handoff と `ThumbnailCreationService` 境界整理の到達点を壊さずに前へ進める。

## 8.1 次フェーズの 3 連続着手

1. Lane A / C 境界として `ThumbnailError` 下段タブの露出固定を片付ける
2. Lane A の本命として `Watcher` 責務分離 Phase2 を進める
3. App shell 薄化の一環として `UI hang` 通知を host 接続だけへ整理する

この 3 本は、完成形へ向けた大帯だが、同時に 1 本へ混ぜると `MainWindow.xaml.cs` で衝突しやすい。
そのため、次フェーズはサブエージェントごとに帯を固定し、レビュー専任役で別々に受ける。

## 9. この段階ではやらないこと

- project 名の一斉 rename
- MainDB schema の変更
- `future` 実験線の大規模取り込み
- `RescueWorkerApplication` の一気削除
- `MainWindow` からの責務引き剥がしを、観測性なしで同時多発させること

## 10. 受け入れ判断

この超大粒度計画に沿う変更は、次を満たす時だけ採る。

1. UI 体感テンポが悪化しない
2. `Watcher` の `visible-only gate`、deferred batch、UI 抑制の整合を壊さない
3. `ThumbnailCreationService` の `Factory + Interface + Args` 境界を壊さない
4. rescue の timeout handoff と failure handoff を説明し続けられる
5. App へ責務を戻さない

## 11. 関連資料

- `AI向け_現在の全体プラン_workthree_2026-03-20.md`
- `Watcher\Docs\AI向け_引き継ぎ_Watcher責務分離_UI詰まり防止_2026-03-20.md`
- `Thumbnail\Docs\AI向け_引き継ぎ_Thumbnail基盤整理と次着手_2026-03-20.md`
- `Docs\人間向け_大粒度フロー_DBとプロジェクト_現状と完成形_2026-03-20.md`
- `Docs\Architecture_DLL_Separation_Plan_2026-03-02.md`
