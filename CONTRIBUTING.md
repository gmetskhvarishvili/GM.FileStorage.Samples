# Contributing

Thanks for taking a look! This is a **sample** repository that demonstrates
[GM.FileStorage](https://github.com/gmetskhvarishvili/GM.FileStorage). It isn't a published
package, so there's no versioning or release process to worry about.

## Prerequisites

- **.NET 10 SDK** (the tests need no external services — the sample uses local disk storage under a temp root).

```bash
dotnet build -c Release
dotnet test  -c Release
```

## Workflow

1. Branch off `master`: `git switch -c fix/something`.
2. Make your change.
3. Add or update tests under `tests/GM.FileStorage.Sample.Tests` where it makes sense.
4. Open a pull request into `master`. CI (`build` + tests) must pass.

## Commit messages

[Conventional Commits](https://www.conventionalcommits.org/) are appreciated for readable history
(`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`), though this repo doesn't release
packages, so they don't drive any automation.

## Code style

Enforced by [`.editorconfig`](.editorconfig). Run `dotnet format` before pushing if unsure.
