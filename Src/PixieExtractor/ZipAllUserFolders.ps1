$destRoot = "\Temp"
$throttle = 2

Add-Type -AssemblyName System.IO.Compression.FileSystem

$folders   = Get-ChildItem -Path $destRoot -Directory
$total     = $folders.Count
$completed = [System.Collections.Concurrent.ConcurrentBag[byte]]::new()

$folders | ForEach-Object -Parallel {
	Add-Type -AssemblyName System.IO.Compression.FileSystem

	$username     = $_.Name
	$sourceFolder = $_.FullName
	$zipPath      = "$($using:destRoot)\$($username).zip"

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

	($using:completed).Add(0)
	$count = ($using:completed).Count
	$pct   = [Math]::Round(($count / $using:total) * 100)
	Write-Progress -Activity "Zipping user folders" -Status "$count of $($using:total)" -PercentComplete $pct

} -ThrottleLimit $throttle

Write-Progress -Activity "Zipping user folders" -Completed
Write-Host "Done. Processed $total folders." -ForegroundColor Green
