# Implementation Plan: topフォルダ徹底整理

更新日: 2026-03-15

## 1. 目的

トップフォルダから .cs / .xaml を完全に排除し、機能別サブフォルダへ分配する。
複数人開発を前提に、「どこを見ればいいか」が一目で分かる構成にする。

## 2. 前提

- ファイル移動は Visual Studio 2026 の「移動」機能で行う（名前空間・参照の自動書き換えが効く）
- partial class の分割ファイルは同一フォルダに置く（コンパイラ制約）
- 既存の `BottomTabs/` の機能別サブフォルダ構成を手本とする
- `src/` 配下の DLL 分離済みプロジェクトは対象外

## 3. 現状（トップ直下のコードファイル: 28本）

### MainWindow 部分クラス群（9本）
| ファイル | 機能 |
|---------|------|
| MainWindow.xaml | メインウィンドウ XAML |
| MainWindow.xaml.cs | メインウィンドウ code-behind |
| MainWindow.DbSwitch.cs | DB切り替え |
| MainWindow.MenuActions.cs | ファイルコピー/移動メニュー |
| MainWindow.Player.cs | プレイヤー起動/タイムスライダー |
| MainWindow.Search.cs | 検索コンボボックス処理 |
| MainWindow.Selection.cs | リスト選択→詳細パネル連動 |
| MainWindow.Tag.cs | タグ一括操作 |
| MainWindow.ThumbnailFailedTab.cs | サムネ失敗タブ（FailureDb連携） |

### ダイアログ Window 群（5ペア = 10本）
| ファイル | 機能 |
|---------|------|
| CommonSettingsWindow.xaml/.cs | 全体共通設定 |
| SettingsWindow.xaml/.cs | 個別DB設定 |
| MessageBoxEx.xaml/.cs | カスタムメッセージボックス |
| RenameFile.xaml/.cs | ファイルリネーム |
| TagEdit.xaml/.cs | タグ編集 |

### 別フォルダで持つべき Window（1ペア = 2本）
| ファイル | 機能 |
|---------|------|
| WatchWindow.xaml/.cs | 監視フォルダ設定（Watcher機能に属する） |

### App エントリ（2本）
| ファイル | 機能 |
|---------|------|
| App.xaml | Application 定義 |
| App.xaml.cs | FirstChanceException 等 |

### ユーティリティ・モデル（4本）
| ファイル | 機能 |
|---------|------|
| AssemblyInfo.cs | アセンブリ属性定義 |
| DebugRuntimeLog.cs | DEBUG用ログ出力 |
| FileNameValidationRule.cs | WPF ValidationRule |
| History.cs | 検索履歴モデル（INotifyPropertyChanged） |
| TreeSource.cs | ツリービューデータソース |

## 4. 新フォルダ構成

