namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの画像更新を、選択中タブだけへ絞るための最小ヘルパ。
    /// </summary>
    public static class UpperTabActivationGate
    {
        public static bool ShouldApplyImageUpdate(object isSelectedValue)
        {
            // TabItem の祖先解決が遅い瞬間は UnsetValue になることがある。
            // 表示不能を避けるため、明確に false の時だけ止める。
            return isSelectedValue is not bool isSelected || isSelected;
        }
    }
}
