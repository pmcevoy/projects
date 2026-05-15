$globalSettings = "~/.claude/settings.json"

if( Test-Path $globalSettings ){
	$bak = "$($globalSettings).bak"
	if( Test-Path $bak ){
		Write-Host "Global settings.json backup already exists at $($bak).  Please review and remove before running this script" -ForegroundColor Red
		return
	}
	else {
		Copy-Item $globalSettings $bak
		Remove-Item $globalSettings
		Write-Host "Existing global settings.json backed up to $($bak) and removed" -ForegroundColor Yellow
	}
}

if(!(Test-Path ~/.claude)){
	Write-Host "Looks like claude is not already installed (or perhaps install dir has changed?) - please install before linking to this settings" -ForgroundColor Red
	return
}

New-Item -Path $globalSettings -ItemType SymbolicLink -Value "$($PSScriptRoot)\settings.json"
Write-Host "Created link to settings.json" -ForegroundColor Green
