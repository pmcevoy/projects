#Documented install procedure (I'm not hacking, I promise!):
# https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_modules?view=powershell-7.6#manually-install-a-modulePROFILE
$moduleDir = "$($HOME)\Documents\PowerShell\Modules\"

Get-ChildItem -Path $PSScriptRoot |
	?{$_.PSIsContainer -eq $true} |
	%{
		$source = "$($_.FullName)\*"
		$destination = "$($moduleDir)\$($_.Name)" 

		Remove-Item -Path $destination -Recurse -Force -ErrorAction SilentlyContinue
		New-Item -ItemType Directory -Force -Path $destination | Out-Null
		Copy-Item -Path $source -Destination $destination -Recurse -Force
	}
