using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PinyinPractice
{
    // Regression tests run against the built application without SendKeys,
    // speech playback, or changing the user's word bank.
    internal static class KeyboardTests
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
                using (var form = new MainForm())
                {
                    SetField(form, "_speechOk", false);
                    SetField(form, "_randomOrder", false);
                    var words = Field<List<WordItem>>(form, "_words");
                    words.Clear();
                    words.Add(WordItem.Create("你好", "ni hao"));
                    words.Add(WordItem.Create("谢谢", "xie xie"));
                    form.Show();
                    Application.DoEvents();

                    var input = Field<TextBox>(form, "_inputBox");
                    var replay = Field<Button>(form, "_replayButton");
                    input.Focus();
                    input.Text = "nihao";
                    input.Select(2, 0);
                    Assert(KeyMessage(input, Keys.F5), "F5 is consumed by the form");
                    Assert(input.Text == "nihao" && input.SelectionStart == 2 && input.SelectionLength == 0,
                        "F5 preserves text and caret without selecting all");
                    SendMessage(input.Handle, 0x0102, new IntPtr('x'), IntPtr.Zero); // WM_CHAR
                    Assert(input.Text == "nixhao", "typing after F5 inserts at the previous caret");

                    input.Text = "nihao";
                    input.Select(1, 2);
                    KeyMessage(input, Keys.F5);
                    Assert(input.SelectionStart == 1 && input.SelectionLength == 2, "F5 preserves an existing selection");
                    input.Select(3, 0);
                    var ctrlR = new KeyEventArgs(Keys.Control | Keys.R);
                    Call(form, "MainForm_KeyDown", input, ctrlR);
                    Assert(ctrlR.SuppressKeyPress && input.SelectionStart == 3 && input.SelectionLength == 0,
                        "Ctrl+R also preserves the caret");
                    replay.Focus();
                    replay.PerformClick();
                    Assert(input.Focused && input.SelectionStart == 3 && input.SelectionLength == 0,
                        "replay button returns focus without selecting all");

                    foreach (Keys key in new[] { Keys.Enter, Keys.Space })
                    {
                        Reset(form, input, "nihao");
                        Assert(KeyMessage(input, key), key + " is consumed");
                        Assert(Field<int>(form, "_index") == 1 && input.Text == string.Empty,
                            key + " correct answer advances exactly once and clears input");
                        Assert(Field<TextBox>(form, "_statusLog").Text.Contains("正确"), key + " logs success");
                        Assert(input.Focused, key + " keeps practice focus");

                        Reset(form, input, "wrong");
                        Assert(KeyMessage(input, key), key + " wrong answer is consumed");
                        Assert(Field<int>(form, "_index") == 0 && Field<int>(form, "_attempts") == 1,
                            key + " wrong answer stays on current word");
                        Assert(input.Text == "wrong" && input.SelectionLength == input.Text.Length,
                            key + " retains existing retry selection behavior");
                        Assert(Field<TextBox>(form, "_statusLog").Text.Contains("错误"), key + " logs error");

                        Reset(form, input, string.Empty);
                        Assert(KeyMessage(input, key), key + " empty answer is consumed");
                        Assert(Field<int>(form, "_index") == 0 && Field<int>(form, "_attempts") == 0 && input.Text.Length == 0,
                            key + " empty answer neither advances nor inserts whitespace");
                    }

                    foreach (Keys modifiers in new[] { Keys.Control, Keys.Alt, Keys.Shift })
                    {
                        Reset(form, input, "nihao");
                        var args = new KeyEventArgs(modifiers | Keys.Space);
                        Call(form, "InputBox_KeyDown", input, args);
                        Assert(!args.SuppressKeyPress && Field<int>(form, "_index") == 0,
                            modifiers + "+Space is not a confirmation shortcut");
                    }

                    Reset(form, input, "ni hao");
                    KeyMessage(input, Keys.Space);
                    Assert(Field<int>(form, "_index") == 1, "pasted spaced pinyin still matches");
                    Reset(form, input, "nihao");
                    var random = Field<CheckBox>(form, "_randomCheck");
                    KeyMessage(random, Keys.Space);
                    Assert(Field<int>(form, "_index") == 0, "Space on a different control does not submit");
                    form.Close();
                }

                using (var import = new ImportForm(new List<WordItem>()))
                {
                    import.Show();
                    Application.DoEvents();
                    var text = Field<TextBox>(import, "_text");
                    text.Text = "你好|ni";
                    text.Select(text.TextLength, 0);
                    Assert(!KeyMessage(text, Keys.Space), "import text Space is not intercepted");
                    SendMessage(text.Handle, 0x0102, new IntPtr(' '), IntPtr.Zero);
                    Assert(text.Text == "你好|ni ", "import text still accepts spaces");
                    SendMessage(text.Handle, 0x0102, new IntPtr('\r'), IntPtr.Zero);
                    Assert(text.Text.Contains("\n") && import.DialogResult != DialogResult.OK,
                        "import text still accepts newline without importing");
                    import.Close();
                }
                Console.WriteLine("KEYBOARD TESTS: PASS (" + _checks + " checks)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("KEYBOARD TESTS: FAIL\n" + ex);
                return 1;
            }
        }

        private static void Reset(MainForm form, TextBox input, string text)
        {
            SetField(form, "_index", 0);
            Call(form, "ShowCurrentWord");
            Field<TextBox>(form, "_statusLog").Clear();
            input.Text = text;
            input.Select(input.TextLength, 0);
            input.Focus();
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
}
