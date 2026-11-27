<#
.SYNOPSIS
    Publish tay các gói npm của rbtui hoặc rb-server (khởi tạo gói trước khi gắn Trusted Publisher).
    Bản theo tham số -Tool của publish-first.ps1 (rbctl).

.DESCRIPTION
    Build binary cho mọi nền tảng chưa có version này trên npm → đặt version → npm publish
    gói nền tảng rồi gói chính (gói đã có version thì bỏ qua) → khôi phục package.json. Chạy từ bất kỳ đâu trong repo, trên Windows có .NET 10 SDK + Node.
    Đăng nhập npm (và OTP nếu bật 2FA) do bạn nhập trực tiếp trong terminal — script không lưu gì.

.PARAMETER Tool
    rbtui | rb-server | router-balancing (gói gộp — publish SAU khi rbctl, rbtui, rb-server đã có cùng version)

.PARAMETER Version
    Version publish. Mặc định = ApplicationDisplayVersion trong router-balancing.csproj.

.PARAMETER DryRun
    Chạy toàn bộ nhưng npm publish --dry-run (không đẩy lên npm).

.EXAMPLE
    pwsh npm/scripts/publish-first-tool.ps1 -Tool rbtui -DryRun
    pwsh npm/scripts/publish-first-tool.ps1 -Tool rb-server
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('rbtui', 'rb-server', 'router-balancing')][string]$Tool,
    [string]$Version,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Require-Command([string]$name, [string]$hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Thiếu '$name'. $hint"
    }
}

# Windows PowerShell 5.1 biến stderr của lệnh native thành lỗi terminating khi
# ErrorActionPreference=Stop — các lệnh "thử" (whoami, view) chạy với Continue.
function Invoke-Quiet([scriptblock]$block) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $block 2>$null } finally { $ErrorActionPreference = $saved }
}

function Invoke-Checked([scriptblock]$block, [string]$what) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what thất bại (exit $LASTEXITCODE)." }
}

Require-Command 'dotnet' 'Cài .NET 10 SDK: https://dotnet.microsoft.com/download'
Require-Command 'node' 'Cài Node.js 20+: https://nodejs.org'
Require-Command 'npm' 'Cài Node.js 20+: https://nodejs.org'

if (-not $Version) {
    $node = Select-Xml -Path 'router-balancing/router-balancing.csproj' -XPath '//ApplicationDisplayVersion' |
        Select-Object -First 1
    $Version = if ($node) { $node.Node.InnerText.Trim() } else { $null }
    if (-not $Version) { throw 'Không đọc được ApplicationDisplayVersion — truyền -Version.' }
}
$Version = $Version.TrimStart('v')

$project = @{ 'rbtui' = 'src/RouterBalancing.Tui/RouterBalancing.Tui.csproj'; 'rb-server' = 'src/RouterBalancing.Server/RouterBalancing.Server.csproj' }[$Tool]

# Mọi gói nền tảng (npm/<tool>-<os>-<cpu>, rid trong config.dotnetRid) + gói chính — gói chính CUỐI
$packages = @(Get-ChildItem npm -Directory -Filter "$Tool-*" | Sort-Object Name | ForEach-Object {
    $json = Get-Content (Join-Path $_.FullName 'package.json') -Raw | ConvertFrom-Json
    @{ Dir = "npm/$($_.Name)"; Name = $json.name; Rid = $json.config.dotnetRid; Binary = ($json.files | Where-Object { $_ -like 'bin/*' } | Select-Object -First 1) }
})
$packages += @{ Dir = "npm/$Tool"; Name = "@anhbui02/$Tool"; Rid = $null; Binary = $null }
$packageJsons = $packages | ForEach-Object { "$($_.Dir)/package.json" }

Write-Host "Publish $Tool $Version lên npm$(if ($DryRun) { ' (DRY RUN)' })" -ForegroundColor Green

# Sao lưu nguyên byte package.json để khôi phục ở cuối — không dựa vào git vì file có thể
# chưa được commit (git checkout không khôi phục được file untracked).
$backups = @{}
foreach ($file in $packageJsons) {
    $full = Join-Path $repo $file
    $backups[$full] = [IO.File]::ReadAllBytes($full)
}

