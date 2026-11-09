# Changelog

All notable changes to OfferDiff. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/).

The release workflow uses the section for a tag as its release notes, so every version tag
(`v0.3.0`) needs a matching `## [0.3.0]` section here.

## [0.3.0]

Pre-release. OfferDiff can now check stores that add their product data with JavaScript.

### Added

- **`--render`**: opens each product page in headless Chromium, lets its JavaScript run, and checks the
  resulting page. It waits up to 10 s for a JSON-LD block that names a `Product`, so an earlier
  Organization or breadcrumb block does not end the wait too soon.
- **`--browser-channel <name>`**: use an installed Chrome or Edge (`chrome`, `msedge`, or their
  `-beta`, `-dev`, `-canary` builds) instead of downloading Chromium.
- **`--install-browser`**: downloads the headless Chromium that `--render` uses (once).
- **`BrowserPageSource`** (new `OfferDiff.Render` library): the browser behind `--render`, with the same
  User-Agent, per-site throttle and robots.txt rules as live mode. Images, media and fonts are not loaded.
- **Private-address blocking in the browser**: every request is checked, including redirect hops,
  WebSockets and popups, through a local guard proxy that uses the same check as live mode.
- **Two downloads per platform**: `offerdiff-<version>-<rid>.zip` (the program alone, as before) and
  `offerdiff-render-<version>-<rid>.zip` (adds the browser driver that `--render` needs).

### Changed

- When the browser or its driver is missing, `--render` prints one line with the fix and exits 2,
  before checking any row.
- In browser mode, a page without Product JSON-LD gets a hint worded for browser mode instead of
  "OfferDiff reads server HTML only".
- User-Agent is now `OfferDiff/0.3.0 (+https://github.com/Vahid-arch/offerdiff)`; the version is set
  once in `Directory.Build.props`.
- Errors about `--delay`, `--allow-private-hosts` and `--pages` now name both `--live` and `--render`.

### Security

- Security: block private IPv4 addresses embedded in NAT64, 6to4 and Teredo IPv6 addresses
- Security: block the deprecated IPv4-compatible (`::/96`) and IPv4-translated (`::ffff:0:0:0/96`) IPv6 ranges

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

Prototype. Tagged as `v0.1.0`, before the release workflow existed, so it has no release binaries.

### Added

- CSV feed reader for Merchant Center style feeds (`12.99 USD` prices, quoted fields, bad rows
  reported with row numbers, duplicate ids reported).
- JSON-LD extractor for schema.org `Product`/`Offer`: offers arrays, `AggregateOffer`,
  `ProductGroup` with `hasVariant`, `@graph` and `mainEntity`; invalid JSON is reported, not thrown.
- Comparer: picks the offer by url, then sku, then single offer, otherwise unknown; exact price
  comparison; match / mismatch / unknown with evidence.
- CLI for local fixture pages with exit codes 0 / 1 / 2, synthetic sample feed and fixtures.
