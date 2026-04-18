using System;
using System.Collections.Generic;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // watch 1回で拾った changed paths は、大文字小文字差異を潰して保持する。
    internal static List<WatchChangedMovie> MergeChangedMovies(
        IEnumerable<WatchChangedMovie> existingMovies,
        IEnumerable<WatchChangedMovie> incomingMovies
    )
    {
        List<WatchChangedMovie> mergedMovies = [];
        Dictionary<string, int> indexByPath = new(StringComparer.OrdinalIgnoreCase);

        void append(IEnumerable<WatchChangedMovie> sourceMovies)
        {
            if (sourceMovies == null)
            {
                return;
            }

            foreach (WatchChangedMovie changedMovie in sourceMovies)
            {
                if (
                    string.IsNullOrWhiteSpace(changedMovie.MoviePath)
                    || (
                        changedMovie.ChangeKind == WatchMovieChangeKind.None
                        && changedMovie.DirtyFields == WatchMovieDirtyFields.None
                    )
                )
                {
                    continue;
                }

                if (indexByPath.TryGetValue(changedMovie.MoviePath, out int existingIndex))
                {
                    WatchChangedMovie current = mergedMovies[existingIndex];
                    mergedMovies[existingIndex] = current with
                    {
                        ChangeKind = current.ChangeKind | changedMovie.ChangeKind,
                        DirtyFields = current.DirtyFields | changedMovie.DirtyFields,
                        ObservedState = MergeWatchMovieObservedState(
                            current.ObservedState,
                            changedMovie.ObservedState
                        ),
                    };
                    continue;
                }

                indexByPath[changedMovie.MoviePath] = mergedMovies.Count;
                mergedMovies.Add(changedMovie);
            }
        }

        append(existingMovies);
        append(incomingMovies);
        return mergedMovies;
    }
}
