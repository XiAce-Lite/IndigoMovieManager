using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WhiteBrowser 互換の addWhere / addOrder で使う、軽量な SQL 風式を評価する。
    /// </summary>
    internal static class WhiteBrowserSkinQueryOverlayCompiler
    {
        public static bool TryCompileWhere(
            string whereClause,
            out Func<MovieRecords, bool> predicate,
            out string errorMessage
        )
        {
            predicate = static _ => true;
            errorMessage = "";

            string normalizedClause = NormalizeWhereClause(whereClause);
            if (string.IsNullOrWhiteSpace(normalizedClause))
            {
                return true;
            }

            try
            {
                SqlExpressionParser parser = new(normalizedClause);
                SqlNode root = parser.ParseBooleanExpression();
                parser.EnsureEnd();
                predicate = movie => SqlValueCoercion.ToBoolean(root.Evaluate(movie));
                return true;
            }
            catch (SqlParseException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static bool TryCompileOrder(
            string orderClause,
            out WhiteBrowserSkinCompiledOrder compiledOrder,
            out string errorMessage
        )
        {
            compiledOrder = WhiteBrowserSkinCompiledOrder.Empty;
            errorMessage = "";

            string normalizedClause = NormalizeOrderClause(orderClause);
            if (string.IsNullOrWhiteSpace(normalizedClause))
            {
                return true;
            }

            try
            {
                SqlExpressionParser parser = new(normalizedClause);
                List<WhiteBrowserSkinCompiledOrderTerm> terms = [];

                while (!parser.IsEnd)
                {
                    SqlNode expression = parser.ParseValueExpression();
                    bool descending = false;
                    if (parser.TryConsumeKeyword("asc"))
                    {
                        descending = false;
                    }
                    else if (parser.TryConsumeKeyword("desc"))
                    {
                        descending = true;
                    }

                    terms.Add(
                        new WhiteBrowserSkinCompiledOrderTerm(
                            movie => expression.Evaluate(movie),
                            descending
                        )
                    );

                    if (!parser.TryConsume(SymbolTokenKind.Comma))
                    {
                        break;
                    }
                }

                parser.EnsureEnd();
                compiledOrder = new WhiteBrowserSkinCompiledOrder(terms);
                return true;
            }
            catch (SqlParseException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string NormalizeWhereClause(string whereClause)
        {
            string normalized = whereClause?.Trim() ?? "";
            if (
                normalized.Length >= 2
                && normalized.StartsWith('{')
                && normalized.EndsWith('}')
            )
            {
                normalized = normalized[1..^1].Trim();
            }

            return normalized;
        }

        private static string NormalizeOrderClause(string orderClause)
        {
            string normalized = orderClause?.Trim() ?? "";
            if (
                normalized.Length >= 2
                && normalized.StartsWith('{')
                && normalized.EndsWith('}')
            )
            {
                normalized = normalized[1..^1].Trim();
            }

            return normalized;
        }

        internal sealed class WhiteBrowserSkinCompiledOrder
        {
            public static WhiteBrowserSkinCompiledOrder Empty { get; } = new([]);

            public WhiteBrowserSkinCompiledOrder(
                IReadOnlyList<WhiteBrowserSkinCompiledOrderTerm> terms
            )
            {
                Terms = terms ?? [];
            }

            public IReadOnlyList<WhiteBrowserSkinCompiledOrderTerm> Terms { get; }

            public bool HasTerms => Terms.Count > 0;
        }

        internal sealed record WhiteBrowserSkinCompiledOrderTerm(
            Func<MovieRecords, object> Selector,
            bool Descending
        );

        internal static class SqlValueCoercion
        {
            public static bool ToBoolean(object value)
            {
                if (value == null)
                {
                    return false;
                }

                if (value is bool boolValue)
                {
                    return boolValue;
                }

                if (TryGetDecimal(value, out decimal decimalValue))
                {
                    return decimalValue != 0;
                }

                string text = ToStringValue(value);
                if (bool.TryParse(text, out bool parsedBool))
                {
                    return parsedBool;
                }

                return !string.IsNullOrWhiteSpace(text);
            }

            public static int Compare(object left, object right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return -1;
                }

                if (right == null)
                {
                    return 1;
                }

                if (TryGetDecimal(left, out decimal leftDecimal) && TryGetDecimal(right, out decimal rightDecimal))
                {
                    return leftDecimal.CompareTo(rightDecimal);
                }

                if (
                    TryGetDateTime(left, out DateTime leftDateTime)
                    && TryGetDateTime(right, out DateTime rightDateTime)
                )
                {
                    return leftDateTime.CompareTo(rightDateTime);
                }

                return StringComparer.CurrentCultureIgnoreCase.Compare(
                    ToStringValue(left),
                    ToStringValue(right)
                );
            }

            public static object Add(object left, object right)
            {
                if (TryGetDecimal(left, out decimal leftDecimal) && TryGetDecimal(right, out decimal rightDecimal))
                {
                    return leftDecimal + rightDecimal;
                }

                return ToStringValue(left) + ToStringValue(right);
            }

            public static object Subtract(object left, object right)
            {
                if (TryGetDecimal(left, out decimal leftDecimal) && TryGetDecimal(right, out decimal rightDecimal))
                {
                    return leftDecimal - rightDecimal;
                }

                return 0m;
            }

            public static object Multiply(object left, object right)
            {
                if (TryGetDecimal(left, out decimal leftDecimal) && TryGetDecimal(right, out decimal rightDecimal))
                {
                    return leftDecimal * rightDecimal;
                }

                return 0m;
            }

            public static object Divide(object left, object right)
            {
                if (
                    TryGetDecimal(left, out decimal leftDecimal)
                    && TryGetDecimal(right, out decimal rightDecimal)
                    && rightDecimal != 0
                )
                {
                    return leftDecimal / rightDecimal;
                }

                return 0m;
            }

            public static bool Like(object input, object pattern)
            {
                string inputText = ToStringValue(input);
                string patternText = ToStringValue(pattern);
                string regexPattern = "^"
                    + Regex.Escape(patternText)
                        .Replace("%", ".*")
                        .Replace("_", ".")
                    + "$";
                return Regex.IsMatch(
                    inputText,
                    regexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
            }

            public static string ToStringValue(object value)
            {
                return value switch
                {
                    null => "",
                    string text => text,
                    bool boolValue => boolValue ? "1" : "0",
                    IFormattable formattable => formattable.ToString(
                        null,
                        CultureInfo.InvariantCulture
                    ),
                    _ => value.ToString() ?? "",
                };
            }

            public static bool TryGetDecimal(object value, out decimal result)
            {
                switch (value)
                {
                    case null:
                        result = 0;
                        return false;
                    case decimal decimalValue:
                        result = decimalValue;
                        return true;
                    case byte byteValue:
                        result = byteValue;
                        return true;
                    case sbyte sbyteValue:
                        result = sbyteValue;
                        return true;
                    case short shortValue:
                        result = shortValue;
                        return true;
                    case ushort ushortValue:
                        result = ushortValue;
                        return true;
                    case int intValue:
                        result = intValue;
                        return true;
                    case uint uintValue:
                        result = uintValue;
                        return true;
                    case long longValue:
                        result = longValue;
                        return true;
                    case ulong ulongValue:
                        result = ulongValue;
                        return true;
                    case float floatValue:
                        result = (decimal)floatValue;
                        return true;
                    case double doubleValue:
                        result = (decimal)doubleValue;
                        return true;
                    case bool boolValue:
                        result = boolValue ? 1 : 0;
                        return true;
                    default:
                        return decimal.TryParse(
                            ToStringValue(value),
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out result
                        );
                }
            }

            private static bool TryGetDateTime(object value, out DateTime result)
            {
                return DateTime.TryParse(
                    ToStringValue(value),
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out result
                ) || DateTime.TryParse(
                    ToStringValue(value),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result
                );
            }
        }

        internal sealed class SqlValueComparer : IComparer<object>
        {
            public static SqlValueComparer Instance { get; } = new();

            public int Compare(object x, object y)
            {
                return SqlValueCoercion.Compare(x, y);
            }
        }

        private abstract class SqlNode
        {
            public abstract object Evaluate(MovieRecords movie);
        }

        private sealed class LiteralNode(object value) : SqlNode
        {
            public override object Evaluate(MovieRecords movie)
            {
                return value;
            }
        }

        private sealed class IdentifierNode(string name) : SqlNode
        {
            public override object Evaluate(MovieRecords movie)
            {
                return ResolveMovieField(movie, name);
            }
        }

        private sealed class UnaryNode(string operatorText, SqlNode operand) : SqlNode
        {
            public override object Evaluate(MovieRecords movie)
            {
                object value = operand.Evaluate(movie);
                return operatorText switch
                {
                    "not" => !SqlValueCoercion.ToBoolean(value),
                    "-" => SqlValueCoercion.TryGetDecimal(value, out decimal decimalValue)
                        ? -decimalValue
                        : 0m,
                    "+" => SqlValueCoercion.TryGetDecimal(value, out decimal decimalValue)
                        ? decimalValue
                        : 0m,
                    _ => value,
                };
            }
        }

        private sealed class BinaryNode(string operatorText, SqlNode left, SqlNode right) : SqlNode
        {
            public override object Evaluate(MovieRecords movie)
            {
                object leftValue = left.Evaluate(movie);
                object rightValue = right.Evaluate(movie);

                return operatorText switch
                {
                    "or" => SqlValueCoercion.ToBoolean(leftValue)
                        || SqlValueCoercion.ToBoolean(rightValue),
                    "and" => SqlValueCoercion.ToBoolean(leftValue)
                        && SqlValueCoercion.ToBoolean(rightValue),
                    "=" => SqlValueCoercion.Compare(leftValue, rightValue) == 0,
                    "!=" or "<>" => SqlValueCoercion.Compare(leftValue, rightValue) != 0,
                    ">" => SqlValueCoercion.Compare(leftValue, rightValue) > 0,
                    ">=" => SqlValueCoercion.Compare(leftValue, rightValue) >= 0,
                    "<" => SqlValueCoercion.Compare(leftValue, rightValue) < 0,
                    "<=" => SqlValueCoercion.Compare(leftValue, rightValue) <= 0,
                    "like" => SqlValueCoercion.Like(leftValue, rightValue),
                    "not like" => !SqlValueCoercion.Like(leftValue, rightValue),
                    "+" => SqlValueCoercion.Add(leftValue, rightValue),
                    "-" => SqlValueCoercion.Subtract(leftValue, rightValue),
                    "*" => SqlValueCoercion.Multiply(leftValue, rightValue),
                    "/" => SqlValueCoercion.Divide(leftValue, rightValue),
                    _ => throw new SqlParseException($"Unsupported operator: {operatorText}"),
                };
            }
        }

        private sealed class IsNullNode(SqlNode operand, bool negate) : SqlNode
        {
            public override object Evaluate(MovieRecords movie)
            {
                object value = operand.Evaluate(movie);
                bool isNull = value == null || string.IsNullOrWhiteSpace(value as string);
                return negate ? !isNull : isNull;
            }
        }

        private sealed class FunctionNode(
            string name,
            IReadOnlyList<SqlNode> arguments,
            string castTypeName
        ) : SqlNode
        {
            public override object Evaluate(MovieRecords movie)
            {
                string normalizedName = name?.Trim().ToLowerInvariant() ?? "";
                return normalizedName switch
                {
                    "upper" => SqlValueCoercion.ToStringValue(arguments[0].Evaluate(movie))
                        .ToUpperInvariant(),
                    "lower" => SqlValueCoercion.ToStringValue(arguments[0].Evaluate(movie))
                        .ToLowerInvariant(),
                    "trim" => EvaluateTrim(movie),
                    "substr" => EvaluateSubstr(movie),
                    "cast" => EvaluateCast(movie),
                    _ => throw new SqlParseException($"Unsupported function: {name}"),
                };
            }

            private object EvaluateTrim(MovieRecords movie)
            {
                string text = SqlValueCoercion.ToStringValue(arguments[0].Evaluate(movie));
                if (arguments.Count < 2)
                {
                    return text.Trim();
                }

                string trimChars = SqlValueCoercion.ToStringValue(arguments[1].Evaluate(movie));
                return string.IsNullOrEmpty(trimChars)
                    ? text.Trim()
                    : text.Trim(trimChars.ToCharArray());
            }

            private object EvaluateSubstr(MovieRecords movie)
            {
                string text = SqlValueCoercion.ToStringValue(arguments[0].Evaluate(movie));
                int startIndex = ResolveInt(arguments.ElementAtOrDefault(1)?.Evaluate(movie), 1);
                int length = ResolveInt(arguments.ElementAtOrDefault(2)?.Evaluate(movie), int.MaxValue);
                int normalizedStart = Math.Max(0, startIndex > 0 ? startIndex - 1 : startIndex);
                if (normalizedStart >= text.Length)
                {
                    return "";
                }

                int normalizedLength = Math.Max(0, Math.Min(length, text.Length - normalizedStart));
                return text.Substring(normalizedStart, normalizedLength);
            }

            private object EvaluateCast(MovieRecords movie)
            {
                object value = arguments.Count > 0 ? arguments[0].Evaluate(movie) : null;
                string typeName = castTypeName?.Trim().ToLowerInvariant() ?? "";
                return typeName switch
                {
                    "int" or "integer" => ResolveInt(value, 0),
                    "real" or "float" or "double" or "numeric" or "decimal" =>
                        SqlValueCoercion.TryGetDecimal(value, out decimal decimalValue)
                            ? decimalValue
                            : 0m,
                    _ => SqlValueCoercion.ToStringValue(value),
                };
            }

            private static int ResolveInt(object value, int fallback)
            {
                return SqlValueCoercion.TryGetDecimal(value, out decimal decimalValue)
                    ? (int)decimal.Truncate(decimalValue)
                    : fallback;
            }
        }

        private static object ResolveMovieField(MovieRecords movie, string name)
        {
            string normalizedName = name?.Trim().ToLowerInvariant() ?? "";
            if (normalizedName.Contains('.'))
            {
                normalizedName = normalizedName[(normalizedName.LastIndexOf('.') + 1)..];
            }

            return normalizedName switch
            {
                "movie_id" or "id" => movie?.Movie_Id ?? 0,
                "movie_name" or "name" => movie?.Movie_Name ?? "",
                "movie_body" or "body" => movie?.Movie_Body ?? "",
                "movie_path" or "path" => movie?.Movie_Path ?? "",
                "movie_length" or "length" or "len" => movie?.Movie_Length ?? "",
                "movie_size" or "size" => movie?.Movie_Size ?? 0,
                "last_date" => movie?.Last_Date ?? "",
                "file_date" => movie?.File_Date ?? "",
                "regist_date" => movie?.Regist_Date ?? "",
                "score" => movie?.Score ?? 0,
                "view_count" or "viewcount" => movie?.View_Count ?? 0,
                "hash" => movie?.Hash ?? "",
                "container" => movie?.Container ?? "",
                "video" => movie?.Video ?? "",
                "audio" => movie?.Audio ?? "",
                "extra" => movie?.Extra ?? "",
                "title" => movie?.Title ?? "",
                "album" => movie?.Album ?? "",
                "artist" => movie?.Artist ?? "",
                "grouping" => movie?.Grouping ?? "",
                "writer" => movie?.Writer ?? "",
                "genre" => movie?.Genre ?? "",
                "track" => movie?.Track ?? "",
                "camera" => movie?.Camera ?? "",
                "create_time" => movie?.Create_Time ?? "",
                "kana" => movie?.Kana ?? "",
                "roma" => movie?.Roma ?? "",
                "tag" or "tags" => movie?.Tags ?? "",
                "comment1" => movie?.Comment1 ?? "",
                "comment2" => movie?.Comment2 ?? "",
                "comment3" => movie?.Comment3 ?? "",
                "drive" => movie?.Drive ?? "",
                "dir" => movie?.Dir ?? "",
                "exist" or "exists" => movie?.IsExists ?? false,
                "ext" => movie?.Ext ?? "",
                _ => "",
            };
        }

        private sealed class SqlExpressionParser
        {
            private readonly List<Token> tokens;
            private int index;

            public SqlExpressionParser(string expressionText)
            {
                tokens = new SqlTokenizer(expressionText).Tokenize();
            }

            public bool IsEnd => Current.Kind == SymbolTokenKind.End;

            private Token Current => tokens[Math.Min(index, tokens.Count - 1)];

            public SqlNode ParseBooleanExpression()
            {
                return ParseOrExpression();
            }

            public SqlNode ParseValueExpression()
            {
                return ParseAdditiveExpression();
            }

            public bool TryConsume(SymbolTokenKind kind)
            {
                if (Current.Kind != kind)
                {
                    return false;
                }

                index++;
                return true;
            }

            public bool TryConsumeKeyword(string keyword)
            {
                if (
                    Current.Kind == SymbolTokenKind.Identifier
                    && string.Equals(Current.Text, keyword, StringComparison.OrdinalIgnoreCase)
                )
                {
                    index++;
                    return true;
                }

                return false;
            }

            public void EnsureEnd()
            {
                if (!IsEnd)
                {
                    throw new SqlParseException($"Unexpected token near '{Current.Text}'.");
                }
            }

            private SqlNode ParseOrExpression()
            {
                SqlNode node = ParseAndExpression();
                while (TryConsumeKeyword("or"))
                {
                    node = new BinaryNode("or", node, ParseAndExpression());
                }

                return node;
            }

            private SqlNode ParseAndExpression()
            {
                SqlNode node = ParseComparisonExpression();
                while (TryConsumeKeyword("and"))
                {
                    node = new BinaryNode("and", node, ParseComparisonExpression());
                }

                return node;
            }

            private SqlNode ParseComparisonExpression()
            {
                SqlNode left = ParseAdditiveExpression();

                if (TryConsumeKeyword("is"))
                {
                    bool negate = TryConsumeKeyword("not");
                    ExpectKeyword("null");
                    return new IsNullNode(left, negate);
                }

                if (TryConsumeKeyword("not"))
                {
                    ExpectKeyword("like");
                    return new BinaryNode("not like", left, ParseAdditiveExpression());
                }

                if (TryConsumeKeyword("like"))
                {
                    return new BinaryNode("like", left, ParseAdditiveExpression());
                }

                if (Current.Kind == SymbolTokenKind.Operator)
                {
                    string operatorText = Current.Text;
                    if (
                        operatorText is "=" or "!=" or "<>" or ">" or ">=" or "<" or "<="
                    )
                    {
                        index++;
                        return new BinaryNode(operatorText, left, ParseAdditiveExpression());
                    }
                }

                return left;
            }

            private SqlNode ParseAdditiveExpression()
            {
                SqlNode node = ParseMultiplicativeExpression();
                while (
                    Current.Kind == SymbolTokenKind.Operator
                    && (Current.Text == "+" || Current.Text == "-")
                )
                {
                    string operatorText = Current.Text;
                    index++;
                    node = new BinaryNode(operatorText, node, ParseMultiplicativeExpression());
                }

                return node;
            }

            private SqlNode ParseMultiplicativeExpression()
            {
                SqlNode node = ParseUnaryExpression();
                while (
                    Current.Kind == SymbolTokenKind.Operator
                    && (Current.Text == "*" || Current.Text == "/")
                )
                {
                    string operatorText = Current.Text;
                    index++;
                    node = new BinaryNode(operatorText, node, ParseUnaryExpression());
                }

                return node;
            }

            private SqlNode ParseUnaryExpression()
            {
                if (TryConsumeKeyword("not"))
                {
                    return new UnaryNode("not", ParseUnaryExpression());
                }

                if (
                    Current.Kind == SymbolTokenKind.Operator
                    && (Current.Text == "+" || Current.Text == "-")
                )
                {
                    string operatorText = Current.Text;
                    index++;
                    return new UnaryNode(operatorText, ParseUnaryExpression());
                }

                return ParsePrimaryExpression();
            }

            private SqlNode ParsePrimaryExpression()
            {
                if (TryConsume(SymbolTokenKind.OpenParen))
                {
                    SqlNode inner = ParseBooleanExpression();
                    Expect(SymbolTokenKind.CloseParen);
                    return inner;
                }

                if (Current.Kind == SymbolTokenKind.String)
                {
                    string text = Current.Text;
                    index++;
                    return new LiteralNode(text);
                }

                if (Current.Kind == SymbolTokenKind.Number)
                {
                    string numericText = Current.Text;
                    index++;
                    return decimal.TryParse(
                        numericText,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out decimal numericValue
                    )
                        ? new LiteralNode(numericValue)
                        : new LiteralNode(numericText);
                }

                if (TryConsumeKeyword("null"))
                {
                    return new LiteralNode(null);
                }

                if (Current.Kind == SymbolTokenKind.Identifier)
                {
                    string identifier = Current.Text;
                    index++;
                    if (TryConsume(SymbolTokenKind.OpenParen))
                    {
                        return ParseFunction(identifier);
                    }

                    return new IdentifierNode(identifier);
                }

                throw new SqlParseException($"Unexpected token near '{Current.Text}'.");
            }

            private SqlNode ParseFunction(string functionName)
            {
                if (string.Equals(functionName, "cast", StringComparison.OrdinalIgnoreCase))
                {
                    SqlNode argument = ParseBooleanExpression();
                    ExpectKeyword("as");
                    string typeName = ExpectIdentifier();
                    Expect(SymbolTokenKind.CloseParen);
                    return new FunctionNode("cast", [argument], typeName);
                }

                List<SqlNode> arguments = [];
                if (!TryConsume(SymbolTokenKind.CloseParen))
                {
                    do
                    {
                        arguments.Add(ParseBooleanExpression());
                    } while (TryConsume(SymbolTokenKind.Comma));

                    Expect(SymbolTokenKind.CloseParen);
                }

                return new FunctionNode(functionName, arguments, "");
            }

            private void Expect(SymbolTokenKind kind)
            {
                if (!TryConsume(kind))
                {
                    throw new SqlParseException($"Expected token '{kind}'.");
                }
            }

            private void ExpectKeyword(string keyword)
            {
                if (!TryConsumeKeyword(keyword))
                {
                    throw new SqlParseException($"Expected keyword '{keyword}'.");
                }
            }

            private string ExpectIdentifier()
            {
                if (Current.Kind != SymbolTokenKind.Identifier)
                {
                    throw new SqlParseException("Expected identifier.");
                }

                string text = Current.Text;
                index++;
                return text;
            }
        }

        private sealed class SqlTokenizer(string text)
        {
            private readonly string source = text ?? "";
            private int index;

            public List<Token> Tokenize()
            {
                List<Token> result = [];
                while (index < source.Length)
                {
                    char current = source[index];
                    if (char.IsWhiteSpace(current))
                    {
                        index++;
                        continue;
                    }

                    if (current == '\'')
                    {
                        result.Add(new Token(SymbolTokenKind.String, ReadStringLiteral()));
                        continue;
                    }

                    if (char.IsDigit(current))
                    {
                        result.Add(new Token(SymbolTokenKind.Number, ReadNumber()));
                        continue;
                    }

                    if (IsIdentifierStart(current))
                    {
                        result.Add(new Token(SymbolTokenKind.Identifier, ReadIdentifier()));
                        continue;
                    }

                    switch (current)
                    {
                        case '(':
                            index++;
                            result.Add(new Token(SymbolTokenKind.OpenParen, "("));
                            break;
                        case ')':
                            index++;
                            result.Add(new Token(SymbolTokenKind.CloseParen, ")"));
                            break;
                        case ',':
                            index++;
                            result.Add(new Token(SymbolTokenKind.Comma, ","));
                            break;
                        case '+':
                        case '-':
                        case '*':
                        case '/':
                        case '=':
                            index++;
                            result.Add(new Token(SymbolTokenKind.Operator, current.ToString()));
                            break;
                        case '!':
                            result.Add(new Token(SymbolTokenKind.Operator, ReadBangOperator()));
                            break;
                        case '<':
                        case '>':
                            result.Add(new Token(SymbolTokenKind.Operator, ReadAngleOperator()));
                            break;
                        default:
                            throw new SqlParseException($"Unexpected character '{current}'.");
                    }
                }

                result.Add(new Token(SymbolTokenKind.End, ""));
                return result;
            }

            private string ReadStringLiteral()
            {
                StringBuilder builder = new();
                index++;
                while (index < source.Length)
                {
                    char current = source[index++];
                    if (current == '\'')
                    {
                        if (index < source.Length && source[index] == '\'')
                        {
                            builder.Append('\'');
                            index++;
                            continue;
                        }

                        return builder.ToString();
                    }

                    builder.Append(current);
                }

                throw new SqlParseException("Unterminated string literal.");
            }

            private string ReadNumber()
            {
                int start = index;
                while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.'))
                {
                    index++;
                }

                return source[start..index];
            }

            private string ReadIdentifier()
            {
                int start = index;
                while (index < source.Length && IsIdentifierPart(source[index]))
                {
                    index++;
                }

                return source[start..index];
            }

            private string ReadBangOperator()
            {
                if (index + 1 < source.Length && source[index + 1] == '=')
                {
                    index += 2;
                    return "!=";
                }

                throw new SqlParseException("Unexpected '!'.");
            }

            private string ReadAngleOperator()
            {
                char current = source[index];
                if (index + 1 < source.Length)
                {
                    char next = source[index + 1];
                    if ((current == '<' && next == '=') || (current == '>' && next == '='))
                    {
                        index += 2;
                        return $"{current}{next}";
                    }

                    if (current == '<' && next == '>')
                    {
                        index += 2;
                        return "<>";
                    }
                }

                index++;
                return current.ToString();
            }

            private static bool IsIdentifierStart(char value)
            {
                return char.IsLetter(value) || value == '_' || value == '.';
            }

            private static bool IsIdentifierPart(char value)
            {
                return char.IsLetterOrDigit(value) || value is '_' or '.' or '$';
            }
        }

        private readonly record struct Token(SymbolTokenKind Kind, string Text);

        internal enum SymbolTokenKind
        {
            Identifier,
            String,
            Number,
            Operator,
            OpenParen,
            CloseParen,
            Comma,
            End,
        }

        private sealed class SqlParseException(string message) : Exception(message);
    }
}
