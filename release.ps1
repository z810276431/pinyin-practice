#Requires -Version 5.1
<#
.SYNOPSIS
    一键发版：编译 -> 测试 -> 打包 -> 提交并推送 GitHub -> 创建/更新 Release 并替换附件。

.DESCRIPTION
    默认行为（不带参数）：
      1. 检查仓库与 GitHub 凭据
      2. 运行 build.cmd 编译三个程序
      3. 运行 test.cmd 执行全部逻辑与键盘回归测试（-SkipTests 可跳过）
      4. 打包 dist\PracticeToolkit-<版本>-win.zip
      5. git add -A 并提交（无变更则跳过）
      6. 推送分支、创建并推送标签
      7. 创建或更新 GitHub Release，替换同名附件

    GitHub 令牌优先取环境变量 GH_TOKEN / GITHUB_TOKEN，
    否则从 git credential 读取 github.com 的凭据（Windows 凭据管理器）。

.PARAMETER Version
    版本号，可写 v1.2.0 或 1.2.0。不填则在最新标签上递增修订号；
    没有任何标签时使用 1.0.0。

.PARAMETER Message
    提交说明。不填则使用 "Release <标签>"。

.PARAMETER Notes
    Release 说明中“本次更新”的内容，支持多行文本。

.PARAMETER NotesFile
    从文件读取“本次更新”内容（UTF-8）。

.PARAMETER SkipTests
    跳过 test.cmd（不推荐）。

.PARAMETER SkipBuild
    跳过编译与测试，直接用现有 exe 打包。

.PARAMETER Force
    标签已存在且指向其他提交时，强制移动标签并覆盖推送。

.PARAMETER DryRun
    只编译、测试、打包并打印计划，不做提交、推送或 Release 操作。

.PARAMETER Yes
    预留参数：脚本默认不询问确认。

.EXAMPLE
    release.cmd

.EXAMPLE
    release.cmd -Version v1.2.0 -Notes "新增乘法高级练习"

.EXAMPLE
    release.cmd -DryRun
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Message,
    [string]$Notes,
    [string]$NotesFile,
    [switch]$SkipTests,
    [switch]$SkipBuild,
    [switch]$Force,
    [switch]$DryRun,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:Token = $null
$script:Slug = $null
$script:StepNo = 0

# ============================================================ 工具函数

function Write-Step {
    param([string]$Text)
    $script:StepNo++
    Write-Host ''
    Write-Host ("[{0}] {1}" -f $script:StepNo, $Text) -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Text)
    Write-Host "    $Text"
}

function Fail {
    param([string]$Text)
    Write-Host ''
    Write-Host "[失败] $Text" -ForegroundColor Red
    exit 1
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    # PowerShell 5.1 会把 git 写到 stderr 的提示（如 Everything up-to-date）
    # 在 ErrorActionPreference=Stop 下转成终止性错误，因此这里临时放宽并只看退出码。
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @GitArgs 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) {
        $text = (@($output) | ForEach-Object { [string]$_ }) -join "`n"
        throw ("git " + ($GitArgs -join ' ') + " 失败（退出码 $code）：`n" + $text)
    }
    return @($output)
}

function Invoke-GitQuiet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git @GitArgs *> $null
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Invoke-GitHubApi {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Body
    )
    $headers = @{
        Authorization  = "token $script:Token"
        'User-Agent'   = 'practice-toolkit-release-script'
        Accept         = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    if ($PSBoundParameters.ContainsKey('Body') -and $Body) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
}

function Get-GitHubToken {
    if ($env:GH_TOKEN -and $env:GH_TOKEN.Trim()) { return $env:GH_TOKEN.Trim() }
    if ($env:GITHUB_TOKEN -and $env:GITHUB_TOKEN.Trim()) { return $env:GITHUB_TOKEN.Trim() }

    $previous = $env:GIT_TERMINAL_PROMPT
    $env:GIT_TERMINAL_PROMPT = '0'
    try {
        $answer = "protocol=https`nhost=github.com`n`n" | & git credential fill 2>$null
    }
    catch {
        $answer = @()
    }
    finally {
        $env:GIT_TERMINAL_PROMPT = $previous
    }

    foreach ($line in @($answer)) {
        if ([string]$line -like 'password=*') {
            return ([string]$line).Substring('password='.Length).Trim()
        }
    }
    Fail '未找到 GitHub 凭据。请先运行 git credential approve，或设置环境变量 GH_TOKEN。'
}

