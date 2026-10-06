# Uploaded media visibility regression

Run from the repository root:

```powershell
dotnet run --project deploy/tests/media-access-security/MediaAccessSecurity.csproj
```

The console fixture uses the real `AppDbContext` query filters with a disposable
SQLite database, real upload controllers/MediaService, Kestrel bound to an ephemeral
loopback port, and the actual upload guard followed by ASP.NET static serving.
It does not start Portfolio, contact a production database, send provider/bot calls,
or modify Docker resources. A synthetic identity header exists only in this test.
Generated files are in a uniquely named OS temporary directory; no recursive cleanup
of user folders is performed.

`--baseline` reproduces the original unguarded static pipeline and a public CSS
control. The normal run proves visibility revocation, authenticated preview,
category/disclosure rules, deleted/orphan owners, cache/HEAD/range/conditional
semantics, raw repeated/encoded/separator aliases, legacy URL/Markdown/topology
compatibility, request-Host independence and topology ownership/deletion.

The SQLite dependency is test-only. Production remains PostgreSQL; the fixture also
checks Npgsql SQL translation without opening a PostgreSQL connection. Production
cookie/antiforgery integration, Linux filesystem behavior, reverse-proxy/CDN cache
configuration and the inventory of actual old files require deployment acceptance.

## Deployment contract

- Keep the sole static-file middleware **after** authentication and the upload guard.
  Never expose the uploads volume from a second file server/CDN origin.
- Keep existing upload paths/volume/backups. No file relocation or database migration
  is required. Unknown/orphaned legacy files are anonymous 404; nothing is deleted.
- `MediaStorage:PublicOrigins` lists explicit owned HTTPS origins, including aliases
  used in old absolute URLs. Defaults are `https://emecworks.com` and
  `https://www.emecworks.com`; add an actual development/alternate deployment origin
  explicitly. Ownership never comes from the request's Host header. External origin
  references do not authorize a local file.
- Public detail routes are one directory deep; relative `../uploads/...` references
  resolve the same way as in the browser. Root-relative links are recommended.
- Any restricted owner blocks a shared **legacy** file. A public page cannot override
  a private authoritative Media owner. New topology uploads have an owner-specific
  Media record and require an active saved topology reference before public access.
- Responses to uploads use private/no-store headers. Purge previously cached upload
  URLs at any caching proxy during deployment: prior downloads/cached copies cannot
  be recalled by an application change.
