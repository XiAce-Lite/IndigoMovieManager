using IndigoMovieManager;
using System.Windows;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class DarkHeavyBackgroundRescueMenuVisibilityTests
{
    [Test]
    public void ResolveDarkHeavyBackgroundRescueMenuVisibility_救済パネルではVisible()
    {
        Visibility result = MainWindow.ResolveDarkHeavyBackgroundRescueMenuVisibility(
            isUpperTabRescueSelected: true
        );

        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void ResolveDarkHeavyBackgroundRescueMenuVisibility_通常パネルではCollapsed()
    {
        Visibility result = MainWindow.ResolveDarkHeavyBackgroundRescueMenuVisibility(
            isUpperTabRescueSelected: false
        );

        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }
}
