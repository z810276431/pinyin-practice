$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$script:Token = $null
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$script:Token = ($cred | Where-Object { $_ -like "password=*" }) -replace "password=",""

function Invoke-GitHubApi {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Body
    )
    $headers = @{
        Authorization  = "token $script:Token"
        'User-Agent'   = 'dbg'
        Accept         = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    if ($PSBoundParameters.ContainsKey('Body') -and $Body) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
}

$base = "https://api.github.com/repos/z810276431/pinyin-practice"
$release = Invoke-GitHubApi -Method Get -Uri "$base/releases/tags/v1.1.0"
Write-Host ("releaseId=" + $release.id)
$raw = Invoke-GitHubApi -Method Get -Uri "$base/releases/$($release.id)/assets"
Write-Host ("rawNull=" + ($null -eq $raw))
if ($null -ne $raw) { Write-Host ("rawType=" + $raw.GetType().FullName) }
$assets = @($raw | Where-Object { $null -ne $_ })
Write-Host ("count=" + $assets.Count)
foreach ($existing in $assets) {
    Write-Host ("type=" + $existing.GetType().FullName)
    $name = [string]$existing.name
    Write-Host ("name=" + $name)
}
Write-Host 'DONE'
