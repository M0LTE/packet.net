# Security policy

Packet.NET is in **pre-alpha**. There are no supported releases yet. Do not
deploy this in any environment you care about.

## Reporting vulnerabilities

If you discover a security issue, please email tom@fann.ing rather than
opening a public issue. We will respond within 7 days.

We do not yet have a CVE numbering authority or a published security mailing
list. That will change before any tagged release.

## Threat model

The high-level threat model lives in `docs/plan.md` under "Security threat
model". Short version:

- Default-localhost bind for AGW; explicit opt-in to expose it on the network.
- TLS-by-default for the web UI; self-signed cert on first start; ACME opt-in.
- Local users with Argon2id password hashing and WebAuthn / passkeys.
- JWT scopes on every REST endpoint.
- Audit log of every write operation.
- Signed (cosign) update artifacts; signature verified before any swap.

If something in this list looks wrong, please get in touch.
