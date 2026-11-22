# Architecture

## 1. Shape of the system

A modular monolith with four projects and a strict one-way dependency chain.

```
PersonalResearchLab.Domain          entities, enums, invariants. No package references.
        ^
PersonalResearchLab.Application     services, DTOs, abstractions. EF Core abstractions only.
        ^
PersonalResearchLab.Infrastructure  EF Core, storage, AI, retrieval, extraction, jobs.
        ^
PersonalResearchLab.Web             Blazor Web App, composition root, UI.
```

The chain is enforced by project references, not by convention. `Application` has no
reference to `Infrastructure`, so it is a compile error to leak a persistence detail upward.

### Why a monolith

The product is single-user and local-first. Splitting into services would add deployment
cost, network failure modes, and distributed-transaction problems with no benefit. The
modular boundaries are kept so a future split is mechanical rather than a rewrite.

---

## 2. Composition root

`src/PersonalResearchLab.Web/ServiceRegistration.cs` is the only place DI is configured.
`AddResearchLab` calls eight private extension methods in dependency order:

| Method | Registers |
| --- | --- |
| `AddPersistence` | `AppDbContext` (scoped), `IAppDbContext` (scoped alias) |
| `AddStorage` | `StorageOptions`, `IFileStorage` → `LocalFileStorage` (singleton) |
| `AddTextProcessing` | tokenizer, language detector, OCR stub, five extractors (all singleton) |
| `AddAi` | `SecretStoreOptions`, `ISecretStore` → `DpapiSecretStore` (singleton), named `ai` HttpClient, `IAiProviderResolver`, `IEmbeddingCoordinator` |
| `AddRetrieval` | `IVectorIndex` → `SqlVectorIndex`, `IRetrievalService` → `HybridRetrievalService` |
| `AddBackgroundWork` | `BackgroundJobQueue` (singleton), `IBackgroundJobQueue` alias, `IngestionWorker` hosted service |
| `AddApplicationServices` | all fourteen Application services plus `WorkspaceContext` (scoped) |
| `AddRealtime` | SignalR, `IJobNotifier` → `SignalRJobNotifier` |

Registration order follows the dependency direction so a missing registration fails at
startup rather than at first use.

### Lifetime rationale

`BackgroundJobQueue` is a singleton because it owns the `Channel<T>` that the worker drains.
It takes `IServiceScopeFactory`, not `IAppDbContext`, because a singleton capturing a scoped
service is a DI violation that .NET rejects at startup with
`Cannot consume scoped service 'IAppDbContext' from singleton 'BackgroundJobQueue'`.

`WorkspaceContext` is scoped because the active workspace is a per-circuit UI concern.

---

## 3. Request flow: a page render

```
Browser
  -> ASP.NET Core routing
  -> App.razor (lang=fa, dir=rtl)
  -> Routes.razor (Router, InteractiveServer)
  -> MainLayout.razor (rail + topbar + content)
  -> Page component
       @inject <ApplicationService>
       OnInitializedAsync -> service method -> IAppDbContext -> SQL Server
       -> record DTO -> Razor markup
```

Pages never touch `IAppDbContext`. They call Application services and receive immutable
records. This keeps query shape and business rules out of the view layer.

### Render mode

Interactive Server is applied globally in `App.razor`:

```razor
<HeadOutlet @rendermode="InteractiveServer" />
<Routes @rendermode="InteractiveServer" />
```

`NavMenu.razor` also declares `@rendermode InteractiveServer` because it holds interactive
state (active link highlighting).

---

## 4. Flow: document ingestion

Upload is asynchronous. The HTTP request returns immediately with a job id.

```
SourceService.UploadAsync
  1. IFileStorage.SaveAsync            -> writes to storage/files/yyyy/MM/{guid}{ext}
  2. hash content                      -> duplicate detection
  3. create Source + SourceVersion     -> status Pending
  4. IngestionService.CreateJobAsync   -> ProcessingJob + 8 ProcessingJobStep rows
  5. IBackgroundJobQueue.EnqueueAsync  -> bounded Channel<IngestionWorkItem>
  -> returns UploadSourceResult(SourceId, SourceVersionId, JobId, WasDuplicate)

IngestionWorker (BackgroundService)
  - reads the channel
  - creates a DI scope per item
  - calls IngestionService.ProcessAsync
  - each stage: BeginStepAsync -> work -> CompleteStepAsync
  - IJobNotifier.JobChangedAsync after every transition
```

