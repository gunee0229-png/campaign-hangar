param([Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot 'ui-hub'
$extensions = @('.html','.css','.js','.png','.jpg','.jpeg','.webp','.svg','.ico','.woff','.woff2')
$names = Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } |
    ForEach-Object { 'assets/ui-hub/' + $_.FullName.Substring($root.Length + 1).Replace('\','/') } |
    Sort-Object
$lines = @('namespace CampaignHangar { public static partial class AppUpdate {',
    'static string[] UiFiles() { return new[] {')
$lines += $names | ForEach-Object { '    "' + $_ + '",' }
$lines += '}; } } }'
[System.IO.File]::WriteAllLines($OutputFile, $lines, [System.Text.UTF8Encoding]::new($false))
