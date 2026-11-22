# Development guide

How to work on this codebase: set it up, change it safely, and verify the change. This is the
practical companion to `AGENTS.md` (hard rules) and `docs/ARCHITECTURE.md` (design).

---

## 1. Prerequisites

| Component | Version verified | Notes |
| --- | --- | --- |
| .NET SDK | 9.0.311 | `global.json` pins it with `rollForward: latestFeature` |
| `dotnet-ef` | 9.0.23 | `dotnet tool install --global dotnet-ef --version 9.0.23` |
| SQL Server | 2019 (15.0.2210.4) | Any edition; a local default instance is assumed |
| OS | Windows | DPAPI secret encryption is Windows-only |

SQL Server 2019 has no native `VECTOR` type. That single constraint shapes the retrieval
design; read `docs/VECTOR_STORAGE.md` before proposing a change to embedding storage.

---

## 2. First-time setup

```powershell
# 1. Restore and build
dotnet build PersonalResearchLab.sln

# 2. Create the schema (migrations only — EnsureCreated is never used)
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure

# 3. Run
dotnet run --project src\PersonalResearchLab.Web
```

The `http` launch profile uses `http://localhost:5103`. If that port is taken, pass
`--urls http://localhost:5199`.

### Why `--startup-project` points at Infrastructure

`dotnet ef` normally boots the web host to find a `DbContext`. This solution ships
`AppDbContextFactory` (an `IDesignTimeDbContextFactory<AppDbContext>`) inside Infrastructure so
the tooling never needs to start Blazor. Pointing `--startup-project` at the Web project fails
in this solution; use Infrastructure for both arguments.

---

## 3. Daily loop

```powershell
# Build with a compact, greppable log
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

The script writes `build-output.log` (git-ignored) and prints a summary plus a deduplicated
error list. Prefer it over raw `dotnet build` when the output is long.

Before rebuilding, make sure no previous run still holds the output DLL:

```powershell
Get-Process -Name "PersonalResearchLab.Web" -ErrorAction SilentlyContinue | Stop-Process -Force
```

A leftover process produces `MSB3027` / `MSB3021` / `MSB3026` file-lock errors that look like
build failures but are not.

---

## 4. Solution map

```
src/PersonalResearchLab.Domain/          entities, enums, invariants — zero package references
  Common/Entity.cs                       base type: Id, CreatedAtUtc, UpdatedAtUtc, RowVersion
  Common/Enums.cs                        every domain enum in one file
  Sources/ Documents/ Evidence/ Knowledge/ Research/ Reports/ Learning/ Ai/ System/

src/PersonalResearchLab.Application/     services, DTOs, abstractions
  Abstractions/                          interfaces the Infrastructure layer implements
  <Feature>/<Feature>Service.cs          one service per feature area
  Text/PersianEnglishTokenizer.cs        tokenizer + ScriptLanguageDetector
  Ingestion/TextChunker.cs               sentence-aware chunker

src/PersonalResearchLab.Infrastructure/  implementations
  Persistence/AppDbContext.cs            EF Core context
  Persistence/EntityConfigurations.cs    fluent configuration for every entity
  Persistence/Migrations/                migrations
  Ai/                                    providers, resolver, secret store, embeddings
  Retrieval/HybridRetrievalService.cs    lexical + vector fusion
  Retrieval/SqlVectorIndex.cs            vector storage over varbinary
  Extraction/TextExtractors.cs           PDF, DOCX, HTML, plain text, image
  Jobs/                                  channel queue + hosted worker
  Storage/LocalFileStorage.cs            file storage with traversal rejection

src/PersonalResearchLab.Web/             composition root and UI
  Program.cs                             pipeline
  ServiceRegistration.cs                 the only place DI is configured
  Components/Pages/                      routable pages
  Components/Layout/                     MainLayout, NavMenu
  Formatting/JalaliDate.cs               UTC -> Jalali display
  Formatting/Labels.cs                   Persian labels for every enum
  Services/WorkspaceContext.cs           active workspace, per circuit
  Realtime/JobHub.cs                     SignalR hub
  wwwroot/app.css                        design system
