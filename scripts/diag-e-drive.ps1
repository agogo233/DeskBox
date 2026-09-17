$ErrorActionPreference = 'Continue'
$root = 'E:\DeskBox'
$proxy = 'D:\project\wingezi\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.ThumbnailProxy.exe'

Write-Output '=== top-level dirs ==='
Get-ChildItem $root -Directory | ForEach-Object { Write-Output ('DIR: ' + $_.FullName) }

$aiDir = Get-ChildItem $root -Directory |
    Where-Object { @(Get-ChildItem $_.FullName -Filter *.lnk -ErrorAction SilentlyContinue).Count -gt 0 } |
    Select-Object -First 1
if ($aiDir) {
    Write-Output '=== lnk diagnostics ==='
    $lnk = Get-ChildItem $aiDir.FullName -Filter *.lnk | Select-Object -First 1
    Write-Output ('LNK: ' + $lnk.FullName + ' attrs=' + $lnk.Attributes + ' len=' + $lnk.Length)
    icacls $lnk.FullName | Select-Object -First 6
    try {
        Get-Content $lnk.FullName -Stream Zone.Identifier -ErrorAction Stop | Select-Object -First 3
    } catch { Write-Output 'zone-stream: none' }
    Write-Output ('HKCR .lnk -> [' + (Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\.lnk' -Name '(default).' -ErrorAction SilentlyContinue).'(default).' + ']')
    try {
        Start-Process -FilePath $lnk.FullName
        Write-Output 'START-PROCESS: OK (app window may have opened)'
    } catch {
        Write-Output ('START-PROCESS FAILED: ' + $_.Exception.Message)
    }
}

Write-Output '=== batch icon probe: every second-level directory ==='
function U32([uint32]$v) { [BitConverter]::GetBytes($v) }
$dirs = Get-ChildItem $root -Directory -Recurse -Depth 1 | Select-Object -First 12
$manifest = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($manifest)
$bw.Write([uint32]0x44584231)
$bw.Write([uint32]1)
$bw.Write([uint32]@($dirs).Count)
foreach ($d in $dirs) {
    $bw.Write([uint32]1)  # Icon mode
    $bw.Write([uint32]48)
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($d.FullName)
    $bw.Write([uint32]$bytes.Length)
    $bw.Write($bytes)
}
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
$frames = New-Object System.IO.BinaryReader($p.StandardOutput.BaseStream)
$count = @($dirs).Count
for ($i = 0; $i -lt $count; $i++) {
    $index = $frames.ReadUInt32()
    $status = $frames.ReadUInt32()
    $length = $frames.ReadUInt32()
    if ($length -gt 0) { [void]$frames.ReadBytes([int]$length) }
    Write-Output ('ICON: ' + $dirs[$index].Name + ' status=' + $status + ' len=' + $length)
}
$p.WaitForExit()
$err = $p.StandardError.ReadToEnd()
if ($err) { Write-Output ('STDERR: ' + $err.Trim()) }
