Import-Csv -Path .\PixieFiles-Small.csv |  %{
	$src = "\\100.72.72.88\pixie\$($_.SourcePath)"
	$dest = "\Temp\$($_.TargetPath)"

	if( Test-Path $src ){
		Copy-Item -Path $src -Destination $dest 
	}
	else {
		Write-Host "Could not find $sourcePath" -ForegroundColor Red
	}
}