```
IndigoMovieManager_fork_workthree/
│
│  ─── トップ直下に残すもの ───
│  App.xaml                          ← WPF 起点（VS が要求する位置）
│  App.xaml.cs
│  App.config
│  AssemblyInfo.cs                   ← GlobalAttribute（VS が要求する位置）
│  IndigoMovieManager_fork.csproj
│  IndigoMovieManager_fork.sln
│  .editorconfig / .gitignore / .gitattributes
│  AGENTS.md / .CLAUDE.md / .GEMINI.md / .CODEX.md
│  README.md / LICENSE.txt
│  AI向け_ブランチ方針_*.md
│  layout*.xml                       ← AvalonDock レイアウト（実行パス相対で読む）
│
│  ─── 新設・再編フォルダ ───
│
├─ Views/                            ★ 新設: メイン画面 + 全ダイアログ View
│  ├─ Main/                          メイン画面の部分クラス群
│  │   ├─ MainWindow.xaml
│  │   ├─ MainWindow.xaml.cs
│  │   ├─ MainWindow.DbSwitch.cs
│  │   ├─ MainWindow.MenuActions.cs
│  │   ├─ MainWindow.Player.cs
│  │   ├─ MainWindow.Search.cs
│  │   ├─ MainWindow.Selection.cs
│  │   └─ MainWindow.Tag.cs
│  │
│  ├─ Settings/                      設定ダイアログ群
│  │   ├─ CommonSettingsWindow.xaml
│  │   ├─ CommonSettingsWindow.xaml.cs
│  │   ├─ SettingsWindow.xaml
│  │   └─ SettingsWindow.xaml.cs
│  │
│  └─ Dialogs/                       汎用ダイアログ群
│      ├─ MessageBoxEx.xaml
│      ├─ MessageBoxEx.xaml.cs
│      ├─ RenameFile.xaml
│      ├─ RenameFile.xaml.cs
│      ├─ TagEdit.xaml
│      └─ TagEdit.xaml.cs
│
├─ ViewModels/                       ★ 改名: ModelViews → ViewModels（MVVM慣例名）
│   ├─ MainWindowViewModel.cs
│   ├─ PendingMoviePlaceholder.cs
│   ├─ ThumbnailErrorProgressViewState.cs
│   ├─ ThumbnailErrorRecordViewModel.cs
│   └─ ThumbnailProgressViewState.cs
│
├─ Models/                           （既存そのまま）
│   ├─ History.cs                    ← トップから移動
│   ├─ TreeSource.cs                 ← トップから移動
│   ├─ MovieCore.cs
│   ├─ MovieCoreMapper.cs
│   ├─ MovieInfo.cs
│   └─ MovieRecords.cs
│
├─ Infrastructure/                   ★ 新設: 横断ユーティリティ
│   ├─ DebugRuntimeLog.cs            ← トップから移動
│   ├─ FileNameValidationRule.cs     ← トップから移動
│   └─ Converter/                    ← 既存 Converter/ を移管
│       ├─ ConverterBindableParameter.cs
│       ├─ FileSizeConverter.cs
│       ├─ NoLockImageConverter.cs
│       └─ ThumbnailProgressPreviewConverter.cs
│
├─ DB/                               （既存そのまま）
├─ Images/                           （既存そのまま）
├─ Themes/                           （既存そのまま）
├─ Properties/                       （既存そのまま）
│
├─ UserControls/                     （既存そのまま）
│
├─ BottomTabs/                       （既存そのまま — 機能別サブフォルダ構成の手本）
│
├─ UpperTabs/                        （既存そのまま）
│
├─ Watcher/                          （既存 + WatchWindow 統合）
│   ├─ WatchWindow.xaml              ← トップから移動
│   ├─ WatchWindow.xaml.cs           ← トップから移動
│   ├─ WatchWindowViewModel.cs       （既存）
│   ├─ MainWindow.Watcher.cs         （既存）
│   └─ ... 既存ファイル群
│
├─ Thumbnail/                        （既存 + ThumbnailFailedTab 統合）
│   ├─ MainWindow.ThumbnailFailedTab.cs   ← トップから移動
│   ├─ MainWindow.ThumbnailCreation.cs    （既存）
│   └─ ... 既存ファイル群
│
├─ Docs/                             （既存そのまま）
├─ src/                              （既存そのまま — DLL分離プロジェクト群）
├─ Tests/                            （既存そのまま）
├─ tools/                            （既存そのまま）
└─ scripts/                          （既存そのまま）
```

## 5. 移動マッピング表

移動は論理グループ単位で実施する。各フェーズ内は独立。

### Phase A: Views/Main/ — メイン画面の部分クラス群

| 移動元 (top) | 移動先 |
|---|---|
| MainWindow.xaml | Views/Main/MainWindow.xaml |
| MainWindow.xaml.cs | Views/Main/MainWindow.xaml.cs |
| MainWindow.DbSwitch.cs | Views/Main/MainWindow.DbSwitch.cs |
| MainWindow.MenuActions.cs | Views/Main/MainWindow.MenuActions.cs |
| MainWindow.Player.cs | Views/Main/MainWindow.Player.cs |
| MainWindow.Search.cs | Views/Main/MainWindow.Search.cs |
| MainWindow.Selection.cs | Views/Main/MainWindow.Selection.cs |
| MainWindow.Tag.cs | Views/Main/MainWindow.Tag.cs |

注意:
- App.xaml の `StartupUri` は `Views/Main/MainWindow.xaml` に更新する
- AvalonDock の `layout.xml` はコードで読み込むパスが変わる可能性がある
  → 実行時テストで確認（`layout.xml` はビルド出力フォルダにコピーされる設定を確認）

