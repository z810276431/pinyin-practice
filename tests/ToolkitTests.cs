using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PracticeToolkit;

internal static class ToolkitTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using (var form = new ToolkitForm())
            {
                SetField(form, "_speechOk", false);
                form.Show();
                Application.DoEvents();
                var pinyin = Field<Button>(form, "_pinyinButton");
                var arithmetic = Field<Button>(form, "_arithmeticButton");
                Assert(pinyin.Text.Contains("拼音打字练习"), "launcher exposes pinyin practice option");
                Assert(arithmetic.Text.Contains("数学加减乘除算术练习"), "launcher exposes arithmetic practice option");
                Assert(string.Equals(Convert.ToString(pinyin.Tag), "PinyinPractice.exe", StringComparison.Ordinal),
                    "pinyin option targets independent executable");
                Assert(string.Equals(Convert.ToString(arithmetic.Tag), "ArithmeticPractice.exe", StringComparison.Ordinal),
                    "arithmetic option targets independent executable");
                Assert(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Convert.ToString(pinyin.Tag))),
                    "pinyin executable is beside launcher");
                Assert(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Convert.ToString(arithmetic.Tag))),
                    "arithmetic executable is beside launcher");
                Assert(form.AcceptButton == null && form.CancelButton != null, "launcher uses normal button Space/Enter behavior and supports Escape");

                int pinyinBefore = RunningProcessCount("PinyinPractice");
                pinyin.Focus();
                Assert(pinyin.Focused, "pinyin option receives keyboard focus");
                pinyin.PerformClick();
                Assert(WaitForProcessCount("PinyinPractice", pinyinBefore + 1), "pinyin launcher option starts independent pinyin process");
                StopNewProcesses("PinyinPractice", pinyinBefore);

                int arithmeticBefore = RunningProcessCount("ArithmeticPractice");
                arithmetic.Focus();
                Assert(arithmetic.Focused, "arithmetic option receives keyboard focus");
                arithmetic.PerformClick();
                Assert(WaitForProcessCount("ArithmeticPractice", arithmeticBefore + 1), "arithmetic launcher option starts independent arithmetic process");
                StopNewProcesses("ArithmeticPractice", arithmeticBefore);
                form.Close();
            }
            Console.WriteLine("TOOLKIT TESTS: PASS (" + _checks + " checks)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("TOOLKIT TESTS: FAIL\n" + ex);
            return 1;
        }
    }

    private static int RunningProcessCount(string processName)
    {
        return Process.GetProcessesByName(processName).Length;
    }

    private static bool WaitForProcessCount(string processName, int target)
    {
        for (int i = 0; i < 30; i++)
        {
            Application.DoEvents();
            if (RunningProcessCount(processName) >= target) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private static void StopNewProcesses(string processName, int keepCount)
    {
        var processes = Process.GetProcessesByName(processName);
        Array.Sort(processes, delegate(Process a, Process b) { return b.StartTime.CompareTo(a.StartTime); });
        int toStop = Math.Max(0, processes.Length - keepCount);
        for (int i = 0; i < toStop; i++)
        {
            try
            {
                if (!processes[i].CloseMainWindow()) processes[i].Kill();
                if (!processes[i].WaitForExit(2000)) processes[i].Kill();
            }
            catch { }
        }
    }

    private static T Field<T>(object owner, string name)
    {
        return (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
    }

    private static void SetField(object owner, string name, object value)
    {
        owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }
}
