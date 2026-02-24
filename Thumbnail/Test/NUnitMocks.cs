using System;

// NUnitが参照できない他のプロジェクトに組み込まれた開発環境でも
// 最低限のエラーを出さずにテストコードを保持・ビルドさせるためのダミー属性とアサーション

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class TestFixtureAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class TestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class SetUpAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class TearDownAttribute : Attribute { }

    public static class Assert
    {
        public static void That(object actual, NUnitConstraint expression)
        {
            // Dummy implementation
        }
    }

    public static class Does
    {
        public static NUnitConstraint StartWith(string expected) => new NUnitConstraint();

        public static NUnitConstraint Contain(string expected) => new NUnitConstraint();

        public static NUnitConstraint EndWith(string expected) => new NUnitConstraint();
    }

    public static class Is
    {
        public static NUnitConstraint EqualTo(object expected) => new NUnitConstraint();

        public static NUnitConstraint GreaterThan(object expected) => new NUnitConstraint();

        public static NUnitConstraint LessThanOrEqualTo(object expected) => new NUnitConstraint();

        public static NUnitConstraint True => new NUnitConstraint();
        public static NUnitConstraint False => new NUnitConstraint();
    }

    public class NUnitConstraint
    {
        public NUnitConstraint IgnoreCase => this;
    }
}
