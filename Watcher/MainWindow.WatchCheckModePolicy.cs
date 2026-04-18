namespace IndigoMovieManager;

public partial class MainWindow
{
    // 強いモード（Manual > Watch > Auto）を優先して圧縮する。
    private static CheckMode MergeCheckMode(CheckMode current, CheckMode incoming)
    {
        return GetCheckModePriority(incoming) > GetCheckModePriority(current)
            ? incoming
            : current;
    }

    private static int GetCheckModePriority(CheckMode mode)
    {
        return mode switch
        {
            CheckMode.Manual => 3,
            CheckMode.Watch => 2,
            _ => 1,
        };
    }
}
