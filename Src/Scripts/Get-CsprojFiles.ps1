param(
		[Parameter(Mandatory)]
		[string]$CsprojPath
	 )

$projFullPath = Get-Item $CsprojPath

$projectFolder = Split-Path $projFullPath -Parent
[xml]$csproj = Get-Content $projFullPath

$csproj.Project.ItemGroup | ForEach-Object {
	foreach ($itemType in @("Compile", "Content", "None", "EmbeddedResource")) {
		$items = $_.PSObject.Properties[$itemType]
			if (-not $items) {
				continue
			}
		foreach ($node in $items.Value) {
			$include = if ($node -is [System.Xml.XmlElement]) { $node.Include } else { $node }
			if ($include -and $include -notlike '*$(*)*') {
				[System.IO.Path]::GetFullPath([System.IO.Path]::Combine($projectFolder, $include))
			}
		}
	}
}
