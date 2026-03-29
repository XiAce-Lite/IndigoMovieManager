# Plan: バッジ固定色のテーマ辞書キー化

## TL;DR

ThumbnailProgressTabView.xaml のスレッド・救済バッジで直書きされている固定色 5 箇所を、
既存テーマ辞書（OriginalColors.xaml / OsSyncColors.xaml）のキー参照へ置き換える。
ダーク時の配色も OsSyncColors.xaml 側で定義し、テーマ切替だけで見た目が追従する構造にする。

IBottomTabFeature / IUpperTabFeature 等のタブ独立化インターフェースは今回スコープ外とし、
テーマ資源化だけを最小スコープで先行する。

---

## Phase A: テーマキー定義の追加

### Step 1 — OriginalColors.xaml にバッジ色キー 5 本追加（*独立*）

対象: `Themes/OriginalColors.xaml`

追加するキー（既存の固定色と同じ値をそのまま入れる）:

| キー名 | 値（ライトテーマ記号色） | 用途 |
|--------|--------------------------|------|
| `BadgeIdleBackground` | `#FFF3F3F3` | 未処理バッジ背景 |
| `BadgeThreadActiveBackground` | `#FFE4F5E3` | 処理中バッジ背景 |
| `BadgeThreadActiveBorder` | `#FF9FC99B` | 処理中バッジ枠線 |
| `BadgeRescueActiveBackground` | `#FFF8E3E3` | 救済中バッジ背景 |
| `BadgeRescueActiveBorder` | `#FFD8A3A3` | 救済中バッジ枠線 |

### Step 2 — OsSyncColors.xaml に同キーのダーク向き配色を追加（*Step 1 と並行可*）

対象: `Themes/OsSyncColors.xaml`

| キー名 | ダーク向き値 |
|--------|-------------|
| `BadgeIdleBackground` | `#FF3A3A3A` |
| `BadgeThreadActiveBackground` | `#FF2D4030` |
| `BadgeThreadActiveBorder` | `#FF5A7A56` |
| `BadgeRescueActiveBackground` | `#FF4A3030` |
| `BadgeRescueActiveBorder` | `#FF8A5555` |

> `BadgeIdleBorder` は既に `{DynamicResource MaterialDesignDivider}` 参照なので新規キー不要。

---

## Phase B: XAML スタイル書き換え

### Step 3 — ThumbnailProgressTabView.xaml の固定色 5 箇所を DynamicResource へ差し替え（*depends on Step 1 & 2*）

対象: `BottomTabs/ThumbnailProgress/ThumbnailProgressTabView.xaml`

変更箇所: 行 90–130 付近の 3 つのスタイル定義

1. `WorkerPanelStatusBadgeBorderStyle`
   - `Background` の `#FFF3F3F3` → `{DynamicResource BadgeIdleBackground}`
   - `BorderBrush` は既に `{DynamicResource MaterialDesignDivider}` なので変更なし

2. `WorkerPanelRescueStatusBadgeBorderStyle` の DataTrigger
   - `Background` の `#FFF8E3E3` → `{DynamicResource BadgeRescueActiveBackground}`
   - `BorderBrush` の `#FFD8A3A3` → `{DynamicResource BadgeRescueActiveBorder}`

3. `WorkerPanelThreadStatusBadgeBorderStyle` の DataTrigger
   - `Background` の `#FFE4F5E3` → `{DynamicResource BadgeThreadActiveBackground}`
   - `BorderBrush` の `#FF9FC99B` → `{DynamicResource BadgeThreadActiveBorder}`

---

## Phase C: 検証

### Step 4 — x64 Debug ビルド確認（*depends on Step 3*）

- `dotnet build -c Debug -p:Platform=x64` が成功すること

### Step 5 — テーマ切替の目視確認（*depends on Step 4*）

- Original テーマ → 進捗タブを開き、スレッドバッジ・救済バッジが従来色で表示されること
- OsSync テーマ（ライト OS）→ 同じバッジが見えること
- OsSync テーマ（ダーク OS or レジストリ切替）→ ダーク向き配色に切り替わること
- テーマ切替時にバッジ色がリアルタイムで追従すること（DynamicResource なので辞書差し替えで追従するはず）

### Step 6 — 副作用確認（*parallel with Step 5*）

- キーが未定義のテーマ辞書を読み込んだ場合にクラッシュしないこと（テーマ辞書が必ず両方にキーを持つことで防止）
- 他のバッジ系 UI（ThumbnailErrorTabView 等）に意図しない影響がないこと
  - ThumbnailErrorTabView.xaml には固定色バッジは現状ない（Background は MaterialDesignPaper 参照済み）

---

## 対象ファイル

| ファイル | 変更内容 |
|---------|---------|
| `Themes/OriginalColors.xaml` | バッジ色 SolidColorBrush × 5 追加 |
| `Themes/OsSyncColors.xaml` | 同キーのダーク値 × 5 追加 |
| `BottomTabs/ThumbnailProgress/ThumbnailProgressTabView.xaml` | 固定色 5 箇所を DynamicResource 参照へ |

---

## 決定事項

- `ApplyTheme` のコード変更は不要（辞書差し替えで DynamicResource が自動追従）
- Themes/Shared/ は新設しない → 既存ファイルに直接追加
- キー名は PascalCase（`BadgeIdleBackground` 等）で、将来他のバッジ UI でも再利用可能
- ダーク色の最終値は目視で微調整可（構造が入れば値だけ変えられる）

---

## スコープ外（明示的に除外）

- IBottomTabFeature / IUpperTabFeature / ITabRuntimeContext の導入
- ThemeStateService のサービス層新設
- App.xaml.cs / Generic.xaml の変更
- 他タブへの展開（今回は ThumbnailProgress バッジのみ）
- ダーク色の最終調整（構造が入れば後から値だけ変えられる）

## 実施結果（2026-03-25）

- `Themes/OriginalColors.xaml`
  - `BadgeIdleBackground` を `#FFF3F3F3` で追加
  - `BadgeThreadActiveBackground` を `#FFE4F5E3` で追加
  - `BadgeThreadActiveBorder` を `#FF9FC99B` で追加
  - `BadgeRescueActiveBackground` を `#FFF8E3E3` で追加
  - `BadgeRescueActiveBorder` を `#FFD8A3A3` で追加
- `Themes/OsSyncColors.xaml`
  - `BadgeIdleBackground` を `#FF3A3A3A` で追加
  - `BadgeThreadActiveBackground` を `#FF2D4030` で追加
  - `BadgeThreadActiveBorder` を `#FF5A7A56` で追加
  - `BadgeRescueActiveBackground` を `#FF4A3030` で追加
  - `BadgeRescueActiveBorder` を `#FF8A5555` で追加
- `BottomTabs/ThumbnailProgress/ThumbnailProgressTabView.xaml`
  - 3 スタイルを固定カラーから辞書キー参照へ変更
  - `WorkerPanelStatusBadgeBorderStyle` の背景を `BadgeIdleBackground` へ
  - `WorkerPanelRescueStatusBadgeBorderStyle` を `BadgeRescueActiveBackground / BadgeRescueActiveBorder` へ
  - `WorkerPanelThreadStatusBadgeBorderStyle` を `BadgeThreadActiveBackground / BadgeThreadActiveBorder` へ

### 反映条件
- `ApplyTheme` のコード変更なし
- `BadgeIdleBorder` は既存の `MaterialDesignDivider` 参照を維持
