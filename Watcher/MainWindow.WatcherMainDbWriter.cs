using System.Collections.Generic;
using IndigoMovieManager.Data;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly IWatchMainDbFacade _watchMainDbFacade = new WatchMainDbFacade();

        // watch 側は batch 登録結果だけを見て、DB 実装の詳細は facade 側へ閉じ込める。
        private Task<int> InsertMoviesToMainDbBatchAsync(
            string dbFullPath,
            List<MovieCore> moviesToInsert
        )
        {
            return _watchMainDbFacade.InsertMoviesBatchAsync(dbFullPath, moviesToInsert);
        }

        // path 基準の既存 snapshot は facade の DTO をそのまま受け取り、watch から SQL を剥がす。
        private Dictionary<string, WatchMainDbMovieSnapshot> BuildExistingMovieSnapshotByPath(
            string snapshotDbFullPath
        )
        {
            return _watchMainDbFacade.LoadExistingMovieSnapshot(snapshotDbFullPath);
        }
    }
}
