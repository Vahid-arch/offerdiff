# Contributing to OfferDiff

Thanks for helping. This guide covers everything you need for a first pull request.

## Build, test and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the exact version is pinned in `global.json`).

```powershell
dotnet build
dotnet test
dotnet run --project src\OfferDiff.Cli -- samples\feed.csv
```

The last command checks the sample feed and should end with `8 rows: 4 match, 2 mismatch, 2 unknown`.

## Project layout

| Folder | What is in it |
|---|---|
| `src/OfferDiff.Core` | Feed reading, JSON-LD extraction and comparison. All logic lives here. |
| `src/OfferDiff.Cli` | Argument parsing and printing only. |
| `tests/OfferDiff.Tests` | xUnit tests for both. |
| `samples/` | Synthetic feed and HTML fixture pages (`example-store.test`). |

## Rules for changes

- **No network access** in code or tests. Pages come from local fixture files.
- **Synthetic data only.** Never commit real store names, URLs, prices or customer data.
  New fixtures use `example-store.test` and invented products.
- **New logic needs a test** that fails when the logic breaks.
- Keep code small and readable; avoid new packages unless they clearly pay for themselves.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: read mainEntity products
fix: report duplicate feed ids
docs: explain exit codes
test: cover CLI exit codes
chore: update CI
refactor: rename project
```

## Pull requests

Every change goes through a pull request with green CI. Nobody pushes to `main` directly.

1. Create a branch, for example `fix/price-parsing`.
2. Make the change, with tests.
3. Run `dotnet build` and `dotnet test` locally.
4. Open a pull request and fill in the template.
5. Wait for CI (Windows and Ubuntu) to pass before merging.