```

Dependency direction is one-way and enforced by project references:

```
Domain  <-  Application  <-  Infrastructure  <-  Web
```

`Application` must never reference `Infrastructure`. If Application needs a capability that
lives in Infrastructure, declare the interface in `Application/Abstractions` and implement it
downward. `ISecretStore` is the canonical example.

---

## 5. Recipes

### Add a page

1. Create `Components/Pages/YourPage.razor`. **Do not name it after an Application record
   type** — `SourceDetail.razor` once shadowed `Application.Sources.SourceDetail` and produced
   28 cascading errors. The page is `SourceView.razor` for that reason.
2. Put `@page "/your-route"` at **column 0**. Leading whitespace breaks the directive.
3. Add `@rendermode InteractiveServer`.
4. Inject the service under a name that differs from the page class. `@inject CompanionService
   Companion` inside `Companion.razor` is `CS0542`. Use `CompanionApi`, `SettingsApi`, etc.
5. Add the namespace to `Components/_Imports.razor` if it is new.
6. Add a `NavItem` to the `NavItems` array in `NavMenu.razor` with an icon, a full label, and a
   short label.
7. Use `JalaliDate` for every date and `Labels` for every enum. Never format a date inline and
   never hardcode a Persian enum name in a page.
8. Verify the route returns 200 and renders Persian content.

Reserved Razor directive names cannot be used as local variables: `page`, `code`, `inherits`,
`inject`, `layout`, `using`, `implements`, `attribute`, `typeparam`. A local `var page` inside
`@{ }` triggers `RZ2005`; use `currentPage`.

### Add an Application service

1. Create `Application/<Feature>/<Feature>Service.cs`, `sealed`.
2. Take `IAppDbContext` and any abstractions in the constructor. Never take a concrete
   Infrastructure type.
3. Return **records**, never entities. The UI must not receive a tracked entity.
4. Async methods take `CancellationToken cancellationToken = default` and pass it down.
5. Register it in `ServiceRegistration.AddApplicationServices` as `AddScoped`.
6. Use `AsNoTracking()` for read-only queries.

### Add an entity and a migration

1. Add the class in `Domain/<Area>/` deriving from `Entity`.
2. Add a `DbSet<T>` to `Application/Abstractions/IAppDbContext.cs` **and** to
   `Infrastructure/Persistence/AppDbContext.cs`.
3. Configure it in `Infrastructure/Persistence/EntityConfigurations.cs`.
4. Create the migration:

```powershell
dotnet ef migrations add YourMigrationName `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure
```

5. Apply it:

```powershell
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure
```

**Relationship configuration trap.** `HasOne<T>()` without a navigation selector creates a
*second* relationship and a duplicate shadow foreign key (`AiConversationId1`, `QuizId1`).
Always use the navigation selector:

```csharp
// Wrong — creates a duplicate shadow FK
builder.HasOne<AiConversation>().WithMany().HasForeignKey(x => x.AiConversationId);

// Right
builder.HasOne(x => x.Conversation).WithMany(c => c.Messages).HasForeignKey(x => x.AiConversationId);
```

### Add a text extractor

1. Implement `ITextExtractor` in `Infrastructure/Extraction/TextExtractors.cs`.
2. `CanHandle(SourceKind kind, string? fileName)` decides applicability.
3. Return `ExtractionResult` with page boundaries preserved. If the format cannot be read,
   return a `Warning` — never return fabricated text.
4. Register it as `AddSingleton<ITextExtractor, YourExtractor>()` in
   `ServiceRegistration.AddTextProcessing`.

### Add an AI provider kind

1. Add the value to `AiProviderKind` in `Domain/Common/Enums.cs`.
2. Implement `IChatCompletionProvider` and/or `IEmbeddingProvider` in
   `Infrastructure/Ai/AiProviders.cs`.
3. Wire it into `AiProviderResolver` so `DataRoute` is honoured. A `LocalOnly` workspace must
   never be served by a `CloudAllowed` provider.
4. Never throw for a missing key. Return a failed result so the UI can explain what is
   unconfigured.
5. Add the Persian label to `Labels.cs` and the settings page.

### Add a background job type

