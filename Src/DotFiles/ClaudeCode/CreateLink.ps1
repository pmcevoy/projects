$links = @(
    @{ Target = "~/.claude/settings.json"; Source = "$PSScriptRoot\settings.json" }
    @{ Target = "~/.claude/CLAUDE.md";     Source = "$PSScriptRoot\CLAUDE.md" }
)

if (!(Test-Path ~/.claude)) {
    Write-Error "~/.claude directory not found — is Claude installed?"
    exit 1
}

$hasError = $false

foreach ($link in $links) {
    $target = $link.Target
    $source = $link.Source

    $item = Get-Item $target -ErrorAction SilentlyContinue

    if ($item -and $item.LinkType -eq 'SymbolicLink') {
        Write-Host "$target is already a symlink — skipping" -ForegroundColor Cyan
        continue
    }

    if ($item) {
        $bak = "$target.bak"
        if (Test-Path $bak) {
            Write-Host "ERROR: backup already exists at $bak — skipping $target (remove the backup manually to proceed)" -ForegroundColor Red
            $hasError = $true
            continue
        }
        Copy-Item $target $bak
        Remove-Item $target
        Write-Host "Backed up $target to $bak" -ForegroundColor Yellow
    }

    New-Item -Path $target -ItemType SymbolicLink -Value $source | Out-Null
    Write-Host "Created symlink: $target -> $source" -ForegroundColor Green
}

if ($hasError) {
    exit 1
}
