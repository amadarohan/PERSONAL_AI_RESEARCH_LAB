# رصدخانه دانش — Personal AI Research Lab

A local-first, Persian-first (RTL) personal research and knowledge-intelligence platform.
Codename: **THE KNOWLEDGE OBSERVATORY**.

The application ingests your own documents, builds a searchable evidence base, answers
questions strictly from that evidence, and keeps every claim traceable to a page.

---

## What this is

A single-user research workbench. You create **workspaces** (one per research topic), add
**sources** (PDF, DOCX, HTML, Markdown, plain text, CSV, JSON), and the platform:

1. extracts text page by page,
2. segments it into overlapping, sentence-aware chunks,
3. builds a Persian/English lexical inverted index,
4. generates embeddings and stores them as packed float32 blobs,
5. retrieves evidence with hybrid lexical + vector search fused by reciprocal rank,
6. answers questions with citations, or refuses when evidence is insufficient,
7. runs a multi-step research plan over local evidence only,
8. tracks claims, evidence stances, conflicts, and a knowledge graph,
9. keeps a timeline, versioned reports, and a spaced-repetition learning loop.

Everything runs on your machine. No cloud call happens unless you explicitly configure a
provider and mark the workspace as cloud-allowed.

---

## Requirements

| Component | Version used and verified |
| --- | --- |
| .NET SDK | 9.0.311 (`global.json` pins it, `rollForward: latestFeature`) |
| Target framework | `net9.0` |
| Database | SQL Server 2019 (15.0.2210.4) — verified against a local default instance |
| EF Core | 9.0.23 (`dotnet-ef` 9.0.23 for migrations) |
| OS verified | Windows (DPAPI secret encryption is Windows-only) |

SQL Server 2019 has **no native `VECTOR` type**. Embeddings are therefore stored as packed
`varbinary` and scored in-process. See `docs/VECTOR_STORAGE.md` for the trade-off.

---

## Getting started

### 1. Configure the connection string

`src/PersonalResearchLab.Web/appsettings.json` ships with integrated-security defaults:

```json
"ConnectionStrings": {
  "Default": "Server=localhost;Database=PersonalResearchLab;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

Override it without editing the file by setting an environment variable:

```powershell
$env:ConnectionStrings__Default = "Server=YOUR_SERVER;Database=PersonalResearchLab;Trusted_Connection=True;TrustServerCertificate=True"
```

### 2. Create the database with migrations

`EnsureCreated()` is never used. The schema comes from migrations only.

```powershell
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure
```

The design-time factory (`AppDbContextFactory`) exists so `dotnet ef` does not need to boot
the web host.

### 3. Run

```powershell
dotnet run --project src\PersonalResearchLab.Web
```

Then open the URL printed in the console (the `http` profile uses `http://localhost:5103`).

### 4. Build only

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

The script writes `build-output.log` and prints a compact summary plus a deduplicated error
list. `build-output.log` is git-ignored.

---

## First run: what to do

1. Open **فضاهای کاری** (`/workspaces`) and create a workspace. It defaults to
   `DataRoute = LocalOnly` and `PreferredLanguage = fa-IR`.
2. Open **منابع** (`/sources`) and upload a document, or create a manual note.
3. Watch **پایش پردازش** (`/monitor`) — the ingestion job shows eight named stages.
4. Open **جست‌وجوی شواهد** (`/search`) and search. Each hit shows its lexical score, vector
   score, fused score, and the reason it was retrieved.
5. Open **همراه پژوهش** (`/companion`) and ask a question. With no provider configured the
   companion refuses explicitly instead of inventing an answer.
6. Open **تنظیمات** (`/settings`) to add an AI provider and model.

To exercise the whole product without any network access, add a provider of kind
**Deterministic (offline)**. It produces deterministic, clearly-labelled output and never
leaves the machine.

---

## Configuration reference

| Key | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Default` | local SQL Server, integrated auth | Database connection |
| `Storage:RootPath` | `storage/files` | Where uploaded files are written |
| `Storage:MaxFileSizeBytes` | `104857600` (100 MB) | Upload size ceiling |
| `Secrets:FilePath` | `storage/secrets/secrets.bin` | DPAPI-protected secret container |
| `AllowedHosts` | `localhost;127.0.0.1` | Host header allow-list |

`Storage:RootPath` and `Secrets:FilePath` are resolved relative to the content root when
they are relative paths.

---

## Project layout

```
PersonalResearchLab.sln
global.json                  SDK pin
NuGet.config                 clears inherited feeds, pins nuget.org
scripts/build.ps1            build + compact log
src/
  PersonalResearchLab.Domain/          entities, enums, invariants
  PersonalResearchLab.Application/     services, DTOs, abstractions
  PersonalResearchLab.Infrastructure/  EF Core, storage, AI, retrieval, jobs
  PersonalResearchLab.Web/             Blazor Web App (Interactive Server)
docs/                                  architecture and operations documentation
```

Dependency direction is strictly one-way:

```
Domain  <-  Application  <-  Infrastructure  <-  Web
```

`Application` never references `Infrastructure`. That is why `ISecretStore` lives in
`Application/Abstractions` even though its only implementation is in `Infrastructure/Ai`.

---

## Documentation

| Document | Contents |
| --- | --- |
| `docs/ARCHITECTURE.md` | Layers, composition root, request and job flows |
| `docs/DATA_MODEL.md` | Every entity, relationship, and index decision |
| `docs/RETRIEVAL.md` | Tokenizer, chunker, lexical index, fusion, refusal policy |
| `docs/VECTOR_STORAGE.md` | Why vectors are blobs, and the scaling limit |
| `docs/AI_PROVIDERS.md` | Provider kinds, data routing, secret handling |
| `docs/SECURITY.md` | Threat model, secret storage, input handling |
| `docs/OPERATIONS.md` | Runbook: migrations, logs, recovery, troubleshooting |
| `docs/DECISIONS.md` | Decision log with rationale and rejected alternatives |
| `docs/UI_GUIDE.md` | Design tokens, RTL rules, Jalali dates, component classes |
| `docs/ACCEPTANCE_SCENARIO.md` | The end-to-end scenario and its verification steps |
| `docs/ROADMAP.md` | What is deliberately not implemented, stated plainly |
| `docs/DEVELOPMENT.md` | Setup, recipes, debugging playbook, verification checklist |
| `docs/API_REFERENCE.md` | Every Application service and abstraction, with real signatures |
| `docs/GLOSSARY.md` | Persian terms mapped to code identifiers |
| `CONTRIBUTING.md` | Workflow, review rules, documentation expectations |

---

## Honest status

Implemented and verified: ingestion pipeline, hybrid retrieval, companion with refusal,
deep research over local evidence, claims and evidence review, knowledge graph, timeline,
versioned reports, spaced repetition, job monitoring, settings, Jalali formatting, RTL shell.

Not implemented, and labelled as such in the UI: web search, reranking, OCR, automatic claim
extraction, automatic knowledge-graph extraction, automatic report generation from findings,
automatic learning-path generation, multi-user authentication.

See `docs/ROADMAP.md` for the full list with the reason for each omission.
