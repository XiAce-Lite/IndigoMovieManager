using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabScrollNavigatorTests
{
    [Test]
    public void 次画面送りはほぼ1画面分進む()
    {
        double target = UpperTabScrollNavigator.CalculateTargetOffset(
            currentOffset: 0,
            viewportHeight: 1000,
            extentHeight: 5000,
            scrollForward: true
        );

        Assert.That(target, Is.EqualTo(920).Within(0.001));
    }

    [Test]
    public void 終端を超える送りは最大位置で止まる()
    {
        double target = UpperTabScrollNavigator.CalculateTargetOffset(
            currentOffset: 4300,
            viewportHeight: 1000,
            extentHeight: 5000,
            scrollForward: true
        );

        Assert.That(target, Is.EqualTo(4000).Within(0.001));
    }

    [Test]
    public void 逆送りは先頭未満へ出ない()
    {
        double target = UpperTabScrollNavigator.CalculateTargetOffset(
            currentOffset: 200,
            viewportHeight: 1000,
            extentHeight: 5000,
            scrollForward: false
        );

        Assert.That(target, Is.EqualTo(0).Within(0.001));
    }

    [Test]
    public void 画面より要素が短い時は移動しない()
    {
        double target = UpperTabScrollNavigator.CalculateTargetOffset(
            currentOffset: 120,
            viewportHeight: 1000,
            extentHeight: 800,
            scrollForward: true
        );

        Assert.That(target, Is.EqualTo(0).Within(0.001));
    }
}
