# Decision log

Each entry records a decision that shaped the codebase, the alternatives that were rejected,
and the cost that was accepted. Decisions are append-only; superseding one means adding a new
entry that references the old.

---

## D-001 — Target `net9.0` with SDK 9.0.311

**Context** The machine has .NET SDK 9.0.311 installed.

**Decision** Target `net9.0` and pin the SDK in `global.json` with
`"rollForward": "latestFeature"`.

**Alternatives** Target `net8.0` (LTS, but not installed). Target `net10.0` (not installed).

**Consequence** `latestFeature` accepts newer 9.0.x patches but refuses a 10.x SDK, so a
machine with only .NET 10 installed fails fast with a clear message instead of building
against an unexpected runtime.

---

## D-002 — SQL Server 2019 as the only datastore

**Context** SQL Server 2019 Enterprise is installed and running locally.

**Decision** Use it for everything: relational data, the lexical index, and embeddings.

**Alternatives** SQLite (simpler, but no full-text parity and weaker concurrency). PostgreSQL
with pgvector (would solve vectors, but is not installed).

**Consequence** One database to back up, one connection string, one transaction boundary.
The cost is D-003.

---

## D-003 — Embeddings as packed `varbinary`, scored in-process

**Context** SQL Server 2019 has no `VECTOR` type. `VECTOR` and `VECTOR_DISTANCE` arrived in
SQL Server 2025.

**Decision** Store `float[]` as little-endian float32 in `varbinary`, unpack and cosine-score
in process.

**Alternatives** Add an external vector store (Qdrant, Milvus, pgvector). Upgrade the database.
Use a managed ANN library.

**Consequence** Query cost is O(n) in workspace embeddings. Viable to roughly 100k chunks;
not viable at a million. Documented in `docs/VECTOR_STORAGE.md` with the upgrade path.

---

## D-004 — Maintained inverted index instead of SQL Server full-text

**Context** SQL Server 2019 ships English and Arabic word breakers only. Neither segments
Persian correctly, so `CONTAINS`/`FREETEXT` give poor recall on Persian text.

**Decision** Build and maintain a `ChunkTerm` table (`DocumentChunkId`, `WorkspaceId`, `Term`,
`Frequency`) using our own tokenizer, and score with TF-IDF in application code.

**Alternatives** Use `CONTAINS` and accept poor Persian recall. Install a third-party Persian
word breaker (not available for this edition).

**Consequence** The index must be rebuilt when a document is reprocessed — which ingestion
already does as stage 6. In exchange, Persian tokenization is correct and the scoring formula
is visible and tunable.

---

## D-005 — `NuGet.config` clears inherited sources

**Context** Machine-wide NuGet configuration points at Nexus feeds at `nexus3.dotin.ir` that
return HTTP 401.

**Decision** `<clear />` all inherited sources and pin `nuget.org` with
`packageSourceMapping` pattern `*`.

**Alternatives** Fix the Nexus credentials. Use `--source` on every command.

**Consequence** Restore works without credentials. The repo is not portable to an environment
that requires an internal feed; that would need a new `NuGet.config`.

---

## D-006 — DPAPI for secrets, plaintext elsewhere

**Context** API keys must not sit in the database or in `appsettings.json`.

**Decision** `ISecretStore` backed by a single file. On Windows, `ProtectedData` with
`DataProtectionScope.CurrentUser`. Elsewhere, plaintext with a `LogWarning` on every write.

**Alternatives** ASP.NET Core Data Protection (key ring management is heavier). Azure Key Vault
(requires cloud, contradicts local-first). Environment variables (visible to every process).

**Consequence** On Windows the file is bound to the user account. Off Windows the keys are
readable by any local process — a documented weakness, logged loudly.

---

## D-007 — `ISecretStore` lives in Application, not Infrastructure

**Context** `SettingsService` (Application) needs to write secrets. Application must not
reference Infrastructure.

**Decision** Declare the interface in `Application/Abstractions/ISecretStore.cs`; implement it
in `Infrastructure/Ai/SecretStore.cs`.

**Alternatives** Put the interface in Infrastructure and let Application reference it —
violates the dependency direction. Put secret writing in Web — leaks a concern into the UI
layer.

**Consequence** The dependency arrow points inward, as intended. This is the reference example
of the rule for future abstractions.

---

