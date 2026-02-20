Import-Csv -Path .\PixieFiles-Small.csv |  %{
	$sourcePath = "\\100.72.72.88\pixie\$($_.SourcePath)"
	if( Test-Path $sourcePath ){
		Write-Host "Found $sourcePath"
	}
	else {
		Write-Host "Could not find $sourcePath" -ForegroundColor Red
	}
}
