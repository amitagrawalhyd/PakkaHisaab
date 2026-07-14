# Used by azure_free_deploy.sh on Windows (Git Bash ships no `zip`, and
# Compress-Archive / ZipFile.CreateFromDirectory both store backslash path
# separators for nested folders under Windows PowerShell's .NET Framework —
# Kudu's Linux-side rsync can't stat those, so the deploy fails silently.
# This walks files manually and forces forward-slash entry names.
param(
    [Parameter(Mandatory = $true)][string]$SourceDir,
    [Parameter(Mandatory = $true)][string]$DestZip
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path $DestZip) { Remove-Item $DestZip -Force }

$resolvedSource = (Resolve-Path $SourceDir).Path
$zip = [System.IO.Compression.ZipFile]::Open($DestZip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -Path $resolvedSource -Recurse -File | ForEach-Object {
        $relPath = $_.FullName.Substring($resolvedSource.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $relPath) | Out-Null
    }
}
finally {
    $zip.Dispose()
}
