---
name: Bug report
about: OfferDiff gave a wrong verdict, crashed, or printed something misleading
labels: bug
---

> **Before you post:** remove private store data. Replace real domains with
> `example-store.test`, and real product names, ids and prices with invented ones.

**Feed row** (the header line plus the row that went wrong):

```csv
id,title,link,price,sale_price,availability

```

**Page JSON-LD snippet** (the `<script type="application/ld+json">` block from the page):

```html

```

**Expected verdict** (match / mismatch / unknown, and why):

**Actual verdict and details** (paste the CLI output for this row):

```

```

**Command and exit code:**

```powershell
dotnet run --project src\OfferDiff.Cli -- <feed.csv>
$LASTEXITCODE
```

**Environment:** OS, and the output of `dotnet --version`:
