namespace IndigoMovieManager;

public partial class MainWindow
{
    // 明示的なユーザー要求中は、手動要求以外の背後走査を後ろへ逃がす。
    internal static bool ShouldDeferBackgroundWorkForUserPriority(
        bool isUserPriorityActive,
        bool isManualMode
    )
    {
        return isUserPriorityActive && !isManualMode;
    }

    // ユーザー要求が終わったら、保留していた背後走査を1回だけ catch-up させる。
    internal static bool ShouldQueueBackgroundCatchUpAfterUserPriority(
        bool isStillActive,
        bool hasDeferredWatchWork
    )
    {
        return !isStillActive && hasDeferredWatchWork;
    }
}