The eight stages are fixed and visible in the monitor before work starts:

1. Validate file
2. Hash and detect duplicates
3. Extract text
4. Detect language
5. Segment into chunks
6. Build lexical index
7. Generate embeddings
8. Complete

A failure in a later stage never discards the output of an earlier successful one. The job
records `FailureReason` and the source version records `ProcessingError`.

### Why a channel and not a database queue

The queue is in-process and bounded at 256 items with a single reader. For a single-user
local application this is simpler and faster than polling a table, and it cannot lose work
across a restart in a way that matters — the source version stays `Pending` and can be
reprocessed from the UI.

---

## 5. Flow: a companion question

```
ResearchCompanionService.AskAsync(workspaceId, question, conversationId)
  1. persist the user message
  2. IRetrievalService.SearchAsync(RetrievalQuery(workspaceId, question, 8))
  3. if zero passages -> return CompanionAnswer.Refused(...)   <-- explicit refusal
  4. IAiProviderResolver.ResolveChatAsync(workspaceId)
       - reads workspace.DataRoute
       - skips CloudAllowed providers when the workspace is LocalOnly
       - returns null when nothing is compatible
  5. if null -> return CompanionAnswer.Refused("no provider configured")
  6. build a prompt with numbered passages, each capped at 1200 chars
  7. call the provider
  8. persist the assistant message with CitationsJson
  9. return CompanionAnswer(Success, Content, ConversationId, Citations, ...)
```

Two independent refusal conditions exist: no evidence, and no compatible provider. Both
return a Persian explanation to the user. Neither falls back to a cloud call.

---

## 6. Flow: deep research

```
DeepResearchService.CreatePlanAsync(workspaceId, objective)
  -> ResearchRun (status AwaitingApproval)
  -> 9 ResearchPlanStep rows (ClarifyObjective .. DraftReport)
  -> timeline event "research.planned"
  -> returns runId

DeepResearchService.ExecuteAsync(runId)     [user must approve first]
  for each step:
    RunStepAsync(step, action)
      - status Running, StartedAtUtc, save, notify
      - run action
      - status Succeeded + OutputSummary, or Failed + FailureReason
      - save, notify
```

`UsedWebSearch` is always `false`. There is no web search provider registered, and the UI
states this on the research page rather than implying otherwise.

---

## 7. Real-time notifications

`JobHub` is mapped at `/hubs/jobs`. `SignalRJobNotifier` sends:

- `"jobChanged"` with the job id, to group `workspace:{workspaceId}`
- `"researchRunChanged"` with the run id, to the same group

`JobHub` exposes `JoinWorkspace(Guid)` and `LeaveWorkspace(Guid)`, and
`static string GroupName(Guid workspaceId) => $"workspace:{workspaceId}"`.

The monitor page does **not** depend on SignalR. It uses an in-process
`System.Threading.Timer` firing every three seconds, guarded by a `SemaphoreSlim` with a
zero-timeout wait so overlapping ticks are dropped. This keeps the page functional even if
the circuit's SignalR connection is degraded.

---

## 8. Error handling strategy

| Layer | Strategy |
| --- | --- |
| Domain | Throws `InvalidOperationException` / `ArgumentException` for invariant violations |
| Application | Lets exceptions propagate; returns result records for expected outcomes (refusal, duplicate, failure) |
| Infrastructure | Logs and rethrows; converts provider failures into typed results where the caller can act |
| Web | Catches per-action, shows a Persian `notice-danger`, never swallows silently |

Expected outcomes are modelled as data (`CompanionAnswer.Success`, `UploadSourceResult.WasDuplicate`,
`IngestionOutcome.Failed`). Unexpected failures are exceptions.

---

## 9. What is deliberately absent

- No authentication or authorization. The app binds to `localhost` and is single-user.
- No distributed cache. `IMemoryCache` is registered for provider metadata only.
- No message broker. The channel is in-process.
- No background scheduler. Jobs are enqueued by user action.
- No multi-tenancy. Workspaces are a research-organisation concept, not a security boundary.

Each omission is a scope decision, documented in `docs/ROADMAP.md`.
