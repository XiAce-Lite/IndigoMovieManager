# installer スクリプト入口

このフォルダは installer 周辺スクリプトの入口メモです。
2026-04-05 時点の正本は `installer/wix` と
`scripts/create_wix_installer_from_release_package.ps1` です。

## 正面入口

- `scripts/create_wix_installer_from_release_package.ps1`
  - `scripts/create_github_release_package.ps1` が作る verify 済み app package を唯一入力にして、
    `MSI + Burn bundle exe` を作ります。
  - worker / engine package の sync や再 publish は行いません。
- `installer/wix/IndigoMovieManager.Product.wixproj`
  - verify 済み package を per-user MSI へ包む product project です。
- `installer/wix/IndigoMovieManager.Bundle.wixproj`
  - 標準 BA で MSI 1 本を束ねる bundle project です。

## v1 の前提

- provenance 正本は package 直下の `rescue-worker.lock.json` と `privateEnginePackages` のまま継承します。
- v1 は install / upgrade / uninstall proof を優先し、self-update は次段に分けます。
- per-user harvest は ICE38 / ICE64 と衝突するため、v1 は `SuppressValidation=true` を使う暫定運用です。

## 次段

- v2 は `scripts/仕様書_WiXv6インストーラーと自己更新_2026-04-05.md` の self-update 節を正本にします。
- v3 は同仕様書の custom BA 節を正本にし、保持項目 UI は v1 proof 後に積みます。
