$targetExe = "e:\Onedrive\Documents\GitHub\Youtube\windows\VixzDesktop\bin\Debug\net9.0-windows\VixzDesktop.exe"
$workingDir = "e:\Onedrive\Documents\GitHub\Youtube\windows\VixzDesktop\bin\Debug\net9.0-windows"

$wscript = New-Object -ComObject WScript.Shell

# 1. Desktop shortcut
$desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$desktopLnk = [System.IO.Path]::Combine($desktopPath, "Vixz Desktop.lnk")
$s1 = $wscript.CreateShortcut($desktopLnk)
$s1.TargetPath = $targetExe
$s1.WorkingDirectory = $workingDir
$s1.Description = "Vixz YouTube Desktop"
$s1.Save()

# 2. Repo root shortcut
$repoLnk = "e:\Onedrive\Documents\GitHub\Youtube\Vixz Desktop.lnk"
$s2 = $wscript.CreateShortcut($repoLnk)
$s2.TargetPath = $targetExe
$s2.WorkingDirectory = $workingDir
$s2.Description = "Vixz YouTube Desktop"
$s2.Save()

Write-Host "Created Desktop shortcut: $desktopLnk"
Write-Host "Created Repo shortcut: $repoLnk"
