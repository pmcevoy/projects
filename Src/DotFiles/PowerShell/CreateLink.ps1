$profileFile = $PROFILE.CurrentUserAllHosts 

if( Test-Path $profileFile ){
	Write-Host "Profile already exists at $profileFile" -ForegroundColor Yellow
	return
}

New-Item -Path $profileFile -ItemType SymbolicLink -Value "$($PSScriptRoot)\profile.ps1"
Write-Host "Created link to profile.ps1" -ForegroundColor Green




