using System;
using System.Globalization;
using System.Text;

namespace ArithmeticPractice
{
    public enum OperationKind { Mixed, Addition, Subtraction, Multiplication, Division }
    public enum PracticeLevel { Beginner, Intermediate, Advanced }

    public sealed class ArithmeticProblem
    {
        public int[] Numbers { get; private set; }
        public OperationKind[] Operators { get; private set; }
        public int Result { get; private set; }
        // -1 means the result is missing; otherwise the index of a missing operand.
        public int MissingIndex { get; private set; }
        public int Answer { get { return MissingIndex < 0 ? Result : Numbers[MissingIndex]; } }

        public ArithmeticProblem(int[] numbers, OperationKind[] operators, int result, int missingIndex)
        {
            if (numbers == null || numbers.Length < 2 || operators == null || operators.Length != numbers.Length - 1)
                throw new ArgumentException("An expression needs at least two operands and matching operators.");
            if (missingIndex < -1 || missingIndex >= numbers.Length)
                throw new ArgumentOutOfRangeException("missingIndex");
            Numbers = (int[])numbers.Clone();
            Operators = (OperationKind[])operators.Clone();
            Result = result;
            MissingIndex = missingIndex;
        }

        public string DisplayText
        {
            get
            {
                var text = new StringBuilder();
                for (int i = 0; i < Numbers.Length; i++)
                {
                    if (i > 0) text.Append(" ").Append(Symbol(Operators[i - 1])).Append(" ");
                    text.Append(i == MissingIndex ? "?" : Numbers[i].ToString(CultureInfo.InvariantCulture));
                }
                text.Append(" = ").Append(MissingIndex < 0 ? "?" : Result.ToString(CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        public string SpeechText
        {
            get
            {
                var text = new StringBuilder();
                if (Numbers.Length > 2) text.Append("从左到右计算。");
                for (int i = 0; i < Numbers.Length; i++)
                {
                    if (i > 0) text.Append("，").Append(SpeechOperator(Operators[i - 1])).Append("，");
                    text.Append(i == MissingIndex ? "未知数" : Numbers[i].ToString(CultureInfo.InvariantCulture));
                }
                text.Append("，等于，").Append(MissingIndex < 0 ? "未知数" : Result.ToString(CultureInfo.InvariantCulture));
                text.Append("。请回答未知数是多少？");
                return text.ToString();
            }
        }

        private static string Symbol(OperationKind operation)
        {
            switch (operation)
            {
                case OperationKind.Addition: return "+";
                case OperationKind.Subtraction: return "−";
                case OperationKind.Multiplication: return "×";
                case OperationKind.Division: return "÷";
                default: throw new ArgumentException("Invalid expression operator.");
            }
        }

        private static string SpeechOperator(OperationKind operation)
        {
            switch (operation)
            {
                case OperationKind.Addition: return "加";
                case OperationKind.Subtraction: return "减";
                case OperationKind.Multiplication: return "乘以";
                case OperationKind.Division: return "除以";
                default: throw new ArgumentException("Invalid expression operator.");
            }
        }
    }

    public sealed class ArithmeticEngine
    {
        private readonly Random _random;
        public ArithmeticEngine(Random random) { _random = random ?? new Random(); }

        public ArithmeticProblem Next(OperationKind requested, int range)
        {
            return Next(requested, range, PracticeLevel.Beginner, 3);
        }

        public ArithmeticProblem Next(OperationKind requested, int range, PracticeLevel level, int operandCount)
        {
            if (!Enum.IsDefined(typeof(OperationKind), requested)) throw new ArgumentOutOfRangeException("requested");
            if (!Enum.IsDefined(typeof(PracticeLevel), level)) throw new ArgumentOutOfRangeException("level");
            if (range < 1 || range > 1000000) throw new ArgumentOutOfRangeException("range");
            if (operandCount < 3 || operandCount > 5) throw new ArgumentOutOfRangeException("operandCount");

            int count = level == PracticeLevel.Advanced ? operandCount : 2;
            var numbers = new int[count];
            var operators = new OperationKind[count - 1];
            for (int i = 0; i < operators.Length; i++)
                operators[i] = requested == OperationKind.Mixed
                    ? (_random.Next(2) == 0 ? OperationKind.Addition : OperationKind.Subtraction)
                    : requested;

            if (requested == OperationKind.Mixed && level == PracticeLevel.Advanced)
            {
                // Every compound advanced problem must contain BOTH + and -.
                operators[0] = OperationKind.Addition;
                operators[1] = OperationKind.Subtraction;
                for (int i = operators.Length - 1; i > 0; i--)
                {
                    int j = _random.Next(i + 1);
                    OperationKind temp = operators[i]; operators[i] = operators[j]; operators[j] = temp;
                }
            }

            int result;
            if (requested == OperationKind.Division)
            {
                // Build backwards: dividend = quotient * all divisors.
                // Each left-to-right division is exact and never divides by zero.
                // Positive terms avoid ambiguous intermediate questions like 0 / ? = 0.
                result = _random.Next(1, Math.Min(10, range) + 1);
                int dividend = result;
                for (int i = 1; i < count; i++)
                {
                    numbers[i] = _random.Next(1, Math.Min(20, range / dividend) + 1);
                    dividend *= numbers[i];
                }
                numbers[0] = dividend;
            }
            else if (requested == OperationKind.Multiplication)
            {
                // Do not allow a zero factor for intermediate equations: ? * 0 = 0
                // would not have a unique solution.
                int minimum = level == PracticeLevel.Intermediate ? 1 : 0;
                numbers[0] = _random.Next(minimum, Math.Min(20, range) + 1);
                result = numbers[0];
                for (int i = 1; i < count; i++)
                {
                    int maximum = result == 0 ? Math.Min(20, range) : Math.Min(20, range / result);
                    numbers[i] = _random.Next(minimum, maximum + 1);
                    result *= numbers[i];
                }
            }
            else
            {
                numbers[0] = _random.Next(range + 1);
                result = numbers[0];
                for (int i = 1; i < count; i++)
                {
                    if (operators[i - 1] == OperationKind.Addition)
                    {
                        numbers[i] = _random.Next(range - result + 1);
                        result += numbers[i];
                    }
                    else
                    {
                        numbers[i] = _random.Next(result + 1);
                        result -= numbers[i];
                    }
                }
            }

            int missing = -1;
            if (level == PracticeLevel.Intermediate)
            {
                // All three equation positions occur; not just the result on the right.
                int position = _random.Next(count + 1);
                missing = position == count ? -1 : position;
            }
            return new ArithmeticProblem(numbers, operators, result, missing);
        }

        public static bool TryParseAnswer(string input, out int answer)
        {
            answer = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string normalized = input.Trim().Replace('−', '-').Replace('－', '-').Replace('＋', '+');
            return int.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out answer);
        }
    }
}
