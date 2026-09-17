$ErrorActionPreference = 'Stop'
$jit = @{}
Get-ChildItem 'D:\project\wingezi\.artifacts\r2r-experiment\jit' -Recurse -File | ForEach-Object { $jit[$_.Name] = $_.Length }
Get-ChildItem 'D:\project\wingezi\.artifacts\r2r-experiment\r2r' -Recurse -File |
    Where-Object { $jit.ContainsKey($_.Name) -and ($_.Length - $jit[$_.Name]) -gt 200KB } |
    Sort-Object Length -Descending |
    ForEach-Object {
        Write-Output ('{0}: jit={1:N1}MB r2r={2:N1}MB (+{3:N1}MB)' -f $_.Name, ($jit[$_.Name]/1MB), ($_.Length/1MB), (($_.Length - $jit[$_.Name])/1MB))
    }
