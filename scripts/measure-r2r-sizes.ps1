$ErrorActionPreference = 'Stop'
foreach ($name in @('jit', 'r2r')) {
    $dir = "D:\project\wingezi\.artifacts\r2r-experiment\$name"
    $size = (Get-ChildItem $dir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    $dll = (Get-Item (Join-Path $dir 'DeskBox.dll')).Length / 1MB
    $native = (Get-ChildItem $dir -Recurse -File | Where-Object { $_.Name -like '*.pak' -or $_.Extension -in '.dll','.exe' } | Measure-Object Length -Sum).Sum / 1MB
    Write-Output ("{0}: total={1:N1}MB DeskBox.dll={2:N1}MB dll+exe+pak={3:N1}MB" -f $name, $size, $dll, $native)
}
