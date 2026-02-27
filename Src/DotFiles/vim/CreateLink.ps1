$vimrc = "~/_vimrc"

if( Test-Path $vimrc ){
	Write-Host "Profile already exists at $vimrc" -ForegroundColor Yellow
	return
}

New-Item -Path $vimrc -ItemType SymbolicLink -Value "$($PSScriptRoot)/_vimrc"
Write-Host "Created link to _vimrc" -ForegroundColor Green




