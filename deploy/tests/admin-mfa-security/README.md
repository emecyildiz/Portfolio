# Admin MFA boundary regression

Run `dotnet run --project deploy/tests/admin-mfa-security/AdminMfaSecurity.csproj`.

This package calls the actual `AccountSecurityController` with recording synthetic
Identity managers. It does not start the portfolio site, connect to PostgreSQL,
send messages, or use real credentials. The web project build compiles Razor views.

Coverage includes default-deny setup-secret disclosure, every sensitive POST with
missing/empty/wrong/padded proof, invalid model and TOTP paths, failed Identity
enrollment, valid setup/enrollment/rotation/reset, retained authorization,
antiforgery/rate-limit/cache attributes, and removal of plaintext proof from
ModelState. A valid setup POST does not authorize a later GET or another POST.
A disposable loopback HTTP fixture exercises the production MFA rate policy:
login permits remain separate, five management attempts succeed and the sixth is
rejected. No external service is contacted; the fixture shuts down in `finally`.

`--baseline` is only for an isolated pre-fix source snapshot. It demonstrates the
three original session-only paths and intentionally fails against patched code.

The synthetic manager isolates controller ordering and side effects; it does not
prove cookie/antiforgery middleware, real password hashing, persistence,
window replenishment, or production/browser behavior. Those integration checks
remain separate.
