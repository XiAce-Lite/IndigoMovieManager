using System.Threading;
using IndigoMovieManager.BottomTabs.ThumbnailProgress;
using IndigoMovieManager;
using System.Windows;

namespace IndigoMovieManager.Tests;

public sealed class ThumbnailProgressTabViewTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void ThumbnailProgressTabView_単体生成で例外を投げない()
    {
        // アプリ共通テーマを積んだ状態で、UserControl 単体生成が崩れないことを確認する。
        EnsureApplicationResources();

        ThumbnailProgressTabView view = new();

        Assert.That(view, Is.Not.Null);
        Assert.That(view.ResizeThumbCheckBox, Is.Not.Null);
        Assert.That(view.GpuDecodeEnabledCheckBox, Is.Not.Null);
        Assert.That(view.PresetLowSpeedRadioButton, Is.Not.Null);
        Assert.That(view.PresetNormalRadioButton, Is.Not.Null);
        Assert.That(view.PresetFastRadioButton, Is.Not.Null);
    }

    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
        {
            return;
        }

        App app = new();
        app.InitializeComponent();
    }
}
