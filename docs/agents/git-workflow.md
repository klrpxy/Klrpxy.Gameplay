# Git Workflow

This repository uses GitHub Flow. Keep `main` releasable, do all work on short-lived branches, and merge changes through pull requests. Do not maintain a long-lived `develop` branch.

## Branches

Create every branch from the latest `main`:

```powershell
git switch main
git pull --ff-only
git switch -c <branch-name>
```

Use these branch names:

- `codex/<topic>` for work performed by Codex;
- `feat/<topic>` for human-authored features;
- `fix/<topic>` for human-authored fixes;
- `docs/<topic>` for human-authored documentation;
- `chore/<topic>` for human-authored maintenance.

Keep the topic short, lowercase, and hyphen-separated. One branch and pull request should cover one coherent change.

## Commits

Use the Chinese commit-message format defined in the root `AGENTS.md`. Keep commits intentional and include a body when future readers need the reason, trade-off, or migration details.

Do not rewrite a branch after other people or automations may have started using it unless the rewrite has been explicitly coordinated. Never force-push `main`.

## Verification

Before pushing, run checks proportional to the change:

- code changes: run the focused tests first, then the relevant build or full test suite when practical;
- source-generator or package changes: run the .NET/Roslyn suite and the required Unity smoke tests at the release boundary;
- documentation-only changes: run `git diff --check` and verify links and examples.

Record skipped checks and their reason in the pull-request description.

## Pull requests

Push the branch and open a pull request targeting `main`:

```powershell
git push -u origin <branch-name>
```

The pull request should explain:

- what changed;
- why it changed;
- the user or developer impact;
- the checks used to validate it.

Use a draft pull request while required work remains. Mark it ready only when the intended scope is complete and verification has passed. Do not push directly to `main`.

Use a merge commit when merging. This preserves useful commit bodies and keeps the pull request visible in history.

## After merging

Return to the latest `main` and remove the short-lived local branch:

```powershell
git switch main
git pull --ff-only
git branch -d <branch-name>
```

Delete the remote branch after merge. Do not reuse a merged branch for unrelated work.

## Releases

Create release tags from `main`. Only create a release branch when an older released version needs continued patch support alongside newer development.
