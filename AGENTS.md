# AGENTS.md — working rules for this repository

Instructions for any automated agent or new contributor. Read this before changing code.

---

## 1. Non-negotiable constraints

These come from the product mandate and are not open to reinterpretation.

1. **Persian is the primary UI language.** All user-facing strings are Persian. Code,
   identifiers, comments, log messages, and commit messages are English.
2. **Full RTL.** `<html lang="fa" dir="rtl">` and `body { direction: rtl }`. Never introduce
   a left-to-right layout assumption. Use logical properties (`margin-inline-start`, not
   `margin-left`) in new CSS.
3. **Jalali dates in the UI, UTC in storage.** Every `DateTime` persisted is UTC. Every
   displayed date goes through `PersonalResearchLab.Web.Formatting.JalaliDate`. Never format
   a date inline with `ToString("yyyy/MM/dd")`.
4. **Default time zone `Asia/Tehran`.** `JalaliDate.DefaultTimeZone` resolves
   `Asia/Tehran` → `Iran Standard Time` → UTC, in that order.
5. **Never use `EnsureCreated()`.** Schema changes go through EF Core migrations.
6. **Every visible interactive element must do something real or be visibly disabled.**
   No decorative buttons, no dead links, no placeholder handlers.
7. **Never fabricate facts, citations, sources, results, or confidence numbers.** If the
   system does not know, it must say so. The companion's refusal path is a feature, not a
   fallback to remove.
8. **Do not claim something works without verifying it.** Build, run, and exercise the path.

---

## 2. Architecture rules

Dependency direction is one-way and enforced by project references:

```
Domain  <-  Application  <-  Infrastructure  <-  Web
```

- `Domain` has **zero** package references. Keep it that way.
- `Application` may reference `Domain` and `Microsoft.EntityFrameworkCore` (for `DbSet`
  access through `IAppDbContext`) only. It must **not** reference `Infrastructure`.
- If `Application` needs a capability implemented in `Infrastructure`, declare the interface
  in `Application/Abstractions` and implement it in `Infrastructure`. `ISecretStore` is the
  canonical example.
- `Web` is the composition root. All DI registration lives in
  `src/PersonalResearchLab.Web/ServiceRegistration.cs`. Do not scatter `AddScoped` calls.

### DI lifetime rules

| Lifetime | Use for |
| --- | --- |
| Singleton | Stateless services, `IFileStorage`, `ISecretStore`, tokenizer, extractors, the job queue |
| Scoped | Anything touching `IAppDbContext`, all Application services, `WorkspaceContext` |
| Hosted service | `IngestionWorker` |

A singleton must never capture a scoped service. `BackgroundJobQueue` takes
`IServiceScopeFactory` for exactly this reason — it creates a scope per work item.

---

## 3. Known pitfalls in this codebase

Each of these has already caused a real build failure. Do not repeat them.

### EF Core

- **`HasOne<T>()` without a navigation selector creates a second relationship** and a
  duplicate shadow foreign key (`AiConversationId1`, `QuizId1`). Always use
  `HasOne(x => x.Navigation)`.
- **`dotnet ef` cannot use the Web project as startup** in this solution. Use
  `--project src\PersonalResearchLab.Infrastructure --startup-project src\PersonalResearchLab.Infrastructure`
  so `AppDbContextFactory` is picked up.
- **`ResearchReportVersion` has a property named `Markdown`**, not `MarkdownBody`.

### Razor

- **Never name an injected property the same as the page class.** `@inject CompanionService
  Companion` inside `Companion.razor` produces `CS0542`. Use `CompanionApi`, `SettingsApi`,
  `WorkspaceApi`, etc.
- **Never name a page file after an Application record type.** `SourceDetail.razor` shadowed
  `Application.Sources.SourceDetail` and produced 28 cascading errors. The page is
  `SourceView.razor`.
