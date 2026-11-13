using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Speech.Synthesis;
using System.Windows.Forms;

namespace PracticeToolkit
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ToolkitForm());
        }
    }

    public sealed class ToolkitForm : Form
    {
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();
        private bool _speechOk;
        private Button _pinyinButton;
        private Button _arithmeticButton;
        private Label _statusLabel;

        public ToolkitForm()
        {
            Text = "学习练习小工具整合包";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(620, 430);
            Size = new Size(720, 500);
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            AccessibleName = "学习练习小工具整合包";
            AccessibleDescription = "请选择拼音打字练习或数学加减乘除算术练习。";

            BuildUi();
            InitSpeech();
            Shown += delegate
            {
                try { _pinyinButton.Focus(); } catch { }
                Say("学习练习小工具整合包。请选择工具。第一个，拼音打字练习。第二个，数学加减乘除算术练习。");
            };
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
                RowCount = 7,
                Padding = new Padding(24)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                AutoSize = true,
                Text = "学习练习小工具整合包",
                Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
                AccessibleName = "标题：学习练习小工具整合包",
                Margin = new Padding(0, 0, 0, 8)
            };
            var intro = new Label
            {
                AutoSize = true,
                Text = "请选择要打开的独立练习工具。可按 Tab 切换，按 Enter 或空格打开。",
                AccessibleName = "请选择工具。按 Tab 切换，按回车或空格打开。",
                Margin = new Padding(0, 0, 0, 16)
            };

            _pinyinButton = CreateToolButton(
                "拼音打字练习",
                "系统语音播报词语，输入拼音后按 Enter 或空格确认。",
                "PinyinPractice.exe");
            _pinyinButton.AccessibleName = "打开拼音打字练习";
            _pinyinButton.AccessibleDescription = "系统语音播报词语，输入拼音，练习拼音打字。";

            _arithmeticButton = CreateToolButton(
                "数学加减乘除算术练习",
                "系统语音播报算术题，输入整数答案后按 Enter 或空格确认。",
                "ArithmeticPractice.exe");
            _arithmeticButton.AccessibleName = "打开数学加减乘除算术练习";
            _arithmeticButton.AccessibleDescription = "练习加法、减法、乘法、除法以及混合运算。";

            _statusLabel = new Label
            {
                AutoSize = true,
                Text = "状态：请选择工具。",
                AccessibleName = "状态：请选择工具。",
                Margin = new Padding(0, 12, 0, 4)
            };
            var help = new Label
            {
                AutoSize = true,
                Text = "提示：两个练习工具也可以绕过整合包，分别直接运行各自的 exe 文件。",
                AccessibleName = "提示：两个练习工具也可以分别直接运行。",
                Margin = new Padding(0, 0, 0, 8)
            };
            var close = new Button
            {
                Text = "退出整合包 (Esc)",
                AutoSize = true,
                AccessibleName = "退出整合包按钮",
                DialogResult = DialogResult.Cancel
            };
            close.Click += delegate { Close(); };

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(intro, 0, 1);
            layout.Controls.Add(_pinyinButton, 0, 2);
            layout.Controls.Add(_arithmeticButton, 0, 3);
            layout.Controls.Add(_statusLabel, 0, 4);
            layout.Controls.Add(help, 0, 5);
            layout.Controls.Add(close, 0, 6);
            Controls.Add(layout);
            CancelButton = close;
        }

        private Button CreateToolButton(string title, string description, string executable)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = title + "\r\n" + description,
                Font = new Font(Font.FontFamily, 13F, FontStyle.Regular),
                Margin = new Padding(0, 4, 0, 10),
                Tag = executable
            };
            button.Click += ToolButton_Click;
            return button;
        }

        private void ToolButton_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            string executable = Convert.ToString(button.Tag);
            string title = button.Text.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, executable);
            if (!File.Exists(path))
            {
                string status = "状态：找不到 " + executable + "。请确认三个 exe 文件在同一目录。";
                UpdateStatus(status);
                Say("打开失败。找不到" + title + "。请确认程序文件在同一目录。");
                MessageBox.Show(this, status, "无法打开工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                try { button.Focus(); } catch { }
                return;
            }

            try
            {
                Process.Start(path);
                UpdateStatus("状态：已打开" + title + "。整合包窗口仍然保留，可继续打开其他工具。" );
                Say("已打开" + title + "。");
                try { button.Focus(); } catch { }
            }
            catch (Exception ex)
            {
                UpdateStatus("状态：打开" + title + "失败：" + ex.Message);
                Say("打开失败。");
            }
        }

        private void InitSpeech()
        {
            try
            {
                _synth.SetOutputToDefaultAudioDevice();
                _synth.Volume = 100;
                _synth.Rate = 0;
                foreach (var voice in _synth.GetInstalledVoices())
                {
                    if (voice == null || voice.VoiceInfo == null || !voice.Enabled) continue;
                    _speechOk = true;
                    string culture = voice.VoiceInfo.Culture == null ? string.Empty : voice.VoiceInfo.Culture.Name;
                    if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        try { _synth.SelectVoice(voice.VoiceInfo.Name); } catch { }
                        break;
                    }
                }
            }
            catch
            {
                _speechOk = false;
            }
        }

        private void UpdateStatus(string status)
        {
            _statusLabel.Text = status;
            _statusLabel.AccessibleName = status;
        }

        private void Say(string text)
        {
            if (!_speechOk || string.IsNullOrEmpty(text)) return;
            try
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch { }
        }
    }
}
