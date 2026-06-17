param(
	$appPoolName,
	$varName,
	$varValue
)
Import-Module IISAdministration

$mgr     = Get-IISServerManager
$appPool = $mgr.ApplicationPools[$appPoolName]
$envVars = $appPool.GetCollection("environmentVariables")

# Update if exists, add if not
$existing = $envVars | Where-Object { $_["name"] -eq $varName }

if ($existing) {
    $existing["value"] = $varValue
} else {
    $entry          = $envVars.CreateElement("add")
    $entry["name"]  = $varName
    $entry["value"] = $varValue
    $envVars.Add($entry)
}

# Commit and recycle
Start-IISCommitDelay
$mgr.CommitChanges()
Stop-IISCommitDelay

Restart-WebItem "IIS:\AppPools\$appPoolName"
