using IndigoMovieManager.Watcher;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // Everything連携の判定と呼び出しを集約するFacade。
    private readonly IIndexProviderFacade _indexProviderFacade =
        FileIndexProviderFactory.CreateFacade();

    // 設定値(0/1/2)をOFF/AUTO/ONへ丸める。
    private static IntegrationMode GetEverythingIntegrationMode()
    {
        int mode = Properties.Settings.Default.EverythingIntegrationMode;
        return mode switch
        {
            0 => IntegrationMode.Off,
            2 => IntegrationMode.On,
            _ => IntegrationMode.Auto,
        };
    }
}
