# Notify Windows after replacing an icon/executable at the same path.
[CmdletBinding()]
param(
    [string]$Path = (Join-Path $PSScriptRoot '../src/TimingAnalyzer.App/bin/Release/net10.0/win-x64/TempoMap.exe'),
    [switch]$RebuildCache,
    [switch]$SkipExplorerRefresh
)
$ErrorActionPreference = 'Stop'
$iconItem = Get-Item -LiteralPath $Path
if ($iconItem.PSIsContainer) { throw 'Specify an executable or icon file.' }
if (-not ('TempoMapShellIcon' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TempoMapShellIcon {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct FileInfo {
        public IntPtr icon; public int index; public uint attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string displayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=80)] public string typeName;
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    public static extern void SHUpdateImage(string path, int index, uint flags, int imageIndex);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    public static extern void SHChangeNotify(int eventId, uint flags, string path, IntPtr unused);
}
'@
}
$cachedIcon = New-Object TempoMapShellIcon+FileInfo
$location = New-Object TempoMapShellIcon+FileInfo
$size = [Runtime.InteropServices.Marshal]::SizeOf($cachedIcon)
# SHGFI_SYSICONINDEX and SHGFI_ICONLOCATION query the existing cache slot and resource.
if ([TempoMapShellIcon]::SHGetFileInfo($iconItem.FullName, 0, [ref]$cachedIcon, $size, 0x4000) -eq [IntPtr]::Zero -or
    [TempoMapShellIcon]::SHGetFileInfo($iconItem.FullName, 0, [ref]$location, $size, 0x1000) -eq [IntPtr]::Zero) {
    throw 'Windows could not resolve the cached icon.'
}
# Updating the file alone does not replace an already-loaded image-list entry.
[TempoMapShellIcon]::SHUpdateImage($location.displayName, $location.index, 0, $cachedIcon.index)
# SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH: tell open views to redraw this item.
[TempoMapShellIcon]::SHChangeNotify(0x2000, 0x1005, $iconItem.FullName, [IntPtr]::Zero)
if ($RebuildCache) {
    # SHCNE_ASSOCCHANGED / SHCNF_IDLIST | SHCNF_FLUSH invalidate the shared image cache.
    [TempoMapShellIcon]::SHChangeNotify(0x8000000, 0x1000, $null, [IntPtr]::Zero)
}
if (-not $SkipExplorerRefresh) {
    $shell = New-Object -ComObject Shell.Application
    foreach ($window in @($shell.Windows())) {
        try {
            if ($window.Document.Folder.Self.Path -eq $iconItem.DirectoryName) { $window.Refresh() }
        } catch { Write-Verbose 'Explorer view unavailable; the item notification was already sent.' }
    }
}
Write-Output "Icon refreshed: $($iconItem.FullName)"
