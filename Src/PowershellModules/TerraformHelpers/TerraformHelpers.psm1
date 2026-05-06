<#
Functions in this file require AWS.Tools modules installed:

Install-Module -Name AWS.Tools.Installer

Install-AWSToolsModule -CleanUp -Scope AllUsers `
	AWS.Tools.IdentityManagement, `
	AWS.Tools.EC2
#>

#Call this function from your Prompt function in $PROFILE
function Get-AwsPrompt {
	try {
		$accountAlias = Get-IamAccountAlias
		$userName = (Get-IamUser).UserName
		"[$($accountAlias)/$($userName)]"
	}
	catch {
		"[No Aws Creds]"
	}
}

function New-AwsSubShell {
	param(
		$profile = "esko-stag"
	)

	. aws-vault exec $profile pwsh.exe
}
New-Alias -Name AwsSubShell -Value New-AwsSubShell
