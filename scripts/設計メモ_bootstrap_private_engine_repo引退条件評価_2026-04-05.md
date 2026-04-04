# 設計メモ bootstrap_private_engine_repo 引退条件評価 2026-04-05

最終更新日: 2026-04-05

## 1. 目的

`bootstrap_private_engine_repo.ps1` をいつ縮退または削除できるかを、
感覚ではなく実績ベースで判定する。

この文書は、`設計メモ_bootstrap_private_engine_repo橋渡し扱い_2026-04-05.md`
で定めた引退条件に対する達成状況の評価メモである。

## 2. 判定結果

2026-04-05 時点の結論は次である。

- 通常運用の正面入口は、実質的に Private repo 側へ移せている
- ただし seed 正本の保守はまだ Public repo 側 bridge 資産に残っている
- したがって `bootstrap_private_engine_repo.ps1` は
  「日常運用には不要だが、seed 保守のためまだ引退しない」
  が妥当である

## 3. 引退条件ごとの評価

### 条件1: Private repo 側だけで初期セットアップ手順が完結している

判定: 達成

根拠:

- `%USERPROFILE%\\source\\repos\\IndigoMovieEngine\\docs\\運用ガイド_PrivateEngine初期化とrelease運用_2026-04-05.md`
  に build / publish / worker ZIP / workflow の入口が揃っている
- `%USERPROFILE%\\source\\repos\\IndigoMovieEngine\\scripts\\README.md`
  でも通常運用 script の入口が揃っている
- 日常の build / publish / release に `bootstrap_private_engine_repo.ps1` は不要である

### 条件2: Private repo 側 docs / workflow / seed が Public repo からの再生成を前提にしていない

判定: 未達

根拠:

- `scripts/private-engine-seed/` の正本はまだ Public repo 側にある
- `bootstrap_private_engine_repo.ps1` は、その seed を Private repo へコピーする橋渡しとして生きている
- したがって通常運用は Private 単独で回るが、seed 更新の正本保守はまだ Public 側へ残っている

### 条件3: Public repo 側の current state を seed 元として参照しなくても、Private repo の更新運用が安定して回る

判定: 条件付き達成

根拠:

- preview / release / rollback / compatibilityVersion 判断は、すでに Private repo 側 docs を正本にしている
- Private repo の build / publish / release asset 運用は、Public repo を経由せずに成立している
- ただし「Private repo を再生成する seed 更新」だけは Public repo 側 bridge に残っている

判断:

- 通常運用という意味では達成
- seed 保守まで含めると未達

### 条件4: 新規環境での Private repo 構築を、手順書と Private repo 正本だけで再現できる

判定: 達成

根拠:

- 既存 Private repo を clone した新規環境では、Private docs と scripts だけで
  build / test / publish / release の再現ができる
- 2026-04-04 から 2026-04-05 にかけて、Private repo 単独 build / test / publish の live 確認が積めている

補足:

- これは「既存 Private repo の利用開始」が再現できるという意味で達成である
- 「Public current state から Private repo を再生成する」用途は、まだ bridge script の担当である

### 条件5: 少なくとも数回の release / preview で bootstrap 再実行が不要だった

判定: 達成

根拠:

- preview run `23978177837`
- preview run `23979016211`
- preview run `23982259537`
- Public release `v1.0.3.4`
- Public release `v1.0.3.5`

上の live 成功では、Private publish artifact / Private release asset を使う本命経路が通っており、
その間に bootstrap 再実行は通常運用の前提になっていない。

## 4. いま残る本当の blocker

bootstrap 引退を止めている本当の点は、日常運用ではない。

残っているのは次だけである。

1. `scripts/private-engine-seed/` の正本所有がまだ Public repo 側にある
2. Private repo を更地から再生成する保険を、まだ Public 側 bridge が握っている

つまり blocker は runtime でも release でもなく、
「seed 保守の ownership」がまだ Public 側に残っていることだけである。

## 5. いまの実務判断

2026-04-05 時点では、次の扱いが最も安全である。

- `bootstrap_private_engine_repo.ps1` は残す
- ただし通常運用 docs の前面には出さない
- Public repo 側では bridge / 保険資産としてだけ扱う
- Phase 6 の残件は
  「bootstrap を消すこと」ではなく
  「seed ownership を Private 側へ完全移送できるか見極めること」
  と読む

## 6. 次に満たすべき条件

bootstrap を本当に引退候補へ進めるなら、次のどちらかが要る。

1. `scripts/private-engine-seed/` の正本を Private repo 側へ移す
2. もしくは seed 再生成自体をやめ、Private repo 単独保守へ固定する

このどちらかが済めば、
`bootstrap_private_engine_repo.ps1` は bridge から「履歴資産」へ下げやすくなる。

## 7. 参照

- `scripts/設計メモ_bootstrap_private_engine_repo橋渡し扱い_2026-04-05.md`
- `scripts/README.md`
- `Thumbnail/Docs/Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md`
- `%USERPROFILE%\\source\\repos\\IndigoMovieEngine\\docs\\運用ガイド_PrivateEngine初期化とrelease運用_2026-04-05.md`
- `%USERPROFILE%\\source\\repos\\IndigoMovieEngine\\docs\\運用ガイド_PrivateEngine_compatibilityVersion_preview_rollback_2026-04-05.md`
