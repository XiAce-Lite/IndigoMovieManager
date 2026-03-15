namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string SavedSearchPreparingMessage = "保存済み検索条件は準備中です。";

        // 保存済み検索条件タブは、まず表示文言の責務だけをここへ寄せる。
        private void InitializeSavedSearchTabShell()
        {
            ApplySavedSearchPlaceholderText();
        }

        // 後で一覧や状態表示へ広げても、文言更新の入口はここで固定する。
        private void ApplySavedSearchPlaceholderText(string message = null)
        {
            if (SavedSearchPlaceholderText == null)
            {
                return;
            }

            SavedSearchPlaceholderText.Text = string.IsNullOrWhiteSpace(message)
                ? SavedSearchPreparingMessage
                : message;
        }
    }
}
