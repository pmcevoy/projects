<#
Functions in this file require AWS.Tools modules installed:

Install-Module -Name AWS.Tools.Installer

Install-AWSToolsModule -CleanUp -Scope AllUsers `
	AWS.Tools.IdentityManagement, `
	AWS.Tools.EC2
#>

#Call this function from your Prompt function in $PROFILE
function Get-AwsPrompt {
	if ($env:AWS_ACCESS_KEY_ID){
		try {
			$accountAlias = Get-IamAccountAlias
			$userName = (Get-IamUser).UserName
			"[$($accountAlias)/$($userName)]" + [Environment]::NewLine
		}
		catch {
			"[Invalid Aws Creds]" + [Environment]::NewLine
		}
	}
	else {
		""
	}
}

function New-AwsSubShell {
	param(
		$profile = "esko-stag"
	)

	. aws-vault exec $profile pwsh.exe
}
New-Alias -Name AwsSubShell -Value New-AwsSubShell
