$destRoot = "\Temp"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$folders   = Get-ChildItem -Path $destRoot -Directory
$total     = $folders.Count
$completed = 0

$folders | %{
	$username     = $_.Name
	$sourceFolder = $_.FullName
	$zipPath      = "$($destRoot)\$($username).zip"

	$completed++
	$pct = [Math]::Round(($completed / $total) * 100)
	Write-Progress -Activity "Zipping user folders" -Status "$completed of $total - $username" -PercentComplete $pct

	if( Test-Path $zipPath ){
		Write-Host "Skipping $username - zip already exists" -ForegroundColor DarkGray
	} else {
		try {
			[System.IO.Compression.ZipFile]::CreateFromDirectory(
				$sourceFolder,
				$zipPath,
				[System.IO.Compression.CompressionLevel]::NoCompression,  #The images are already in a compressed format, so no need to re-compress
				$false
			)
			[System.IO.Directory]::Delete($sourceFolder, $true) #Remove-Item can be slow and fail on recurse, so use native delete
		} catch {
			Write-Host "Failed to zip $username`: $_" -ForegroundColor Red
		}
	}
}

Write-Progress -Activity "Zipping user folders" -Completed
Write-Host "Done. Processed $total folders." -ForegroundColor Green