### Phase B: Views/Settings/ — 設定ダイアログ

| 移動元 (top) | 移動先 |
|---|---|
| CommonSettingsWindow.xaml | Views/Settings/CommonSettingsWindow.xaml |
| CommonSettingsWindow.xaml.cs | Views/Settings/CommonSettingsWindow.xaml.cs |
| SettingsWindow.xaml | Views/Settings/SettingsWindow.xaml |
| SettingsWindow.xaml.cs | Views/Settings/SettingsWindow.xaml.cs |

### Phase C: Views/Dialogs/ — 汎用ダイアログ

| 移動元 (top) | 移動先 |
|---|---|
| MessageBoxEx.xaml | Views/Dialogs/MessageBoxEx.xaml |
| MessageBoxEx.xaml.cs | Views/Dialogs/MessageBoxEx.xaml.cs |
| RenameFile.xaml | Views/Dialogs/RenameFile.xaml |
| RenameFile.xaml.cs | Views/Dialogs/RenameFile.xaml.cs |
| TagEdit.xaml | Views/Dialogs/TagEdit.xaml |
| TagEdit.xaml.cs | Views/Dialogs/TagEdit.xaml.cs |

### Phase D: Models/ — モデル統合

| 移動元 (top) | 移動先 |
|---|---|
| History.cs | Models/History.cs |
| TreeSource.cs | Models/TreeSource.cs |

### Phase E: Infrastructure/ — 横断ユーティリティ新設

| 移動元 (top) | 移動先 |
|---|---|
| DebugRuntimeLog.cs | Infrastructure/DebugRuntimeLog.cs |
| FileNameValidationRule.cs | Infrastructure/FileNameValidationRule.cs |

| 移動元 (既存フォルダ) | 移動先 |
|---|---|
| Converter/ConverterBindableParameter.cs | Infrastructure/Converter/ConverterBindableParameter.cs |
| Converter/FileSizeConverter.cs | Infrastructure/Converter/FileSizeConverter.cs |
| Converter/NoLockImageConverter.cs | Infrastructure/Converter/NoLockImageConverter.cs |
| Converter/ThumbnailProgressPreviewConverter.cs | Infrastructure/Converter/ThumbnailProgressPreviewConverter.cs |

### Phase F: 機能フォルダへの統合

| 移動元 (top) | 移動先 |
|---|---|
| WatchWindow.xaml | Watcher/WatchWindow.xaml |
| WatchWindow.xaml.cs | Watcher/WatchWindow.xaml.cs |
| MainWindow.ThumbnailFailedTab.cs | Thumbnail/MainWindow.ThumbnailFailedTab.cs |

### Phase G: フォルダ改名

| 旧名 | 新名 | 理由 |
|---|---|---|
| ModelViews/ | ViewModels/ | MVVM の一般的な慣例名に合わせる |

### Phase H: layout.xml 配置確認

`layout.xml` や `layout.missing-*.xml` はトップに残す。
AvalonDock が読み込む相対パスが実行ディレクトリ基準であるため、移動不要。

## 6. トップに残るファイル一覧（整理後）

```
App.xaml                     WPF 起点（VS が要求する位置）
App.xaml.cs
App.config
AssemblyInfo.cs              GlobalAttribute（VS が要求する位置）
IndigoMovieManager_fork.csproj
IndigoMovieManager_fork.sln
.editorconfig
.gitignore
.gitattributes
AGENTS.md
.CLAUDE.md / .GEMINI.md / .CODEX.md
README.md
LICENSE.txt
AI向け_ブランチ方針_*.md
layout.xml
layout.missing-*.xml
```

.cs / .xaml はトップに App 系と AssemblyInfo だけが残る。
この 3 本は WPF/MSBuild の特殊ファイルのため移動しない。

## 7. 名前空間の方針

VS2026 の移動機能は `namespace` を更新するが、方針を統一しておく:

| フォルダ | 名前空間 |
|---------|---------|
| Views/Main/ | `IndigoMovieManager` (MainWindow 部分クラスは既存維持) |
| Views/Settings/ | `IndigoMovieManager` (既存維持) |
| Views/Dialogs/ | `IndigoMovieManager` (既存維持) |
| ViewModels/ | `IndigoMovieManager.ModelViews` → `IndigoMovieManager.ViewModels` |
| Models/ | `IndigoMovieManager` (既存維持) |
| Infrastructure/ | `IndigoMovieManager` (既存維持) |
| Infrastructure/Converter/ | `IndigoMovieManager` (既存維持) |

