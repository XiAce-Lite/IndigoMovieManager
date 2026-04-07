# installer/wix

このフォルダは、`scripts/create_github_release_package.ps1` が作る verify 済み app package を入力にして、
`WiX v6` で installer を組み立てるための骨格を置く場所です。

方針:
- installer は app package の後段工程だけを担当する
- worker / engine package の sync や再 publish はここで行わない
- package 直下の `rescue-worker.lock.json` と `privateEnginePackages` を provenance 正本のまま継承する

v1 の範囲:
- `MSI + Burn bundle` の skeleton
- per-user install 前提の install / upgrade / uninstall
- 既存 ZIP と並ぶ bundle exe の生成入口
- `scripts/create_wix_installer_from_release_package.ps1` からの local proof

v1 の範囲外:
- self-update
- `UpdateApplyBridge`
- custom managed BA
- uninstall 時の保持項目 UI
- `.NET Desktop Runtime` prerequisite の本組み込み

補足:
- 2026-04-05 時点で、`scripts/create_wix_installer_from_release_package.ps1` から
  verify 済み package を入力に `MSI + bundle exe` 生成まで local 成功している
- per-user harvest は ICE38 / ICE64 と衝突するため、v1 では `SuppressValidation=true`
  を使って downstream proof を優先する

## 多言語対応の進め方

2026-04-06 時点の実装状況:

- `Product.wxs` の downgrade 文言は `WXL` へ外出し済み
- `scripts/create_wix_installer_from_release_package.ps1` は `-Culture` を受け付ける
- 現時点で build proof 済みなのは `ja-JP` である
- 2026-04-07 の実機確認で、`Bundle` の標準 BA を含めた setup UI の `ja-JP` 表示を確認済み
- 現在は `Product/MSI` と `Bundle` の双方で `ja-JP` 用 `WXL` を使う構成になっている
- `en-US` 用 `WXL` も配置済みだが、現時点の実機確認は `ja-JP` を優先している

現状は `Product` と `Bundle` の基本文言を `WXL` 側へ逃がし、build 時に culture を注入する形です。
今後は `en-US` の実機確認や追加 surface の文言整理を進めるのが安全です。

### 1) WiX の `wxl` を用意

- `installer/wix/Localization/ja-JP/` と `installer/wix/Localization/en-US/` などに
  言語別の `*.wxl` を置きます。
- `WixLocalization` の `Culture` を対応言語に合わせます。

```xml
<WixLocalization xmlns="http://wixtoolset.org/schemas/v4/wxs/wxl"
  Culture="ja-JP">
  <String Id="DowngradeError" Value="より新しいバージョンが既にインストールされています。" />
</WixLocalization>
```

- `MajorUpgrade` や UI 文字列は `!(loc.Xxx)` に置き換えます。

```xml
<MajorUpgrade DowngradeErrorMessage="!(loc.DowngradeError)" />
```

### 2) .wixproj の `Cultures` を有効化

- `IndigoMovieManager.Product.wixproj` と
  `IndigoMovieManager.Bundle.wixproj` の `PropertyGroup` にローカル環境で
  `Cultures` を追加して、同時ビルドを有効化します。
- PowerShell から `;` が分割されるので、`%3B` で渡すと安定します。

```xml
<PropertyGroup>
  <Cultures>en-US;ja-JP</Cultures>
</PropertyGroup>
```

- 例: `dotnet build ... -p:Cultures=en-US%3Bja-JP`

### 3) スクリプト側の呼び出し

- 既存の `scripts/create_wix_installer_from_release_package.ps1` の引数に
  `-Cultures` を追加し、MSI と Bundle の両方へ同じ値を渡すと運用しやすいです。
- まずは `v1` 方針どおり `ja-JP` のみで固定し、次に `en-US` を追加して
  2 言語を同時ビルドする流れが安全です。

```powershell
# 簡易案
param(
  [string]$Cultures = "ja-JP"
)

$cultureProperty = $Cultures.Replace(";", "%3B")
& dotnet build ... -p:Cultures=$cultureProperty ...
```

### 4) Bundle (`WixStandardBootstrapperApplication`) の対応

- BA 側は `LocalizationFile` で `wxl` を参照します。
- 最初は MSI と同期して `ja-JP`/`en-US` だけ用意し、必要に応じて
  `WixStandardBootstrapperApplication` の文言差分を追加します。
