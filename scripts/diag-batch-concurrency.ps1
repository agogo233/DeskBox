$ErrorActionPreference = 'Continue'
$proxy = 'D:\project\wingezi\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.ThumbnailProxy.exe'

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
    $ok = 0; $fail = 0
    for ($i = 0; $i -lt $paths.Count; $i++) {
        [void]$frames.ReadUInt32(); $status = $frames.ReadUInt32(); $length = $frames.ReadUInt32()
        if ($length -gt 0) { [void]$frames.ReadBytes([int]$length) }
        if ($status -eq 0) { $ok++ } else { $fail++ }
    }
    $p.WaitForExit()
    return "ok=$ok fail=$fail"
}

$dirs = @(Get-ChildItem 'E:\DeskBox' -Directory -Recurse -Depth 1 | ForEach-Object { $_.FullName })
$lnks = @(Get-ChildItem 'E:\DeskBox' -Recurse -Filter *.lnk -Depth 1 | Select-Object -First 8 | ForEach-Object { $_.FullName })
Write-Output ('dirs=' + $dirs.Count + ' lnks=' + $lnks.Count)

Write-Output '=== E2: folder pairs (concurrency 2), 3 rounds ==='
for ($r = 1; $r -le 3; $r++) { Write-Output ('round ' + $r + ': ' + (RunBatch @($dirs[0], $dirs[1]))) }

Write-Output '=== E3: 8 lnk files concurrent (concurrency 8), 3 rounds ==='
for ($r = 1; $r -le 3; $r++) { Write-Output ('round ' + $r + ': ' + (RunBatch $lnks)) }

Write-Output '=== E3b: 8 folders concurrent, 3 rounds (repro stability) ==='
for ($r = 1; $r -le 3; $r++) { Write-Output ('round ' + $r + ': ' + (RunBatch $dirs)) }
