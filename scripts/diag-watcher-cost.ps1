$ErrorActionPreference = 'Continue'

# WinRT projection helpers for PowerShell 5.1
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function AwaitOp($operation, $resultType) {
    $asTask = $asTaskGeneric.MakeGenericMethod($resultType)
    $task = $asTask.Invoke($null, @($operation))
    $task.Wait() | Out-Null
    return $task.Result
}

[Windows.Storage.StorageFolder, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.Search.QueryOptions, Windows.Storage.Search, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.Search.StorageItemQueryResult, Windows.Storage.Search, ContentType = WindowsRuntime] | Out-Null

$targets = @('E:\DeskBox\综合\截图', 'E:\DeskBox\综合')
foreach ($path in $targets) {
    Write-Output ("=== " + $path + " ===")
    for ($round = 1; $round -le 3; $round++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $folder = AwaitOp ([Windows.Storage.StorageFolder]::GetFolderFromPathAsync($path)) ([Windows.Storage.StorageFolder])
        $t1 = $sw.ElapsedMilliseconds

        $sw.Restart()
        $options = New-Object Windows.Storage.Search.QueryOptions
        $options.FolderDepth = [Windows.Storage.Search.FolderDepth]::Shallow
        $options.IndexerOption = [Windows.Storage.Search.IndexerOption]::UseIndexerWhenAvailable
        $query = $folder.CreateItemQueryWithOptions($options)
        $t2a = $sw.ElapsedMilliseconds
        $sw.Restart()
        $items = AwaitOp ($query.GetItemsAsync(0, 1)) ([System.Collections.Generic.IReadOnlyList[Windows.Storage.IStorageItem]])
        $t2b = $sw.ElapsedMilliseconds

        $sw.Restart()
        $fsw = New-Object System.IO.FileSystemWatcher($path)
        $fsw.EnableRaisingEvents = $true
        $t3 = $sw.ElapsedMilliseconds
        $fsw.Dispose()

        $sw.Restart()
        $count = [System.Linq.Enumerable]::Count([System.IO.Directory]::EnumerateFileSystemEntries($path))
        $t4 = $sw.ElapsedMilliseconds

        Write-Output ("round " + $round + ": GetFolder=" + $t1 + "ms  CreateQuery=" + $t2a + "ms  GetItems(0,1)=" + $t2b + "ms  FSW=" + $t3 + "ms  Enumerate(" + $count + ")=" + $t4 + "ms")
        Start-Sleep -Milliseconds 200
    }
}
