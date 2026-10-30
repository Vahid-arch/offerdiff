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
OfferDiff.Cli <feed.csv> [--pages <folder>]
```

`--pages` is the folder with the local product pages. Default: a `pages` folder next to the CSV file.
A link such as `https://example-store.test/products/blue-mug?variant=2` is read from
`<pages>\products\blue-mug.html` (the query is ignored when choosing the file).

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

## Limitations

- The CLI reads local fixture pages only: only URLs on `example-store.test` are accepted. Live
  fetching exists in the library (`HttpPageSource`) but is not yet available from the CLI.
- JSON-LD only: no microdata or RDFa.
- `@type` must be the plain name (`"Product"`); prefixed forms such as `"schema:Product"` or
  `"https://schema.org/Product"` are not recognized.
- No JavaScript rendering: structured data added by scripts is not seen.
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

## Sample data

All sample data is synthetic. The store `example-store.test` and its products are invented;
`.test` is a reserved domain that never resolves on the internet.

## Status

Prototype, not yet published.

## License

[MIT](LICENSE)
