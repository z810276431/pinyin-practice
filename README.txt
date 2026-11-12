Pinyin Practice - Windows 拼音打字练习（适配争渡读屏）
=====================================================

功能
- 启动后自动语音播报当前词的读音
- 输入拼音后按 Enter 确认，语音反馈正确/错误
- 正确后自动进入下一组词
- 支持按键重新播报当前读音
- 支持预设词库（words.txt）

运行
1. 双击 build.cmd 编译生成 PinyinPractice.exe（需要 Windows 自带 .NET Framework 4.x 的 csc.exe）
2. 双击 PinyinPractice.exe 运行

快捷键
- Enter：提交答案
- F5 或 Ctrl+R：重新播报当前读音
- Ctrl+N：跳到下一题（不计分，重新播报）
- Tab：在控件间切换

词库格式（words.txt，UTF-8）
每行一条：汉字|拼音
- 拼音可用空格分隔音节，例如：你好|ni hao 或 你好|nǐ hǎo
- 以 # 开头的行为注释，空行忽略
- 程序会忽略声调和大小写进行判定（也接受带声调输入）

争渡读屏说明
- 主要控件已设置 AccessibleName/AccessibleDescription
- 状态变化会更新可聚焦的状态文本并通过系统语音播报
- 建议在输入框内操作，使用 F5 重听读音，Enter 提交
