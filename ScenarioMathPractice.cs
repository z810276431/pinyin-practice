using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Speech.Synthesis;
using System.Text;
using System.Windows.Forms;

namespace ScenarioMathPractice
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(ScenarioSelfTest.Run());
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ScenarioForm());
        }
    }

    internal static class DefaultBank
    {
        // 内置默认题库：情景文本|答案算式|类别。数字占位符 {1}{2}{3}{4}。
        public const string Text =
            "小明有{1}件颜色不同的上衣和{2}条颜色不同的裤子，他一共有几种不同的穿法？|{1}*{2}|乘\r\n" +
            "每盒铅笔有{1}支，妈妈买了{2}盒，一共有多少支铅笔？|{1}*{2}|乘\r\n" +
            "树上有{1}只小鸟，又飞来了{2}只，现在树上有多少只小鸟？|{1}+{2}|加\r\n" +
            "妈妈买了{1}个苹果，一家人吃了{2}个，还剩多少个苹果？|{1}-{2}|减\r\n" +
            "把{1}颗糖平均分给{2}个小朋友，每人分到几颗糖？|{1}/{2}|除\r\n" +
            "妈妈买了{1}个苹果，吃了{2}个，又买了{3}个梨，现在一共有多少个水果？|{1}-{2}+{3}|混合\r\n" +
            "每盒有{1}支钢笔，每箱有{2}盒，学校买了{3}箱，一共有多少支钢笔？|{1}*{2}*{3}|乘\r\n" +
            "一支圆珠笔{1}元，小明买了{2}支，付给售货员{3}元，应找回多少元？|{3}-{1}*{2}|混合\r\n" +
            "{1}名同学去划船，先走了{2}名，剩下的同学每{3}人坐一条船，一共需要几条船？|({1}-{2})/{3}|除\r\n" +
            "水果店运来{1}箱苹果，每箱{2}千克，卖出{3}千克后又运进{4}千克，现在有多少千克苹果？|{1}*{2}-{3}+{4}|混合\r\n" +
            "小明有{1}元钱，买了{2}支每支{3}元的铅笔，又买了一本{4}元的笔记本，还剩多少元？|{1}-{2}*{3}-{4}|混合\r\n" +
            "一栋楼每层住{1}户，共有{2}层；旁边一栋楼每层住{3}户，共有{4}层，两栋楼一共住多少户？|{1}*{2}+{3}*{4}|乘\r\n" +
            "老师买了{1}盒粉笔，每盒{2}支，用去{3}支后，剩下的平均分给{4}个年级，每个年级分到几支？|({1}*{2}-{3})/{4}|除\r\n";

        public const string TemplateHeader =
            "# 情景数学应用题练习 · 题库模板（UTF-8 编码）\r\n" +
            "# 每行一条题目，格式：情景文本|答案算式|类别\r\n" +
            "#   情景文本：{1}{2}{3}{4} 是数字占位符，题目本身以问句结尾，语音会朗读整段文字。\r\n" +
            "#   答案算式：由占位符和 + - * / 以及括号组成，从左到右计算，先乘除后加减。\r\n" +
            "#   类别：加、减、乘、除或混合；留空按混合处理。\r\n" +
            "# 难度由算式中的运算符个数自动决定：1 个=初级，2 个=中级，3 个=高级。\r\n" +
            "# 乘除类题目（类别为乘或除）：初级固定在 9×9 乘法表以内；中级和高级与范围联动，乘法因数、除法被除数不超过所选范围。\r\n" +
            "# 出题时加减和混合题的所有结果都会保持在所选范围内，除法保证整除，减法不出现负数。\r\n" +
            "# 以 # 或 // 开头的行为注释，空行忽略；行内 # 之后的内容也算注释。\r\n" +
            "# 修改后可以在练习窗口按 Ctrl+I 导入，选择替换或追加。\r\n\r\n";
    }

    internal static class ScenarioSelfTest
    {
        public static int Run()
        {
            int failures = 0;

            // ---------- 解析 ----------
            var errors = new List<string>();
            var warnings = new List<string>();
            var items = ScenarioBank.Parse(DefaultBank.Text, errors, warnings);
            failures += Expect(items.Count == 13, "default bank parses 13 items, got " + items.Count);
            failures += Expect(warnings.Count == 0 && errors.Count == 0, "default bank parses without warnings");

            var bad = new StringBuilder();
            bad.Append("没有占位符的题目|{1}+{2}\r\n");          // 文本缺少占位符
            bad.Append("# 注释行\r\n");
            bad.Append("\r\n");
            bad.Append("缺少算式|\r\n");                          // 算式为空
            bad.Append("只有一列\r\n");                           // 缺少分隔
            bad.Append("购买情景|{1}+{9}|加\r\n");                // 占位符超界
            bad.Append("分糖情景|{1}/{0}|除\r\n");                // 占位符序号非法
            bad.Append("苹果情景|{1}+|加\r\n");                   // 算式悬空
            bad.Append("括号情景|({1}+{2}|加\r\n");               // 括号不闭合
            bad.Append("太多占位|{5}+{6}|加\r\n");                // 超过 4
            bad.Append("苹果{1}个和梨{2}个，一共几个|{1}+{1}|加\r\n"); // 文本占位符 {2} 未在算式中使用
            bad.Append("买笔情景，每支笔{1}元，买了{2}支，付了{3}元|{3}-{1}*{2}|混合\r\n"); // 合法行
            var badWarnings = new List<string>();
            var badItems = ScenarioBank.Parse(bad.ToString(), new List<string>(), badWarnings);
            failures += Expect(badItems.Count == 1 && badItems[0].Expression == "{3}-{1}*{2}", "invalid lines skipped, valid kept");
            failures += Expect(badWarnings.Count == 9, "one warning per invalid line, got " + badWarnings.Count);

            failures += Expect(ScenarioItem.ParseCategory("乘") == ScenarioCategory.Multiplication, "category 乘");
            failures += Expect(ScenarioItem.ParseCategory("除法") == ScenarioCategory.Division, "category 除法");
            failures += Expect(ScenarioItem.ParseCategory("") == ScenarioCategory.Mixed, "empty category means mixed");
            failures += Expect(ScenarioItem.ParseCategory("运算") == ScenarioCategories.Invalid, "unknown category rejected");

            // ---------- 求值 ----------
            failures += EvalCheck("{1}*{2}", new[] { 2, 3 }, 100, true, 6);
            failures += EvalCheck("{1}-{2}+{3}", new[] { 5, 2, 4 }, 100, true, 7);
            failures += EvalCheck("{3}-{1}*{2}", new[] { 2, 3, 10 }, 100, true, 4);
            failures += EvalCheck("({1}-{2})/{3}", new[] { 9, 3, 2 }, 100, true, 3);
            failures += EvalCheck("{1}/{2}", new[] { 7, 2 }, 100, false, 0);
            failures += EvalCheck("{1}/{2}", new[] { 8, 0 }, 100, false, 0);
            failures += EvalCheck("{1}-{2}", new[] { 2, 5 }, 100, false, 0);
            failures += EvalCheck("{1}*{2}", new[] { 9, 9 }, 20, false, 0);
            failures += EvalCheck("{1}+{2}", new[] { 5, 6 }, 10, false, 0);
            failures += EvalCheck("({1}+{2})*{3}", new[] { 2, 3, 4 }, 100, true, 20);

            // ---------- 出题约束 ----------
            var engine = new ScenarioEngine(new Random(20260401));
            engine.Load(items);
            var categories = new[] { ScenarioCategory.All, ScenarioCategory.Addition, ScenarioCategory.Subtraction, ScenarioCategory.Multiplication, ScenarioCategory.Division, ScenarioCategory.Mixed };
            var levels = new[] { ScenarioLevel.Beginner, ScenarioLevel.Intermediate, ScenarioLevel.Advanced };
            var ranges = new[] { 10, 20, 100 };
            foreach (var category in categories)
            foreach (var level in levels)
            foreach (int range in ranges)
            {
                for (int i = 0; i < 300; i++)
                {
                    var problem = engine.Next(category, level, range);
                    if (problem == null)
                    {
                        // 某些类别+级别+范围的组合可能没有对应模板，允许为空但必须在全部题库时非空。
                        if (category == ScenarioCategory.All) { failures += Expect(false, "all-category " + level + " range=" + range + " must produce problems"); break; }
                        continue;
                    }
                    string why;
                    if (!ValidateProblem(problem, category, level, range, out why))
                    {
                        Console.WriteLine("FAIL generation: " + problem.DisplayText + " answer=" + problem.Answer + " category=" + category + " level=" + level + " range=" + range + " reason=" + why);
                        failures++;
                        break;
                    }
                }
            }

            // ---------- 题库保存往返 ----------
            string tempPath = Path.Combine(Path.GetTempPath(), "scenario-bank-roundtrip-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                ScenarioBank.Save(tempPath, items);
                var reloaded = ScenarioBank.Parse(ScenarioBank.ReadTextFile(tempPath), new List<string>(), new List<string>());
                failures += Expect(reloaded.Count == items.Count, "save/load roundtrip keeps item count");
                bool same = reloaded.Count == items.Count;
                if (same)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (reloaded[i].Text != items[i].Text || reloaded[i].Expression != items[i].Expression || reloaded[i].Category != items[i].Category) { same = false; break; }
                    }
                }
                failures += Expect(same, "save/load roundtrip keeps content");
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }

            // ---------- 答案解析 ----------
            failures += ParseCheck("12", true, 12);
            failures += ParseCheck(" 8 ", true, 8);
            failures += ParseCheck("－5", true, -5);
            failures += ParseCheck("", false, 0);
            failures += ParseCheck("1.5", false, 0);
            failures += ParseCheck("abc", false, 0);

            // ---------- 参数检查 ----------
            failures += ExceptionCheck(delegate { engine.Next(ScenarioCategory.All, ScenarioLevel.Beginner, 0); });
            failures += ExceptionCheck(delegate { engine.Next(ScenarioCategory.All, ScenarioLevel.Beginner, 1000001); });

            Console.WriteLine(failures == 0 ? "SCENARIO SELFTEST: PASS" : "SCENARIO SELFTEST: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        private static bool ValidateProblem(ScenarioProblem problem, ScenarioCategory filter, ScenarioLevel level, int range, out string why)
        {
            why = string.Empty;
            if (problem == null) { why = "null"; return false; }
            if (problem.DisplayText.IndexOf('{') >= 0) { why = "placeholder not filled"; return false; }
            if (problem.DisplayText.Length != problem.SpeechText.Length) { why = "speech differs from display"; return false; }
            // 乘除类题目：初级固定 9×9 乘法表（81 以内）；中级和高级与范围联动，
            // 乘法积不超过范围数的平方，除法被除数不超过所选范围。
            int bound = range;
            if (problem.Category == ScenarioCategory.Multiplication)
                bound = level == ScenarioLevel.Beginner ? 81 : range * range;
            else if (problem.Category == ScenarioCategory.Division)
                bound = level == ScenarioLevel.Beginner ? 81 : range;
            if (problem.Answer < 0 || problem.Answer > bound) { why = "answer outside range"; return false; }
            if (problem.Category == ScenarioCategory.Division && level == ScenarioLevel.Beginner && problem.Answer > 9)
            { why = "beginner division quotient outside 9x9 table"; return false; }
            if (problem.DisplayText.IndexOf('？') < 0 && problem.DisplayText.IndexOf('?') < 0)
            { why = "missing question mark"; return false; }
            return true;
        }

        private static int EvalCheck(string expression, int[] numbers, int max, bool expectedResult, int expectedValue)
        {
            int value;
            bool result = ScenarioExpression.TryEvaluate(expression, numbers, max, out value);
            bool ok = result == expectedResult && (!result || value == expectedValue);
            if (!ok) Console.WriteLine("FAIL eval: '" + expression + "' [" + string.Join(",", numbers) + "] max=" + max + " result=" + result + " value=" + value);
            return ok ? 0 : 1;
        }

        private static int ParseCheck(string input, bool expectedResult, int expectedAnswer)
        {
            int answer;
            bool result = ScenarioEngine.TryParseAnswer(input, out answer);
            bool ok = result == expectedResult && (!result || answer == expectedAnswer);
            if (!ok) Console.WriteLine("FAIL parse: '" + input + "' result=" + result + " answer=" + answer);
            return ok ? 0 : 1;
        }

        private static int ExceptionCheck(Action action)
        {
            try { action(); Console.WriteLine("FAIL expected argument exception"); return 1; }
            catch (ArgumentOutOfRangeException) { return 0; }
        }

        private static int Expect(bool condition, string name)
        {
            if (!condition) { Console.WriteLine("FAIL: " + name); return 1; }
            return 0;
        }
    }

    /// <summary>简化的题库导入对话框：粘贴文字或从文件导入，替换或追加，Ctrl+Enter 确认。</summary>
    public sealed class ImportForm : Form
    {
        private readonly TextBox _text;
        private readonly Label _preview;
        private readonly RadioButton _replace;
        private readonly RadioButton _append;
        private readonly Button _ok;
        private readonly Button _file;

        public List<ScenarioItem> Imported { get; private set; }
        public List<string> Warnings { get; private set; }
        public bool AppendMode { get { return _append.Checked; } }

        public ImportForm()
        {
            Text = "导入情景题库";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 520);
            Size = new Size(760, 600);
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            KeyPreview = true;
            AccessibleName = "导入情景题库";
            AccessibleDescription = "粘贴题库文字或从文件导入，选择替换或追加，按 Ctrl+Enter 开始导入。";

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                AutoSize = true,
                Text = "每行一条题目，格式：情景文本|答案算式|类别。粘贴文字或从文件导入。",
                AccessibleName = "导入说明。每行一条题目。",
                Margin = new Padding(0, 0, 0, 6)
            };
            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                WordWrap = false,
                Font = new Font("Microsoft YaHei UI", 11F),
                AccessibleName = "题库文字输入区",
                AccessibleDescription = "粘贴或输入题库文字，Enter 换行，Ctrl+Enter 开始导入。"
            };
            _text.TextChanged += delegate { UpdatePreview(); };

            _replace = new RadioButton { Text = "替换现有题库", AutoSize = true, Checked = true, AccessibleName = "替换现有题库" };
            _append = new RadioButton { Text = "追加到现有题库", AutoSize = true, AccessibleName = "追加到现有题库" };
            var modes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0) };
            modes.Controls.Add(_replace);
            modes.Controls.Add(_append);

            _preview = new Label { AutoSize = true, Text = "预览：尚未输入内容。", AccessibleName = "导入预览", Margin = new Padding(0, 6, 0, 0) };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 0) };
            _file = new Button { Text = "从文件导入…", AutoSize = true, AccessibleName = "从文件导入按钮" };
            _file.Click += delegate { LoadFromFile(); };
            _ok = new Button { Text = "开始导入 (Ctrl+Enter)", AutoSize = true, AccessibleName = "开始导入按钮" };
            _ok.Click += delegate { DoImport(); };
            var cancel = new Button { Text = "取消 (Esc)", AutoSize = true, AccessibleName = "取消导入按钮", DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(_file);
            buttons.Controls.Add(_ok);
            buttons.Controls.Add(cancel);

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(_text, 0, 1);
            layout.Controls.Add(modes, 0, 2);
            layout.Controls.Add(_preview, 0, 3);
            layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout);
            CancelButton = cancel;
            KeyDown += ImportForm_KeyDown;
            Shown += delegate { try { _text.Focus(); } catch { } };
        }

        private void ImportForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                DoImport();
            }
        }

        private void LoadFromFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*", Title = "选择题库文件" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _text.Text = ScenarioBank.ReadTextFile(dialog.FileName);
                    Summary("已载入文件：" + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "读取文件失败：" + ex.Message, "导入题库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void UpdatePreview()
        {
            var warnings = new List<string>();
            int count = ScenarioBank.Parse(_text.Text, new List<string>(), warnings).Count;
            _preview.Text = "预览：可导入 " + count + " 条题目，" + warnings.Count + " 条提示。";
            _preview.AccessibleName = _preview.Text;
        }

        private void Summary(string text)
        {
            _preview.Text = text;
            _preview.AccessibleName = text;
        }

        private void DoImport()
        {
            var warnings = new List<string>();
            var items = ScenarioBank.Parse(_text.Text, new List<string>(), warnings);
            if (items.Count == 0)
            {
                MessageBox.Show(this, "没有可导入的题目。请检查格式：情景文本|答案算式|类别。", "导入题库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Imported = items;
            Warnings = warnings;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class ScenarioForm : Form
    {
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();
        private readonly ScenarioEngine _engine = new ScenarioEngine(new Random());
        private readonly List<ScenarioItem> _items = new List<ScenarioItem>();
        private bool _speechOk;
        private ScenarioProblem _current;
        private int _questionNumber;
        private int _correctCount;
        private int _wrongCount;

        private ComboBox _categoryBox;
        private ComboBox _levelBox;
        private ComboBox _rangeBox;
        private Label _rangeLabel;
        private Label _questionLabel;
        private Label _progressLabel;
        private Label _statusLabel;
        private TextBox _answerBox;
        private TextBox _historyBox;
        private Button _replayButton;
        private Button _submitButton;
        private Button _nextButton;

        public ScenarioForm()
        {
            Text = "情景数学应用题练习";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 640);
            Size = new Size(960, 720);
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            AccessibleName = "情景数学应用题练习";
            AccessibleDescription = "听读情景应用题，从题目里找出数字关系，输入整数答案，按回车或空格确认。设置选项可用 Alt+1 到 Alt+3 键盘切换。";
            BuildUi();
            InitSpeech();
            LoadBank(useStatus: false);
            KeyDown += ScenarioForm_KeyDown;
            Shown += delegate { NewProblem(true); try { _answerBox.Focus(); } catch { } };
            FormClosed += delegate { try { _synth.SpeakAsyncCancelAll(); } catch { } try { _synth.Dispose(); } catch { } };
        }

        private void BuildUi()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var title = new Label { AutoSize = true, Text = "情景数学应用题练习", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AccessibleName = "标题：情景数学应用题练习", Margin = new Padding(0, 0, 0, 10) };
            var settings = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            settings.Controls.Add(SettingsLabel("类别：", 0));
            _categoryBox = SettingsBox("题目类别", "筛选加、减、乘、除或混合情景题，选择全部时所有题库题目都可能出现。", 110, new[] { "全部", "加法", "减法", "乘法", "除法", "混合" }, 0);
            settings.Controls.Add(_categoryBox);
            settings.Controls.Add(SettingsLabel("级别：", 14));
            _levelBox = SettingsBox("练习级别", "初级是单步计算；中级是两步计算；高级是三步混合运算。", 110, new[] { "初级", "中级", "高级" }, 0);
            settings.Controls.Add(_levelBox);
            settings.Controls.Add(SettingsLabel("范围：", 14));
            _rangeLabel = settings.Controls[settings.Controls.Count - 1] as Label;
            _rangeBox = SettingsBox("数字范围", "选择十以内、二十以内或一百以内，加减和混合题的计算结果不会超过所选范围；乘除类题目按级别决定范围。", 110, new[] { "10以内", "20以内", "100以内" }, 1);
            settings.Controls.Add(_rangeBox);

            _progressLabel = new Label { AutoSize = true, Text = "准备开始", AccessibleName = "练习进度", Margin = new Padding(0, 2, 0, 8) };
            _questionLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(860, 0),
                Text = "（等待出题）",
                Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 90, 170),
                AccessibleName = "当前情景题",
                AccessibleDescription = "语音会朗读题目。请从情景中找出数字关系，计算并回答整数答案。",
                Margin = new Padding(0, 0, 0, 10)
            };
            var answerPrompt = new Label { AutoSize = true, Text = "输入本题的整数答案，然后按 Enter 或空格确认：", AccessibleName = "答案输入提示：输入本题的整数答案。", Margin = new Padding(0, 4, 0, 4) };
            _answerBox = new TextBox { Dock = DockStyle.Top, Font = new Font("Microsoft YaHei UI", 16F), ImeMode = ImeMode.Off, AccessibleName = "情景题答案输入框", AccessibleDescription = "输入本题的整数答案，按回车或空格确认。按 F5 重听题目，输入和光标不会改变。", Margin = new Padding(0, 0, 0, 8) };
            _answerBox.KeyDown += AnswerBox_KeyDown;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            _replayButton = new Button { Text = "重听题目 (F5)", AutoSize = true, AccessibleName = "重听题目按钮" };
            _replayButton.Click += delegate { ReplayProblem(); };
            _submitButton = new Button { Text = "确认 (Enter / 空格)", AutoSize = true, AccessibleName = "确认答案按钮" };
            _submitButton.Click += delegate { SubmitAnswer(); };
            _nextButton = new Button { Text = "换一题 (Ctrl+N)", AutoSize = true, AccessibleName = "换一题按钮" };
            _nextButton.Click += delegate { NewProblem(true); };
            var importButton = new Button { Text = "导入题库 (Ctrl+I)", AutoSize = true, AccessibleName = "导入题库按钮" };
            importButton.Click += delegate { ImportBank(); };
            var templateButton = new Button { Text = "导出模板 (Ctrl+T)", AutoSize = true, AccessibleName = "导出题库模板按钮" };
            templateButton.Click += delegate { ExportTemplate(); };
            var reloadButton = new Button { Text = "重新加载 (Ctrl+L)", AutoSize = true, AccessibleName = "重新加载题库按钮" };
            reloadButton.Click += delegate { LoadBank(useStatus: true); NewProblem(true); };
            buttons.Controls.Add(_replayButton);
            buttons.Controls.Add(_submitButton);
            buttons.Controls.Add(_nextButton);
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(templateButton);
            buttons.Controls.Add(reloadButton);

            _statusLabel = new Label { AutoSize = true, Text = "状态：准备开始。", AccessibleName = "状态：准备开始。", Margin = new Padding(0, 2, 0, 8) };
            var help = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(880, 0),
                Text = "初级：单步计算；中级：两步计算；高级：三步混合运算。乘除类题目：初级固定9×9乘法表以内，中级和高级与范围联动。题库文件 情景题库.txt 与程序放在同一目录。设置选项可用键盘切换：Alt+1 类别、Alt+2 级别、Alt+3 范围，每按一次切到下一项，无需鼠标。",
                AccessibleName = "级别说明。初级单步计算；中级两步计算；高级三步混合运算。乘除类题目初级固定9×9乘法表以内，中级和高级与范围联动。设置选项可用 Alt+1、Alt+2、Alt+3 键盘切换，每按一次切到下一项。",
                Margin = new Padding(0, 0, 0, 8)
            };
            _historyBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, AccessibleName = "答题记录", AccessibleDescription = "最近的题目、答案和对错记录。", TabStop = true };

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(settings, 0, 1);
            layout.Controls.Add(_progressLabel, 0, 2);
            layout.Controls.Add(_questionLabel, 0, 3);
            layout.Controls.Add(answerPrompt, 0, 4);
            layout.Controls.Add(_answerBox, 0, 5);
            layout.Controls.Add(buttons, 0, 6);
            layout.Controls.Add(_statusLabel, 0, 7);
            layout.Controls.Add(_historyBox, 0, 8);
            Controls.Add(layout);

            _categoryBox.SelectedIndexChanged += SettingsChanged;
            _levelBox.SelectedIndexChanged += SettingsChanged;
            _rangeBox.SelectedIndexChanged += SettingsChanged;
            UpdateRangeState();
        }

        /// <summary>初级乘除类题目固定 9×9 乘法表以内，此时停用范围下拉框；中级和高级与范围联动。</summary>
        private void UpdateRangeState()
        {
            if (_rangeBox == null || _rangeLabel == null) return;
            bool multDiv = SelectedCategory == ScenarioCategory.Multiplication || SelectedCategory == ScenarioCategory.Division;
            bool fixedByLevel = multDiv && SelectedLevel == ScenarioLevel.Beginner;
            _rangeBox.Enabled = !fixedByLevel;
            _rangeLabel.Enabled = !fixedByLevel;
            _rangeBox.AccessibleDescription = fixedByLevel
                ? "初级乘除类题目固定在9×9乘法表以内。当前不可用。"
                : "选择十以内、二十以内或一百以内。加减和混合题的计算结果、乘除类题目的因数和被除数都不会超过所选范围；初级乘除类题目固定在9×9乘法表以内。";
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

        private static string BankPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "情景题库.txt"); }
        }

        private ScenarioCategory SelectedCategory
        {
            get
            {
                int i = _categoryBox == null ? 0 : _categoryBox.SelectedIndex;
                return i < 0 || i > 5 ? ScenarioCategory.All : (ScenarioCategory)i;
            }
        }
        private ScenarioLevel SelectedLevel { get { int i = _levelBox == null ? 0 : _levelBox.SelectedIndex; return i < 0 || i > 2 ? ScenarioLevel.Beginner : (ScenarioLevel)i; } }
        private int SelectedRange { get { if (_rangeBox == null) return 20; return _rangeBox.SelectedIndex == 0 ? 10 : (_rangeBox.SelectedIndex == 2 ? 100 : 20); } }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || _questionLabel == null) return;
            UpdateRangeState();
            _questionNumber = 0; _correctCount = 0; _wrongCount = 0; _historyBox.Clear(); NewProblem(true);
        }

        /// <summary>从 情景题库.txt 加载题库；文件缺失或没有有效题目时回退到内置题库。</summary>
        private void LoadBank(bool useStatus)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var items = new List<ScenarioItem>();
            string source = "内置题库";
            try
            {
                string path = BankPath;
                if (File.Exists(path))
                {
                    items = ScenarioBank.Parse(ScenarioBank.ReadTextFile(path), errors, warnings);
                    source = "情景题库.txt";
                }
            }
            catch (Exception ex)
            {
                errors.Add("读取题库文件失败：" + ex.Message);
            }
            if (items.Count == 0)
            {
                items = ScenarioBank.Parse(DefaultBank.Text, errors, warnings);
                source = "内置题库";
            }
            _items.Clear();
            _items.AddRange(items);
            _engine.Load(_items);
            if (useStatus)
            {
                string message = "状态：已加载" + source + "，共 " + _items.Count + " 条题目。";
                if (warnings.Count > 0) message += "有 " + warnings.Count + " 条格式提示，详见答题记录。";
                UpdateStatus(message, false);
                Say("已加载" + source + "，共" + _items.Count + "条题目。", true);
            }
            foreach (string warning in warnings) AppendHistory("题库提示：" + warning);
            foreach (string error in errors) AppendHistory("题库错误：" + error);
        }

        private void NewProblem(bool speak)
        {
            _current = _engine.Next(SelectedCategory, SelectedLevel, SelectedRange);
            if (_current == null)
            {
                _questionLabel.Text = "（当前类别和级别暂无题目）";
                _questionLabel.AccessibleName = "当前没有可用题目。请导入题库、更换类别或级别。";
                UpdateStatus("状态：当前类别和级别暂无题目。请按 Ctrl+I 导入题库，或更换类别和级别。", true);
                if (speak) Say("当前类别和级别暂无题目。请导入题库，或更换类别和级别。", true);
                return;
            }
            _questionNumber++; _answerBox.Text = string.Empty;
            _questionLabel.Text = _current.DisplayText;
            _questionLabel.AccessibleName = "当前情景题：" + _current.SpeechText;
            UpdateProgress(); UpdateStatus("状态：请听题，输入整数答案，按 Enter 或空格确认。", false);
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
            string category = _categoryBox == null || _categoryBox.SelectedItem == null ? "全部" : _categoryBox.SelectedItem.ToString();
            string level = _levelBox == null || _levelBox.SelectedItem == null ? "初级" : _levelBox.SelectedItem.ToString();
            string text = category + "；" + level;
            if (SelectedCategory == ScenarioCategory.Multiplication || SelectedCategory == ScenarioCategory.Division)
                text += "；乘除范围" + (SelectedLevel == ScenarioLevel.Beginner ? "9×9乘法表以内" : SelectedRange + "以内");
            return text;
        }

        private void ReplayProblem()
        {
            if (_current == null) return;
            int start = _answerBox.SelectionStart, length = _answerBox.SelectionLength;
            Say("请听题。" + _current.SpeechText, true); UpdateStatus("状态：重新播报当前情景题。", false);
            _answerBox.Focus(); _answerBox.Select(start, length);
        }

        private void SubmitAnswer()
        {
            if (_current == null) return;
            int answer;
            if (!ScenarioEngine.TryParseAnswer(_answerBox.Text, out answer))
            {
                Say("请输入整数答案。", true); UpdateStatus("状态：请输入本题的整数答案，再按 Enter 或空格确认。", true);
                try { _answerBox.Focus(); _answerBox.SelectAll(); } catch { } return;
            }
            string record = _current.DisplayText + "，回答 " + answer;
            if (answer == _current.Answer)
            {
                _correctCount++; UpdateProgress(); AppendHistory(record + "，正确"); UpdateStatus("状态：正确。", false); Say("正确。", true);
                NewProblem(false); if (_current != null) Say("下一题。" + _current.SpeechText, false); return;
            }
            _wrongCount++; UpdateProgress(); AppendHistory(record + "，错误"); UpdateStatus("状态：错误，请重新读题计算。按 F5 可以重听题目。", false); Say("错误，请重新计算。", true);
            try { _answerBox.Focus(); _answerBox.SelectAll(); } catch { }
        }

        private void ImportBank()
        {
            using (var dialog = new ImportForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Imported == null || dialog.Imported.Count == 0) return;
                if (!dialog.AppendMode) _items.Clear();
                _items.AddRange(dialog.Imported);
                SaveBank();
                _engine.Load(_items);
                _questionNumber = 0; _correctCount = 0; _wrongCount = 0; _historyBox.Clear();
                string mode = dialog.AppendMode ? "追加" : "替换";
                string message = "状态：已" + mode + "导入 " + dialog.Imported.Count + " 条题目，题库现有 " + _items.Count + " 条。";
                if (dialog.Warnings != null && dialog.Warnings.Count > 0)
                {
                    message += "有 " + dialog.Warnings.Count + " 条格式提示，详见答题记录。";
                    foreach (string warning in dialog.Warnings) AppendHistory("导入提示：" + warning);
                }
                UpdateStatus(message, true);
                Say("导入完成。题库现有" + _items.Count + "条题目。", true);
                NewProblem(true);
            }
        }

        private void SaveBank()
        {
            try { ScenarioBank.Save(BankPath, _items); }
            catch (Exception ex)
            {
                AppendHistory("保存题库文件失败：" + ex.Message);
                UpdateStatus("状态：保存题库文件失败：" + ex.Message, true);
            }
        }

        private void ExportTemplate()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "情景题库模板.txt");
            try
            {
                File.WriteAllText(path, DefaultBank.TemplateHeader + DefaultBank.Text, new UTF8Encoding(true));
                UpdateStatus("状态：已导出题库模板：" + path, true);
                Say("已导出题库模板。", true);
            }
            catch (Exception ex)
            {
                UpdateStatus("状态：导出模板失败：" + ex.Message, true);
                Say("导出模板失败。", true);
            }
        }

        private void AppendHistory(string text) { _historyBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + "\r\n"); }
        private void UpdateStatus(string text, bool log) { _statusLabel.Text = text; _statusLabel.AccessibleName = text; if (log) AppendHistory(text.Replace("状态：", string.Empty)); }
        private void Say(string text, bool flush) { if (!_speechOk || string.IsNullOrEmpty(text)) return; try { if (flush) _synth.SpeakAsyncCancelAll(); _synth.SpeakAsync(text); } catch { } }

        private void AnswerBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Space && e.Modifiers == Keys.None)) { e.SuppressKeyPress = true; SubmitAnswer(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; _answerBox.Text = string.Empty; }
        }

        private void ScenarioForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R)) { e.SuppressKeyPress = true; ReplayProblem(); }
            else if (e.Control && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; NewProblem(true); }
            else if (e.Control && e.KeyCode == Keys.I) { e.SuppressKeyPress = true; ImportBank(); }
            else if (e.Control && e.KeyCode == Keys.T) { e.SuppressKeyPress = true; ExportTemplate(); }
            else if (e.Control && e.KeyCode == Keys.L) { e.SuppressKeyPress = true; LoadBank(true); NewProblem(true); }
            else if (e.Alt && e.KeyCode == Keys.D1) { e.SuppressKeyPress = true; CycleBox(_categoryBox, "类别"); }
            else if (e.Alt && e.KeyCode == Keys.D2) { e.SuppressKeyPress = true; CycleBox(_levelBox, "级别"); }
            else if (e.Alt && e.KeyCode == Keys.D3) { e.SuppressKeyPress = true; CycleBox(_rangeBox, "范围"); }
        }

        /// <summary>用键盘循环切换设置下拉框：每按一次切到下一项，切完自动换一题并播报。</summary>
        private void CycleBox(ComboBox box, string name)
        {
            if (box == null) return;
            if (!box.Enabled)
            {
                UpdateStatus("状态：" + name + "选项当前不可用。", false);
                Say(name + "选项当前不可用。", true);
                return;
            }
            int next = box.SelectedIndex < 0 ? 0 : (box.SelectedIndex + 1) % box.Items.Count;
            box.SelectedIndex = next; // 触发 SettingsChanged，自动重置进度并换一题
            Say(name + "：" + box.SelectedItem + "。", false);
        }
    }
}