## D-008 — `DataRoute` enforced in one place

**Context** Content must not leave the machine unless the user allows it. Scattering the check
across call sites guarantees one will be missed.

**Decision** `AiProviderResolver.FindConfigurationAsync` is the only place that decides. A
`LocalOnly` workspace never receives a `CloudAllowed` provider. When nothing qualifies,
resolution returns `null` and the caller refuses.

**Alternatives** Check in each service. Check in the UI. Default to cloud and let the user opt
out.

**Consequence** Every AI call inherits the guarantee for free. The cost is that a
misconfigured provider produces a refusal rather than a working call — which is the correct
failure mode.

---

## D-009 — In-process channel instead of a database-backed queue

**Context** Ingestion is long-running and must not block a request.

**Decision** A bounded `Channel<IngestionWorkItem>` (capacity 256, `SingleReader`) drained by
`IngestionWorker : BackgroundService`, each item in its own DI scope.

**Alternatives** Hangfire (extra dependency, extra tables). A `ProcessingJob` polling loop
(more DB churn, worse latency). `Task.Run` fire-and-forget (no backpressure, no cancellation).

**Consequence** Zero extra infrastructure and real backpressure via `TryWrite`. The cost is
that queued work is lost on process restart — the job row stays `Running` and must be
reprocessed. Acceptable for a single-user tool; documented in `docs/OPERATIONS.md`.

---

## D-010 — `Timer`-based monitor refresh, not SignalR

**Context** `/monitor` shows live job progress. SignalR is already wired for job notifications.

**Decision** The monitor page refreshes on a `Timer` and re-queries. SignalR notifications
drive the dashboard and research pages.

**Alternatives** A JavaScript SignalR client bridge on the monitor page.

**Consequence** No JS interop to maintain, and the page works even if the hub connection drops.
The cost is a small amount of redundant querying while the page is open.

---

## D-011 — `UseStatusCodePagesWithReExecute` plus a real `/not-found` page

**Context** Unmatched URLs never reach the Blazor router, so a `<NotFound>` block in
`Routes.razor` cannot render them. The observed behaviour was `HTTP 404` with
`Content-Length: 0` — a blank Kestrel response.

**Decision** Add `app.UseStatusCodePagesWithReExecute("/not-found")` and make
`NotFound.razor` a routable page with `@page "/not-found"`.

**Alternatives** A `<NotFound>` block alone (does not work — verified). A custom middleware
(duplicates what the framework provides).

**Consequence** A 404 returns the full Persian page (verified: 9969 bytes, contains
"این صفحه وجود ندارد"). The `<NotFound>` block in `Routes.razor` is retained for the router
contract but is not the mechanism that serves unmatched URLs.

Note: `createScopeForStatusCodePages` does not exist as a parameter in .NET 9 — passing it
produces `CS1739`.

---

## D-012 — No authentication

**Context** Single-user local tool.

**Decision** No login, no users table, no authorization.

**Alternatives** ASP.NET Core Identity (heavy, implies multi-user). A shared password (false
security).

**Consequence** Anyone with local access has full access. `AllowedHosts` restricts to
localhost. Adding auth is a prerequisite for any hosted deployment, and is listed as a gap in
`docs/SECURITY.md`.

---

## D-013 — Persian-first UI with Jalali dates, UTC storage

**Context** The primary user writes and reads Persian.

**Decision** `dir="rtl"` on `<html>`, Persian labels throughout, Jalali formatting at the
display boundary only. All timestamps stored and compared in UTC. Display timezone defaults to
`Asia/Tehran`.

**Alternatives** Store local time (breaks ordering across DST). Store Jalali strings (breaks
arithmetic).

**Consequence** `JalaliDate` is the single conversion point. `ToLocal` resolves
`Asia/Tehran` → `Iran Standard Time` → UTC, so it works on a machine with no Iran timezone
data.

---

## D-014 — Deterministic offline provider

**Context** The product must be demonstrable with no network and no credentials, without
pretending to be intelligent.

**Decision** `DeterministicFakeChatProvider` / `DeterministicFakeEmbeddingProvider`, reporting
`ProviderName = "Deterministic (offline)"`, `ModelName = "fake-chat-v1"` /
`"fake-embed-v1"`, `DataRoute = LocalOnly`.