The queue is a bounded `Channel<IngestionWorkItem>` drained by `IngestionWorker`. Adding a new
work item type means extending the channel payload and the worker's dispatch. Keep the queue a
singleton and resolve scoped services through `IServiceScopeFactory` — a singleton capturing
`IAppDbContext` fails at startup.

---

## 6. Conventions

| Rule | Reason |
| --- | --- |
| File-scoped namespaces, nullable enabled, implicit usings | Consistent with the rest of the solution |
| `sealed` on services unless inheritance is intended | Prevents accidental extension points |
| Application services return records | The UI cannot mutate tracked entities |
| Persian for user-facing strings, English for code | Product mandate; keeps logs greppable |
| Logical CSS properties (`margin-inline-start`) | RTL must not depend on physical sides |
| UTC in storage, Jalali at the display boundary | One conversion point, no ambiguity |
| Comments explain *why* | The *what* is in the code |

---

## 7. Debugging playbook

### The app starts but a page is blank

Check the browser console and the terminal for a Blazor circuit error. A common cause is a
service that throws in `OnInitializedAsync`; the circuit dies and the page stops updating
without an HTTP error.

### `Cannot consume scoped service ... from singleton ...`

A singleton captured a scoped dependency. Resolve it per operation through
`IServiceScopeFactory` instead. `BackgroundJobQueue` does exactly this.

### `MSB3027` / `MSB3021` / `MSB3026`

A previous `PersonalResearchLab.Web` process still holds the DLL. Stop it and rebuild.

### `dotnet ef` cannot find a `DbContext`

You pointed `--startup-project` at the Web project. Use Infrastructure for both arguments.

### Persian search returns nothing

Check `ChunkTerms` for the workspace. SQL Server full-text ships only English and Arabic word
breakers, so the app maintains its own inverted index. If `ChunkTerms` is empty, ingestion did
not reach the lexical stage — check `/monitor` for the stage status.

### A stage shows a green check but did nothing

That was a real defect, fixed in D-021. `ProcessingStatus.Skipped` now exists and the monitor
renders it as `–` with a `رد‌شده` label. If you add a stage that can legitimately do no work,
return `Skipped` rather than `Succeeded`.

### PowerShell loses `PSModulePath` or parser state

Symptoms: `$env:Path : The term ... is not recognized`, `The ampersand (&) character is not
allowed`, `dotnet : The term 'dotnet' is not recognized`. Use the absolute path and write logs
explicitly as UTF-8:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build PersonalResearchLab.sln 2>&1 |
  Out-File -FilePath build.log -Encoding utf8
```

Plain `>` redirection produces UTF-16, which the file readers cannot parse. Retry the identical
command once if a parser error appears; it usually succeeds on the second attempt.

---

## 8. Verification checklist

Do not declare work done until all of these hold.

1. `Get-Process -Name "PersonalResearchLab.Web"` returns nothing.
2. Build is clean: `0 Warning(s)`, `0 Error(s)`.
3. The app starts and every route returns HTTP 200 (or 404 for a deliberately bad path).
4. Any new page renders real data, not a hardcoded mock.
5. Any new interactive control has a working handler or a `disabled` attribute with a reason.
6. Documentation updated if behaviour, configuration, or schema changed.

There is currently **no automated test project**. Verification is manual and must be stated as
such. See `docs/ROADMAP.md` §13.

---

## 9. Where to look when stuck

| Question | Document |
| --- | --- |
| How are the layers wired? | `docs/ARCHITECTURE.md` |
| What does this table mean? | `docs/DATA_MODEL.md` |
| How does retrieval score a hit? | `docs/RETRIEVAL.md` |
| Why are vectors blobs? | `docs/VECTOR_STORAGE.md` |
| How is data routing enforced? | `docs/AI_PROVIDERS.md` |
| What is the threat model? | `docs/SECURITY.md` |
| How do I recover a broken database? | `docs/OPERATIONS.md` |
| Why was this built this way? | `docs/DECISIONS.md` |
| What are the CSS and RTL rules? | `docs/UI_GUIDE.md` |
| What is deliberately missing? | `docs/ROADMAP.md` |
| What do the Persian terms mean? | `docs/GLOSSARY.md` |
| What is the service API? | `docs/API_REFERENCE.md` |
