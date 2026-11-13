using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Speech.Synthesis;

namespace PinyinPractice
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.Run());
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class WordItem
    {
        public string Hanzi;
        public string Pinyin;
        public string Speech;          // 朗读文本，留空时朗读汉字
        public string Normalized;      // spaced, tone-less, lowercase, ü -> v
        public string Compact;         // spaces removed

        public static WordItem Create(string hanzi, string pinyin)
        {
            return Create(hanzi, pinyin, null);
        }

        public static WordItem Create(string hanzi, string pinyin, string speech)
        {
            var w = new WordItem();
            w.Hanzi = hanzi == null ? string.Empty : hanzi.Trim();
            w.Pinyin = pinyin == null ? string.Empty : pinyin.Trim();
            w.Speech = string.IsNullOrEmpty(speech) ? w.Hanzi : speech.Trim();
            w.Normalized = PinyinText.Normalize(w.Pinyin);
            w.Compact = PinyinText.Compact(w.Normalized);
            return w;
        }

        public string Key
        {
            get { return Hanzi + "\u0001" + Compact; }
        }
    }

    internal static class PinyinText
    {
        // Convert any pinyin spelling (with tone marks, tone digits, ü, spaces)
        // into a comparable form: lowercase ascii letters and single spaces.
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string s = input.Trim().ToLowerInvariant();
            s = s.Replace("u:", "v").Replace("U:", "v");

            // Precomposed u-umlaut forms -> v before decomposition.
            s = s.Replace('\u00fc', 'v'); // ü
            s = s.Replace('\u01d6', 'v'); // ǖ
            s = s.Replace('\u01d8', 'v'); // ǘ
            s = s.Replace('\u01da', 'v'); // ǚ
            s = s.Replace('\u01dc', 'v'); // ǜ

            s = s.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc == UnicodeCategory.NonSpacingMark)
                {
                    // Decomposed ü: "u" + combining diaeresis
                    if (c == '\u0308' && sb.Length > 0 && sb[sb.Length - 1] == 'u')
                    {
                        sb[sb.Length - 1] = 'v';
                    }
                    // tone marks are dropped
                    continue;
                }

                if (c >= 'a' && c <= 'z')
                {
                    sb.Append(c);
                }
                else if (c == 'v')
                {
                    sb.Append(c);
                }
                else if (c == ' ' || c == '\'' || c == '-' || c == '\t' || c == ',' || c == '.')
                {
                    sb.Append(' ');
                }
                // digits and other punctuation are dropped (tone numbers)
            }

            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
        }

        public static string Compact(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return string.Empty;
            return normalized.Replace(" ", string.Empty);
        }

        public static bool Matches(string rawInput, WordItem word)
        {
            if (word == null) return false;
            var n = Normalize(rawInput);
            var c = Compact(n);
            if (c.Length == 0) return false;
            return c == word.Compact || n == word.Normalized;
        }
    }

    /// <summary>
    /// 词库解析、读写。导入文本每行一条：汉字|拼音 或 汉字|拼音|朗读文本。
    /// 也兼容全角竖线、制表符、逗号、分号、等号、冒号，以及“汉字 拼音”空格分隔。
    /// </summary>
    internal static class WordBank
    {
        public const string TemplateText =
"# ============================================================\r\n" +
"# 拼音打字练习 · 词库模板（导入用）\r\n" +
"# ------------------------------------------------------------\r\n" +
"# 每行一条词语，基本格式：\r\n" +
"#     汉字|拼音\r\n" +
"# 需要指定朗读文本时（例如多音字读错），用三个字段：\r\n" +
"#     汉字|拼音|朗读文本\r\n" +
"# ------------------------------------------------------------\r\n" +
"# 说明：\r\n" +
"# 1）拼音带不带声调都可以，音节之间可以用空格，也可以不空格。\r\n" +
"#    以下写法都可以：你好|nǐ hǎo    你好|ni hao    你好|nihao\r\n" +
"# 2）# 开头的行为注释，会被忽略；空行也会被忽略。\r\n" +
"# 3）判定时忽略声调、大小写和空格；练习时连续输入 nihao，按回车或空格确认。\r\n" +
"#    词库中及粘贴的答案仍可含音节空格；导入窗口的空格键仍用于输入空格。\r\n" +
"# 4）ü 可以写作 ü、v 或 u:。\r\n" +
"# 5）分隔符除了竖线，也支持全角竖线｜、制表符、逗号、分号、等号和冒号。\r\n" +
"# 6）在下面示例后继续添加，或整段替换成自己的词语即可。\r\n" +
"# ============================================================\r\n" +
"\r\n" +
"你好|ni hao\r\n" +
"谢谢|xie xie\r\n" +
"再见|zai jian\r\n" +
"学习|xue xi\r\n" +
"中国|zhong guo\r\n" +
"拼音|pin yin\r\n";

        private static readonly char[] Separators = new char[] { '|', '\uff5c', '\t', ',', '\uff0c', ';', '\uff1b', '=', ':', '\uff1a' };

        private static readonly Regex HanziFirst = new Regex(
            @"^\s*([\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\u3007\u00b7\u3005]+)\s*([A-Za-z0-9\u00c0-\u024f\s'’\u2019\-:]+)$",
            RegexOptions.Compiled);

        private static readonly Regex LeadingNumber = new Regex(@"^\s*\d+\s*[\.、\)．）]\s*", RegexOptions.Compiled);

        public static List<WordItem> Parse(string text, List<string> errors, List<string> warnings)
        {
            var items = new List<WordItem>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            if (errors != null) errors.Clear();
            if (warnings != null) warnings.Clear();
            if (string.IsNullOrEmpty(text)) return items;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#") || line.StartsWith("//")) continue;

                int hash = line.IndexOf('#');
                if (hash > 0) line = line.Substring(0, hash).Trim();
                if (line.Length == 0) continue;

                line = LeadingNumber.Replace(line, string.Empty).Trim();
                if (line.Length == 0) continue;

                string hanzi;
                string pinyin;
                string speech;

                int sepIndex = line.IndexOfAny(Separators);
                if (sepIndex > 0)
                {
                    var fields = line.Split(Separators, 3);
                    hanzi = CleanField(fields[0]);
                    pinyin = fields.Length > 1 ? CleanField(fields[1]) : string.Empty;
                    speech = fields.Length > 2 ? CleanField(fields[2]) : string.Empty;
                }
                else
                {
                    var m = HanziFirst.Match(line);
                    if (m.Success)
                    {
                        hanzi = CleanField(m.Groups[1].Value);
                        pinyin = CleanField(m.Groups[2].Value);
                        speech = string.Empty;
                    }
                    else
                    {
                        hanzi = CleanField(line);
                        pinyin = string.Empty;
                        speech = string.Empty;
                    }
                }

                if (hanzi.Length == 0)
                {
                    if (errors != null) errors.Add("第 " + lineNo + " 行：没有识别到汉字（内容：" + Shorten(line) + "）");
                    continue;
                }
                if (pinyin.Length == 0)
                {
                    if (errors != null) errors.Add("第 " + lineNo + " 行：缺少拼音，请用竖线分隔汉字和拼音（内容：" + Shorten(line) + "）");
                    continue;
                }
                if (PinyinText.Compact(PinyinText.Normalize(pinyin)).Length == 0)
                {
                    if (errors != null) errors.Add("第 " + lineNo + " 行：拼音里没有字母（内容：" + Shorten(line) + "）");
                    continue;
                }

                var item = WordItem.Create(hanzi, pinyin, speech);
                if (seen.ContainsKey(item.Key))
                {
                    if (warnings != null) warnings.Add("第 " + lineNo + " 行：" + hanzi + " 与第 " + seen[item.Key] + " 行重复，已合并");
                    continue;
                }
                seen[item.Key] = lineNo;
                items.Add(item);
            }

            return items;
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 20 ? s : s.Substring(0, 20) + "…";
        }

        private static string CleanField(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Trim().Trim('\u3000').Trim();
            s = s.Trim('"', '\'', '“', '”', '‘', '’', '《', '》', '<', '>');
            return s.Trim();
        }

        public static string ReadTextFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            try
            {
                var strict = new UTF8Encoding(false, true);
                text = strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    text = Encoding.GetEncoding("gb18030").GetString(bytes);
                }
                catch
                {
                    text = Encoding.Default.GetString(bytes);
                }
            }

            if (text.Length > 0 && text[0] == '\ufeff') text = text.Substring(1);
            return text;
        }

        public static List<WordItem> Load(string path, List<string> errors, List<string> warnings)
        {
            if (!File.Exists(path)) return new List<WordItem>();
            return Parse(ReadTextFile(path), errors, warnings);
        }

        public static void Save(string path, IEnumerable<WordItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 拼音打字练习 词库文件（UTF-8）");
            sb.AppendLine("# 格式：汉字|拼音，或 汉字|拼音|朗读文本");
            sb.AppendLine("# 以 # 开头为注释；判定答案时忽略声调、大小写和空格。");
            sb.AppendLine();
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrEmpty(it.Hanzi) || string.IsNullOrEmpty(it.Pinyin)) continue;
                if (!string.IsNullOrEmpty(it.Speech) && it.Speech != it.Hanzi)
                {
                    sb.AppendLine(it.Hanzi + "|" + it.Pinyin + "|" + it.Speech);
                }
                else
                {
                    sb.AppendLine(it.Hanzi + "|" + it.Pinyin);
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    internal static class SelfTest
    {
        public static int Run()
        {
            int failures = 0;
            failures += ExpectNormalized("simple", "ni hao", "ni hao");
            failures += ExpectNormalized("tone marks", "n\u01d0 h\u0101o", "ni hao");
            failures += ExpectNormalized("tone digits", "zhong1 guo2", "zhong guo");
            failures += ExpectNormalized("extra spaces", "  pin   yin  ", "pin yin");
            failures += ExpectNormalized("precomposed umlaut", "l\u00fc", "lv");
            failures += ExpectNormalized("v spelling", "nv", "nv");
            failures += ExpectNormalized("uppercase", "NI HAO", "ni hao");

            failures += ExpectMatch("compact", "nihao", "ni hao", true);
            failures += ExpectMatch("spaced", "ni hao", "ni hao", true);
            failures += ExpectMatch("tone input", "n\u01d0 h\u0101o", "ni hao", true);
            failures += ExpectMatch("digits input", "ni3 hao3", "ni hao", true);
            failures += ExpectMatch("wrong order", "ni hoa", "ni hao", false);
            failures += ExpectMatch("empty", "", "ni hao", false);

            failures += ExpectParse("template", WordBank.TemplateText, 6, 0);
            failures += ExpectParse("pipe", "你好|ni hao", 1, 0);
            failures += ExpectParse("fullwidth pipe", "你好｜ni hao", 1, 0);
            failures += ExpectParse("tab", "你好\tni hao", 1, 0);
            failures += ExpectParse("comma", "你好，ni hao", 1, 0);
            failures += ExpectParse("space separated", "你好 ni hao", 1, 0);
            failures += ExpectParse("numbered list", "3. 你好 ni hao", 1, 0);
            failures += ExpectParse("inline comment", "你好|ni hao # 问候", 1, 0);
            failures += ExpectParse("speech field", "你好|ni hao|你好呀", 1, 0);
            failures += ExpectParse("missing pinyin", "你好", 0, 1);
            failures += ExpectParse("duplicate merged", "你好|ni hao\r\n你好|ni1 hao3", 1, 0);
            failures += ExpectParse("tone marks row", "学习|xu\u00e9 x\u00ed", 1, 0);

            Console.WriteLine(failures == 0 ? "SELFTEST: PASS" : "SELFTEST: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        private static int ExpectNormalized(string name, string input, string expected)
        {
            var actual = PinyinText.Normalize(input);
            var ok = string.Equals(actual, expected, StringComparison.Ordinal);
            if (!ok)
            {
                Console.WriteLine("FAIL [" + name + "] input='" + input + "' expected='" + expected + "' actual='" + actual + "'");
            }
            return ok ? 0 : 1;
        }

        private static int ExpectMatch(string name, string input, string wordPinyin, bool shouldMatch)
        {
            var word = WordItem.Create("test", wordPinyin);
            var ok = PinyinText.Matches(input, word) == shouldMatch;
            if (!ok)
            {
                Console.WriteLine("FAIL [" + name + "] input='" + input + "' word='" + wordPinyin + "' shouldMatch=" + shouldMatch);
            }
            return ok ? 0 : 1;
        }

        private static int ExpectParse(string name, string text, int expectedItems, int expectedErrors)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var items = WordBank.Parse(text, errors, warnings);
            var ok = items.Count == expectedItems && errors.Count == expectedErrors;
            if (!ok)
            {
                Console.WriteLine("FAIL [" + name + "] items=" + items.Count + " (want " + expectedItems + "), errors=" + errors.Count + " (want " + expectedErrors + ")");
                foreach (var e in errors) Console.WriteLine("    error: " + e);
            }
            return ok ? 0 : 1;
        }
    }

    public sealed class ImportForm : Form
    {
        private readonly List<WordItem> _existing;
        private readonly TextBox _text;
        private readonly Label _status;
        private readonly RadioButton _replace;
        private readonly RadioButton _append;
        private readonly Button _ok;

        public List<WordItem> Imported { get; private set; }
        public bool AppendMode { get { return _append.Checked; } }

        public ImportForm(List<WordItem> existing)
        {
            _existing = existing ?? new List<WordItem>();
            Imported = new List<WordItem>();

            Text = "导入词库";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(700, 560);
            MinimumSize = new Size(560, 460);
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            KeyPreview = true;
            AccessibleName = "导入词库窗口";
            AccessibleDescription = "把文字导入为新的练习词库，每行格式为汉字竖线拼音。";

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(16)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                AutoSize = true,
                Text = "把词库文字粘贴到下面，或从文件导入。每行一条，格式：汉字|拼音（也可写成 汉字|拼音|朗读文本）。\r\n拼音带不带声调都可以；# 开头的行是注释。",
                AccessibleName = "导入说明：每行一条，格式为汉字竖线拼音。拼音可以带声调，也可以不带。井号开头的行是注释。",
                Margin = new Padding(0, 0, 0, 8)
            };

            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular),
                AccessibleName = "词库文本",
                AccessibleDescription = "在此粘贴或输入词库内容，每行一条。按 Ctrl 加回车开始导入。",
                Margin = new Padding(0, 0, 0, 8)
            };
            _text.TextChanged += delegate { UpdatePreview(); };

            var tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            var fromFile = new Button { Text = "从文件导入…", AutoSize = true, AccessibleName = "从文件导入按钮" };
            fromFile.Click += delegate { LoadFromFile(); };
            var paste = new Button { Text = "粘贴", AutoSize = true, AccessibleName = "粘贴剪贴板内容按钮" };
            paste.Click += delegate { PasteFromClipboard(); };
            var clear = new Button { Text = "清空", AutoSize = true, AccessibleName = "清空文本按钮" };
            clear.Click += delegate { _text.Text = string.Empty; };
            var template = new Button { Text = "模板", AutoSize = true, AccessibleName = "插入模板按钮" };
            template.Click += delegate { InsertTemplate(); };
            var saveTemplate = new Button { Text = "另存模板并打开", AutoSize = true, AccessibleName = "另存模板文件并打开按钮" };
            saveTemplate.Click += delegate { SaveTemplateToDisk(); };

            tools.Controls.Add(fromFile);
            tools.Controls.Add(paste);
            tools.Controls.Add(clear);
            tools.Controls.Add(template);
            tools.Controls.Add(saveTemplate);

            var modes = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _replace = new RadioButton
            {
                AutoSize = true,
                Text = "替换现有词库",
                Checked = true,
                AccessibleName = "替换现有词库"
            };
            _append = new RadioButton
            {
                AutoSize = true,
                Text = "追加到现有词库（当前 " + _existing.Count + " 条）",
                AccessibleName = "追加到现有词库，当前 " + _existing.Count + " 条"
            };
            modes.Controls.Add(_replace);
            modes.Controls.Add(_append);

            _status = new Label
            {
                AutoSize = true,
                Text = "状态：还没有内容。",
                AccessibleName = "状态：还没有内容。",
                Margin = new Padding(0, 4, 0, 8)
            };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 0)
            };
            _ok = new Button { Text = "开始导入 (Ctrl+Enter)", AutoSize = true, AccessibleName = "开始导入按钮" };
            _ok.Click += delegate { DoImport(); };
            var cancel = new Button { Text = "取消 (Esc)", AutoSize = true, DialogResult = DialogResult.Cancel, AccessibleName = "取消按钮" };
            bottom.Controls.Add(_ok);
            bottom.Controls.Add(cancel);

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(_text, 0, 1);
            layout.Controls.Add(tools, 0, 2);
            layout.Controls.Add(modes, 0, 3);
            layout.Controls.Add(_status, 0, 4);
            layout.Controls.Add(bottom, 0, 5);

            Controls.Add(layout);
            AcceptButton = _ok;
            CancelButton = cancel;

            KeyDown += ImportForm_KeyDown;
            Shown += delegate { try { _text.Focus(); } catch { } };
        }

        private void ImportForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return))
            {
                e.SuppressKeyPress = true;
                DoImport();
            }
        }

        private void LoadFromFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择词库文本文件";
                dlg.Filter = "文本文件 (*.txt;*.csv;*.log)|*.txt;*.csv;*.log|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var text = WordBank.ReadTextFile(dlg.FileName);
                    _text.Text = text;
                    Summary("状态：已读取文件 " + Path.GetFileName(dlg.FileName) + "。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "读取文件失败：" + ex.Message, "导入词库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void PasteFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _text.Text = Clipboard.GetText();
                    Summary("状态：已粘贴剪贴板内容。");
                }
                else
                {
                    Summary("状态：剪贴板里没有文字。");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "粘贴失败：" + ex.Message, "导入词库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InsertTemplate()
        {
            _text.Text = WordBank.TemplateText;
            Summary("状态：已插入模板，可直接修改其中的词语。");
        }

        private void SaveTemplateToDisk()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "词库模板.txt");
                File.WriteAllText(path, WordBank.TemplateText, new UTF8Encoding(true));
                Summary("状态：模板已保存到 " + path);
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存模板失败：" + ex.Message, "导入词库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Summary(string text)
        {
            _status.Text = text;
            _status.AccessibleName = text;
        }

        private void UpdatePreview()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var items = WordBank.Parse(_text.Text, errors, warnings);

            if (_text.Text.Trim().Length == 0)
            {
                Summary("状态：还没有内容。");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("状态：已识别 ").Append(items.Count).Append(" 条词语");
            if (warnings.Count > 0) sb.Append("，").Append(warnings.Count).Append(" 行重复已合并");
            if (errors.Count > 0) sb.Append("，").Append(errors.Count).Append(" 行无法识别");
            sb.Append("。");
            if (errors.Count > 0) sb.Append(errors[0]);
            Summary(sb.ToString());
        }

        private void DoImport()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var items = WordBank.Parse(_text.Text, errors, warnings);

            if (items.Count == 0)
            {
                var msg = errors.Count > 0
                    ? "没有识别到可用词语。请检查下面几行：\r\n" + JoinErrors(errors)
                    : "没有识别到可用词语。每行请写成：汉字|拼音，例如 你好|ni hao。";
                MessageBox.Show(this, msg, "导入词库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Summary("状态：没有识别到可用词语。");
                try { _text.Focus(); } catch { }
                return;
            }

            if (errors.Count > 0)
            {
                var ask = "有 " + errors.Count + " 行无法识别，跳过它们继续导入吗？\r\n" + JoinErrors(errors);
                if (MessageBox.Show(this, ask, "导入词库", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                {
                    return;
                }
            }

            Imported = items;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string JoinErrors(List<string> errors)
        {
            var sb = new StringBuilder();
            int limit = Math.Min(errors.Count, 8);
            for (int i = 0; i < limit; i++) sb.AppendLine(errors[i]);
            if (errors.Count > limit) sb.AppendLine("……还有 " + (errors.Count - limit) + " 行");
            return sb.ToString();
        }
    }

    public sealed class MainForm : Form
    {
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();
        private readonly List<WordItem> _words = new List<WordItem>();
        private readonly Random _rng = new Random();

        private bool _speechOk;
        private bool _chineseVoice;
        private bool _randomOrder = true;

        private int _index;
        private int _attempts;
        private WordItem _current;

        private Label _wordLabel;
        private Label _progressLabel;
        private Label _statusLabel;
        private TextBox _inputBox;
        private TextBox _statusLog;
        private CheckBox _randomCheck;
        private Button _replayButton;
        private Button _submitButton;
        private Button _skipButton;
        private Button _importButton;
        private Button _templateButton;

        public MainForm()
        {
            Text = "拼音打字练习";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(680, 520);
            Size = new Size(820, 640);
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            AccessibleName = "拼音打字练习";
            AccessibleDescription = "听读音，输入拼音，按回车或空格确认的拼音打字练习程序。";

            BuildUi();
            InitSpeech();
            LoadWords();

            KeyDown += MainForm_KeyDown;
            Shown += MainForm_Shown;
            FormClosed += delegate
            {
                try { _synth.SpeakAsyncCancelAll(); } catch { }
                try { _synth.Dispose(); } catch { }
            };
        }

        private void BuildUi()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 10,
                Padding = new Padding(18),
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 9; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var title = new Label
            {
                AutoSize = true,
                Text = "拼音打字练习",
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                AccessibleName = "标题：拼音打字练习",
                Margin = new Padding(0, 0, 0, 10)
            };

            _progressLabel = new Label
            {
                AutoSize = true,
                Text = "准备开始",
                AccessibleName = "进度",
                Margin = new Padding(0, 0, 0, 6)
            };

            _wordLabel = new Label
            {
                AutoSize = true,
                Text = "（等待加载）",
                Font = new Font(Font.FontFamily, 22F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 90, 170),
                AccessibleName = "当前词语",
                AccessibleDescription = "当前正在练习的词语。程序会用系统语音播报它的读音。",
                Margin = new Padding(0, 0, 0, 10)
            };

            var inputLabel = new Label
            {
                AutoSize = true,
                Text = "输入拼音，然后按 Enter 或空格确认：",
                AccessibleName = "输入提示",
                Margin = new Padding(0, 6, 0, 4)
            };

            _inputBox = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Regular),
                ImeMode = ImeMode.Off,
                AccessibleName = "拼音输入框",
                AccessibleDescription = "连续输入词语的拼音，不必分隔音节，按回车或空格确认。按 F5 可重播读音并保留输入和光标，按 Ctrl+N 可跳过。",
                Margin = new Padding(0, 0, 0, 8)
            };
            _inputBox.KeyDown += InputBox_KeyDown;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 2, 0, 4)
            };

            _replayButton = new Button { Text = "重播读音 (F5)", AutoSize = true, AccessibleName = "重播读音按钮" };
            _replayButton.Click += delegate { ReplayWord(); };

            _submitButton = new Button { Text = "确认 (Enter / 空格)", AutoSize = true, AccessibleName = "确认答案按钮", AccessibleDescription = "在拼音输入框内按回车或空格提交答案。" };
            _submitButton.Click += delegate { SubmitAnswer(); };

            _skipButton = new Button { Text = "跳过 (Ctrl+N)", AutoSize = true, AccessibleName = "跳过当前词语按钮" };
            _skipButton.Click += delegate { SkipWord(); };

            buttons.Controls.Add(_replayButton);
            buttons.Controls.Add(_submitButton);
            buttons.Controls.Add(_skipButton);

            var bankButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 8)
            };

            _importButton = new Button { Text = "导入词库 (Ctrl+I)", AutoSize = true, AccessibleName = "导入词库按钮，快捷键 Ctrl 加 I" };
            _importButton.Click += delegate { ImportWordBank(); };

            _templateButton = new Button { Text = "导出词库模板 (Ctrl+T)", AutoSize = true, AccessibleName = "导出词库模板按钮，快捷键 Ctrl 加 T" };
            _templateButton.Click += delegate { ExportTemplate(); };

            bankButtons.Controls.Add(_importButton);
            bankButtons.Controls.Add(_templateButton);

            _statusLabel = new Label
            {
                AutoSize = true,
                Text = "状态：准备开始。按 F5 可重播读音。",
                AccessibleName = "状态",
                Margin = new Padding(0, 4, 0, 8)
            };

            _statusLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                AccessibleName = "练习记录",
                AccessibleDescription = "最近的状态和反馈记录，可聚焦后用读屏软件重听。",
                Margin = new Padding(0, 0, 0, 8),
                TabStop = true
            };

            _randomCheck = new CheckBox
            {
                AutoSize = true,
                Text = "随机抽取词语",
                Checked = true,
                AccessibleName = "随机抽取词语",
                Margin = new Padding(0, 0, 0, 4)
            };
            _randomCheck.CheckedChanged += delegate
            {
                _randomOrder = _randomCheck.Checked;
                UpdateStatus(_randomOrder ? "已切换为随机抽取词语。" : "已切换为按词库顺序练习。", false);
            };

            var help = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "快捷键：Enter 或空格确认；F5 或 Ctrl+R 重播读音（保留输入和光标）；Ctrl+N 跳过；Ctrl+I 导入词库；Ctrl+T 导出词库模板。\r\n词库文件 words.txt 与程序放在同一目录，每行格式：汉字|拼音（拼音可带声调、可用空格分隔；可再加第三个字段指定朗读文本）。",
                AccessibleName = "帮助",
                Margin = new Padding(0, 0, 0, 0)
            };

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(_progressLabel, 0, 1);
            layout.Controls.Add(_wordLabel, 0, 2);
            layout.Controls.Add(inputLabel, 0, 3);
            layout.Controls.Add(_inputBox, 0, 4);
            layout.Controls.Add(buttons, 0, 5);
            layout.Controls.Add(bankButtons, 0, 6);
            layout.Controls.Add(_statusLabel, 0, 7);
            layout.Controls.Add(_randomCheck, 0, 8);
            layout.Controls.Add(_statusLog, 0, 9);

            Controls.Add(layout);
        }

        private void InitSpeech()
        {
            try
            {
                _synth.SetOutputToDefaultAudioDevice();
                _synth.Rate = 0;
                _synth.Volume = 100;

                foreach (var v in _synth.GetInstalledVoices())
                {
                    if (v == null || v.VoiceInfo == null || !v.Enabled) continue;
                    _speechOk = true;
                    var culture = v.VoiceInfo.Culture != null ? v.VoiceInfo.Culture.Name : string.Empty;
                    if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        try { _synth.SelectVoice(v.VoiceInfo.Name); } catch { }
                        _chineseVoice = true;
                    }
                }

                if (!_speechOk)
                {
                    _statusLabel.Text = "状态：未检测到可用的系统语音，仍可打字练习。";
                }
            }
            catch
            {
                _speechOk = false;
            }
        }

        private static string WordBankPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "words.txt"); }
        }

        private void LoadWords()
        {
            _words.Clear();
            var errors = new List<string>();
            var warnings = new List<string>();

            if (File.Exists(WordBankPath))
            {
                try
                {
                    _words.AddRange(WordBank.Load(WordBankPath, errors, warnings));
                }
                catch
                {
                    // fall back to built-in words below
                }
            }

            if (_words.Count == 0)
            {
                AddDefault("你好", "ni hao");
                AddDefault("谢谢", "xie xie");
                AddDefault("再见", "zai jian");
                AddDefault("学习", "xue xi");
                AddDefault("中国", "zhong guo");
                AddDefault("拼音", "pin yin");
            }

            if (warnings.Count > 0) UpdateStatus("词库中有 " + warnings.Count + " 行重复词语已合并。", true);
            if (errors.Count > 0) UpdateStatus("词库中有 " + errors.Count + " 行无法识别：" + errors[0], true);

            if (_randomOrder) Shuffle(_words);
            _index = 0;
        }

        private void AddDefault(string hanzi, string pinyin)
        {
            _words.Add(WordItem.Create(hanzi, pinyin));
        }

        private void Shuffle(List<WordItem> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            StartRound();
            try { _inputBox.Focus(); } catch { }
        }

        private void StartRound()
        {
            if (_words.Count == 0) return;
            _index = 0;
            _attempts = 0;
            ShowCurrentWord();
            Say("请听读音。" + WordSpeech(), true);
            try { _inputBox.Focus(); } catch { }
        }

        private string WordSpeech()
        {
            if (_current == null) return string.Empty;
            // Prefer the Chinese voice reading the word itself (real pronunciation).
            if (_chineseVoice) return _current.Speech;
            // No Chinese voice installed: fall back to the pinyin spelling.
            return _current.Pinyin;
        }

        private void ShowCurrentWord()
        {
            if (_words.Count == 0) return;
            _current = _words[_index % _words.Count];
            _attempts = 0;

            _wordLabel.Text = _current.Hanzi;
            _wordLabel.AccessibleName = "当前词语：" + _current.Hanzi;
            _progressLabel.Text = "第 " + (_index + 1) + " 个，共 " + _words.Count + " 个";
            _progressLabel.AccessibleName = _progressLabel.Text;
            _inputBox.Text = string.Empty;

            UpdateStatus("状态：" + _progressLabel.Text + "。请听读音并输入拼音，按 Enter 或空格确认。", false);
        }

        private void Say(string text, bool flush)
        {
            if (!_speechOk || string.IsNullOrEmpty(text)) return;
            try
            {
                if (flush) _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch
            {
                // ignore speech failures
            }
        }

        private void ReplayWord()
        {
            if (_current == null) return;
            Say("请听读音。" + WordSpeech(), true);
            UpdateStatus("状态：重新播报当前词语读音。", false);
            // Replaying must not select all text or move the user's caret.
            // Restore the selection explicitly when returning from a button.
            int selectionStart = _inputBox.SelectionStart;
            int selectionLength = _inputBox.SelectionLength;
            _inputBox.Focus();
            _inputBox.Select(selectionStart, selectionLength);
        }

        private void SubmitAnswer()
        {
            if (_current == null) return;
            var input = _inputBox.Text;

            if (input == null || input.Trim().Length == 0)
            {
                Say("请先输入拼音。", true);
                UpdateStatus("状态：请先输入拼音，再按 Enter 或空格确认。", true);
                try { _inputBox.Focus(); } catch { }
                return;
            }

            if (PinyinText.Matches(input, _current))
            {
                UpdateStatus("状态：正确。答案：" + _current.Pinyin, true);
                Say("正确。", false);

                _index++;
                if (_index >= _words.Count)
                {
                    if (_randomOrder) Shuffle(_words);
                    _index = 0;
                    Say("本轮完成。", false);
                }

                ShowCurrentWord(); // also clears input
                Say("请听读音。" + WordSpeech(), false);
                try { _inputBox.Focus(); } catch { }
                return;
            }

            _attempts++;
            if (_attempts == 1)
            {
                UpdateStatus("状态：错误，请重新输入。按 F5 可重播读音。", true);
                Say("错误。请重新输入。按 F5 可以重播读音。", true);
            }
            else
            {
                UpdateStatus("状态：错误。正确答案是 " + _current.Hanzi + "，拼音是 " + _current.Pinyin + "。按 Ctrl+N 可跳过。", true);
                Say("错误。正确答案是。" + WordSpeech() + "。", true);
            }

            try { _inputBox.Focus(); _inputBox.SelectAll(); } catch { }
        }

        private void SkipWord()
        {
            if (_current == null) return;
            _index++;
            if (_index >= _words.Count)
            {
                if (_randomOrder) Shuffle(_words);
                _index = 0;
                Say("本轮完成。", false);
            }
            ShowCurrentWord();
            Say("请听读音。" + WordSpeech(), true);
            try { _inputBox.Focus(); } catch { }
        }

        private void ImportWordBank()
        {
            Say("导入词库。请粘贴或选择文本文件。按 Ctrl 加回车开始导入。", true);
            using (var dlg = new ImportForm(_words))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var imported = dlg.Imported;
                if (imported == null || imported.Count == 0) return;

                int added;
                if (dlg.AppendMode)
                {
                    var keys = new Dictionary<string, bool>(StringComparer.Ordinal);
                    foreach (var w in _words) keys[w.Key] = true;
                    added = 0;
                    foreach (var w in imported)
                    {
                        if (keys.ContainsKey(w.Key)) continue;
                        _words.Add(w);
                        keys[w.Key] = true;
                        added++;
                    }
                }
                else
                {
                    _words.Clear();
                    _words.AddRange(imported);
                    added = imported.Count;
                }

                var saved = SaveWordBank();
                if (_randomOrder) Shuffle(_words);
                _index = 0;
                StartRound();

                var msg = "状态：导入成功，新增 " + added + " 条，当前共 " + _words.Count + " 条词语。" + (saved ? "词库已保存。" : "词库未能保存到文件，仅本次有效。");
                UpdateStatus(msg, true);
                Say("导入成功。共 " + _words.Count + " 个词语。", true);
            }
        }

        private bool SaveWordBank()
        {
            try
            {
                if (File.Exists(WordBankPath))
                {
                    try { File.Copy(WordBankPath, WordBankPath + ".bak", true); } catch { }
                }
                WordBank.Save(WordBankPath, _words);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ExportTemplate()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "词库模板.txt");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# 拼音打字练习 · 当前词库导出（" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "）");
                sb.AppendLine("# 修改后可通过 Ctrl+I 导入，选择“替换现有词库”。");
                sb.AppendLine();
                foreach (var it in _words)
                {
                    if (!string.IsNullOrEmpty(it.Speech) && it.Speech != it.Hanzi)
                    {
                        sb.AppendLine(it.Hanzi + "|" + it.Pinyin + "|" + it.Speech);
                    }
                    else
                    {
                        sb.AppendLine(it.Hanzi + "|" + it.Pinyin);
                    }
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

                UpdateStatus("状态：已将 " + _words.Count + " 条词语导出到 " + path, true);
                Say("已导出词库模板，共 " + _words.Count + " 条词语。", true);
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
            }
            catch (Exception ex)
            {
                UpdateStatus("状态：导出词库模板失败：" + ex.Message, true);
                Say("导出失败。", true);
            }
        }

        private void UpdateStatus(string status, bool log)
        {
            _statusLabel.Text = status;
            _statusLabel.AccessibleName = status;
            if (log)
            {
                var line = DateTime.Now.ToString("HH:mm:ss") + "  " + status.Replace("状态：", string.Empty);
                _statusLog.AppendText(line + "\r\n");
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Only the practice input treats a plain Space as confirmation.
            // Leave system/IME shortcuts (e.g. Ctrl+Space, Alt+Space) alone.
            if (e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Space && e.Modifiers == Keys.None))
            {
                e.SuppressKeyPress = true;
                SubmitAnswer();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _inputBox.Text = string.Empty;
            }
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
            {
                e.SuppressKeyPress = true;
                ReplayWord();
            }
            else if (e.Control && e.KeyCode == Keys.N)
            {
                e.SuppressKeyPress = true;
                SkipWord();
            }
            else if (e.Control && e.KeyCode == Keys.L)
            {
                e.SuppressKeyPress = true;
                LoadWords();
                StartRound();
                UpdateStatus("状态：已重新加载词库，共 " + _words.Count + " 个词语。", true);
            }
            else if (e.Control && e.KeyCode == Keys.I)
            {
                e.SuppressKeyPress = true;
                ImportWordBank();
            }
            else if (e.Control && e.KeyCode == Keys.T)
            {
                e.SuppressKeyPress = true;
                ExportTemplate();
            }
        }
    }
}
