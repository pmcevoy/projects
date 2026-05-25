function prompt {
    $origLastExitCode = $LASTEXITCODE

	$prompt += Get-AwsPrompt

    $prompt += Write-VcsStatus
    $prompt += Write-Prompt "$($ExecutionContext.SessionState.Path.CurrentLocation)"
    $prompt += Write-Prompt "$(if ($PsDebugContext) {' [DBG]: '} else {''})" -ForegroundColor Magenta
    $prompt += "$('>' * ($nestedPromptLevel + 1)) "

    $LASTEXITCODE = $origLastExitCode
    $prompt
}

Import-Module posh-git
$GitPromptSettings.PathStatusSeparator.Text = ""
$GitPromptSettings.AfterStatus.Text = "]`n"

Import-Module TerraformHelpers

#Disable directory higlighting for dir
#https://github.com/PowerShell/PowerShell/issues/18778#issuecomment-1354060311
$PSStyle.FileInfo.Directory = "`e[34m"

# Enable kubectl autocomplete
# https://kubernetes.io/docs/tasks/tools/install-kubectl-windows/#enable-shell-autocompletion
kubectl completion powershell | Out-String | Invoke-Expression


