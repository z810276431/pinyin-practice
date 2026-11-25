using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ScenarioMathPractice;

internal static class ScenarioKeyboardTests
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
            using (var form = new ScenarioForm())
            {
                SetField(form, "_speechOk", false);
                form.Show();
                Application.DoEvents();

                var input = Field<TextBox>(form, "_answerBox");
                var replay = Field<Button>(form, "_replayButton");
                var category = Field<ComboBox>(form, "_categoryBox");
                var level = Field<ComboBox>(form, "_levelBox");
                var range = Field<ComboBox>(form, "_rangeBox");
                var question = Field<Label>(form, "_questionLabel");

                Assert(category.Items.Count == 6 && category.SelectedIndex == 0, "category offers all, add, sub, mul, div and mixed");
                Assert(level.Items.Count == 3 && level.SelectedIndex == 0, "level offers beginner, intermediate and advanced");
                Assert(range.Items.Count == 3 && range.SelectedIndex == 1, "range defaults to within twenty");

                // 初级题目应为单步情景（内置题库保证存在）。
                Assert(question.Text != "（等待出题）" && question.Text.IndexOf('（') != 0, "first scenario problem is shown");
                Assert(question.Text.IndexOf('{') < 0, "scenario text has placeholders filled");

                // 切换级别与类别仍能出题。
                level.SelectedIndex = 1;
                Application.DoEvents();
                Assert(Field<ScenarioProblem>(form, "_current") != null, "intermediate produces problems");
                level.SelectedIndex = 2;
                Application.DoEvents();
                Assert(Field<ScenarioProblem>(form, "_current") != null, "advanced produces problems");
                level.SelectedIndex = 0;
                category.SelectedIndex = 3; // 乘法
                Application.DoEvents();
                Assert(Field<ScenarioProblem>(form, "_current") != null, "multiplication category produces problems");
                Assert(!range.Enabled, "beginner multiplication category disables range selector");
                category.SelectedIndex = 4; // 除法
                Application.DoEvents();
                Assert(Field<ScenarioProblem>(form, "_current") != null, "division category produces problems");
                Assert(!range.Enabled, "beginner division category disables range selector");
                category.SelectedIndex = 2; // 减法
                Application.DoEvents();
                Assert(range.Enabled, "subtraction category re-enables range selector");
                level.SelectedIndex = 1; // 中级
                category.SelectedIndex = 3; // 乘法
                Application.DoEvents();
                Assert(Field<ScenarioProblem>(form, "_current") != null, "intermediate multiplication produces problems");
                Assert(range.Enabled, "intermediate multiplication links range selector");
                level.SelectedIndex = 0;
                category.SelectedIndex = 0;

                // F5 保留输入与光标。
                var current = Field<ScenarioProblem>(form, "_current");
                input.Text = "12";
                input.Select(1, 0);
                Assert(KeyMessage(input, Keys.F5), "F5 is consumed by scenario form");
                Assert(input.Text == "12" && input.SelectionStart == 1 && input.SelectionLength == 0,
                    "F5 preserves scenario answer and caret");
                SendMessage(input.Handle, 0x0102, new IntPtr('3'), IntPtr.Zero);
                Assert(input.Text == "132", "typing after scenario F5 uses preserved caret");

                replay.Focus();
                replay.PerformClick();
                Assert(input.Focused && input.SelectionStart == 2 && input.SelectionLength == 0,
                    "scenario replay button preserves caret");

                // 提交正确答案进入下一题；错误答案留在原题并全选。
                foreach (Keys key in new[] { Keys.Enter, Keys.Space })
                {
                    current = Field<ScenarioProblem>(form, "_current");
                    int before = Field<int>(form, "_questionNumber");
                    input.Text = current.Answer.ToString();
                    input.Select(input.TextLength, 0);
                    Assert(KeyMessage(input, key), key + " is consumed for scenario answer");
                    Assert(Field<int>(form, "_questionNumber") == before + 1 && input.Text == string.Empty,
                        key + " correct answer opens one new scenario problem");
                    Assert(Field<int>(form, "_correctCount") > 0, key + " increments correct count");

                    current = Field<ScenarioProblem>(form, "_current");
                    before = Field<int>(form, "_questionNumber");
                    input.Text = (current.Answer == 0 ? 1 : current.Answer + 1).ToString();
                    Assert(KeyMessage(input, key), key + " wrong scenario answer is consumed");
                    Assert(Field<int>(form, "_questionNumber") == before && Field<int>(form, "_wrongCount") > 0,
                        key + " wrong answer stays on same scenario problem");
                    Assert(input.SelectionLength == input.Text.Length, key + " selects wrong answer for retry");

                    before = Field<int>(form, "_questionNumber");
                    input.Text = string.Empty;
                    Assert(KeyMessage(input, key), key + " empty scenario answer is consumed");
                    Assert(Field<int>(form, "_questionNumber") == before && input.Text.Length == 0,
                        key + " empty answer does not advance or insert whitespace");
                }

                // Ctrl/Alt/Shift + 空格不提交。
                foreach (Keys modifiers in new[] { Keys.Control, Keys.Alt, Keys.Shift })
                {
                    int before = Field<int>(form, "_questionNumber");
                    var args = new KeyEventArgs(modifiers | Keys.Space);
                    Call(form, "AnswerBox_KeyDown", input, args);
                    Assert(!args.SuppressKeyPress && Field<int>(form, "_questionNumber") == before,
                        modifiers + "+Space does not submit scenario answer");
                }

                // Alt+数字 循环切换设置选项，无需鼠标。
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D1));
                Assert(category.SelectedIndex == 1, "Alt+1 cycles category to addition");
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D1));
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D1));
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D1));
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D1));
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D1));
                Assert(category.SelectedIndex == 0, "Alt+1 wraps category back to all");
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D2));
                Assert(level.SelectedIndex == 1, "Alt+2 cycles practice level");
                Call(form, "ScenarioForm_KeyDown", form, new KeyEventArgs(Keys.Alt | Keys.D3));
                Assert(range.SelectedIndex == 2, "Alt+3 cycles number range");
                level.SelectedIndex = 0;
                category.SelectedIndex = 0;
                range.SelectedIndex = 1;
                Application.DoEvents();

                // 换一题增加题号。
                int beforeNext = Field<int>(form, "_questionNumber");
                Call(form, "NewProblem", true);
                Application.DoEvents();
                Assert(Field<int>(form, "_questionNumber") == beforeNext + 1, "new problem advances question number");

                form.Close();
            }

            // 整合包应包含第三个工具按钮。
            using (var toolkit = new PracticeToolkit.ToolkitForm())
            {
                SetField(toolkit, "_speechOk", false);
                toolkit.Show();
                Application.DoEvents();
                var scenarioButton = Field<Button>(toolkit, "_scenarioButton");
                Assert(scenarioButton != null && scenarioButton.Tag != null &&
                    Convert.ToString(scenarioButton.Tag) == "ScenarioMathPractice.exe",
                    "toolkit launcher offers scenario practice button");
                toolkit.Close();
            }

            Console.WriteLine("SCENARIO KEYBOARD TESTS: PASS (" + _checks + " checks)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SCENARIO KEYBOARD TESTS: FAIL\n" + ex);
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
        var field = owner.GetType().GetField(name, PrivateInstance);
        if (field == null) throw new MissingFieldException(owner.GetType().Name, name);
        return (T)field.GetValue(owner);
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
