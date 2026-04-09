using IndigoMovieManager;
using System.Windows;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DarkHeavyBackgroundRescueMenuVisibilityTests
{
    [Test]
    public void ResolveRescueOnlyContextMenuVisibility_上側救済タブではVisible()
    {
        Visibility result = MainWindow.ResolveRescueOnlyContextMenuVisibility(
            isUpperTabRescueSelected: true,
            isBottomThumbnailErrorTabSelected: false
        );

        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void ResolveRescueOnlyContextMenuVisibility_下部失敗DebugタブでもVisible()
    {
        Visibility result = MainWindow.ResolveRescueOnlyContextMenuVisibility(
            isUpperTabRescueSelected: false,
            isBottomThumbnailErrorTabSelected: true
        );

        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void ResolveRescueOnlyContextMenuVisibility_通常パネルではCollapsed()
    {
        Visibility result = MainWindow.ResolveRescueOnlyContextMenuVisibility(
            isUpperTabRescueSelected: false,
            isBottomThumbnailErrorTabSelected: false
        );

        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ResolveSendToThumbnailRescueTabMenuVisibility_通常パネルではVisible()
    {
        Visibility result = MainWindow.ResolveSendToThumbnailRescueTabMenuVisibility(
            isUpperTabRescueSelected: false,
            isBottomThumbnailErrorTabSelected: false
        );

        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void ResolveSendToThumbnailRescueTabMenuVisibility_上側救済タブではCollapsed()
    {
        Visibility result = MainWindow.ResolveSendToThumbnailRescueTabMenuVisibility(
            isUpperTabRescueSelected: true,
            isBottomThumbnailErrorTabSelected: false
        );

        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ResolveSendToThumbnailRescueTabMenuVisibility_下部失敗DebugタブではCollapsed()
    {
        Visibility result = MainWindow.ResolveSendToThumbnailRescueTabMenuVisibility(
            isUpperTabRescueSelected: false,
            isBottomThumbnailErrorTabSelected: true
        );

        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }
}
