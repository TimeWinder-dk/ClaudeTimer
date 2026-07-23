# Best-effort taskbar pin for ClaudeTimer.
# Windows 10 1903+ and Windows 11 block programmatic taskbar pinning, so this
# may silently do nothing. In that case, use the Start-menu shortcut and
# right-click -> "Fastgør til proceslinjen".
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetPath
)

$ErrorActionPreference = 'SilentlyContinue'

try {
    $dir = Split-Path -Parent $TargetPath
    $leaf = Split-Path -Leaf $TargetPath
    $shell = New-Object -ComObject Shell.Application
    $folder = $shell.Namespace($dir)
    $item = $folder.ParseName($leaf)

    # Look for a verb whose name matches "pin to taskbar" in the current UI language.
    $verb = $item.Verbs() | Where-Object {
        $_.Name -replace '&', '' -match 'taskbar|proceslinje'
    } | Select-Object -First 1

    if ($verb) {
        $verb.DoIt()
    }
}
catch {
    # Ignore: pinning is not guaranteed to be available.
}
