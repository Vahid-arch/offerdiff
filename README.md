# OfferDiff

[![CI](https://github.com/Vahid-arch/offerdiff/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Vahid-arch/offerdiff/actions/workflows/ci.yml)

Checks that the price, currency and availability in a product feed match the structured data on each product page.

## Why

Google Merchant Center crawls your product pages and compares them with your feed. When the
page says something different (for example the feed says 34.50 USD and the page says 39.00 USD),
the product can be disapproved with **"Mismatched value (page crawl)"**. See Google's help page:
<https://support.google.com/merchants/answer/9992725>

OfferDiff finds those differences before Google does, and shows the evidence for each row:
the feed value, the page value and the reason.

## Download

Ready-to-run binaries are on the [Releases page](https://github.com/Vahid-arch/offerdiff/releases).
They are self-contained single files, so you do **not** need .NET installed. Releases are marked as
pre-releases while OfferDiff is at 0.x.

| File | For |
|---|---|
| `offerdiff-<version>-win-x64.zip` | Windows (64-bit) |
| `offerdiff-<version>-linux-x64.zip` | Linux (64-bit, glibc) |
| `offerdiff-<version>-osx-x64.zip` | macOS on Intel |
| `offerdiff-<version>-osx-arm64.zip` | macOS on Apple silicon (M1 and later) |
| `SHA256SUMS.txt` | checksums of the zips |

Each zip holds the `offerdiff` program, `LICENSE` and this README. To check a download, compare
its SHA-256 with the line in `SHA256SUMS.txt`:

```powershell
Get-FileHash .\offerdiff-0.2.0-win-x64.zip -Algorithm SHA256   # Windows
```
```sh
sha256sum -c SHA256SUMS.txt --ignore-missing                   # Linux
shasum -a 256 -c SHA256SUMS.txt --ignore-missing               # macOS
```

Run it from the folder you unzipped (`my-feed.csv` is your own feed):

```powershell
.\offerdiff.exe my-feed.csv --live                            # Windows
```
```sh
./offerdiff my-feed.csv --live                                 # Linux and macOS
```

On Linux or macOS, if you get "permission denied", run `chmod +x offerdiff` once. On macOS the
binary is not notarized, so Gatekeeper may block it the first time; remove the download quarantine
flag with:

```sh
xattr -d com.apple.quarantine offerdiff
```

The zips do not include the sample feeds. To try the demo store, download
[`samples/live-demo-feed.csv`](samples/live-demo-feed.csv) and run `offerdiff live-demo-feed.csv --live`.

## Quick start (Windows)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Get the code and run it on the sample feed:

```powershell
git clone https://github.com/Vahid-arch/offerdiff.git
cd offerdiff
dotnet run --project src\OfferDiff.Cli -- samples\feed.csv
```

Run the tests with `dotnet test`.

### Options

```
usage: offerdiff <feed.csv> [--pages <folder>]
       offerdiff <feed.csv> --live [--delay <seconds>] [--allow-private-hosts]

  --pages <folder>         fixture mode: folder with local pages (default: "pages" next to the CSV)
  --live                   fetch the real product pages from the feed's links
  --delay <seconds>        live mode: minimum gap between requests to one site (default 1, at least 0.5)
  --allow-private-hosts    live mode: allow local network addresses, for your own staging server
```

Without `--live`, the tool runs in **fixture mode**: a link such as
`https://example-store.test/products/blue-mug?variant=2` is read from
`<pages>\products\blue-mug.html` (the query is ignored when choosing the file).

## Checking a live store

```powershell
dotnet run --project src\OfferDiff.Cli -- my-feed.csv --live
```

With `--live`, OfferDiff downloads each product page from the link in the feed and checks it the same
way. It is careful with the sites it visits:

- at most 1 request per second per site (change with `--delay`), and it honours `Retry-After` on 429/503
- respects `robots.txt` (RFC 9309): disallowed pages are not fetched and show as **unknown**
- blocks private and internal addresses (see [Security](#security)) unless you pass `--allow-private-hosts`
- gives up on a page after 15 seconds, and on pages larger than 5 MB

Progress (`checking 12/240: <id>`) goes to stderr, so the table on stdout stays clean for scripts:
`dotnet run --project src\OfferDiff.Cli -- my-feed.csv --live > report.txt`.

> **Only check stores you own or have permission to check.**

## Try live mode on the demo store

The project has a small public demo store on GitHub Pages, so you can try `--live` against real URLs
without checking anyone else's site: <https://vahid-arch.github.io/offerdiff/demo/>. Its five
products are invented, prices are in AED, and nothing is for sale.

```powershell
dotnet run --project src\OfferDiff.Cli -- samples\live-demo-feed.csv --live
```

Expected result line (exit code 1, because of the deliberate mismatch):

```
6 rows: 5 match, 1 mismatch, 0 unknown
```

| Row | Page pattern | Expected |
|---|---|---|
| `MUG-SAND` | single Product + Offer | match |
| `TRAY-PALM` | feed says 79.00 AED, page says 89.00 AED | **mismatch** (deliberate): `price: feed 79.00, page 89.00` |
| `LAMP-DUNE` | on sale: feed `sale_price` 199.00 AED equals the page offer | match |
| `CANDLE-OUD` | out of stock on page and in feed | match |
| `CUSHION-FALAJ-S` | ProductGroup variant `?variant=1`, 70.00 AED | match |
| `CUSHION-FALAJ-L` | ProductGroup variant `?variant=2`, 95.00 AED | match |

The demo pages live in [`docs/demo`](docs/demo). A test reads them from disk and checks these exact
verdicts, so the demo stays correct in CI.

## Sample output

Real output of the command above:

```
ID             VERDICT   MATCHED BY    DETAILS
MUG-BLUE       match     url           feed and page agree: 12.99 USD, in_stock
KETTLE-RED     mismatch  url           price: feed 34.50, page 39.00
TEAPOT-GREEN   mismatch  url           currency: feed USD, page EUR
BOWL-LARGE     match     url           feed and page agree: 14.40 USD, in_stock
BOARD-WALNUT   match     url           feed and page agree: 45.00 USD, out_of_stock
APRON-LINEN-M  match     url           feed and page agree: 29.00 USD, preorder
COASTER-OAK    unknown   -             no Product offer on page
                                       no JSON-LD block on page
LADLE-STEEL    unknown   -             no Product offer on page
                                       JSON-LD block 1: invalid JSON (line 10: The JSON object contains a trailing comma at the end which is not supported in this mode)

8 rows: 4 match, 2 mismatch, 2 unknown
```

## Feed format

CSV with the columns `id,title,link,price,sale_price,availability`, for example:

```
MUG-BLUE,Blue Mug,https://example-store.test/products/blue-mug,12.99 USD,,in_stock
```

- `price` and `sale_price` look like `12.99 USD` (Merchant Center style). `sale_price` may be empty.
- `availability` is one of `in_stock`, `out_of_stock`, `preorder`, `backorder`.
- Bad rows are reported with their row number and skipped; they do not stop the run.

## How matching works

The tool reads schema.org `Product` / `Offer` data from the page's JSON-LD in these shapes: a
`Product` at the top level, inside `@graph` or inside `mainEntity`; `offers` as one object or an
array; `AggregateOffer`; `ProductGroup` with `hasVariant`; and offers without `"@type": "Offer"`
when they have a `price`. Other shapes are listed under [Limitations](#limitations). It then picks the offer
for a feed row in this order:

1. **url**: the offer `url` equals the feed `link` (scheme and host ignore case; path and query must
   be exact, so `?variant=2` only matches `?variant=2`)
2. **sku**: the offer `sku` equals the feed `id`
3. **single offer**: the page has exactly one offer
4. otherwise **unknown** ("ambiguous variants"). It never guesses.

If several offers match at the same step and disagree, the result is **unknown** ("conflicting
structured data").

The feed price used is `sale_price` when present, else `price`. Prices are compared exactly.

### Verdicts

| Verdict | Meaning |
|---|---|
| `match` | Price, currency and availability on the page equal the feed. |
| `mismatch` | At least one of them differs. Each difference is listed with feed and page value. |
| `unknown` | The tool could not check: page missing or rejected, no or invalid JSON-LD, ambiguous variants, conflicting offers, or the page lacks a price, currency or availability. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | No mismatches (unknown rows and some bad feed rows alone do not fail) |
| 1 | At least one mismatch |
| 2 | Error: no argument, feed file not found or unreadable (for example locked by Excel), pages folder not found, or no feed row could be read (for example `;` instead of `,`, missing columns, or an empty feed) |

## JavaScript-rendered pages

OfferDiff reads the HTML the server sends. It does not run JavaScript. Many stores built as
single-page apps (Angular, React, Vue, Next.js and similar) send an almost empty page and add the
product JSON-LD later in the browser. OfferDiff then finds no structured data, and the row is
**unknown**. When the page looks like such an app, the details add:

```
structured data may be added by JavaScript; OfferDiff reads server HTML only (see README: JavaScript-rendered pages)
```

This matters for Merchant Center too. Google's guide
[Generate structured data with JavaScript](https://developers.google.com/search/docs/appearance/structured-data/generate-structured-data-with-javascript)
warns: *"Be aware that dynamically-generated markup can make Shopping crawls less frequent and less
reliable"*, which is a problem for fast-changing values such as price and availability.

**Recommendation:** put the product JSON-LD in the server HTML, through server-side rendering (SSR)
or prerendering of product pages. Then Google and OfferDiff both see the same price and availability
as the feed.

The hint is a guess from signs such as `<app-root>`, `ng-version`, `<div id="root">`,
`<div id="app">`, `__NEXT_DATA__`, `data-beasties-container`, or a page body with almost no text.
It never changes the verdict.

## Limitations

- Fixture mode (the default) accepts only URLs on `example-store.test`; use `--live` for real stores.
- Live mode checks pages one at a time, so a large feed takes at least one second per page on the same site.
- JSON-LD only: no microdata or RDFa.
- `@type` must be the plain name (`"Product"`); prefixed forms such as `"schema:Product"` or
  `"https://schema.org/Product"` are not recognized.
- No JavaScript rendering, also in live mode (see [JavaScript-rendered pages](#javascript-rendered-pages)): the page is read as the server sends it, so structured
  data added by scripts is not seen.
- No `priceSpecification`: only `price`, and `lowPrice`/`highPrice` on `AggregateOffer`
  (a price range gives unknown).
- Availability is limited to `InStock`, `OutOfStock`, `PreOrder` and `BackOrder`.
- CSV quoted fields cannot contain line breaks.
- JSON-LD blocks are found with a simple regex, not a full HTML parser.

## Security

Live fetching (`HttpPageSource`) blocks private and internal addresses by default, because the feed
decides which URLs are requested. Every resolved IP is checked before connecting, including on
redirects. Blocked: loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16,
fe80::/10, including cloud metadata at 169.254.169.254), unique-local IPv6 (fc00::/7), unspecified,
multicast, carrier-grade NAT (100.64/10) and their IPv4-mapped IPv6 forms. A blocked link gives
**unknown** with `blocked private address <ip>`.

To check your own staging server on the local network, use `HttpPageSource.Create(allowPrivateHosts: true)`.
The system proxy is not used, because a proxy would hide the real target address from this check.

Polite by default: at most 1 request per second per site; honours Retry-After on 429/503 (waits up to 30s, retries once).

Respects robots.txt (RFC 9309) by default.

## Sample data

All sample data is synthetic. The store `example-store.test` and its products are invented;
`.test` is a reserved domain that never resolves on the internet.

## Status

Prototype, not yet published.

## License

[MIT](LICENSE)
