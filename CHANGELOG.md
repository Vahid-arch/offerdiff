# Changelog

All notable changes to OfferDiff. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/).

The release workflow uses the section for a tag as its release notes, so every version tag
(`v0.2.0`) needs a matching `## [0.2.0]` section here.

## [0.2.0]

Pre-release. OfferDiff can now check live stores.

### Added

- **Live mode** in the library (`HttpPageSource`): fetches real product pages over HTTP(S) with a
  15 s timeout, a 5 MB page limit, up to 5 redirects and gzip/deflate/brotli support.
- **Private-address blocking** (SSRF protection): loopback, private, link-local (including cloud
  metadata), unique-local IPv6, carrier-grade NAT, multicast and IPv4-mapped forms are refused on
  every connection, including redirects.
- **Polite fetching**: at most 1 request per second per site; honours `Retry-After` on 429/503
  (waits up to 30 s, retries once).
- **robots.txt** support (RFC 9309): disallowed pages are not fetched and show as unknown.
- **CLI option `--live`**, with `--delay <seconds>` and `--allow-private-hosts`; progress goes to stderr.
- **JavaScript-rendered page hint**: when a page has no JSON-LD but looks like a JavaScript app, the
  details say so. The verdict stays unknown.
- **Demo store on GitHub Pages** (`docs/demo`) and `samples/live-demo-feed.csv` to try live mode
  against URLs the project owns.
- **Release binaries**: self-contained single-file `offerdiff` for win-x64, linux-x64, osx-x64 and
  osx-arm64, with `SHA256SUMS.txt`.

### Changed

- User-Agent is now `OfferDiff/0.2.0 (+https://github.com/Vahid-arch/offerdiff)`.
- The usage text names the command `offerdiff`, like the release binaries.

## [0.1.0]

Prototype (not tagged or released as binaries).

### Added

- CSV feed reader for Merchant Center style feeds (`12.99 USD` prices, quoted fields, bad rows
  reported with row numbers, duplicate ids reported).
- JSON-LD extractor for schema.org `Product`/`Offer`: offers arrays, `AggregateOffer`,
  `ProductGroup` with `hasVariant`, `@graph` and `mainEntity`; invalid JSON is reported, not thrown.
- Comparer: picks the offer by url, then sku, then single offer, otherwise unknown; exact price
  comparison; match / mismatch / unknown with evidence.
- CLI for local fixture pages with exit codes 0 / 1 / 2, synthetic sample feed and fixtures.
