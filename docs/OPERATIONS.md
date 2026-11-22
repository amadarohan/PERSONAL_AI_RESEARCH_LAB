# Operations runbook

## 1. Prerequisites

| Requirement | Verified version |
| --- | --- |
| .NET SDK | 9.0.311 |
| Target framework | `net9.0` |
| SQL Server | 2019 (RTM-GDR) 15.0.2210.4 Enterprise, instance `MSSQLSERVER` |
| `dotnet-ef` | 9.0.23 (global tool) |
| OS | Windows (verified); Linux/macOS expected to work with a SQL Server connection string |

`global.json` pins the SDK with `"rollForward": "latestFeature"`, so a newer 9.0.x patch is
accepted but a 10.x SDK is not silently used.

---

## 2. First run

```powershell
# 1. Restore and build
dotnet build PersonalResearchLab.sln

# 2. Create or update the database
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure

# 3. Run
dotnet run --project src\PersonalResearchLab.Web
```

Both `--project` and `--startup-project` are required. `AppDbContextFactory` exists so `dotnet
ef` builds the model without booting the web host; pointing `--startup-project` at Web would
work but is slower and can fail if the host has startup dependencies.

Default URLs come from `Properties/launchSettings.json`:

- `http` profile: `http://localhost:5103`
- `https` profile: `https://localhost:7272` and `http://localhost:5103`

---

## 3. Connection string

Default in `appsettings.json`:

```
Server=localhost;Database=PersonalResearchLab;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

Override without editing the file:

```powershell
$env:ConnectionStrings__Default = "Server=.;Database=PersonalResearchLab;Trusted_Connection=True;TrustServerCertificate=True"
```

The double underscore is the ASP.NET Core configuration separator for `ConnectionStrings:Default`.

`MultipleActiveResultSets=True` is required: several services issue a query while iterating
another result set.

---

## 4. Build script

```powershell
.\scripts\build.ps1
```

Writes `build-output.log` (UTF-8) and prints:

```
=== SUMMARY ===
Build succeeded / FAILED
Warning(s): n
Error(s): n
=== ERRORS (n) ===
```

The script prepends `C:\Program Files\dotnet` to `PATH` so it works even when the shell has
lost its environment.

---

## 5. Common failures

### `MSB3027` / `MSB3021` — file is locked

**Symptom**

```
error MSB3027: Could not copy "...\PersonalResearchLab.Web.dll" to "...". Exceeded retry count of 10.
error MSB3021: Unable to copy file ... The process cannot access the file because it is being used by another process.
```

**Cause** A previous `PersonalResearchLab.Web` process is still running and holding the output
DLL.

**Fix**

```powershell
Get-Process -Name "PersonalResearchLab.Web" -ErrorAction SilentlyContinue | Stop-Process -Force
```

Run this **before** every build. It is the single most common build failure in this repo.

### `dotnet` not recognized

The PowerShell session has lost `PATH`. Use the absolute path:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build PersonalResearchLab.sln
```

### `NU1301` / HTTP 401 from a package feed

`NuGet.config` clears inherited sources and pins `nuget.org` with
`packageSourceMapping` pattern `*`. If a machine-wide `Nexus` feed is configured and returns
401, the clear is what prevents it from being consulted. Do not remove the `<clear />`.

### `dotnet ef` cannot build the model

Use both flags:

```powershell
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure
```

### App starts but every page is blank

Check the database exists and migrations are applied:

```powershell
sqlcmd -S localhost -E -Q "SELECT COUNT(*) FROM PersonalResearchLab.sys.tables"
```

Expected: `42`.

### `Failed to determine the https port for redirect`

Harmless when running the `http` profile. `UseHttpsRedirection` logs a warning because no
HTTPS port is configured. It does not affect functionality.

---

## 6. Database operations

### Apply migrations

```powershell
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure
```

### Add a migration

```powershell
dotnet ef migrations add <Name> `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure `
  --output-dir Persistence\Migrations
```

`EnsureCreated()` is never used. Every schema change is a migration.

### Inspect table count

```powershell
sqlcmd -S localhost -E -d PersonalResearchLab -Q "SELECT COUNT(*) FROM sys.tables"
```

### Reset the database

**This deletes all data.** Confirm before running.

