$sourceRoot  = "\\100.72.72.88\pixie"
$destRoot    = "\Temp"
$csvFilePath = ".\PixieFiles-Small.csv"

$totalRecords = (Get-Content $csvFilePath | Measure-Object).Count - 1
$recordCounter = 0

Get-Content -Path $csvFilePath |  %{
	$parts = $_.Split(",")
	($sourcePath,$username,$itemDate,$itemID,$extension,$itemNameNotJoined) = $parts
	$itemName = $itemNameNotJoined -join ","  #Re-add any commas that were removed by split

	$src = "$($sourceRoot)\$($sourcePath)"
	$dest = "$($destRoot)\$($_.TargetPath)"
	$percentComplete = ($recordCounter++ / $totalRecords) * 100
	Write-Progress -Activity "Copying" -Status "$($percentComplete)% complete" -PercentComplete $percentComplete

	if( !(Test-Path $src) ){
		Write-Host "Could not find $src" -ForegroundColor Red
		return
	}
	if( (Test-Path $dest) ){
		Write-Host "Skipping as it allready exists: $dest" -ForegroundColor DarkGray
		return
	}


	if( !(Test-Path "$($destRoot)\$($username)") ){
		New-Item -Type Directory "$($destRoot)\$($username)\"  | Out-Null
	}
	if( !(Test-Path "$($destRoot)\$($username)\$($date)") ){
		New-Item -Type Directory "$($destRoot)\$($username)\$($date)\" | Out-Null
	}

	Copy-Item -Path $src -Destination $dest 
}