Step 'Kiểm tra đăng nhập npm'
$user = Invoke-Quiet { npm whoami }
if (-not $user) {
    Write-Host 'Chưa đăng nhập — mở npm login (làm theo hướng dẫn trên trình duyệt/terminal).'
    Invoke-Checked { npm login } 'npm login'
    $user = (npm whoami)
}
Write-Host "Đăng nhập: $user"

# Gói đã có version này thì bỏ qua — chạy lại được sau lỗi giữa chừng, và dùng được để
# khởi tạo gói nền tảng mới (trusted publisher chỉ gắn được vào gói đã tồn tại).
Step "Kiểm tra gói nào đã có $Version trên npm"
$pending = @()
foreach ($p in $packages) {
    $existing = Invoke-Quiet { npm view "$($p.Name)@$Version" version }
    if ($existing) { Write-Host "  bỏ qua  $($p.Name)@$Version (đã có)" }
    else { Write-Host "  publish $($p.Name)@$Version"; $pending += $p }
}
if ($pending.Count -eq 0) {
    Write-Host "`nMọi gói đã có $Version — không có gì để publish." -ForegroundColor Yellow
    exit 0
}

$hostRid = "win-$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())"

try {
    foreach ($p in ($pending | Where-Object { $_.Rid })) {
        Step "Build $($p.Rid) (single-file, self-contained)"
        if ($p.Rid -like 'osx-*') {
            Write-Warning 'Binary macOS build từ Windows chưa được ký ad-hoc — dùng để khởi tạo gói; bản phát hành thật do CI build trên runner macOS.'
        }
        $bin = "$($p.Dir)/bin"
        New-Item -ItemType Directory -Force $bin | Out-Null
        Invoke-Checked {
            dotnet publish $project -c Release -r $p.Rid --self-contained `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
                -o $bin --nologo
        } "dotnet publish $($p.Rid)"
        $binaryPath = Join-Path $p.Dir $p.Binary
        if (-not (Test-Path $binaryPath)) { throw "Thiếu $binaryPath sau khi publish." }
        # Chỉ chạy thử được binary cùng kiến trúc với máy này
        if ($p.Rid -eq $hostRid) { Invoke-Checked { & $binaryPath --version } "$($p.Rid) --version" }
    }

    Step "Đặt version $Version"
    Invoke-Checked { node npm/scripts/set-version-tool.mjs $Tool $Version } 'set-version'

    Step 'Test launcher'
    Push-Location "npm/$Tool"
    try { Invoke-Checked { npm test } 'npm test' } finally { Pop-Location }

    # Gói nền tảng TRƯỚC: gói chính ghim optionalDependencies vào đúng version này
    foreach ($p in $pending) {
        Step "npm publish $($p.Name)@$Version"
        Push-Location $p.Dir
        try {
            $publishArgs = @('publish', '--access', 'public')
            if ($Version -like '*-*') { $publishArgs += @('--tag', 'next') }
            if ($DryRun) { $publishArgs += '--dry-run' }
            Invoke-Checked { npm @publishArgs } "npm publish $($p.Name)"
        }
        finally { Pop-Location }
    }
}
finally {
    Step 'Khôi phục package.json về bản trước khi chạy'
    foreach ($entry in $backups.GetEnumerator()) {
        [IO.File]::WriteAllBytes($entry.Key, $entry.Value)
    }
}

if (-not $DryRun) {
    Step 'Kiểm tra trên registry'
    foreach ($p in $packages) { Write-Host "$($p.Name): $(Invoke-Quiet { npm view "$($p.Name)@$Version" version })" }
}

Write-Host "`nXong." -ForegroundColor Green
if (-not $DryRun) {
    Write-Host @"
Việc tiếp theo (một lần):
  1. npmjs.com → TỪNG gói @anhbui02/$Tool* → Settings → Trusted Publisher → GitHub Actions:
     ngocanh0202 / router-balancing / workflow npm-tools.yml / environment npm
  2. GitHub → Settings → Environments → tạo 'npm'
  3. Thử: npm install -g @anhbui02/$Tool ; $Tool --version
"@
}
