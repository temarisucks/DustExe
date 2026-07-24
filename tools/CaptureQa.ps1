param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\publish\Dust.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\qa'),
    [int]$DroneIndex = -1,
    [int]$CoreColorIndex = -1,
    [int]$FrameColorIndex = -1,
    [int]$TogglePerkIndex = -1,
    [int]$RunMapSizeIndex = -1,
    [int]$RunStrictnessIndex = -1,
    [int]$RunHollowAmountIndex = -1,
    [string]$DisableHollowTypeIndexes = '',
    [switch]$DisableDifficultyScaling,
    [switch]$PickupCargo,
    [switch]$ActivatePerk,
    [switch]$CaptureMissionLog,
    [int]$MissionLogHoldMs = 180,
    [switch]$CaptureLoading,
    [int]$WanderSteps = 0,
    [int]$EncounterFrames = 0,
    [int]$IdleFrames = 0,
    [int]$IdleDelayMs = 120,
    [int]$StepDelayMs = 105,
    [int]$ChamberDelayMs = 1800
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class DustWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
'@

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$previousSettingsOverride = $env:DUST_SETTINGS_FILE
$env:DUST_SETTINGS_FILE = [IO.Path]::GetFullPath((Join-Path $OutputDirectory 'qa-settings.json'))
$process = Start-Process -FilePath (Resolve-Path $ExePath) -WindowStyle Hidden -PassThru
try {
    $handle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 30 -and $handle -eq [IntPtr]::Zero; $attempt++) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        $handle = $process.MainWindowHandle
    }
    if ($handle -eq [IntPtr]::Zero) { throw 'Dust window was not created.' }

    function Save-DustWindow([string]$Path) {
        $rect = New-Object DustWindowCapture+RECT
        [DustWindowCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
        $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $hdc = $graphics.GetHdc()
        try { [DustWindowCapture]::PrintWindow($handle, $hdc, 2) | Out-Null }
        finally { $graphics.ReleaseHdc($hdc); $graphics.Dispose() }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }

    function Send-DustClick([int]$DesignX, [int]$DesignY) {
        $rect = New-Object DustWindowCapture+RECT
        [DustWindowCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        $scale = [Math]::Min($width / 1280.0, $height / 800.0)
        $offsetX = ($width - 1280 * $scale) / 2
        $offsetY = ($height - 800 * $scale) / 2
        $clientX = [int][Math]::Round($offsetX + $DesignX * $scale)
        $clientY = [int][Math]::Round($offsetY + $DesignY * $scale)
        $mousePosition = [IntPtr](($clientY -shl 16) -bor ($clientX -band 0xffff))
        [DustWindowCapture]::SendMessage($handle, 0x0201, [IntPtr]1, $mousePosition) | Out-Null
        [DustWindowCapture]::SendMessage($handle, 0x0202, [IntPtr]0, $mousePosition) | Out-Null
    }

    Start-Sleep -Milliseconds 400
    Save-DustWindow (Join-Path $OutputDirectory 'title.png')

    Send-DustClick 988 396
    Start-Sleep -Milliseconds 180
    if ($DroneIndex -ge 0 -and $DroneIndex -le 4) {
        $droneX = @(574, 705, 837, 969, 1100)[$DroneIndex]
        Send-DustClick $droneX 267
        Start-Sleep -Milliseconds 120
    }
    function Select-DustColor([int]$PaintPartX, [int]$ColorIndex) {
        if ($ColorIndex -lt 0 -or $ColorIndex -gt 11) { return }
        Send-DustClick $PaintPartX 418
        Start-Sleep -Milliseconds 80
        $colorX = 562 + 110 * ($ColorIndex % 6)
        $colorY = if ($ColorIndex -lt 6) { 528 } else { 590 }
        Send-DustClick $colorX $colorY
        Start-Sleep -Milliseconds 100
    }
    Select-DustColor 672 $CoreColorIndex
    Select-DustColor 1002 $FrameColorIndex
    Save-DustWindow (Join-Path $OutputDirectory 'customize.png')

    Send-DustClick 166 694
    Start-Sleep -Milliseconds 120
    Send-DustClick 988 580
    Start-Sleep -Milliseconds 180
    Save-DustWindow (Join-Path $OutputDirectory 'settings.png')

    Send-DustClick 166 694
    Start-Sleep -Milliseconds 120
    Send-DustClick 988 488
    Start-Sleep -Milliseconds 180
    Save-DustWindow (Join-Path $OutputDirectory 'achievements.png')
    Send-DustClick 552 142
    Start-Sleep -Milliseconds 140
    Save-DustWindow (Join-Path $OutputDirectory 'perks.png')
    if ($TogglePerkIndex -ge 0 -and $TogglePerkIndex -le 7) {
        if ($TogglePerkIndex -lt 7) {
            Send-DustClick 300 (227 + 57 * $TogglePerkIndex)
        }
        else {
            for ($index = 0; $index -lt $TogglePerkIndex; $index++) {
                [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]0x28, [IntPtr]0) | Out-Null
                [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]0x28, [IntPtr]0) | Out-Null
                Start-Sleep -Milliseconds 30
            }
        }
        Start-Sleep -Milliseconds 100
        Send-DustClick 950 588
        Start-Sleep -Milliseconds ([Math]::Max(0, $MissionLogHoldMs))
        Save-DustWindow (Join-Path $OutputDirectory 'perks-toggled.png')
    }
    Send-DustClick 166 694
    Start-Sleep -Milliseconds 120
    Send-DustClick 988 212
    Start-Sleep -Milliseconds 180
    function Advance-DustRunOption([int]$Current, [int]$Target, [int]$Count, [int]$PlusX, [int]$PlusY) {
        if ($Target -lt 0 -or $Target -ge $Count) { return }
        $steps = ($Target - $Current + $Count) % $Count
        for ($step = 0; $step -lt $steps; $step++) {
            Send-DustClick $PlusX $PlusY
            Start-Sleep -Milliseconds 60
        }
    }
    Advance-DustRunOption 1 $RunMapSizeIndex 3 545 254
    Advance-DustRunOption 1 $RunStrictnessIndex 3 545 358
    Advance-DustRunOption 2 $RunHollowAmountIndex 4 545 462
    $disabledTypes = @($DisableHollowTypeIndexes -split '[,; ]+' |
        Where-Object { $_ -match '^[0-6]$' } | ForEach-Object { [int]$_ } | Select-Object -Unique)
    if ($disabledTypes.Count -gt 6) { $disabledTypes = $disabledTypes[0..5] }
    foreach ($typeIndex in $disabledTypes) {
        Send-DustClick 1090 (210 + 49 * $typeIndex)
        Start-Sleep -Milliseconds 60
    }
    if ($DisableDifficultyScaling) {
        Send-DustClick 520 548
        Start-Sleep -Milliseconds 60
    }
    Save-DustWindow (Join-Path $OutputDirectory 'run-settings.png')
    Send-DustClick 1027 681
    if ($CaptureLoading) {
        Start-Sleep -Milliseconds 70
        Save-DustWindow (Join-Path $OutputDirectory 'loading.png')
    }
    Start-Sleep -Milliseconds $ChamberDelayMs
    Save-DustWindow (Join-Path $OutputDirectory 'chamber.png')
    if ($CaptureMissionLog) {
        [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]0x51, [IntPtr]0) | Out-Null
        [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]0x51, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 180
        Save-DustWindow (Join-Path $OutputDirectory 'mission-dossier-q.png')

        # Playing inputs must be swallowed while the physical file is open.
        foreach ($key in @(0x57, 0x45, 0x20, 0x52)) {
            [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]$key, [IntPtr]0) | Out-Null
            [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]$key, [IntPtr]0) | Out-Null
        }
        Start-Sleep -Milliseconds 180
        Save-DustWindow (Join-Path $OutputDirectory 'mission-dossier-input-lock.png')

        [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]0x51, [IntPtr]0) | Out-Null
        [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]0x51, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 100
        Send-DustClick 462 95
        Start-Sleep -Milliseconds 180
        Save-DustWindow (Join-Path $OutputDirectory 'mission-dossier-click.png')
        Send-DustClick 1063 118
        Start-Sleep -Milliseconds 120
        Save-DustWindow (Join-Path $OutputDirectory 'mission-dossier-closed.png')
    }
    if ($ActivatePerk) {
        [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]0x20, [IntPtr]0) | Out-Null
        [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]0x20, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 250
        Save-DustWindow (Join-Path $OutputDirectory 'perk-active.png')
    }
    if ($PickupCargo) {
        [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]0x45, [IntPtr]0) | Out-Null
        [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]0x45, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 250
        Save-DustWindow (Join-Path $OutputDirectory 'cargo-carried.png')
    }

    for ($frame = 1; $frame -le $IdleFrames; $frame++) {
        Start-Sleep -Milliseconds $IdleDelayMs
        Save-DustWindow (Join-Path $OutputDirectory ("idle-{0:00}.png" -f $frame))
    }

    if ($WanderSteps -gt 0) {
        $movementKeys = @(0x57, 0x41, 0x53, 0x44)
        $captureEvery = if ($EncounterFrames -gt 0) {
            [Math]::Max(1, [Math]::Floor($WanderSteps / $EncounterFrames))
        } else { $WanderSteps + 1 }
        $frame = 0
        for ($step = 1; $step -le $WanderSteps; $step++) {
            $key = Get-Random -InputObject $movementKeys
            [DustWindowCapture]::SendMessage($handle, 0x0100, [IntPtr]$key, [IntPtr]0) | Out-Null
            [DustWindowCapture]::SendMessage($handle, 0x0101, [IntPtr]$key, [IntPtr]0) | Out-Null
            Start-Sleep -Milliseconds $StepDelayMs
            if ($EncounterFrames -gt 0 -and ($step % $captureEvery -eq 0 -or $step -eq $WanderSteps)) {
                $frame++
                Save-DustWindow (Join-Path $OutputDirectory ("encounter-{0:00}.png" -f $frame))
            }
        }
    }
}
finally {
    if (-not $process.HasExited -and $handle -ne [IntPtr]::Zero) {
        [DustWindowCapture]::SendMessage($handle, 0x0010, [IntPtr]0, [IntPtr]0) | Out-Null
        $process.WaitForExit(3000) | Out-Null
    }
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    $env:DUST_SETTINGS_FILE = $previousSettingsOverride
}
