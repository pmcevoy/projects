Import-Module posh-git

#Disable directory higlighting for dir
#https://github.com/PowerShell/PowerShell/issues/18778#issuecomment-1354060311
$PSStyle.FileInfo.Directory = "`e[34m"

# Enable kubectl autocomplete
# https://kubernetes.io/docs/tasks/tools/install-kubectl-windows/#enable-shell-autocompletion
kubectl completion powershell | Out-String | Invoke-Expression
