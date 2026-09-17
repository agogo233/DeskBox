$ErrorActionPreference = 'Continue'
$proxy = 'D:\project\wingezi\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.ThumbnailProxy.exe'
$dirs = @(Get-ChildItem 'E:\DeskBox' -Directory -Recurse -Depth 1 | Select-Object -First 4 | ForEach-Object { $_.FullName })
$lnks = @(Get-ChildItem 'E:\DeskBox' -Recurse -Filter *.lnk -Depth 1 | Select-Object -First 4 | ForEach-Object { $_.FullName })
$mixed = @(); for ($i = 0; $i -lt 4; $i++) { $mixed += $dirs[$i]; $mixed += $lnks[$i] }

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
    for ($i = 0; $i -lt $paths.Count; $i++) {
        $index = $frames.ReadUInt32(); $status = $frames.ReadUInt32(); $length = $frames.ReadUInt32()
        if ($length -gt 0) { [void]$frames.ReadBytes([int]$length) }
        $leaf = Split-Path $paths[$index] -Leaf
        $kind = if (Test-Path $paths[$index] -PathType Container) { 'DIR ' } else { 'file' }
        Write-Output ('  ' + $kind + ' ' + $leaf + ' => ' + $(if ($status -eq 0) { 'OK' } else { 'FAIL' }))
    }
    $p.WaitForExit()
}

for ($r = 1; $r -le 4; $r++) { Write-Output ('round ' + $r + ':'); RunBatch $mixed }