```powershell
sqlcmd -S localhost -E -Q "ALTER DATABASE PersonalResearchLab SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE PersonalResearchLab;"
dotnet ef database update --project src\PersonalResearchLab.Infrastructure --startup-project src\PersonalResearchLab.Infrastructure
```

---

## 7. Storage

| Setting | Default | Meaning |
| --- | --- | --- |
| `Storage:RootPath` | `storage/files` | Uploaded files, partitioned `yyyy/MM/` |
| `Storage:MaxFileSizeBytes` | `104857600` | 100 MB |
| `Secrets:FilePath` | `storage/secrets/secrets.bin` | DPAPI-protected secret container |

Both paths are relative to the content root (`src/PersonalResearchLab.Web` when run with
`dotnet run --project`).

### Reset the secret store

**This deletes all stored API keys.** Confirm before running.

```powershell
Remove-Item src\PersonalResearchLab.Web\storage\secrets\secrets.bin -Force
```

The app recreates the file on the next write. Providers keep their `ApiKeySecretName`
references, so they will report `HasSecret = false` until keys are re-entered on the settings
page.

### Delete stored files

**This deletes uploaded source files.** Confirm before running.

```powershell
Remove-Item src\PersonalResearchLab.Web\storage\files -Recurse -Force
```

Database rows remain and will reference missing files. Prefer deleting sources through the UI.

---

## 8. Background processing

Ingestion runs on an in-process bounded channel (capacity 256, single reader) drained by
`IngestionWorker : BackgroundService`.

### A source is stuck in `Running`

1. Open `/monitor` and find the job.
2. Check `CurrentStage` — it names the stage that did not finish.
3. Check `FailureReason`.
4. If the job is genuinely hung, press cancel. This sets `CancellationRequested`; the worker
   observes it at the next stage boundary.
5. If the process was killed mid-job, the job row stays `Running` forever. Restart the app and
   use **Reprocess** on the source, which creates a fresh job.

### Cancel a job

`JobMonitorService.CancelAsync` calls `IBackgroundJobQueue.RequestCancellationAsync`. The
worker checks the flag between stages, so cancellation is cooperative and may take a moment.

### Reprocess a source

`SourceService.ReprocessAsync` creates a new `ProcessingJob` and enqueues it. The previous
`SourceVersion` is retained; a new version is produced.

### Queue is full

`TryWrite` returns `false` when 256 items are pending. The upload path surfaces this as an
error rather than silently dropping the work.

---

## 9. Logs

| Destination | Content |
| --- | --- |
| Console | Structured ASP.NET Core logging |
| `build-output.log` | Build output from `scripts/build.ps1` |

Default levels in `appsettings.json`:

```
Default: Information
Microsoft.AspNetCore: Warning
Microsoft.EntityFrameworkCore.Database.Command: Warning
```

`Database.Command` is `Warning` deliberately: at `Information` every SQL statement, including
parameter values, is written to the log.

To debug a specific area, raise its category in `appsettings.Development.json` rather than
lowering the global level.

---

## 10. Health checks

There is no `/health` endpoint. Verify manually:

```powershell
# All routes should return 200
"/", "/workspaces", "/sources", "/search", "/companion", "/research",
"/knowledge", "/claims", "/timeline", "/reports", "/learning", "/monitor", "/settings" |
  ForEach-Object {
    $r = Invoke-WebRequest "http://localhost:5103$_" -UseBasicParsing -SkipHttpErrorCheck
    "{0,-14} {1}" -f $_, $r.StatusCode
  }

# Unknown routes should return 404 with the Persian not-found page
(Invoke-WebRequest "http://localhost:5103/does-not-exist" -UseBasicParsing -SkipHttpErrorCheck).StatusCode
```

Expected: thirteen `200`s and one `404`.

---

## 11. Deployment notes

This application is designed to run on the user's own machine. If it is ever hosted:

- Set `ASPNETCORE_ENVIRONMENT=Production` so HSTS and the exception handler are active.
- Set `ASPNETCORE_URLS` explicitly; do not rely on `launchSettings.json`.
- Change `AllowedHosts` to the real host name.
- Move the secret store off plaintext by running on Windows, or replace `ISecretStore`.
- Add authentication before exposing it to anyone else. There is none today.
- Point `Storage:RootPath` at a backed-up volume.
