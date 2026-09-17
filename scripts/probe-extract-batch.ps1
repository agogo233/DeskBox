$ErrorActionPreference = 'Stop'
$proxy = 'D:\project\wingezi\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.ThumbnailProxy.exe'

$manifest = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($manifest)
$bw.Write([uint32]0x44584231)  # magic
$bw.Write([uint32]1)           # version
$bw.Write([uint32]3)           # count

function AddRequest([uint32]$mode, [uint32]$size, [string]$path) {
    $bw.Write($mode)
    $bw.Write($size)
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($path)
    $bw.Write([uint32]$bytes.Length)
    $bw.Write($bytes)
}

AddRequest 1 48 'C:\Windows\notepad.exe'
AddRequest 0 256 'C:\Windows'
AddRequest 1 48 'C:\Windows\does_not_exist.xyz'
$bw.Flush()
$manifest.Position = 0

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $proxy
$psi.Arguments = '--extract-batch'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$p = [System.Diagnostics.Process]::Start($psi)
$manifest.CopyTo($p.StandardInput.BaseStream)
$p.StandardInput.BaseStream.Close()

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$outStream = $p.StandardOutput.BaseStream
$frames = New-Object System.IO.BinaryReader($outStream)
for ($i = 0; $i -lt 3; $i++) {
    $index = $frames.ReadUInt32()
    $status = $frames.ReadUInt32()
    $length = $frames.ReadUInt32()
    $payload = if ($length -gt 0) { $frames.ReadBytes([int]$length) } else { @() }
    $head = if ($payload.Length -ge 2) { [System.Text.Encoding]::ASCII.GetString($payload[0..1]) } else { '' }
    Write-Output ("frame index={0} status={1} len={2} head='{3}' atMs={4}" -f $index, $status, $length, $head, $sw.ElapsedMilliseconds)
}
$p.WaitForExit()
Write-Output ("exit={0} totalMs={1}" -f $p.ExitCode, $sw.ElapsedMilliseconds)
