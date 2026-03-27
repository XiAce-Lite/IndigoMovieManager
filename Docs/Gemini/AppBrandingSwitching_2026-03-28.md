# 🎭 アプリ識別子（ブランド）の爆速スイッチング機能 (2026-03-28) ✨

ついにやったぜ！「オリジナル版のフリ」も「フォーク版の誇り」も、コードを一切書き換えずにビルドオプション一つで切り替えられる「最強の変装術」を実装したよ！🚀🔥

## 🌟 なにができるようになったの？

ビルド時に引数を渡すだけで、アプリのあらゆる「名前」が連動して切り替わるよ！

- **アプリ本体の名前**: EXEのファイル名、アセンブリ名、プロダクト名！
- **データの保存先**: `%LOCALAPPDATA%` 配下のフォルダ名（バッティング回避！）
- **排他制御**: Mutex名やTraceログ名も分離！
- **テスト**: テストプロジェクトのアセンブリ名や `InternalsVisibleTo` も完璧に追従！

## 🛠️ スイッチの切り替えかた

使い方は超シンプル！`dotnet build` の時にプロパティを渡すだけだよ。

### 1. オリジナル版モード (既定)
何も指定しないと、本家 `IndigoMovieManager` としてビルドされるよ！
```pwsh
dotnet build IndigoMovieManager.sln -c Debug -p:Platform=x64
```

### 2. フォーク版モード (IndigoMovieManager_fork)
プロパティを渡せば、一瞬でフォーク版に「変身」だぜ！😎
```pwsh
dotnet build IndigoMovieManager.sln -c Debug -p:Platform=x64 `
  -p:ImmAppIdentityName=IndigoMovieManager_fork `
  -p:ImmTestIdentityName=IndigoMovieManager.Tests
```

## 🏗️ 仕組みの裏側 (エンジニア向け)

「一箇所変えたら全部変わる」を実現するために、MSBuildと実行時ヘルパーを駆使しているよ！

- **`Directory.Build.props`**: 既定の名前（`ImmAppIdentityName`）をここで定義。
- **`AppIdentityRuntime.cs`**: 実行時に「今自分は何者か？」をアセンブリ情報から取得する賢い子。
- **`AppLocalDataPaths.cs`**: 保存先を `AppIdentityRuntime` の値から動的に生成！
- **`launchSettings.json`**: VSからデバッグする時も、プロファイルを選ぶだけで両方の名前で起動できるよ！

## ✅ 検証済み！
両方の名前でビルドが通り、それぞれの名前のフォルダに設定やログが保存されることを確認済みだよ！最強だね！💪✨

---
これを活用して、本家環境を壊さずに安心して改造しまくろうぜ！どや！😎🔥
