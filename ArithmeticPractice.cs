using System;
using System.Collections.Generic;
using System.Drawing;
using System.Speech.Synthesis;
using System.Windows.Forms;

namespace ArithmeticPractice
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(ArithmeticSelfTest.Run());
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ArithmeticForm());
        }
    }

    internal static class ArithmeticSelfTest
    {
        public static int Run()
        {
            int failures = 0;
            var engine = new ArithmeticEngine(new Random(12345));
            var operations = new[] { OperationKind.Mixed, OperationKind.Addition, OperationKind.Subtraction, OperationKind.Multiplication, OperationKind.Division };
            var levels = new[] { PracticeLevel.Beginner, PracticeLevel.Intermediate, PracticeLevel.Advanced };
            var ranges = new[] { 10, 20, 100 };

            foreach (var operation in operations)
            foreach (var level in levels)
            foreach (int range in ranges)
            foreach (int count in new[] { 3, 4, 5 })
            {
                bool sawLeftUnknown = false;
                bool sawRightUnknown = false;
                bool sawResultUnknown = false;
                for (int i = 0; i < 400; i++)
                {
                    var problem = engine.Next(operation, range, level, count);
                    string why;
                    if (!Validate(problem, operation, level, range, count, out why))
                    {
                        Console.WriteLine("FAIL generation: " + problem.DisplayText + " answer=" + problem.Answer + " mode=" + operation + " level=" + level + " range=" + range + " count=" + count + " reason=" + why);
                        failures++;
                        break;
                    }
                    if (problem.MissingIndex == 0) sawLeftUnknown = true;
                    else if (problem.MissingIndex == 1) sawRightUnknown = true;
                    else if (problem.MissingIndex < 0) sawResultUnknown = true;
                }
                if (level == PracticeLevel.Intermediate && !(sawLeftUnknown && sawRightUnknown && sawResultUnknown))
                {
                    Console.WriteLine("FAIL intermediate coverage: " + operation + " range=" + range);
                    failures++;
                }
            }

            failures += ParseCheck("12", true, 12);
            failures += ParseCheck("-3", true, -3);
            failures += ParseCheck("−3", true, -3);
            failures += ParseCheck(" 8 ", true, 8);
            failures += ParseCheck("", false, 0);
            failures += ParseCheck("1.5", false, 0);
            failures += ParseCheck("abc", false, 0);
            failures += ExceptionCheck(delegate { engine.Next(OperationKind.Addition, 20, PracticeLevel.Advanced, 2); });
            failures += ExceptionCheck(delegate { engine.Next(OperationKind.Addition, 20, PracticeLevel.Advanced, 6); });

            Console.WriteLine(failures == 0 ? "ARITHMETIC SELFTEST: PASS" : "ARITHMETIC SELFTEST: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        private static bool Validate(ArithmeticProblem problem, OperationKind requested, PracticeLevel level, int range, int requestedCount, out string why)
        {
            why = string.Empty;
            if (problem == null) { why = "null"; return false; }
            int expectedCount = level == PracticeLevel.Advanced ? requestedCount : 2;
            if (problem.Numbers.Length != expectedCount || problem.Operators.Length != expectedCount - 1) { why = "count"; return false; }
            if (level == PracticeLevel.Beginner || level == PracticeLevel.Advanced)
            {
                if (problem.MissingIndex != -1) { why = "wrong missing position"; return false; }
            }
            else if (problem.MissingIndex < -1 || problem.MissingIndex >= 2) { why = "intermediate missing position"; return false; }

            int calculated = problem.Numbers[0];
            bool hasPlus = false, hasMinus = false;
            for (int i = 0; i < problem.Operators.Length; i++)
            {
                var op = problem.Operators[i];
                if (requested == OperationKind.Mixed)
                {
                    if (op != OperationKind.Addition && op != OperationKind.Subtraction) { why = "mixed contains non +/-"; return false; }
                }
                else if (op != requested) { why = "wrong operator"; return false; }

                int number = problem.Numbers[i + 1];
                if (op == OperationKind.Addition) { hasPlus = true; calculated += number; }
                else if (op == OperationKind.Subtraction) { hasMinus = true; calculated -= number; }
                else if (op == OperationKind.Multiplication) calculated *= number;
                else
                {
                    if (number == 0 || calculated % number != 0) { why = "non-exact division"; return false; }
                    calculated /= number;
                }
                if (calculated < 0 || calculated > range) { why = "intermediate outside range"; return false; }
            }
            if (calculated != problem.Result) { why = "result"; return false; }
            if (problem.Answer < 0 || problem.Answer > range) { why = "answer outside range"; return false; }
            if (requested == OperationKind.Mixed && level == PracticeLevel.Advanced && (!hasPlus || !hasMinus)) { why = "advanced mixed must contain + and -"; return false; }
            if (problem.DisplayText.IndexOf('?') < 0 || problem.SpeechText.IndexOf("未知数", StringComparison.Ordinal) < 0) { why = "question rendering"; return false; }
            return true;
        }

        private static int ParseCheck(string input, bool expectedResult, int expectedAnswer)
        {
            int answer;
            bool result = ArithmeticEngine.TryParseAnswer(input, out answer);
            bool ok = result == expectedResult && (!result || answer == expectedAnswer);
            if (!ok) Console.WriteLine("FAIL parse: '" + input + "' result=" + result + " answer=" + answer);
            return ok ? 0 : 1;
        }

        private static int ExceptionCheck(Action action)
        {
            try { action(); Console.WriteLine("FAIL expected argument exception"); return 1; }
            catch (ArgumentOutOfRangeException) { return 0; }
        }
    }

    public sealed class ArithmeticForm : Form
    {
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();
        private readonly ArithmeticEngine _engine = new ArithmeticEngine(new Random());
        private bool _speechOk;
        private ArithmeticProblem _current;
        private int _questionNumber;
        private int _correctCount;
        private int _wrongCount;

        private ComboBox _operationBox;
        private ComboBox _levelBox;
        private ComboBox _operandCountBox;
        private ComboBox _rangeBox;
        private Label _operandCountLabel;
        private Label _questionLabel;
        private Label _progressLabel;
        private Label _statusLabel;
        private TextBox _answerBox;
        private TextBox _historyBox;
        private Button _replayButton;
        private Button _submitButton;
        private Button _nextButton;

        public ArithmeticForm()
        {
            Text = "数学加减乘除算术练习";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 570);
            Size = new Size(920, 680);
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            AccessibleName = "数学加减乘除算术练习";
            AccessibleDescription = "选择运算、级别和难度，听题后输入整数答案，按回车或空格确认。";
            BuildUi();
            InitSpeech();
            KeyDown += ArithmeticForm_KeyDown;
            Shown += delegate { NewProblem(true); try { _answerBox.Focus(); } catch { } };
            FormClosed += delegate { try { _synth.SpeakAsyncCancelAll(); } catch { } try { _synth.Dispose(); } catch { } };
        }

        private void BuildUi()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var title = new Label { AutoSize = true, Text = "数学加减乘除算术练习", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AccessibleName = "标题：数学加减乘除算术练习", Margin = new Padding(0, 0, 0, 10) };
            var settings = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            settings.Controls.Add(SettingsLabel("运算类型：", 0));
            _operationBox = SettingsBox("运算类型", "选择复合运算、加法、减法、乘法或除法。", 130, new[] { "复合运算", "加法", "减法", "乘法", "除法" }, 0);
            settings.Controls.Add(_operationBox);
            settings.Controls.Add(SettingsLabel("级别：", 14));
            _levelBox = SettingsBox("练习级别", "初级是两个数求结果；中级的未知数可在等式任意位置；高级可选择三个、四个或五个数。", 110, new[] { "初级", "中级", "高级" }, 0);
            settings.Controls.Add(_levelBox);
            _operandCountLabel = SettingsLabel("数量：", 14);
            settings.Controls.Add(_operandCountLabel);
            _operandCountBox = SettingsBox("高级数字数量", "高级练习使用的数字数量，可选择三、四或五，默认三个。", 90, new[] { "3个数", "4个数", "5个数" }, 0);
            settings.Controls.Add(_operandCountBox);
            settings.Controls.Add(SettingsLabel("范围：", 14));
            _rangeBox = SettingsBox("数字范围", "选择十以内、二十以内或一百以内，题目计算过程不会超过所选范围。", 110, new[] { "10以内", "20以内", "100以内" }, 1);
            settings.Controls.Add(_rangeBox);

            _progressLabel = new Label { AutoSize = true, Text = "准备开始", AccessibleName = "练习进度", Margin = new Padding(0, 2, 0, 8) };
            _questionLabel = new Label { AutoSize = true, Text = "（等待出题）", Font = new Font(Font.FontFamily, 24F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 90, 170), AccessibleName = "当前算术题", AccessibleDescription = "问号所在位置是需要回答的未知数。", Margin = new Padding(0, 0, 0, 10) };
            var answerPrompt = new Label { AutoSize = true, Text = "输入问号代表的整数，然后按 Enter 或空格确认：", AccessibleName = "答案输入提示：输入问号代表的整数。", Margin = new Padding(0, 4, 0, 4) };
            _answerBox = new TextBox { Dock = DockStyle.Top, Font = new Font("Microsoft YaHei UI", 16F), ImeMode = ImeMode.Off, AccessibleName = "算术答案输入框", AccessibleDescription = "输入问号代表的整数，按回车或空格确认。按 F5 重听，输入和光标不会改变。", Margin = new Padding(0, 0, 0, 8) };
            _answerBox.KeyDown += AnswerBox_KeyDown;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            _replayButton = new Button { Text = "重听题目 (F5)", AutoSize = true, AccessibleName = "重听题目按钮" };
            _replayButton.Click += delegate { ReplayProblem(); };
            _submitButton = new Button { Text = "确认 (Enter / 空格)", AutoSize = true, AccessibleName = "确认答案按钮" };
            _submitButton.Click += delegate { SubmitAnswer(); };
            _nextButton = new Button { Text = "换一题 (Ctrl+N)", AutoSize = true, AccessibleName = "换一题按钮" };
            _nextButton.Click += delegate { NewProblem(true); };
            buttons.Controls.Add(_replayButton); buttons.Controls.Add(_submitButton); buttons.Controls.Add(_nextButton);

            _statusLabel = new Label { AutoSize = true, Text = "状态：准备开始。", AccessibleName = "状态：准备开始。", Margin = new Padding(0, 2, 0, 8) };
            var help = new Label { AutoSize = true, Text = "初级：2个数求结果；中级：问号可在等式任意位置；高级：3至5个数。复合运算高级同时包含加法和减法。", AccessibleName = "级别说明。初级两个数求结果；中级问号可在任意位置；高级三至五个数；复合高级同时有加法和减法。", Margin = new Padding(0, 0, 0, 8) };
            _historyBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, AccessibleName = "答题记录", AccessibleDescription = "最近的题目、答案和对错记录。", TabStop = true };

            layout.Controls.Add(title, 0, 0); layout.Controls.Add(settings, 0, 1); layout.Controls.Add(_progressLabel, 0, 2); layout.Controls.Add(_questionLabel, 0, 3); layout.Controls.Add(answerPrompt, 0, 4); layout.Controls.Add(_answerBox, 0, 5); layout.Controls.Add(buttons, 0, 6); layout.Controls.Add(_statusLabel, 0, 7); layout.Controls.Add(_historyBox, 0, 8);
            Controls.Add(layout);

            _operationBox.SelectedIndexChanged += SettingsChanged;
            _levelBox.SelectedIndexChanged += LevelChanged;
            _operandCountBox.SelectedIndexChanged += SettingsChanged;
            _rangeBox.SelectedIndexChanged += SettingsChanged;
            UpdateOperandCountState();
        }

        private static Label SettingsLabel(string text, int left) { return new Label { Text = text, AutoSize = true, Margin = new Padding(left, 7, 2, 0) }; }
        private static ComboBox SettingsBox(string name, string description, int width, string[] items, int selected)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, AccessibleName = name, AccessibleDescription = description };
            box.Items.AddRange(items); box.SelectedIndex = selected; return box;
        }

        private void InitSpeech()
        {
            try
            {
                _synth.SetOutputToDefaultAudioDevice(); _synth.Rate = 0; _synth.Volume = 100;
                foreach (var voice in _synth.GetInstalledVoices())
                {
                    if (voice == null || voice.VoiceInfo == null || !voice.Enabled) continue;
                    _speechOk = true;
                    var culture = voice.VoiceInfo.Culture == null ? string.Empty : voice.VoiceInfo.Culture.Name;
                    if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) { try { _synth.SelectVoice(voice.VoiceInfo.Name); } catch { } break; }
                }
            }
            catch { _speechOk = false; }
        }

        private OperationKind SelectedOperation { get { int i = _operationBox == null ? 0 : _operationBox.SelectedIndex; return i < 0 || i > 4 ? OperationKind.Mixed : (OperationKind)i; } }
        private PracticeLevel SelectedLevel { get { int i = _levelBox == null ? 0 : _levelBox.SelectedIndex; return i < 0 || i > 2 ? PracticeLevel.Beginner : (PracticeLevel)i; } }
        private int SelectedOperandCount { get { return _operandCountBox == null || _operandCountBox.SelectedIndex < 0 ? 3 : _operandCountBox.SelectedIndex + 3; } }
        private int SelectedRange { get { if (_rangeBox == null) return 20; return _rangeBox.SelectedIndex == 0 ? 10 : (_rangeBox.SelectedIndex == 2 ? 100 : 20); } }

        private void LevelChanged(object sender, EventArgs e) { UpdateOperandCountState(); SettingsChanged(sender, e); }
        private void UpdateOperandCountState()
        {
            bool advanced = SelectedLevel == PracticeLevel.Advanced;
            _operandCountBox.Enabled = advanced;
            _operandCountLabel.Enabled = advanced;
            _operandCountBox.AccessibleDescription = advanced ? "高级练习使用的数字数量，可选择三、四或五。" : "只有高级练习可以设置数字数量。当前不可用。";
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || _questionLabel == null) return;
            _questionNumber = 0; _correctCount = 0; _wrongCount = 0; _historyBox.Clear(); NewProblem(true);
        }

        private void NewProblem(bool speak)
        {
            _current = _engine.Next(SelectedOperation, SelectedRange, SelectedLevel, SelectedOperandCount);
            _questionNumber++; _answerBox.Text = string.Empty;
            _questionLabel.Text = _current.DisplayText;
            _questionLabel.AccessibleName = "当前算术题：" + _current.SpeechText;
            UpdateProgress(); UpdateStatus("状态：请输入问号代表的整数，按 Enter 或空格确认。", false);
            if (speak) Say("请听题。" + _current.SpeechText, true);
            try { _answerBox.Focus(); } catch { }
        }

        private void UpdateProgress()
        {
            _progressLabel.Text = LevelText() + "；第 " + _questionNumber + " 题；答对 " + _correctCount + " 题；答错 " + _wrongCount + " 次";
            _progressLabel.AccessibleName = _progressLabel.Text;
        }

        private string LevelText()
        {
            string text = _levelBox == null || _levelBox.SelectedItem == null ? "初级" : _levelBox.SelectedItem.ToString();
            if (SelectedLevel == PracticeLevel.Advanced) text += "，" + SelectedOperandCount + "个数";
            return text;
        }

        private void ReplayProblem()
        {
            if (_current == null) return;
            int start = _answerBox.SelectionStart, length = _answerBox.SelectionLength;
            Say("请听题。" + _current.SpeechText, true); UpdateStatus("状态：重新播报当前算术题。", false);
            _answerBox.Focus(); _answerBox.Select(start, length);
        }

        private void SubmitAnswer()
        {
            if (_current == null) return;
            int answer;
            if (!ArithmeticEngine.TryParseAnswer(_answerBox.Text, out answer))
            {
                Say("请输入整数答案。", true); UpdateStatus("状态：请输入问号代表的整数，再按 Enter 或空格确认。", true);
                try { _answerBox.Focus(); _answerBox.SelectAll(); } catch { } return;
            }
            string record = _current.DisplayText + "，回答 " + answer;
            if (answer == _current.Answer)
            {
                _correctCount++; UpdateProgress(); AppendHistory(record + "，正确"); UpdateStatus("状态：正确。", false); Say("正确。", true);
                NewProblem(false); Say("下一题。" + _current.SpeechText, false); return;
            }
            _wrongCount++; UpdateProgress(); AppendHistory(record + "，错误"); UpdateStatus("状态：错误，请重新计算。按 F5 可以重听题目。", false); Say("错误，请重新计算。", true);
            try { _answerBox.Focus(); _answerBox.SelectAll(); } catch { }
        }

        private void AppendHistory(string text) { _historyBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + "\r\n"); }
        private void UpdateStatus(string text, bool log) { _statusLabel.Text = text; _statusLabel.AccessibleName = text; if (log) AppendHistory(text.Replace("状态：", string.Empty)); }
        private void Say(string text, bool flush) { if (!_speechOk || string.IsNullOrEmpty(text)) return; try { if (flush) _synth.SpeakAsyncCancelAll(); _synth.SpeakAsync(text); } catch { } }

        private void AnswerBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Space && e.Modifiers == Keys.None)) { e.SuppressKeyPress = true; SubmitAnswer(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; _answerBox.Text = string.Empty; }
        }

        private void ArithmeticForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R)) { e.SuppressKeyPress = true; ReplayProblem(); }
            else if (e.Control && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; NewProblem(true); }
        }
    }
}
