using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DockLayoutRestorePolicyTests
{
    [Test]
    public void 必須の下部タブが欠けたlayoutは互換外として弾く()
    {
        string layoutText =
            """
            <LayoutRoot>
              <LayoutAnchorable ContentId="ToolTagEditor" />
              <LayoutAnchorable ContentId="ToolBookmark" />
              <LayoutAnchorable ContentId="ToolTagBar" />
              <LayoutAnchorable ContentId="ToolThumbnailProgress" />
            </LayoutRoot>
            """;

        string actual = MainWindow.FindMissingRequiredDockLayoutReason(
            layoutText,
            shouldShowThumbnailErrorBottomTab: false,
            shouldShowDebugTab: false
        );

        Assert.That(actual, Is.EqualTo("missing-extension-bottom-tab"));
    }

    [Test]
    public void 必須の下部タブが揃ったlayoutはそのまま復元候補に残す()
    {
        string layoutText =
            """
            <LayoutRoot>
              <LayoutAnchorable ContentId="ToolExtension" />
              <LayoutAnchorable ContentId="ToolBookmark" />
              <LayoutAnchorable ContentId="ToolTagBar" />
              <LayoutAnchorable ContentId="ToolThumbnailProgress" />
              <LayoutAnchorable ContentId="ToolTagEditor" />
            </LayoutRoot>
            """;

        string actual = MainWindow.FindMissingRequiredDockLayoutReason(
            layoutText,
            shouldShowThumbnailErrorBottomTab: false,
            shouldShowDebugTab: false
        );

        Assert.That(actual, Is.Empty);
    }

    [Test]
    public void Debugタブ表示対象なのにLogが欠けたlayoutは互換外として弾く()
    {
        string layoutText =
            """
            <LayoutRoot>
              <LayoutAnchorable ContentId="ToolExtension" />
              <LayoutAnchorable ContentId="ToolBookmark" />
              <LayoutAnchorable ContentId="ToolTagBar" />
              <LayoutAnchorable ContentId="ToolThumbnailProgress" />
              <LayoutAnchorable ContentId="ToolTagEditor" />
              <LayoutAnchorable ContentId="ToolDebug" />
            </LayoutRoot>
            """;

        string actual = MainWindow.FindMissingRequiredDockLayoutReason(
            layoutText,
            shouldShowThumbnailErrorBottomTab: false,
            shouldShowDebugTab: true
        );

        Assert.That(actual, Is.EqualTo("missing-log-tool"));
    }
}
