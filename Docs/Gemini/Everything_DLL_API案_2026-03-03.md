# Everything DLL API案（2026-03-03）

## 1. 方針
- 現行実装との整合を優先し、Provider APIは同期で定義する。
- 呼び出し側（ホスト）は必要に応じて `Task.Run` で非同期化する。
- 失敗は原則 `reason` で返し、業務継続可能な例外はProvider外へ投げない。
- `IntegrationMode` の判定はFacade責務に固定し、Providerはmode非依存で実装する。

## 2. 契約モデル案

```csharp
namespace IndigoMovieManager.FileIndex.Contracts;

public enum IntegrationMode
{
    Off = 0,
    Auto = 1,
    On = 2,
}

public sealed class FileIndexQueryOptions
{
    public required string RootPath { get; init; }
    public bool IncludeSubdirectories { get; init; }
    public string CheckExt { get; init; } = "";
    public DateTime? ChangedSinceUtc { get; init; }
}

public sealed class AvailabilityResult
{
    public bool CanUse { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class FileIndexMovieResult
{
    public bool Success { get; init; }
    public List<string> MoviePaths { get; init; } = [];
    public DateTime? MaxObservedChangedUtc { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class FileIndexThumbnailBodyResult
{
    public bool Success { get; init; }
    public HashSet<string> Bodies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Reason { get; init; } = "";
}
```

## 3. Providerインターフェース案

```csharp
namespace IndigoMovieManager.FileIndex.Contracts;

public interface IFileIndexProvider
{
    AvailabilityResult CheckAvailability();

    FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options);

    FileIndexThumbnailBodyResult CollectThumbnailBodies(string thumbFolder);
}
```

## 4. Facadeインターフェース案

```csharp
namespace IndigoMovieManager.FileIndex.Contracts;

public sealed class ScanByProviderResult
{
    public string Strategy { get; init; } = "filesystem"; // everything/filesystem
    public string Reason { get; init; } = "";
    public List<string> MoviePaths { get; init; } = [];
    public DateTime? MaxObservedChangedUtc { get; init; }
}

public interface IIndexProviderFacade
{
    ScanByProviderResult CollectMoviePathsWithFallback(
        FileIndexQueryOptions options,
        IntegrationMode mode
    );

    FileIndexThumbnailBodyResult CollectThumbnailBodiesWithFallback(
        string thumbFolder,
        IntegrationMode mode
    );
}
```

## 5. Facade責務（固定）
- OFF/AUTO/ONの分岐
- `auto_not_available` / `setting_disabled` のようなmode起点reasonの組み立て
- Provider可用性判定の実行
- Provider失敗時のフォールバック判断
- `strategy` と `reason` の返却

## 6. Facade責務外（固定）
- DB保存/読込（`last_sync_utc`）
- 通知文言生成
- ログ出力
- UIスレッド制御

## 7. 例外方針
- 可用性不備、検索失敗、打ち切りは `Success=false + reason` を返す。
- 引数不正（null/空）など開発時検出が必要なもののみ `ArgumentException` を許可。
- `auto_not_available` はFacadeが生成し、Providerは返さない。

## 8. 未決事項
- `Strategy` をstring固定（`everything`/`filesystem`）のまま維持するか、enum化するか。
- `CollectThumbnailBodiesWithFallback` の戻り値に「フォールバックしたか」を明示フラグで持つか。
