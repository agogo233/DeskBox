$ErrorActionPreference = 'Continue'
$proxy = 'D:\project\wingezi\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.ThumbnailProxy.exe'

# Enumerate real folders at runtime - no non-ASCII literals in this file.
$all = Get-ChildItem 'E:\DeskBox' -Directory -Recurse -Depth 1 | Select-Object -First 4
$targets = @($all | ForEach-Object { $_.FullName })
Write-Output ('targets: ' + ($targets -join ' ; '))

function RunOneShot([string]$path) {
    $p = Start-Process -FilePath $proxy -ArgumentList @('--icon-only', $path, '48') -PassThru `
        -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\oneshot.bmp" -RedirectStandardError "$env:TEMP\oneshot.err" -Wait
    $err = (Get-Content "$env:TEMP\oneshot.err" -Raw -ErrorAction SilentlyContinue)
    return ('exit=' + $p.ExitCode + ' err=' + ($err -replace "`r`n", ' '))
}

function RunBatch([string[]]$paths) {
    $manifest = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($manifest)
    $bw.Write([uint32]0x44584231); $bw.Write([uint32]1); $bw.Write([uint32]$paths.Count)
    foreach ($t in $paths) {
        $bw.Write([uint32]1); $bw.Write([uint32]48)
        $b = [System.Text.Encoding]::Unicode.GetBytes($t)
        $bw.Write([uint32]$b.Length); $bw.Write($b)
    }
    $bw.Flush(); $manifest.Position = 0
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $proxy; $psi.Arguments = '--extract-batch'
    $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $manifest.CopyTo($p.StandardInput.BaseStream); $p.StandardInput.BaseStream.Close()
    $frames = New-Object System.IO.BinaryReader($p.StandardOutput.BaseStream)
    $out = @()
    for ($i = 0; $i -lt $paths.Count; $i++) {
        $index = $frames.ReadUInt32(); $status = $frames.ReadUInt32(); $length = $frames.ReadUInt32()
        if ($length -gt 0) { [void]$frames.ReadBytes([int]$length) }
        $out += ('#' + $index + '=' + $(if ($status -eq 0) { 'OK' } else { 'FAIL' }))
    }
    $p.WaitForExit()
    $err = $p.StandardError.ReadToEnd()
    return (($out -join ' ') + ' | stderr: ' + (($err -replace "`r`n", ' | ').Trim()))
}

Write-Output '=== A: one-shot sequential ==='
foreach ($t in $targets) { Write-Output ((Split-Path $t -Leaf) + ' => ' + (RunOneShot $t)) }

Write-Output '=== B: batch with ONE request per process (4 processes) ==='
foreach ($t in $targets) { Write-Output ((Split-Path $t -Leaf) + ' => ' + (RunBatch @($t))) }

Write-Output '=== C: one batch process with all 4 (concurrent workers) ==='
Write-Output (RunBatch $targets)

Write-Output '=== D: one-shot again (state changed?) ==='
foreach ($t in $targets) { Write-Output ((Split-Path $t -Leaf) + ' => ' + (RunOneShot $t)) }
