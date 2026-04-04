# 設計メモ bootstrap_private_engine_repo 引退 2026-04-05

最終更新日: 2026-04-05

## 1. 結論

`bootstrap_private_engine_repo.ps1` と `scripts/private-engine-seed/` は、
2026-04-05 時点で Public repo から引退させる。

以後の正面入口は次に固定する。

- Public repo
  - app に機能を追加し、配る
- Private repo
  - clone して build / publish / release する

## 2. 引退理由

次がすでに満たせているためである。

1. Private repo が GitHub 上に実在し、正本として push / CI / release が回っている
2. Private repo 側 docs だけで build / publish / preview / rollback を説明できる
3. Public repo 側 preview / release も、Private publish artifact / Private release asset を pin して live 成功している
4. Public repo 側に seed 正本を残さなくても、通常運用と本番 release が成立している

## 3. 今後の初期化方法

新規環境では、Public repo の bootstrap を使わない。

やることは次だけである。

1. Private repo を clone する
2. `%USERPROFILE%\\source\\repos\\IndigoMovieEngine\\docs\\運用ガイド_PrivateEngine初期化とrelease運用_2026-04-05.md`
   を開く
3. `scripts/build_private_engine.ps1`
   `scripts/publish_private_engine.ps1`
   `scripts/create_rescue_worker_artifact_package.ps1`
   を Private repo 側で使う

## 4. Public repo に残すもの

引退後も Public repo に残すのは、consumer 側責務だけである。

- launcher
- lock / verify
- Private artifact / release asset の同期
- app package 作成
- app release workflow

## 5. 履歴の扱い

bootstrap は失敗ではなく、移行橋渡しとして役目を終えた。

したがって今後は

- 実行 script としては残さない
- seed 正本も Public では持たない
- ただしこの引退メモは履歴として残す

## 6. 参照

- `scripts/README.md`
- `Thumbnail/Docs/Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/TASK-008_main repo残置責務とexternal worker運用_2026-04-03.md`
- `Private repo: %USERPROFILE%\\source\\repos\\IndigoMovieEngine\\docs\\運用ガイド_PrivateEngine初期化とrelease運用_2026-04-05.md`
- `Private repo: %USERPROFILE%\\source\\repos\\IndigoMovieEngine\\docs\\運用ガイド_PrivateEngine_compatibilityVersion_preview_rollback_2026-04-05.md`
