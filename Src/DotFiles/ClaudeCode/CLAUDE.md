## Shell command rules

Do not create compound commands that change directory to the current working directory.
The shell CWD is preset at session start and persists between commands, so
`cd <CWD> && cmd` and `Set-Location <CWD>; cmd` are always redundant — they produce
a compound command that isn't covered by the allow-list and forces a manual approval
prompt for a command that would have auto-approved on its own.

If a command genuinely needs a *different* directory, using cd/Set-Location is fine
(and prompting for approval in that case is expected).

Note: `git` finds the repo root automatically from any subdirectory — no cd needed
even when the repo root differs from the CWD.

## AWS access

Never run AWS CLI commands or otherwise connect to the AWS account directly — not even
read-only `describe`/`list` calls. Instead, hand the user the exact command(s) with a
short explanation of what each does and why, and they will proxy them and paste the
output back. This applies to every AWS interaction, in every project.
