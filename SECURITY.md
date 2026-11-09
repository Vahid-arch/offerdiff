# Security policy

## Supported versions

Only the latest release gets security fixes.

| Version | Supported |
|---|---|
| 0.3.x (latest) | Yes |
| Older versions | No |

## Reporting a vulnerability

Please report it privately through
[GitHub private vulnerability reporting](https://github.com/Vahid-arch/offerdiff/security/advisories/new).
**Do not open a public issue**, because that exposes the problem before a fix exists.

Please include:

- the OfferDiff version (and your OS, if it matters)
- the steps to reproduce the problem
- the impact: what an attacker could do

**Do not share store data**, whether real or sampled from a real store. Replace domains, products,
prices and customer details with invented values such as `example-store.test`.

## What to expect

You should get an acknowledgement within 7 days. This is best effort, because OfferDiff is a
volunteer project.

## Scope

OfferDiff fetches public product pages from the links in a feed. Its protections against
server-side request forgery (SSRF), such as blocking private and internal addresses, are described
in the [README's Security section](README.md#security).