現時点ではほぼ全て `IndigoMovieManager` 名前空間で統一されている。
フォルダ構造とは独立して名前空間は据え置きとし、破壊的変更を避ける。

唯一の例外: `ModelViews/` → `ViewModels/` 改名に伴い、
`WatchWindow.xaml.cs` の `namespace IndigoMovieManager.ModelView` は
`IndigoMovieManager.ViewModels` に合わせる（VS が自動修正するが確認する）。

## 8. 実施順と確認ポイント

```
Phase A (MainWindow群)
  → ビルド確認
  → App.xaml StartupUri 確認
  → layout.xml 読み込みテスト
Phase B (Settings)
  → ビルド確認
Phase C (Dialogs)
  → ビルド確認
Phase D (Models統合)
  → ビルド確認
Phase E (Infrastructure + Converter統合)
  → ビルド確認
Phase F (Watcher/Thumbnail統合)
  → ビルド確認
Phase G (ModelViews → ViewModels 改名)
  → ビルド確認
  → namespace 確認
全Phase完了後:
  → 全テスト実行
  → 手動起動 → 主要画面巡回
```

## 9. csproj への影響

VS2026 の移動機能が `.csproj` を自動更新する。手動対応が必要な可能性がある項目:

- `App.xaml` の `StartupUri="MainWindow.xaml"` → `StartupUri="Views/Main/MainWindow.xaml"`
- `layout.xml` 等の `<Content>` / `<None>` パス（CopyToOutputDirectory 設定がある場合）
- `<Compile Remove>` で旧パスを除外している記述（あれば更新）

## 10. リスク

| リスク | 対策 |
|--------|------|
| partial class が別フォルダに散るとコンパイルエラー | 既存 BottomTabs/ / Thumbnail/ / Watcher/ で実績あり。VS2026 は partial を正しく解決する |
| AvalonDock layout.xml のパス | layout.xml はビルド出力コピーなので実行パス問題なし。コードでの読み込みパスだけ要確認 |
| StartupUri 変更で起動しない | Phase A 後に即テスト |
| git diff が「ファイル削除＋新規作成」になる | VS の移動は git mv 相当。git の rename detection で追跡可能 |
| 名前空間変更で using 不整合 | 名前空間は基本据え置き方針。ViewModels だけ変えるが VS が自動修正 |

## 11. 整理後のフォルダ概要図

```
top/
├── Views/                  UI の窓口。画面を探すならここ
│   ├── Main/               メイン画面（partial class 群）
│   ├── Settings/           設定系ダイアログ
│   └── Dialogs/            汎用ダイアログ
├── ViewModels/             画面ごとの ViewModel
├── Models/                 データモデル（Movie, History, TreeSource）
├── Infrastructure/         横断ユーティリティ
│   └── Converter/          WPF ValueConverter群
├── UserControls/           再利用 UI 部品
├── BottomTabs/             下部タブ（機能別サブフォルダ）
├── UpperTabs/              上部タブ
├── DB/                     SQLite DAO
├── Watcher/                監視フォルダ機能（Window含む）
├── Thumbnail/              サムネイル生成機能（Window含む）
├── Themes/                 WPF テーマ
├── Images/                 プレースホルダー画像
├── Properties/             プロジェクト設定
├── Docs/                   設計ドキュメント
├── src/                    DLL 分離プロジェクト群
├── Tests/                  テスト
├── tools/                  ツール
└── scripts/                スクリプト
```

## 12. ゴミ掃除（一緒にやっておく候補）

| ファイル | 判断 |
|---------|------|
| build_log.txt | 削除（再生成可能） |
| temp_summary.txt | 削除（一時ファイル） |
| .codex_msbuild_build.log | 削除（ビルドログ） |
| .codex_msbuild_thumbnailerror.log | 削除（ビルドログ） |
| maimai.wb / みずがめ座.wb | テスト用DBなら .gitignore に追加、またはテスト専用フォルダへ |
| layout.missing-*.xml | 保存価値がなければ削除（デバッグ用スナップショット） |