function Get-RepoSlug {
    $url = @(Invoke-Git remote get-url origin)
    if ($url.Count -eq 0) { Fail '当前仓库没有 origin 远端。' }
    $text = [string]$url[0]
    $match = [regex]::Match($text, 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$')
    if (-not $match.Success) { Fail "无法从远端地址识别 GitHub 仓库：$text" }
    return ($match.Groups['owner'].Value + '/' + $match.Groups['repo'].Value)
}

function ConvertTo-VersionText {
    param([string]$Text)
    $clean = $Text.Trim().TrimStart('v', 'V')
    $parsed = [version]'0.0.0'
    if (-not [version]::TryParse($clean, [ref]$parsed)) {
        Fail "版本号格式不正确：$Text（应形如 v1.2.0）"
    }
    return $clean
}

function Get-LatestVersionText {
    # 同时参考本地与远端标签，避免远端已有版本被误判为首次发版。
    $tags = @(Invoke-Git tag --list 'v*')
    foreach ($remoteLine in @(Invoke-Git ls-remote --tags --refs origin 'v*')) {
        $text = [string]$remoteLine
        $index = $text.LastIndexOf('refs/tags/')
        if ($index -lt 0) { continue }
        $tags += $text.Substring($index + 'refs/tags/'.Length).Trim()
    }

    $best = $null
    $bestText = $null
    foreach ($tag in $tags) {
        $clean = ([string]$tag).TrimStart('v', 'V')
        $parsed = [version]'0.0.0'
        if (-not [version]::TryParse($clean, [ref]$parsed)) { continue }
        if ($null -eq $best -or $parsed -gt $best) {
            $best = $parsed
            $bestText = $clean
        }
    }
    return $bestText
}

function New-PlannedVersion {
    $latest = Get-LatestVersionText
    if (-not $latest) { return '1.0.0' }
    $parsed = [version]$latest
    return ('{0}.{1}.{2}' -f $parsed.Major, $parsed.Minor, ($parsed.Build + 1))
}

function Get-Sha256 {
    param([string]$Path)
    # 直接使用 .NET 计算，避免依赖 Get-FileHash（在部分 PowerShell 环境中不可用）。
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = $sha.ComputeHash($stream)
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
    $builder = New-Object System.Text.StringBuilder
    foreach ($byte in $hash) { [void]$builder.Append($byte.ToString('x2')) }
    return $builder.ToString()
}

function New-ReleaseNotes {
    param(
        [string]$Tag,
        [string]$Notes,
        [string]$NotesFile,
        [string]$ZipName,
        [string]$Sha256
    )

    $updateText = $Notes
    if (-not $updateText -and $NotesFile) {
        if (-not (Test-Path $NotesFile)) { Fail "找不到说明文件：$NotesFile" }
        $updateText = Get-Content -Path $NotesFile -Encoding UTF8 -Raw
    }
    if (-not $updateText) {
        $updateText = '维护性更新：优化使用体验与稳定性。'
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("## 学习练习小工具整合包 $Tag")
    $lines.Add('')
    $lines.Add('适配读屏的 Windows 学习练习工具，一个入口选择不同练习。')
    $lines.Add('')
    $lines.Add('### 本次更新')
    $lines.Add('')
    $lines.Add($updateText.Trim())
    $lines.Add('')
    $lines.Add('### 包含工具')
    $lines.Add('')
    $lines.Add('| 程序 | 说明 |')
    $lines.Add('| --- | --- |')
    $lines.Add('| PracticeToolkit.exe | 整合包启动器，选择要练习的工具 |')
    $lines.Add('| PinyinPractice.exe | 拼音打字练习，可独立运行 |')
    $lines.Add('| ArithmeticPractice.exe | 数学加减乘除算术练习，可独立运行 |')
    $lines.Add('')
    $lines.Add('### 下载与安装')
    $lines.Add('')
    $lines.Add(('1. 下载 `{0}` 并解压到任意目录。' -f $ZipName))
    $lines.Add('2. 双击 `PracticeToolkit.exe`，选择要练习的工具。')
    $lines.Add('3. 三个 exe 必须在同一目录；也可以直接运行单个练习程序。')
    $lines.Add('')
    $lines.Add('### 运行要求')
    $lines.Add('')
    $lines.Add('- Windows 7 及以上')
    $lines.Add('- .NET Framework 4.x（Windows 自带）')
    $lines.Add('- 建议安装中文系统语音（简体中文 Windows 默认自带）')
    $lines.Add('')
    $lines.Add('### 快捷键')
    $lines.Add('')
    $lines.Add('| 按键 | 功能 |')
    $lines.Add('| --- | --- |')
    $lines.Add('| Enter / 空格 | 确认答案 |')
    $lines.Add('| F5 / Ctrl+R | 重听当前题目或读音 |')
    $lines.Add('| Ctrl+N | 下一题 / 换一题 |')
    $lines.Add('')
    $lines.Add('### 校验')
    $lines.Add('')
    $lines.Add(('安装包 `{0}` 的 SHA256：' -f $ZipName))
    $lines.Add('')
    $lines.Add('```')
    $lines.Add($Sha256)
    $lines.Add('```')
    return ($lines -join "`n")
}

# ============================================================ 主流程

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
Push-Location $root

try {
    Write-Host ''
    Write-Host '学习练习小工具整合包 · 一键发版' -ForegroundColor Green
    Write-Host "仓库：$root"

    if ((Invoke-GitQuiet rev-parse --is-inside-work-tree) -ne 0) {
        Fail "当前目录不是 git 仓库：$root"
    }

    # ---------- 1. 版本号与仓库
    Write-Step '确定版本与仓库'
    if (-not $Version) {
        $Version = New-PlannedVersion
        Write-Info "未指定版本，按最新标签递增修订号：v$Version"
    }
    else {
        $Version = ConvertTo-VersionText $Version
        Write-Info "使用指定版本：v$Version"
    }
    $tag = 'v' + $Version
    $zipName = "PracticeToolkit-$tag-win.zip"
    $script:Slug = Get-RepoSlug
    Write-Info "标签：$tag"
    Write-Info "GitHub 仓库：$script:Slug"
    Write-Info "安装包：$zipName"

    if (-not $Message) { $Message = "Release $tag" }

    # ---------- 2. 凭据（dry run 也校验，提前暴露问题）
    Write-Step '检查 GitHub 凭据'
    $script:Token = Get-GitHubToken
    try {
        $who = Invoke-GitHubApi -Method Get -Uri 'https://api.github.com/user'
        Write-Info "已认证为：$($who.login)"
        $repoInfo = Invoke-GitHubApi -Method Get -Uri "https://api.github.com/repos/$script:Slug"
        Write-Info "仓库可写检查：$($repoInfo.full_name)（私有：$($repoInfo.private)）"
    }
    catch {
        Fail "GitHub API 访问失败：$($_.Exception.Message)"
    }

    # ---------- 3. 编译
    Write-Step '编译程序'
    if ($SkipBuild) {
        Write-Info '已指定 -SkipBuild，跳过编译与测试。'
    }
    else {
        & cmd /c "`"$root\build.cmd`""
        if ($LASTEXITCODE -ne 0) { Fail '编译失败，已中止发版。' }
    }

    $exes = @('PracticeToolkit.exe', 'PinyinPractice.exe', 'ArithmeticPractice.exe')
    foreach ($exe in $exes) {
        $path = Join-Path $root $exe
        if (-not (Test-Path $path)) { Fail "缺少可执行文件：$exe。请先完整编译。" }
        Write-Info ("{0}  {1:N0} 字节" -f $exe, (Get-Item $path).Length)
    }

    # ---------- 4. 测试
    Write-Step '运行测试'
    if ($SkipBuild -or $SkipTests) {
        Write-Info '已跳过测试。'
    }
    else {
        & cmd /c "`"$root\test.cmd`""
        if ($LASTEXITCODE -ne 0) { Fail '测试未通过，已中止发版。' }
    }

    # ---------- 5. 打包
    Write-Step '打包安装文件'
    $dist = Join-Path $root 'dist'
    $stage = Join-Path $dist "PracticeToolkit-$tag"
    if (Test-Path $stage) { Remove-Item -Path $stage -Recurse -Force }
    # 旧版安装包不必保留：每次发版都重新生成同名文件。
    if (Test-Path $dist) {
        Get-ChildItem -Path $dist -Filter 'PracticeToolkit-*-win.zip' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    $payload = @(
        'PracticeToolkit.exe',
        'PinyinPractice.exe',
        'ArithmeticPractice.exe',
        'words.txt',
        'README.txt',
        'build.cmd',
        'test.cmd',
        'release.cmd',
        'release.ps1'
    )
    if (Test-Path (Join-Path $root '词库模板.txt')) { $payload += '词库模板.txt' }
    foreach ($item in $payload) {
        $source = Join-Path $root $item
        if (Test-Path $source) { Copy-Item -Path $source -Destination $stage -Force }
    }

    $zipPath = Join-Path $dist $zipName
    if (Test-Path $zipPath) { Remove-Item -Path $zipPath -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipSize = (Get-Item $zipPath).Length
    $zipHash = Get-Sha256 $zipPath
    Write-Info ("已生成 {0}（{1:N0} 字节）" -f $zipName, $zipSize)
    Write-Info "SHA256：$zipHash"

    if ($DryRun) {
        Write-Step '预演结束'
        Write-Host ''
        Write-Host '已指定 -DryRun，未提交、未推送、未创建 Release。' -ForegroundColor Yellow
        Write-Host "计划提交：$Message" -ForegroundColor Yellow
        Write-Host "计划标签：$tag" -ForegroundColor Yellow
        Write-Host "计划附件：$zipName、$($exes -join '、')、words.txt" -ForegroundColor Yellow
        exit 0
    }

    # ---------- 6. 提交
    Write-Step '提交到本地仓库'
    & git add -A
    if ($LASTEXITCODE -ne 0) { Fail 'git add 失败。' }

    $staged = @(& git diff --cached --name-only 2>$null)
    if ($staged.Count -eq 0) {
        Write-Info '没有需要提交的变更，跳过提交。'
    }
    else {
        Write-Info ("待提交文件 {0} 个：" -f $staged.Count)
        foreach ($file in $staged) { Write-Info "  $file" }
        Invoke-Git commit -m $Message | Out-Null
        Write-Info '已创建提交。'
    }

    $headBranch = (@(Invoke-Git rev-parse --abbrev-ref HEAD))[0].Trim()
    $headCommit = (@(Invoke-Git rev-parse HEAD))[0].Trim()
    Write-Info "分支：$headBranch"
    Write-Info "提交：$headCommit"

    # ---------- 7. 标签
    Write-Step "准备标签 $tag"
    $tagExists = ((Invoke-GitQuiet rev-parse -q --verify "refs/tags/$tag") -eq 0)
    if ($tagExists) {
        $tagCommit = (@(Invoke-Git rev-list -n 1 $tag))[0].Trim()
        if ($tagCommit -ne $headCommit) {
            if (-not $Force) {
                Fail "标签 $tag 已存在并指向 $tagCommit，不是当前提交。请换版本号，或加 -Force 强制移动。"
            }
            Invoke-Git tag -f -a $tag -m "Release $tag" | Out-Null
            Write-Info '已强制移动标签到当前提交。'
        }
        else {
            Write-Info '标签已指向当前提交。'
        }
    }
    else {
        Invoke-Git tag -a $tag -m "Release $tag" | Out-Null
        Write-Info '已创建标签。'
    }

    # ---------- 8. 推送
    Write-Step '推送到 GitHub'
    $pushArgs = @('push', 'origin', "$headBranch`:$headBranch")
    if ($Force) { $pushArgs += '--force-with-lease' }
    Invoke-Git @pushArgs | Out-Null
    Write-Info "已推送分支 $headBranch。"

    $tagPushArgs = @('push', 'origin', "refs/tags/$tag")
    if ($Force) { $tagPushArgs += '--force' }
    Invoke-Git @tagPushArgs | Out-Null
    Write-Info "已推送标签 $tag。"

    # ---------- 9. Release
    Write-Step '创建或更新 GitHub Release'
    $base = "https://api.github.com/repos/$script:Slug"
    $release = $null
    try {
        $release = Invoke-GitHubApi -Method Get -Uri "$base/releases/tags/$tag"
        Write-Info '已存在同一标签的 Release，将更新说明与附件。'
    }
    catch {
        $release = $null
    }

    $notesBody = New-ReleaseNotes -Tag $tag -Notes $Notes -NotesFile $NotesFile -ZipName $zipName -Sha256 $zipHash
    if (-not $release) {
        $payloadJson = @{
            tag_name         = $tag
            target_commitish = $headBranch
            name             = "学习练习小工具整合包 $tag"
            body             = $notesBody
            draft            = $false
            prerelease       = $false
        } | ConvertTo-Json -Depth 5
        $release = Invoke-GitHubApi -Method Post -Uri "$base/releases" -Body $payloadJson
        Write-Info "已创建 Release #$($release.id)。"
    }
    else {
        $payloadJson = @{
            tag_name   = $tag
            name       = "学习练习小工具整合包 $tag"
            body       = $notesBody
            draft      = $false
            prerelease = $false
        } | ConvertTo-Json -Depth 5
        $release = Invoke-GitHubApi -Method Patch -Uri "$base/releases/$($release.id)" -Body $payloadJson
        Write-Info "已更新 Release #$($release.id)。"
    }

    # 同名旧附件先删除，保证附件与本次构建一致
    $uploadFiles = @(@{ Path = $zipPath; Name = $zipName })
    foreach ($exe in $exes) { $uploadFiles += @{ Path = (Join-Path $root $exe); Name = $exe } }
    $wordsPath = Join-Path $root 'words.txt'
    if (Test-Path $wordsPath) { $uploadFiles += @{ Path = $wordsPath; Name = 'words.txt' } }

    $assets = @(Invoke-GitHubApi -Method Get -Uri "$base/releases/$($release.id)/assets")
    foreach ($existing in $assets) {
        foreach ($file in $uploadFiles) {
            if ($existing.name -eq $file.Name) {
                Invoke-GitHubApi -Method Delete -Uri "$base/releases/assets/$($existing.id)" | Out-Null
                Write-Info "已删除旧附件：$($existing.name)"
                break
            }
        }
    }

    # 上传新附件
    $uploadBase = "https://uploads.github.com/repos/$script:Slug/releases/$($release.id)/assets"
    foreach ($file in $uploadFiles) {
        $encodedName = [uri]::EscapeDataString($file.Name)
        $url = "$uploadBase`?name=$encodedName"
        $response = & curl.exe -sS -X POST `
            -H "Authorization: token $script:Token" `
            -H 'User-Agent: practice-toolkit-release-script' `
            -H 'Content-Type: application/octet-stream' `
            --data-binary "@$($file.Path)" $url 2>&1
        $responseText = ($response -join "`n")
        if ($LASTEXITCODE -ne 0) { Fail "上传附件 $($file.Name) 失败：$responseText" }
        try {
            $parsed = $responseText | ConvertFrom-Json
        }
        catch {
            Fail "上传附件 $($file.Name) 返回无法解析：$responseText"
        }
        if ($parsed.state -ne 'uploaded') { Fail "上传附件 $($file.Name) 失败：$responseText" }
        Write-Info ("已上传 {0}（{1:N0} 字节）" -f $parsed.name, $parsed.size)
    }

    # ---------- 10. 结果
    Write-Step '发版完成'
    $final = Invoke-GitHubApi -Method Get -Uri "$base/releases/tags/$tag"
    $finalAssets = @($final.assets)
    Write-Host ''
    Write-Host ("版本：{0}" -f $final.tag_name) -ForegroundColor Green
    Write-Host ("页面：{0}" -f $final.html_url) -ForegroundColor Green
    Write-Host ("SHA256：{0}" -f $zipHash)
    Write-Host ''
    Write-Host '附件：'
    foreach ($asset in $finalAssets) {
        Write-Host ("  - {0}（{1:N0} 字节）" -f $asset.name, $asset.size)
    }
    Write-Host ''
    exit 0
}
catch {
    Write-Host ''
    Write-Host "[失败] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
