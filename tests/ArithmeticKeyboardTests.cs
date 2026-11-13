using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ArithmeticPractice;

internal static class ArithmeticKeyboardTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using (var form = new ArithmeticForm())
            {
                SetField(form, "_speechOk", false);
                form.Show();
                Application.DoEvents();

                var input = Field<TextBox>(form, "_answerBox");
                var replay = Field<Button>(form, "_replayButton");
                var operation = Field<ComboBox>(form, "_operationBox");
                var level = Field<ComboBox>(form, "_levelBox");
                var operandCount = Field<ComboBox>(form, "_operandCountBox");
                var problem = Field<ArithmeticProblem>(form, "_current");

                Assert(level.Items.Count == 3 && level.SelectedIndex == 0, "level group offers beginner, intermediate and advanced");
                Assert(!operandCount.Enabled && operandCount.Items.Count == 3 && operandCount.SelectedIndex == 0,
                    "operand count defaults to three and is disabled below advanced");
                level.SelectedIndex = 1;
                Application.DoEvents();
                problem = Field<ArithmeticProblem>(form, "_current");
                Assert(problem.Numbers.Length == 2 && !operandCount.Enabled, "intermediate uses two numbers and keeps count disabled");
                Assert(problem.DisplayText.IndexOf('?') >= 0, "intermediate renders an unknown equation position");
                level.SelectedIndex = 2;
                Application.DoEvents();
                Assert(operandCount.Enabled, "advanced enables operand count selector");
                foreach (int selected in new[] { 0, 1, 2 })
                {
                    operandCount.SelectedIndex = selected;
                    Application.DoEvents();
                    problem = Field<ArithmeticProblem>(form, "_current");
                    Assert(problem.Numbers.Length == selected + 3, "advanced generates selected operand count " + (selected + 3));
                }
                operation.SelectedIndex = 0;
                operandCount.SelectedIndex = 0;
                Application.DoEvents();
                problem = Field<ArithmeticProblem>(form, "_current");
                Assert(Array.IndexOf(problem.Operators, OperationKind.Addition) >= 0 && Array.IndexOf(problem.Operators, OperationKind.Subtraction) >= 0,
                    "advanced compound operation contains both addition and subtraction");
                input.Text = "123";
                input.Select(1, 0);
                Assert(KeyMessage(input, Keys.F5), "F5 is consumed by arithmetic form");
                Assert(input.Text == "123" && input.SelectionStart == 1 && input.SelectionLength == 0,
                    "F5 preserves arithmetic answer and caret");
                SendMessage(input.Handle, 0x0102, new IntPtr('4'), IntPtr.Zero);
                Assert(input.Text == "1423", "typing after arithmetic F5 uses preserved caret");
                input.Select(2, 0);
                replay.Focus();
                replay.PerformClick();
                Assert(input.Focused && input.SelectionStart == 2 && input.SelectionLength == 0,
                    "arithmetic replay button preserves caret");

                foreach (Keys key in new[] { Keys.Enter, Keys.Space })
                {
                    problem = Field<ArithmeticProblem>(form, "_current");
                    int before = Field<int>(form, "_questionNumber");
                    input.Text = problem.Answer.ToString();
                    input.Select(input.TextLength, 0);
                    Assert(KeyMessage(input, key), key + " is consumed for arithmetic answer");
                    Assert(Field<int>(form, "_questionNumber") == before + 1 && input.Text == string.Empty,
                        key + " correct answer opens one new problem");
                    Assert(Field<int>(form, "_correctCount") > 0, key + " increments correct count");

                    problem = Field<ArithmeticProblem>(form, "_current");
                    before = Field<int>(form, "_questionNumber");
                    input.Text = (problem.Answer + 1).ToString();
                    Assert(KeyMessage(input, key), key + " wrong arithmetic answer is consumed");
                    Assert(Field<int>(form, "_questionNumber") == before && Field<int>(form, "_wrongCount") > 0,
                        key + " wrong answer stays on same problem");
                    Assert(input.SelectionLength == input.Text.Length, key + " selects wrong answer for retry");

                    before = Field<int>(form, "_questionNumber");
                    input.Text = string.Empty;
                    Assert(KeyMessage(input, key), key + " empty arithmetic answer is consumed");
                    Assert(Field<int>(form, "_questionNumber") == before && input.Text.Length == 0,
                        key + " empty answer does not advance or insert whitespace");
                }

                foreach (Keys modifiers in new[] { Keys.Control, Keys.Alt, Keys.Shift })
                {
                    int before = Field<int>(form, "_questionNumber");
                    var args = new KeyEventArgs(modifiers | Keys.Space);
                    Call(form, "AnswerBox_KeyDown", input, args);
                    Assert(!args.SuppressKeyPress && Field<int>(form, "_questionNumber") == before,
                        modifiers + "+Space does not submit arithmetic answer");
                }

                form.Close();
            }

            Console.WriteLine("ARITHMETIC KEYBOARD TESTS: PASS (" + _checks + " checks)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ARITHMETIC KEYBOARD TESTS: FAIL\n" + ex);
            return 1;
        }
    }

    private static bool KeyMessage(Control control, Keys key)
    {
        var message = Message.Create(control.Handle, 0x0100, new IntPtr((int)key), IntPtr.Zero);
        return (bool)typeof(Control).GetMethod("ProcessKeyMessage", PrivateInstance)
            .Invoke(control, new object[] { message });
    }

    private static T Field<T>(object owner, string name)
    {
        return (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
    }

    private static void SetField(object owner, string name, object value)
    {
        owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
    }

    private static object Call(object owner, string name, params object[] args)
    {
        return owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, args);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }
}
