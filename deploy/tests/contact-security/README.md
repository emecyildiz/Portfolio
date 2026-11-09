# Contact protection and legacy ticket regression

From the repository root:

```powershell
dotnet run --project deploy/tests/contact-security/ContactSecurity.csproj
```

The fixture uses the actual `HireController`, `TurnstileOptions` and
`TurnstileValidator`, a disposable in-memory SQLite `AppDbContext`, and a
recording HTTP handler that simulates Siteverify. It never starts Portfolio,
connects to a real database, calls Cloudflare, or sends an email/bot message.

`--baseline` is for the pre-fix source: it demonstrates disabled production
verification, 60 proof-free submissions filling the shared quota, rejection of
an independent inquiry, and predictable zero-ticket lookup, with a normal ticket
control. It intentionally fails against the fixed source.

The normal run covers configuration guards and binding, Development opt-out,
provider rejection/error/hostname/action cases, denied storage/receipt/outbox,
independent valid acceptance, unchanged contact and outbox limits, honeypot and
invalid form/service handling, zero GUID D/N/B/P/X/whitespace aliases, normal
ticket tracking, and POST/antiforgery/rate/no-store metadata.

Production and Staging require enabled Turnstile, widget keys, and an expected
hostname before startup; Development can still run offline. Do not run publicly
with the Development environment. Current nonzero tickets are untouched. No
schema change is needed to reject legacy zero identifiers; backfill or a unique
index is a separate data-hygiene step if an affected database is found.

This does not promise availability against verified distributed abuse, atomic
quota counts, production cookie/antiforgery routing, or actual provider service
behavior. Those are separate deployment/product checks, not claims made by this
offline regression.
