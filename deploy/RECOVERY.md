# Emecworks recovery bundle

The daily server backups contain the portfolio, n8n, CTI, and Ratemet
databases; portfolio uploads and ASP.NET data-protection keys; and n8n data and
persistent files. A complete disaster recovery also requires the root-only
environment files stored under `/etc/emecworks`.

## Export an encrypted off-site copy

Run this command from a normal interactive PowerShell terminal:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\export-recovery-bundle.ps1
```

The script:

1. Verifies and packages the newest completed Emecworks and Ratemet backups
   together with the seven production environment files on the VPS.
2. Downloads that temporary package over SSH.
3. Uses 7-Zip header encryption and asks for a password in the terminal.
4. Tests the encrypted archive.
5. Removes the temporary plaintext files from the computer and VPS.

The export refuses an older pre-CTI Emecworks backup. The newest main backup
must use `backup_format=3`, contain `cti-database.dump`, and pass its recorded
SHA256 checks before it can be exported.

The encrypted archive is written to:

```text
%USERPROFILE%\Documents\Emecworks-Recovery
```

Use a unique passphrase of at least five random words. Do not reuse an account
password. Store the passphrase separately from the archive. Keep at least one
additional copy on another device or trusted cloud storage.

Create a new recovery bundle whenever production secrets change. Also create
one periodically so the off-site database copy remains current.

Never commit a recovery archive, extracted environment file, or recovery
passphrase to Git.

## High-level restore order

1. Provision a clean Ubuntu server and install Docker.
2. Clone the portfolio and Ratemet repositories at their recorded production
   commits.
3. Decrypt the recovery archive on a trusted computer.
4. Transfer the inner package to the new server over SSH.
5. Restore the `/etc/emecworks` files as `root:root` with mode `0600`.
6. Recreate the Docker volumes and restore the four PostgreSQL dumps.
7. Restore uploads, ASP.NET data-protection keys, n8n data, and n8n files.
8. Start the portfolio, CTI database, n8n, Ratemet, and Cloudflare Tunnel
   Compose projects and validate their health checks and network boundaries.
9. Rotate the Cloudflare tunnel token and externally exposed credentials after
   recovery.

Perform the detailed restore against an isolated test environment before using
it on production data.
