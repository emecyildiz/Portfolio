# Client assets

Tailwind, fonts, EasyMDE, and vis-network are built or copied into `wwwroot` so the running site does not depend on third-party CDNs.

After changing Tailwind classes or dependency versions, run:

```powershell
npm.cmd install
npm.cmd run assets:build
```

The generated files are committed intentionally so Visual Studio and local .NET builds work without an additional Node step. Docker rebuilds them from `package-lock.json` for production consistency.
