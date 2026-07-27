param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$executable = Get-Item -LiteralPath $resolvedExecutable

if ($executable.Extension -ne '.exe') {
    throw "Release candidate must be an executable."
}

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class ShellIconProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
'@

$shell = New-Object -ComObject Shell.Application
$folder = $shell.Namespace($executable.DirectoryName)
if ($null -eq $folder) {
    throw "Windows Shell could not enumerate the release directory."
}

$shellItem = $folder.ParseName($executable.Name)
if ($null -eq $shellItem) {
    throw "Windows Shell could not parse the executable."
}

$null = $shellItem.ExtendedProperty('System.FileDescription')
$null = $shellItem.ExtendedProperty('System.Size')
$null = $shellItem.ExtendedProperty('System.FileVersion')

for ($iteration = 0; $iteration -lt 100; $iteration++) {
    $associatedIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($resolvedExecutable)
    if ($null -eq $associatedIcon) {
        throw "Associated-icon extraction returned no icon."
    }

    $bitmap = $associatedIcon.ToBitmap()
    if ($bitmap.Width -le 0 -or $bitmap.Height -le 0) {
        throw "Associated icon decoded to an invalid bitmap."
    }
    $bitmap.Dispose()
    $associatedIcon.Dispose()

    foreach ($sizeFlag in @(0x0u, 0x1u)) {
        $fileInfo = New-Object ShellIconProbe+SHFILEINFO
        $result = [ShellIconProbe]::SHGetFileInfo(
            $resolvedExecutable,
            0,
            [ref] $fileInfo,
            [Runtime.InteropServices.Marshal]::SizeOf($fileInfo),
            0x100u -bor $sizeFlag)
        if ($result -eq [IntPtr]::Zero -or $fileInfo.hIcon -eq [IntPtr]::Zero) {
            throw "SHGetFileInfo could not extract the executable icon."
        }
        [ShellIconProbe]::DestroyIcon($fileInfo.hIcon) | Out-Null
    }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("WUBT10-ReleaseTest-" + [Guid]::NewGuid().ToString('N'))
$resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
if (-not $resolvedTestRoot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary package test directory resolved outside the system temporary directory."
}

New-Item -ItemType Directory -Path $resolvedTestRoot | Out-Null
try {
    $copiedPath = Join-Path $resolvedTestRoot $executable.Name
    Copy-Item -LiteralPath $resolvedExecutable -Destination $copiedPath
    $renamedName = 'WU-BT10-Windows-Fix-renamed-test.exe'
    Rename-Item -LiteralPath $copiedPath -NewName $renamedName
    $renamedPath = Join-Path $resolvedTestRoot $renamedName

    $diagnosisPath = Join-Path $resolvedTestRoot 'diagnosis.json'
    $diagnosisProcess = Start-Process `
        -FilePath $renamedPath `
        -ArgumentList @('--diagnose-output', $diagnosisPath) `
        -Wait `
        -PassThru
    if ($diagnosisProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $diagnosisPath)) {
        throw "Read-only packaged diagnosis failed."
    }
    Get-Content -Raw -LiteralPath $diagnosisPath | ConvertFrom-Json | Out-Null

    $guiProcess = Start-Process -FilePath $renamedPath -PassThru
    $guiProcess.WaitForInputIdle(15000) | Out-Null
    Start-Sleep -Milliseconds 1500
    $guiProcess.Refresh()
    if ($guiProcess.HasExited -or [string]::IsNullOrWhiteSpace($guiProcess.MainWindowTitle)) {
        throw "Packaged GUI did not open a responsive main window."
    }
    $guiProcess.CloseMainWindow() | Out-Null
    if (-not $guiProcess.WaitForExit(10000)) {
        throw "Packaged GUI did not close cleanly."
    }

    Remove-Item -LiteralPath $diagnosisPath
    Remove-Item -LiteralPath $renamedPath
}
finally {
    if (Test-Path -LiteralPath $resolvedTestRoot) {
        $remaining = Get-ChildItem -LiteralPath $resolvedTestRoot -Force
        if ($remaining.Count -eq 0) {
            Remove-Item -LiteralPath $resolvedTestRoot
        }
    }
}

[pscustomobject]@{
    File = $executable.Name
    Bytes = $executable.Length
    ProductVersion = $executable.VersionInfo.ProductVersion
    Signature = (Get-AuthenticodeSignature -LiteralPath $resolvedExecutable).Status
    SHA256 = (Get-FileHash -LiteralPath $resolvedExecutable -Algorithm SHA256).Hash
    ShellIconExtractions = 200
    AssociatedIconExtractions = 100
    CopyRenameDelete = 'Passed'
    ReadOnlyDiagnosis = 'Passed'
    GuiOpenClose = 'Passed'
}
