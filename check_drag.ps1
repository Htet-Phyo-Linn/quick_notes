Add-Type -Path "D:\CODE\quick_notes\src\QuickNote.Avalonia\QuickNote.Avalonia.Desktop\bin\Debug\net10.0\Avalonia.Base.dll"
Write-Output "--- DragDrop methods ---"
[Avalonia.Input.DragDrop].GetMethods() | ForEach-Object { $_.ToString() }
Write-Output "--- DragEventArgs properties ---"
[Avalonia.Input.DragEventArgs].GetProperties() | ForEach-Object { $_.ToString() }
