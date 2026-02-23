$sourceRoot  = "\\100.72.72.88\pixie"
$destRoot    = "\Temp"
$csvFilePath = ".\PixieFiles-Small.csv"

$totalRecords = (Get-Content $csvFilePath | Measure-Object).Count - 1
$recordCounter = 0

Import-Csv -Path $csvFilePath |  %{
	$src = "$($sourceRoot)\$($_.SourcePath)"
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

	($username, $date, $ignore) = $_.TargetPath.Split("\")

	if( !(Test-Path "$($destRoot)\$($username)") ){
		New-Item -Type Directory "$($destRoot)\$($username)\"  | Out-Null
	}
	if( !(Test-Path "$($destRoot)\$($username)\$($date)") ){
		New-Item -Type Directory "$($destRoot)\$($username)\$($date)\" | Out-Null
	}

	Copy-Item -Path $src -Destination $dest 
}

