using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScenarioMathPractice
{
    public enum ScenarioCategory { All, Addition, Subtraction, Multiplication, Division, Mixed }
    public enum ScenarioLevel { Beginner, Intermediate, Advanced }

    /// <summary>题库中的一条情景模板：情景文本 + 答案算式 + 类别。</summary>
    public sealed class ScenarioItem
    {
        public string Text;          // 含 {1}{2}{3} 占位符的情景文本，以问句结尾
        public string Expression;    // 由占位符和 + - * ( ) 组成的答案算式
        public ScenarioCategory Category;
        public int PlaceholderCount; // 算式引用的最大占位符序号
        public int OperatorCount;    // 算式中的运算符个数，决定级别

        public static ScenarioCategory ParseCategory(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ScenarioCategory.Mixed;
            string value = text.Trim();
            if (value == "加" || value == "加法" || value == "+" || value == "add") return ScenarioCategory.Addition;
            if (value == "减" || value == "减法" || value == "-" || value == "sub") return ScenarioCategory.Subtraction;
            if (value == "乘" || value == "乘法" || value == "*" || value == "×" || value == "mul") return ScenarioCategory.Multiplication;
            if (value == "除" || value == "除法" || value == "/" || value == "÷" || value == "div") return ScenarioCategory.Division;
            if (value == "混" || value == "混合" || value == "复合") return ScenarioCategory.Mixed;
            return ScenarioCategories.Invalid;
        }

        public static string CategoryName(ScenarioCategory category)
        {
            switch (category)
            {
                case ScenarioCategory.Addition: return "加";
                case ScenarioCategory.Subtraction: return "减";
                case ScenarioCategory.Multiplication: return "乘";
                case ScenarioCategory.Division: return "除";
                case ScenarioCategory.Mixed: return "混合";
                default: return "全部";
            }
        }
    }

    public static class ScenarioCategories
    {
        public const ScenarioCategory Invalid = (ScenarioCategory)99;
    }

    /// <summary>解析题库文本。与 WordBank 类似：错误行跳过并记录。</summary>
    public sealed class ScenarioBank
    {
        private static readonly char[] Separators = new char[] { '|', '\uff5c', '\t' };

        public static List<ScenarioItem> Parse(string text, List<string> errors, List<string> warnings)
        {
            var items = new List<ScenarioItem>();
            if (text == null) return items;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string raw = lines[lineIndex];
                string line = StripComment(raw).Trim();
                if (line.Length == 0) continue;
                int number = lineIndex + 1;
                string[] parts = line.Split(Separators);
                if (parts.Length < 2)
                {
                    warnings.Add("第 " + number + " 行：缺少竖线分隔，已跳过。");
                    continue;
                }
                string body = parts[0].Trim();
                string expression = parts[1].Trim();
                string categoryText = parts.Length > 2 ? parts[2] : null;

                if (body.Length == 0) { warnings.Add("第 " + number + " 行：情景文本为空，已跳过。"); continue; }
                if (expression.Length == 0) { warnings.Add("第 " + number + " 行：答案算式为空，已跳过。"); continue; }
                if (body.IndexOf('{') < 0) { warnings.Add("第 " + number + " 行：情景文本缺少 {1} 等数字占位符，已跳过。"); continue; }

                ScenarioCategory category = ScenarioItem.ParseCategory(categoryText);
                if (category == ScenarioCategories.Invalid)
                {
                    warnings.Add("第 " + number + " 行：类别“" + (categoryText == null ? "" : categoryText.Trim()) + "”无法识别，已按混合处理。");
                    category = ScenarioCategory.Mixed;
                }

                List<int> placeholderNumbers;
                string parseError;
                if (!ScenarioExpression.Validate(expression, out placeholderNumbers, out parseError))
                {
                    warnings.Add("第 " + number + " 行：算式无效（" + parseError + "），已跳过。");
                    continue;
                }
                foreach (int used in placeholderNumbers)
                {
                    if (body.IndexOf("{" + used + "}", StringComparison.Ordinal) < 0)
                    {
                        warnings.Add("第 " + number + " 行：算式使用了文本中不存在的占位符 {" + used + "}，已跳过。");
                        parseError = "placeholder";
                        break;
                    }
                }
                if (parseError != null) continue;
                foreach (int textPlaceholder in TextPlaceholders(body))
                {
                    if (!placeholderNumbers.Contains(textPlaceholder))
                    {
                        warnings.Add("第 " + number + " 行：情景文本中的占位符 {" + textPlaceholder + "} 未在算式中使用，已跳过。");
                        parseError = "unused placeholder";
                        break;
                    }
                }
                if (parseError != null) continue;
                if (placeholderNumbers.Count == 0)
                {
                    warnings.Add("第 " + number + " 行：算式没有使用任何占位符，已跳过。");
                    continue;
                }
                if (placeholderNumbers.Count > 4)
                {
                    warnings.Add("第 " + number + " 行：占位符超过四个，已跳过。");
                    continue;
                }

                var item = new ScenarioItem();
                item.Text = body;
                item.Expression = expression;
                item.Category = category;
                item.PlaceholderCount = placeholderNumbers[placeholderNumbers.Count - 1];
                item.OperatorCount = ScenarioExpression.CountOperators(expression);
                if (item.OperatorCount < 1 || item.OperatorCount > 3)
                {
                    warnings.Add("第 " + number + " 行：算式运算符个数需在 1 到 3 之间，已跳过。");
                    continue;
                }
                items.Add(item);
            }
            return items;
        }

        private static string StripComment(string line)
        {
            if (line == null) return string.Empty;
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#") || trimmed.StartsWith("//")) return string.Empty;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            int slash = line.IndexOf("//", StringComparison.Ordinal);
            if (slash >= 0) line = line.Substring(0, slash);
            return line;
        }

        /// <summary>收集情景文本中出现的 {n} 占位符序号。</summary>
        private static List<int> TextPlaceholders(string body)
        {
            var result = new List<int>();
            int index = body.IndexOf('{');
            while (index >= 0)
            {
                int close = body.IndexOf('}', index + 1);
                if (close < 0) break;
                int value;
                if (int.TryParse(body.Substring(index + 1, close - index - 1), NumberStyles.None, CultureInfo.InvariantCulture, out value) && !result.Contains(value))
                    result.Add(value);
                index = body.IndexOf('{', close + 1);
            }
            return result;
        }

        public static string ReadTextFile(string path)
        {
            using (var reader = new System.IO.StreamReader(path, System.Text.Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        public static void Save(string path, IEnumerable<ScenarioItem> items)
        {
            var builder = new StringBuilder();
            foreach (var item in items)
            {
                builder.Append(item.Text).Append('|').Append(item.Expression);
                if (item.Category != ScenarioCategory.Mixed) builder.Append('|').Append(ScenarioItem.CategoryName(item.Category));
                builder.Append("\r\n");
            }
            System.IO.File.WriteAllText(path, builder.ToString(), new System.Text.UTF8Encoding(true));
        }
    }

    /// <summary>占位符算式的解析、校验与求值。支持 + - * 、括号、从左到右计算。</summary>
    public static class ScenarioExpression
    {
        public static int CountOperators(string expression)
        {
            int count = 0;
            foreach (char c in expression)
            {
                if (c == '+' || c == '-' || c == '*' || c == '\u00d7' || c == '/' || c == '\u00f7') count++;
            }
            return count;
        }

        public static bool Validate(string expression, out List<int> placeholders, out string error)
        {
            placeholders = new List<int>();
            error = null;
            if (string.IsNullOrWhiteSpace(expression)) { error = "empty"; return false; }
            string normalized = Normalize(expression);
            int index = 0;
            var seen = new HashSet<int>();
            if (!ParseExpression(normalized, ref index, placeholders, seen, false, out error)) return false;
            if (index != normalized.Length) { error = "unexpected char at " + index; return false; }
            placeholders.Sort();
            return true;
        }

        private static string Normalize(string expression)
        {
            var builder = new StringBuilder();
            foreach (char c in expression)
            {
                switch (c)
                {
                    case '\uff0b': builder.Append('+'); break;      // 全角 +
                    case '\uff0d': case '\u2212': builder.Append('-'); break; // 全角/数学减号
                    case '\u00d7': case '\uff58': case '\uff38': builder.Append('*'); break; // × x X
                    case '\u00f7': case '\uff0f': builder.Append('/'); break; // ÷ ／
                    case '\uff08': builder.Append('('); break;      // 全角括号
                    case '\uff09': builder.Append(')'); break;
                    case '\uff5b': builder.Append('{'); break;      // 全角花括号
                    case '\uff5d': builder.Append('}'); break;
                    case '\u2460': builder.Append("{1}"); break;    // ①
                    case '\u2461': builder.Append("{2}"); break;    // ②
                    case '\u2462': builder.Append("{3}"); break;    // ③
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }

        // 递归下降：expression = term (('+'|'-') term)* ; term = factor ('*' factor)*
        private static bool ParseExpression(string text, ref int index, List<int> placeholders, HashSet<int> seen, bool insideParenthesis, out string error)
        {
            error = null;
            if (!ParseTerm(text, ref index, placeholders, seen, out error)) return false;
            while (index < text.Length)
            {
                char c = text[index];
                if (c == '+' || c == '-')
                {
                    index++;
                    if (!ParseTerm(text, ref index, placeholders, seen, out error)) return false;
                }
                else if (c == ')')
                {
                    if (!insideParenthesis) { error = "unbalanced )"; return false; }
                    return true;
                }
                else { error = "unexpected char '" + c + "'"; return false; }
            }
            if (insideParenthesis) { error = "missing )"; return false; }
            return true;
        }

        private static bool ParseTerm(string text, ref int index, List<int> placeholders, HashSet<int> seen, out string error)
        {
            error = null;
            if (!ParseFactor(text, ref index, placeholders, seen, out error)) return false;
            while (index < text.Length)
            {
                char c = text[index];
                if (c == '*')
                {
                    index++;
                    if (!ParseFactor(text, ref index, placeholders, seen, out error)) return false;
                }
                else if (c == '/')
                {
                    // 除法允许出现在算式里，由求值阶段保证整除且除数非零，
                    // 生成时对不满足条件的数字代入直接重试。
                    index++;
                    if (!ParseFactor(text, ref index, placeholders, seen, out error)) return false;
                }
                else return true;
            }
            return true;
        }

        private static bool ParseFactor(string text, ref int index, List<int> placeholders, HashSet<int> seen, out string error)
        {
            error = null;
            if (index >= text.Length) { error = "unexpected end"; return false; }
            char c = text[index];
            if (c == '(')
            {
                index++;
                if (!ParseExpression(text, ref index, placeholders, seen, true, out error)) return false;
                if (index >= text.Length || text[index] != ')') { error = "missing )"; return false; }
                index++;
                return true;
            }
            if (c == '{')
            {
                int close = text.IndexOf('}', index + 1);
                if (close < 0) { error = "missing }"; return false; }
                string inner = text.Substring(index + 1, close - index - 1);
                int number;
                if (!int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1 || number > 4)
                {
                    error = "bad placeholder {" + inner + "}";
                    return false;
                }
                if (seen.Add(number)) placeholders.Add(number);
                index = close + 1;
                return true;
            }
            error = "unexpected char '" + c + "'";
            return false;
        }

        /// <summary>按代入的数字求值。要求每个中间结果都在 [0, max] 内、除法整除且除数非零。</summary>
        public static bool TryEvaluate(string expression, int[] numbers, int max, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(expression) || numbers == null || max < 1) return false;
            string normalized = Normalize(expression);
            int index = 0;
            int value;
            if (!EvalExpression(normalized, ref index, numbers, max, out value)) return false;
            if (index != normalized.Length) return false;
            result = value;
            return true;
        }

        private static bool EvalExpression(string text, ref int index, int[] numbers, int max, out int value)
        {
            value = 0;
            int left;
            if (!EvalTerm(text, ref index, numbers, max, out left)) return false;
            while (index < text.Length)
            {
                char c = text[index];
                if (c == '+' || c == '-')
                {
                    index++;
                    int right;
                    if (!EvalTerm(text, ref index, numbers, max, out right)) return false;
                    left = c == '+' ? left + right : left - right;
                    if (left < 0 || left > max) return false; // 情景数量不为负且不超范围
                }
                else if (c == ')') { value = left; return true; }
                else return false;
            }
            value = left;
            return true;
        }

        private static bool EvalTerm(string text, ref int index, int[] numbers, int max, out int value)
        {
            value = 0;
            int left;
            if (!EvalFactor(text, ref index, numbers, max, out left)) return false;
            while (index < text.Length && (text[index] == '*' || text[index] == '/'))
            {
                char op = text[index];
                index++;
                int right;
                if (!EvalFactor(text, ref index, numbers, max, out right)) return false;
                if (op == '*')
                {
                    left *= right;
                }
                else
                {
                    if (right == 0 || left % right != 0) return false; // 整除且除数非零
                    left /= right;
                }
                if (left < 0 || left > max) return false;
            }
            value = left;
            return true;
        }

        private static bool EvalFactor(string text, ref int index, int[] numbers, int max, out int value)
        {
            value = 0;
            if (index >= text.Length) return false;
            char c = text[index];
            if (c == '(')
            {
                index++;
                int inner;
                if (!EvalExpression(text, ref index, numbers, max, out inner)) return false;
                if (index >= text.Length || text[index] != ')') return false;
                index++;
                value = inner;
                return true;
            }
            if (c == '{')
            {
                int close = text.IndexOf('}', index + 1);
                if (close < 0) return false;
                int number;
                if (!int.TryParse(text.Substring(index + 1, close - index - 1), NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
                if (number < 1 || number > numbers.Length) return false;                index = close + 1;
                value = numbers[number - 1];
                return true;
            }
            return false;
        }

        /// <summary>把情景文本中的占位符替换为数字，生成展示与朗读文本。</summary>
        public static string FillText(string text, int[] numbers)
        {
            var builder = new StringBuilder(text);
            for (int i = 0; i < numbers.Length; i++)
                builder.Replace("{" + (i + 1) + "}", numbers[i].ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    /// <summary>一道已生成的情景题：完整题面与答案。</summary>
    public sealed class ScenarioProblem
    {
        public string DisplayText { get; private set; }
        public string SpeechText { get; private set; }
        public int Answer { get; private set; }
        public ScenarioCategory Category { get; private set; }
        public ScenarioLevel Level { get; private set; }

        public ScenarioProblem(string display, string speech, int answer, ScenarioCategory category, ScenarioLevel level)
        {
            DisplayText = display;
            SpeechText = speech;
            Answer = answer;
            Category = category;
            Level = level;
        }
    }

    public sealed class ScenarioEngine
    {
        private const int AttemptsPerTemplate = 1200;
        private const int AttemptsPerPhase = 300;
        private readonly Random _random;
        private List<ScenarioItem> _items;

        public ScenarioEngine(Random random) { _random = random ?? new Random(); }

        public void Load(IEnumerable<ScenarioItem> items)
        {
            _items = new List<ScenarioItem>();
            if (items == null) return;
            foreach (var item in items) if (item != null) _items.Add(item);
        }

        public int Count { get { return _items == null ? 0 : _items.Count; } }

        /// <summary>按类别、级别和数字范围出一道题。无可用模板或生成失败时返回 null。</summary>
        public ScenarioProblem Next(ScenarioCategory filter, ScenarioLevel level, int range)
        {
            if (range < 1 || range > 1000000) throw new ArgumentOutOfRangeException("range");
            if (_items == null || _items.Count == 0) return null;

            var candidates = new List<ScenarioItem>();
            foreach (var item in _items)
            {
                if (!MatchesCategory(item, filter)) continue;
                if (!MatchesLevel(item, level)) continue;
                candidates.Add(item);
            }
            if (candidates.Count == 0) return null;

            // 随机起点遍历候选模板；每条模板先在全范围内取数，多次失败后逐步收缩数字池，
            // 让乘除类高级模板也能稳定出题（评估约束始终按原范围校验）。
            int start = _random.Next(candidates.Count);
            for (int offset = 0; offset < candidates.Count; offset++)
            {
                var item = candidates[(start + offset) % candidates.Count];
                // 乘除类题目（类别为乘或除）：初级固定在 9×9 乘法表以内；
                // 中级和高级与范围联动，乘法因数、除法被除数不超过所选范围。
                bool isMultiply = item.Category == ScenarioCategory.Multiplication;
                bool isDivide = item.Category == ScenarioCategory.Division;
                int generationBase = range;
                int evaluateMax = range;
                int divisorCap = 0;
                if (isMultiply)
                {
                    if (level == ScenarioLevel.Beginner)
                    {
                        // 初级乘法因数不超过 9，积不超过 81，正好在乘法表以内。
                        generationBase = 9;
                        evaluateMax = 81;
                    }
                    else
                    {
                        // 中级和高级与范围联动：因数不超过所选范围，积不超过范围数的平方。
                        generationBase = range;
                        evaluateMax = range * range;
                    }
                }
                else if (isDivide)
                {
                    if (level == ScenarioLevel.Beginner)
                    {
                        // 初级除法被除数不超过 81、除数和商不超过 9，都是表内除法。
                        generationBase = 81;
                        evaluateMax = 9;
                        divisorCap = 9;
                    }
                    else
                    {
                        // 中级和高级与范围联动：被除数不超过所选范围，商不超过被除数。
                        generationBase = range;
                        evaluateMax = range;
                    }
                }
                for (int attempt = 0; attempt < AttemptsPerTemplate; attempt++)
                {
                    var numbers = GenerateNumbers(item, GenerationBound(generationBase, attempt), divisorCap);
                    if (numbers == null) continue;
                    int answer;
                    if (!ScenarioExpression.TryEvaluate(item.Expression, numbers, evaluateMax, out answer)) continue;
                    if (answer < 0 || answer > evaluateMax) continue;
                    string display = ScenarioExpression.FillText(item.Text, numbers);
                    string speech = ScenarioExpression.FillText(item.Text, numbers);
                    return new ScenarioProblem(display, speech, answer, item.Category, level);
                }
            }
            return null;
        }

        /// <summary>第 phase 阶段（每阶段 AttemptsPerPhase 次）使用的取数上限。</summary>
        private static int GenerationBound(int range, int attempt)
        {
            int phase = Math.Min(3, attempt / AttemptsPerPhase);
            switch (phase)
            {
                case 0: return range;
                case 1: return Math.Max(2, range / 2);
                case 2: return Math.Max(2, range / 5);
                default: return Math.Max(2, range / 10);
            }
        }

        private static bool MatchesCategory(ScenarioItem item, ScenarioCategory filter)
        {
            if (filter == ScenarioCategory.All) return true;
            return item.Category == filter;
        }

        private static bool MatchesLevel(ScenarioItem item, ScenarioLevel level)
        {
            switch (level)
            {
                case ScenarioLevel.Beginner: return item.OperatorCount == 1;
                case ScenarioLevel.Intermediate: return item.OperatorCount == 2;
                case ScenarioLevel.Advanced: return item.OperatorCount == 3;
                default: return true;
            }
        }

        private int[] GenerateNumbers(ScenarioItem item, int bound, int divisorCap)
        {
            var numbers = new int[item.PlaceholderCount];
            for (int i = 0; i < numbers.Length; i++)
            {
                int cap = bound;
                if (divisorCap > 0 && item.Expression.IndexOf("/{" + (i + 1) + "}", StringComparison.Ordinal) >= 0)
                {
                    // 表达式中紧跟在除号后面的占位符是除数，初级除法除数不超过 9。
                    cap = Math.Min(cap, divisorCap);
                }
                // 占位数字从 1 开始，避免“0 件上衣”这类无意义情景；0 可以由减法产生。
                numbers[i] = _random.Next(1, cap + 1);
            }
            return numbers;
        }

        public static bool TryParseAnswer(string input, out int answer)
        {
            answer = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string normalized = input.Trim().Replace('\uff0d', '-').Replace('\u2212', '-').Replace('\uff0b', '+');
            return int.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out answer);
        }
    }
}