**Alternatives** Require a real provider to use the app (blocks first run). A stub that
returns plausible prose (fabricates content — forbidden).

**Consequence** The full pipeline can be exercised offline, and the UI always shows that the
output came from a deterministic fake.

---

## D-015 — `Claim.Status` mutable only through `SetStatusAsync`

**Context** "Verified" must mean something. If any code path can set `Status`, the field is
decorative.

**Decision** `ClaimsService.SetStatusAsync` is the only writer. Setting `Supported` promotes
`EpistemicStatus` to `Verified` and stamps `VerifiedAtUtc`.

**Alternatives** Let services set `Status` directly. Derive status from evidence counts.

**Consequence** Verification is an explicit human act with a timestamp. Nothing an AI produces
can mark a claim verified.

---

## D-016 — `AiProposal` for human-in-the-loop

**Context** A model can suggest a claim, a relationship, or a report edit. Applying it
automatically would let generated content masquerade as reviewed content.

**Decision** Model suggestions are stored as `AiProposal` rows requiring acceptance.

**Alternatives** Apply suggestions directly with an `IsAiGenerated` flag.

**Consequence** Nothing enters the knowledge base unreviewed. The cost is an extra step for the
user, which is the point.

---

## D-017 — Modular monolith, not microservices

**Context** One user, one machine, one database.

**Decision** Four projects in one solution with a strict dependency direction:
Domain ← Application ← Infrastructure ← Web.

**Alternatives** Separate services (network boundaries with no benefit). A single project
(no enforced boundaries).

**Consequence** Boundaries are enforced by the compiler, not by convention. Domain has zero
package references; Application references only Domain plus EF Core abstractions and logging
abstractions.

---

## D-018 — `AppDbContextFactory` for design-time tooling

**Context** `dotnet ef` with the Web project as startup fails or is slow because it boots the
host.

**Decision** Add `IDesignTimeDbContextFactory<AppDbContext>` in Infrastructure and always pass
`--project` and `--startup-project` pointing at Infrastructure.

**Alternatives** Boot the web host for migrations (slow, fragile).

**Consequence** Migrations work without a running host. The command is longer, so it is
documented in `docs/OPERATIONS.md` and `README.md`.

---

## D-019 — `RowVersion` concurrency token on every entity

**Context** Blazor Server holds a circuit open; two tabs can edit the same row.

**Decision** `Entity.RowVersion` is an EF concurrency token, and `Touch()` updates
`UpdatedAtUtc`.

**Alternatives** Last-write-wins. Explicit locking.

**Consequence** A conflicting save throws `DbUpdateConcurrencyException` instead of silently
overwriting. `UpdatedAtUtc` is accurate because mutation paths call `Touch()`.

---

## D-020 — Documentation written from verified code, not from intent

**Context** The original specification was supplied as an attachment that is not recoverable
from the session transcript.

**Decision** Write every document by reading the actual source first, and state only what the
code does. Where something is not implemented, say so explicitly.

**Alternatives** Reconstruct the specification from memory (would invent requirements).
Write aspirational docs (would misrepresent the product).

**Consequence** The docs match the code. Every "not implemented" claim in `docs/ROADMAP.md`
corresponds to a real absence in the source.

---

## D-021 — A skipped stage must not render as a completed stage

**Context** `CompleteStepAsync` in `IngestionService` always wrote `ProcessingStatus.Succeeded`,
regardless of what the stage actually did. The embedding stage returns early when no embedding
provider is configured, so its detail text said `رد شد: ...` while its status badge said
`موفق` with a green check. The monitor page therefore showed a completed stage that had done
no work.

**Decision** `EmbeddingBatchResult` carries an explicit `Status`, and `CompleteStepAsync`
accepts an optional status that defaults to `Succeeded`. The coordinator returns `Skipped`
when no provider is resolvable or there is nothing to embed, and `Failed` when the provider
throws.

**Alternatives** Infer the status from the detail string (fragile, couples presentation to
logic). Mark the whole job `Skipped` (wrong: extraction and the lexical index did succeed).
Leave it as `Succeeded` and rely on the detail text (the badge is what users scan).

**Consequence** A stage that did no work is visually distinct from one that completed. The job
still reports `موفق` because the work that mattered did happen. Verified: stage 6 persists as
`5` (`Skipped`) and renders with a `–` marker and the label `رد‌شده`.
