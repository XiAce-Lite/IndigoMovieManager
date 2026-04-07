# 取り込み済み外部スキンAPI 利用集計 (2026-04-07)

## 対象

- 対象フォルダ
  - `skin\`
  - `Tests\IndigoMovieManager.Tests\TestData\WhiteBrowserSkins\`
- 集計対象文字列
  - `wb.<API名>` の参照を正規表現で抽出
  - `htm`, `html`, `js`, `css` を対象

## スキン構成

- `skin` 配下
  - `Compat`
  - `DefaultGridWB`
  - `SimpleGridWB`
- テストデータ配下
  - `TutorialCallbackGrid`
  - `WhiteBrowserDefaultBig`
  - `WhiteBrowserDefaultGrid`
  - `WhiteBrowserDefaultList`
  - `WhiteBrowserDefaultSmall`

> `WhiteBrowserDefault*` は実体として `WhiteBrowserSkins` 相当として集約されているため、集計上 `WhiteBrowserSkins` として表示されています。

## API 利用一覧

| API | 使用回数 | 使用スキン数 |
| --- | ---: | ---: |
| `onCreateThum` | 6 | 1 |
| `trace` | 6 | 1 |
| `focusThum` | 2 | 2 |
| `update` | 2 | 2 |
| `find` | 1 | 1 |
| `getDBName` | 1 | 1 |
| `getSkinName` | 1 | 1 |
| `onSetFocus` | 1 | 1 |
| `onSetSelect` | 1 | 1 |
| `onSkinEnter` | 1 | 1 |
| `onUpdate` | 1 | 1 |
| `scrollSetting` | 1 | 1 |

## API ごとの使用スキン

- `onCreateThum`: `WhiteBrowserSkins`
- `trace`: `SimpleGridWB`
- `focusThum`: `SimpleGridWB`, `WhiteBrowserSkins`
- `update`: `SimpleGridWB`, `WhiteBrowserSkins`
- `find`: `SimpleGridWB`
- `getDBName`: `SimpleGridWB`
- `getSkinName`: `SimpleGridWB`
- `onSetFocus`: `WhiteBrowserSkins`
- `onSetSelect`: `WhiteBrowserSkins`
- `onSkinEnter`: `WhiteBrowserSkins`
- `onUpdate`: `WhiteBrowserSkins`
- `scrollSetting`: `WhiteBrowserSkins`

## 補足

- 合計API数: 12
- 合計参照件数: 29
- 最も依存が高いAPIは `onCreateThum` と `trace`（各 6回）