- **Razor directive names are reserved as local variable names**: `page`, `code`, `inherits`,
  `inject`, `layout`, `using`, `implements`, `attribute`, `typeparam`. A local `var page`
  inside `@{ }` triggers `RZ2005`. Use `currentPage`.
- **`@page` must start at column 0.** Leading whitespace breaks the directive.

### Blazor rendering

- Unmatched URLs never reach the Blazor router, so `<NotFound>` in `Routes.razor` cannot
  render them. `Program.cs` calls `UseStatusCodePagesWithReExecute("/not-found")` and
  `Components/Pages/NotFound.razor` is a real routable page.
- `UseStatusCodePagesWithReExecute` in .NET 9 has **no** `createScopeForStatusCodePages`
  parameter. Passing it is a compile error.

### PowerShell on this machine

The interactive session intermittently loses `PSModulePath` and parser state. Symptoms:
`$env:Path : The term ... is not recognized`, `The ampersand (&) character is not allowed`,
`dotnet : The term 'dotnet' is not recognized`.

Reliable pattern:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build PersonalResearchLab.sln 2>&1 |
  Out-File -FilePath build.log -Encoding utf8
```

Then read `build.log`. Retry the identical command once if a parser error appears — it
usually succeeds on the second attempt. Never rely on bare `dotnet` being on `PATH`.

Always write build logs with `-Encoding utf8`. Plain `>` redirection produces UTF-16, which
the file readers cannot parse.

### Build file locks

`MSB3027` / `MSB3021` means a leftover `PersonalResearchLab.Web` process still holds the
output DLL. Check and stop it before rebuilding:

```powershell
Get-Process -Name "PersonalResearchLab.Web" -ErrorAction SilentlyContinue | Stop-Process -Force
```

---

## 4. Code conventions

- File-scoped namespaces. Nullable enabled. Implicit usings enabled.
- `sealed` on service classes unless inheritance is intended.
- Async methods take `CancellationToken cancellationToken = default` and pass it down.
- Application services return **records** (immutable view models), never entities, to the UI.
- Never return a secret value from any service. `SettingsService` reports `HasSecret` only.
- Persian display strings for domain enums live in `Web/Formatting/Labels.cs`. Do not
  hardcode Persian enum names inside pages.
- Comments explain *why*, not *what*. Do not narrate obvious code.

---

## 5. Verification checklist before declaring work done

1. `Get-Process -Name "PersonalResearchLab.Web"` returns nothing.
2. Build is clean: `0 Warning(s)`, `0 Error(s)`.
3. The app starts and every route returns HTTP 200 (or 404 for a deliberately bad path).
4. Any new page renders real data, not a hardcoded mock.
5. Any new interactive control has a working handler or a `disabled` attribute with a reason.
6. Documentation updated if behaviour, configuration, or schema changed.

---

## 6. Where things live

| Concern | Location |
| --- | --- |
| Entities and enums | `src/PersonalResearchLab.Domain` |
| Service interfaces | `src/PersonalResearchLab.Application/Abstractions` |
| Application services | `src/PersonalResearchLab.Application/<Feature>` |
| EF Core context and config | `src/PersonalResearchLab.Infrastructure/Persistence` |
| Migrations | `src/PersonalResearchLab.Infrastructure/Persistence/Migrations` |
| AI providers and resolver | `src/PersonalResearchLab.Infrastructure/Ai` |
| Retrieval | `src/PersonalResearchLab.Infrastructure/Retrieval` |
| Text extraction | `src/PersonalResearchLab.Infrastructure/Extraction` |
| Background queue and worker | `src/PersonalResearchLab.Infrastructure/Jobs` |
| DI composition | `src/PersonalResearchLab.Web/ServiceRegistration.cs` |
| Pages | `src/PersonalResearchLab.Web/Components/Pages` |
| Design system | `src/PersonalResearchLab.Web/wwwroot/app.css` |
| Persian labels | `src/PersonalResearchLab.Web/Formatting/Labels.cs` |
| Jalali dates | `src/PersonalResearchLab.Web/Formatting/JalaliDate.cs` |
