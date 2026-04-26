using IndigoMovieManager;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class EverythingDetailPolicyTests
{
    [Test]
    public void DescribeEverythingDetail_path_not_eligibleを説明文へ変換する()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail(
            "path_not_eligible:unc_path"
        );

        Assert.That(result.code, Is.EqualTo("path_not_eligible:unc_path"));
        Assert.That(result.message, Is.EqualTo("UNC/NASパスはEverything高速経路の対象外です"));
    }

    [Test]
    public void DescribeEverythingDetail_path_not_eligible_drive_typeは固定ドライブ以外の文言を返す()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail(
            "path_not_eligible:drive_type_removable"
        );

        Assert.That(result.code, Is.EqualTo("path_not_eligible:drive_type_removable"));
        Assert.That(
            result.message,
            Is.EqualTo("ローカル固定ドライブ以外のため対象外です (drive_type_removable)")
        );
    }

    [Test]
    public void DescribeEverythingDetail_ok_watch_deferred_batchは再開文言を返す()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail(
            "ok:watch_deferred_batch folder=E:\\Movies"
        );

        Assert.That(result.code, Is.EqualTo("ok:watch_deferred_batch folder=E:\\Movies"));
        Assert.That(result.message, Is.EqualTo("前回繰り延べた watch 候補の処理を再開しています"));
    }

    [Test]
    public void DescribeEverythingDetail_okは候補収集成功文言を返す()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail(
            "ok:provider=everythinglite index=cached"
        );

        Assert.That(result.code, Is.EqualTo("ok:provider=everythinglite index=cached"));
        Assert.That(result.message, Is.EqualTo("Everything連携で候補収集に成功しました"));
    }

    [Test]
    public void DescribeEverythingDetail_setting_disabledは無効文言を返す()
    {
        (string code, string message) result =
            InvokeDescribeEverythingDetail("setting_disabled");

        Assert.That(result.code, Is.EqualTo("setting_disabled"));
        Assert.That(result.message, Is.EqualTo("設定でEverything連携が無効です"));
    }

    [Test]
    public void DescribeEverythingDetail_result_truncatedは通常監視へ戻す文言を返す()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail(
            "everything_result_truncated:3/10"
        );

        Assert.That(result.code, Is.EqualTo("everything_result_truncated:3/10"));
        Assert.That(
            result.message,
            Is.EqualTo("検索結果が上限件数に達したため通常監視へ切り替えます")
        );
    }

    [Test]
    public void DescribeEverythingDetail_query_errorは例外文言を返す()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail(
            "everything_query_error:System.Exception"
        );

        Assert.That(result.code, Is.EqualTo("everything_query_error:System.Exception"));
        Assert.That(
            result.message,
            Is.EqualTo(
                "Everything連携で例外が発生しました (everything_query_error:System.Exception)"
            )
        );
    }

    [Test]
    public void DescribeEverythingDetail_空文字はunknownとして扱う()
    {
        (string code, string message) result = InvokeDescribeEverythingDetail("");

        Assert.That(result.code, Is.EqualTo("unknown"));
        Assert.That(
            result.message,
            Is.EqualTo("不明な理由のため通常監視へ切り替えます (unknown)")
        );
    }

    private static (string code, string message) InvokeDescribeEverythingDetail(string detail)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "DescribeEverythingDetail",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);
        object result = method.Invoke(null, [detail])!;
        PropertyInfo codeProperty =
            result.GetType().GetProperty("Code")
            ?? result.GetType().GetProperty("Item1")!;
        PropertyInfo messageProperty =
            result.GetType().GetProperty("Message")
            ?? result.GetType().GetProperty("Item2")!;
        return (
            (string)(codeProperty.GetValue(result) ?? ""),
            (string)(messageProperty.GetValue(result) ?? "")
        );
    }
}
