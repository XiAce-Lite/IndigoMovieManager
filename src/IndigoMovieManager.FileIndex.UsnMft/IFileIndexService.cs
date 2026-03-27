using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    /// <summary>
    /// ファイル索引サービスの公開インターフェース。
    ///
    /// 【全体の流れでの位置づけ】
    ///   監視フォルダ登録
    ///     → ★ここ★ IFileIndexService.RebuildIndexAsync() でディスクを走査
    ///     → 索引を構築（Admin権限があれば USN/MFT で高速、なければ通常走査にフォールバック）
    ///     → Search() で動画ファイルを高速検索 → 結果を MainWindow の一覧へ反映
    ///
    /// バックエンドは AdminUsnMft（爆速）と StandardFileSystem（汎用）の2種類。
    /// 権限に応じて自動切替する。Everything 連携はこの上位レイヤーで行う。
    /// </summary>
    public interface IFileIndexService : IDisposable
    {
        string ActiveBackendName { get; }

        FileIndexBackendMode ActiveBackendMode { get; }

        int IndexedCount { get; }

        Task<int> RebuildIndexAsync(IProgress<IndexProgress> progress, CancellationToken cancellationToken);

        IReadOnlyList<SearchResultItem> Search(string query, int maxResults);
    }
}
