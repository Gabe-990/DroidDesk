[CmdletBinding()]
param(
    [string]$Repository = 'Gabe-990/DroidDesk',
    [switch]$Public
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command gh.exe -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI is not installed. Install it with: winget install --id GitHub.cli --exact'
}
& gh auth status
if ($LASTEXITCODE -ne 0) {
    throw 'Sign in first with: gh auth login'
}

if (-not (Test-Path (Join-Path $root '.git'))) {
    git init -b main
}
if ($LASTEXITCODE -ne 0) { throw 'git init failed.' }

git add .
if ($LASTEXITCODE -ne 0) { throw 'git add failed.' }

git diff --cached --quiet
if ($LASTEXITCODE -eq 1) {
    git commit -m 'Initial DroidDesk Windows app'
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed. Set your Git name and email, then run this script again.' }
}

$remoteUrl = "https://github.com/$Repository.git"
$existingRemote = git remote get-url origin 2>$null
if (-not $existingRemote) {
    $repoView = gh repo view $Repository --json name 2>$null
    if ($LASTEXITCODE -ne 0) {
        if ($Public) {
            gh repo create $Repository --public --source . --remote origin --push
        } else {
            gh repo create $Repository --private --source . --remote origin --push
        }
        if ($LASTEXITCODE -ne 0) { throw 'GitHub repository creation or initial push failed.' }
        Write-Host "Created and pushed https://github.com/$Repository"
        exit 0
    }
    git remote add origin $remoteUrl
    if ($LASTEXITCODE -ne 0) { throw 'Could not add the origin remote.' }
} elseif ($existingRemote -ne $remoteUrl) {
    throw "The current origin remote is $existingRemote, not $remoteUrl. Check the repository before pushing."
}

git branch -M main
git push -u origin main
if ($LASTEXITCODE -ne 0) { throw 'Push failed.' }
Write-Host "Pushed DroidDesk to https://github.com/$Repository"
