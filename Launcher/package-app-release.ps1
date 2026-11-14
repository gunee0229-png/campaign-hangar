param(
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Notes = ""
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$app = (Resolve-Path -LiteralPath $AppDirectory).Path
$exe = Join-Path $app 'CampaignHangar.exe'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
if (-not $version) { throw '앱 실행 파일의 버전이 없습니다.' }
$product = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductName
if ($product -eq 'CampaignHangar.development') { $channel = 'development' }
elseif ($product -eq 'CampaignHangar.release') { $channel = 'release' }
else { throw '개발판·배포판 구분이 없는 실행 파일은 패키징하지 않습니다.' }
$assembly = [System.Reflection.Assembly]::LoadFrom($exe)
$files = $assembly.GetType('CampaignHangar.AppUpdate').GetField('Files').GetValue($null)
$packageUrl = $assembly.GetType('CampaignHangar.AppUpdate').GetMethod('PackageUrl').Invoke($null, @($version))
if ($files.Count -eq 0) { throw '업데이트 파일 목록이 없습니다.' }
foreach ($name in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $app $name) -PathType Leaf)) { throw "필수 파일 없음: $name" }
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$zipPath = Join-Path $output ("CampaignHangar-" + $version + ".zip")
$manifestPath = Join-Path $output 'latest.json'
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $manifestPath)) { throw '기존 릴리스 파일을 덮어쓰지 않습니다. 새 출력 폴더를 선택하세요.' }
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $files) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $app $name), $name) | Out-Null
    }
} finally { $zip.Dispose() }
@{
    version = $version
    channel = $channel
    url = $packageUrl
    sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    size = (Get-Item -LiteralPath $zipPath).Length
    notes = $Notes
} | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "앱 ZIP 생성: $zipPath"
Write-Output "업데이트 정보 생성: $manifestPath"
Write-Output '로컬 파일만 만들었습니다. 공개 배포는 진행하지 않았습니다.'
