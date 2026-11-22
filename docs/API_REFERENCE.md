# API reference — Application layer

Every public service the UI can call, with real signatures taken from the source. This is a
map, not a tutorial: for behaviour and rationale see `docs/ARCHITECTURE.md` and
`docs/RETRIEVAL.md`.

All services are registered **scoped** in `ServiceRegistration.AddApplicationServices`. All
async methods take `CancellationToken cancellationToken = default` as the last parameter; that
parameter is omitted from the tables below for readability.

---

## 1. Conventions

- Services return **records**, never entities. The UI never receives a tracked entity.
- Read-only queries use `AsNoTracking()`.
- A method that cannot answer returns a result object with `Success = false` and a reason. It
  does not throw for a missing provider, missing evidence, or missing configuration.
- Persian strings returned by services are user-facing text. English strings are internal
  tokens (for example `RetrievalReason`) that the UI translates.

---

## 2. `WorkspaceService`

`PersonalResearchLab.Application.Workspaces`

| Method | Returns |
| --- | --- |
| `ListAsync(bool includeArchived = false)` | `IReadOnlyList<WorkspaceSummary>` |
| `GetAsync(Guid id)` | `ResearchWorkspace?` |
| `GetSummaryAsync(Guid id)` | `WorkspaceSummary?` |
| `CreateAsync(CreateWorkspaceRequest request)` | `ResearchWorkspace` |
| `UpdateAsync(Guid id, UpdateWorkspaceRequest request)` | `Task` |
| `SetArchivedAsync(Guid id, bool archived)` | `Task` |
| `DeleteAsync(Guid id)` | `Task` |

```csharp
public sealed record CreateWorkspaceRequest(
    string Name,
    string? Description = null,
    string? Objective = null,
    string? AccentColor = null,
    string? PreferredLanguage = null,
    DataRoute DataRoute = DataRoute.LocalOnly,
    string? AiInstructions = null);
```

`CreateWorkspaceRequest.DataRoute` defaults to `LocalOnly`. That default is deliberate: a new
workspace must not be able to reach a cloud provider until the user says so explicitly.

`WorkspaceSummary` carries `SourceCount`, `ClaimCount`, `NodeCount`, and `RunCount` so list
pages do not issue N+1 queries.

---

## 3. `SourceService`

`PersonalResearchLab.Application.Sources`

| Method | Returns |
| --- | --- |
| `ListAsync(Guid workspaceId, bool includeArchived = false)` | `IReadOnlyList<SourceListItem>` |
| `GetDetailAsync(Guid sourceId)` | `SourceDetail?` |
| `UploadAsync(Guid workspaceId, Stream content, string fileName, string? title, SourceKind? kind)` | `UploadSourceResult` |
| `CreateNoteSourceAsync(Guid workspaceId, string title, string body)` | `UploadSourceResult` |
| `SetArchivedAsync(Guid sourceId, bool archived)` | `Task` |
| `SetReliabilityAsync(Guid sourceId, int? rating, string? rationale)` | `Task` |
| `ReprocessAsync(Guid sourceId)` | `Guid` (the new job id) |
| `InferKind(string fileName)` *(static)* | `SourceKind` |

`UploadAsync` writes the file through `IFileStorage`, computes a content hash for duplicate
detection, creates a `SourceVersion`, and enqueues an ingestion job. It does not extract text
inline — that happens on the worker.

`InferKind` maps a file extension to `SourceKind`. It is static because it has no state and the
UI uses it to preview the detected kind before upload.

---

## 4. `SearchService`

`PersonalResearchLab.Application.Search`

| Method | Returns |
| --- | --- |
| `SearchAsync(Guid workspaceId, string query, int maxResults = 10, Guid? sourceIdFilter = null, bool includeCrossWorkspace = false)` | `SearchResponse` |
| `GetDocumentAsync(Guid documentId)` | `DocumentReaderModel?` |
| `GetChunkPageAsync(Guid chunkId)` | `int?` |

```csharp
public sealed record SearchResponse(string Query, IReadOnlyList<SearchHit> Hits, string Explanation);

public sealed record SearchHit(
    Guid ChunkId, Guid DocumentId, Guid SourceId,
    string SourceTitle, string DocumentTitle,
    int PageNumber, int ChunkIndex, string Text,
    double LexicalScore, double VectorScore, double FusedScore,
    string RetrievalReason);
```

`Explanation` is a Persian sentence describing how the result set was produced. It is not
decorative: it tells the user whether the vector arm contributed or whether the result is
lexical-only.

`RetrievalReason` is one of `"lexical match"`, `"semantic similarity"`, or
`"lexical match + semantic similarity"`. The UI translates these; services emit English tokens.

---

## 5. `ResearchCompanionService`

`PersonalResearchLab.Application.Companion`

| Method | Returns |
| --- | --- |
| `AskAsync(Guid workspaceId, string question, Guid? conversationId = null)` | `CompanionAnswer` |
| `ListConversationsAsync(Guid workspaceId)` | `IReadOnlyList<ConversationSummary>` |
| `GetMessagesAsync(Guid conversationId)` | `IReadOnlyList<ConversationMessage>` |
| `Refused(string reason, Guid? conversationId = null)` *(static)* | `CompanionAnswer` |

```csharp
public sealed record CompanionAnswer(
    bool Success,
    string Content,
    Guid? ConversationId,
    IReadOnlyList<Citation> Citations,
    string? Error = null,
    string? ProviderDescription = null,
    DataRoute? DataRoute = null);
```

`Success = false` with a populated `Error` is the **refusal path**, and it is a feature. It is
returned when retrieval finds no passage above the relevance floor, or when no chat provider is
resolved for the workspace. The companion never answers from model memory alone.

`DataRoute` is surfaced so the UI can state where the call ran. It is `null` when no call was
made.

---

## 6. `DeepResearchService`

`PersonalResearchLab.Application.Research`

| Method | Returns |
| --- | --- |
| `CreatePlanAsync(Guid workspaceId, string objective)` | `Guid` (run id) |
| `GetRunAsync(Guid runId)` | `ResearchRunDetail?` |
| `ListRunsAsync(Guid workspaceId)` | `IReadOnlyList<ResearchRunSummary>` |
| `ExecuteAsync(Guid runId)` | `Task` |

`CreatePlanAsync` produces a plan in `ResearchRunStatus.AwaitingApproval`. `ExecuteAsync` runs
the nine `ResearchPlanStepKind` stages and moves the run to `Completed` or `Failed`. The
approval gate exists so a plan is never executed without the user seeing it first.

`ResearchRunSummary.UsedWebSearch` is always `false` — web search is not implemented. See
`docs/ROADMAP.md` §1.

---

## 7. `ClaimsService`

`PersonalResearchLab.Application.Claims`

| Method | Returns |
| --- | --- |
| `GetOverviewAsync(Guid workspaceId, ClaimStatus? statusFilter = null)` | `ClaimsOverview` |
| `GetAsync(Guid claimId)` | `ClaimDetail?` |
| `CreateAsync(Guid workspaceId, string statement, ClaimType claimType, string? temporalContext)` | `Guid` |
| `SetStatusAsync(Guid claimId, ClaimStatus status, string? annotation)` | `Task` |
| `ResolveConflictAsync(Guid conflictId, string resolution)` | `Task` |

`Claim.Status` is mutable **only** through `SetStatusAsync` (D-015). That method also promotes
`EpistemicStatus` when the new status is `Supported`, and writes an audit entry. Do not set the
property directly from a page.

---

## 8. `KnowledgeGraphService`

`PersonalResearchLab.Application.Knowledge`

| Method | Returns |
| --- | --- |
| `GetGraphAsync(Guid workspaceId, KnowledgeNodeKind? kindFilter = null)` | `KnowledgeGraphView` |
| `GetNodeAsync(Guid nodeId)` | `KnowledgeNodeDetail?` |
| `CreateNodeAsync(Guid workspaceId, string name, KnowledgeNodeKind kind, string? description)` | `Guid` |
| `CreateRelationshipAsync(Guid workspaceId, Guid fromNodeId, Guid toNodeId, KnowledgeRelationKind kind, string? description)` | `Guid` |
| `ConfirmNodeAsync(Guid nodeId)` | `Task` |
| `ConfirmRelationshipAsync(Guid relationshipId)` | `Task` |

Nodes and relationships are created manually. There is no automatic extraction from documents
(`docs/ROADMAP.md` §5). `KnowledgeGraphView` reports isolated nodes explicitly so the user can
see that the graph is sparse rather than assuming it is complete.

---

## 9. `ReportsService`

`PersonalResearchLab.Application.Reports`

| Method | Returns |
| --- | --- |
| `ListAsync(Guid workspaceId)` | `IReadOnlyList<ReportListItem>` |
| `GetAsync(Guid reportId)` | `ReportDetail?` |
| `CreateAsync(Guid workspaceId, string title, string template, string markdown, string citationsJson, Guid? researchRunId)` | `Guid` |
| `AddVersionAsync(Guid reportId, string markdown, string citationsJson, string? changeNote)` | `int` (new version number) |
| `DeleteAsync(Guid reportId)` | `Task` |

`AddVersionAsync` never overwrites. It appends a `ResearchReportVersion` and increments
`CurrentVersionNumber`. The entity property is `Markdown`, not `MarkdownBody`.

---

## 10. `LearningService`

`PersonalResearchLab.Application.Learning`

| Method | Returns |
| --- | --- |
| `GetOverviewAsync(Guid workspaceId)` | `LearningOverview` |
| `GetPathAsync(Guid pathId)` | `LearningPathDetail?` |
| `ReviewCardAsync(Guid flashcardId, int quality)` | `ReviewOutcome` |
| `GetQuizAsync(Guid quizId)` | `QuizDetail?` |
| `SubmitAttemptAsync(Guid quizId, IReadOnlyDictionary<Guid, int> answers)` | `QuizAttemptResult` |

`ReviewCardAsync` implements SM-2. `quality` is 0–5. Paths and quizzes are created manually;
automatic generation from sources is not implemented (`docs/ROADMAP.md` §7).

---

## 11. `TimelineService`

`PersonalResearchLab.Application.Timeline`

| Method | Returns |
| --- | --- |
| `GetAsync(Guid workspaceId, string? eventTypeFilter = null, int take = 100)` | `TimelineView` |
| `AddAsync(Guid workspaceId, string eventType, string title, string? detail = null, Guid? relatedEntityId = null)` | `Task` |

Events are ordered newest first. Timestamps are stored UTC and rendered Jalali by the page.

---

## 12. `DashboardService`

`PersonalResearchLab.Application.Dashboard`

| Method | Returns |
| --- | --- |
| `GetSnapshotAsync()` | `DashboardSnapshot` |

```csharp
public sealed record DashboardMetrics(
    int WorkspaceCount, int SourceCount, int DocumentCount, int ChunkCount,
    int EmbeddedChunkCount, int ClaimCount, int UnreviewedClaimCount,
    int ContestedClaimCount, int NodeCount, int RelationshipCount,
    int OpenConflictCount, int ActiveJobCount, int FailedJobCount,
    int ActiveRunCount, int ReportCount);
```

Every number is a direct `COUNT` over real rows. `EmbeddedChunkCount` is reported separately
from `ChunkCount` so vector coverage is visible rather than implied.

---

## 13. `JobMonitorService`

`PersonalResearchLab.Application.Monitoring`

| Method | Returns |
| --- | --- |
| `ListAsync(Guid? workspaceId = null, int take = 50)` | `IReadOnlyList<JobSummary>` |
| `GetAsync(Guid jobId)` | `JobDetail?` |
| `CancelAsync(Guid jobId)` | `Task` |

`CancelAsync` sets a cooperative flag; the worker observes it between stages. It does not abort
a stage mid-flight.

`JobStepDetail.Status` can be `Skipped`. A skipped stage is rendered with `–` and a `رد‌شده`
label, never a green check (D-021).

---

## 14. `SettingsService`

`PersonalResearchLab.Application.Settings`

| Method | Returns |
| --- | --- |
| `GetOverviewAsync()` | `SettingsOverview` |
| `CreateProviderAsync(string name, AiProviderKind kind, string baseUrl, DataRoute dataRoute, string? apiKeySecretName, string? apiKeyValue)` | `Guid` |
| `UpdateProviderAsync(Guid providerId, string name, AiProviderKind kind, string baseUrl, DataRoute dataRoute, bool isEnabled, string? apiKeySecretName, string? apiKeyValue)` | `Task` |
| `SetProviderEnabledAsync(Guid providerId, bool enabled)` | `Task` |
| `DeleteProviderAsync(Guid providerId)` | `Task` |
| `CreateModelAsync(Guid providerId, AiCapability capability, string modelName, int dimensions, bool isDefault)` | `Guid` |
| `SetDefaultModelAsync(Guid modelId)` | `Task` |
| `DeleteModelAsync(Guid modelId)` | `Task` |
| `SecretExistsAsync(string name)` | `Task<bool>` |
| `SetSecretAsync(string name, string value)` | `Task` |
| `RemoveSecretAsync(string name)` | `Task` |

**No method returns a secret value.** `SecretExistsAsync` reports presence only. The database
stores the secret *name*; the value lives in `ISecretStore` (DPAPI on Windows).

---

## 15. `IngestionService`

`PersonalResearchLab.Application.Ingestion`

| Method | Returns |
| --- | --- |
| `CreateJobAsync(Guid workspaceId, Guid sourceVersionId, string title)` | `ProcessingJob` |
| `ProcessAsync(Guid sourceVersionId, Guid jobId)` | `IngestionOutcome` |

```csharp
public sealed record IngestionOutcome(
    bool Success, int ChunkCount, int TermCount, string EmbeddingSummary, string? Error);

public sealed record EmbeddingBatchResult(
    IReadOnlySet<Guid> EmbeddedChunkIds,
    string Summary,
    ProcessingStatus Status = ProcessingStatus.Succeeded);
```

`ProcessAsync` runs the eight fixed stages. `EmbeddingBatchResult.Status` is carried explicitly
because the embedding stage can legitimately do no work — no provider configured, or the
provider failed. Reporting those as `Succeeded` would put a green check next to a stage that
did nothing (D-021).

The eight stage names, in order, are Persian and stored in `ProcessingJobSteps.Name`:

1. `اعتبارسنجی فایل`
2. `درهم‌سازی و تشخیص تکرار`
3. `استخراج متن`
4. `تشخیص زبان`
5. `قطعه‌بندی متن`
6. `ساخت نمایه واژگانی`
7. `تولید تعبیه برداری`
8. `پایان`

---

## 16. Abstractions

Declared in `Application/Abstractions`, implemented in `Infrastructure`.

### `IAppDbContext`

Exposes 42 `DbSet<T>` properties plus `SaveChangesAsync`. The Application layer never sees a
SQL Server type.

### `IRetrievalService`

```csharp
Task<IReadOnlyList<RetrievedPassage>> SearchAsync(RetrievalQuery query, CancellationToken ct = default);

public sealed record RetrievalQuery(
    Guid WorkspaceId, string QueryText, int MaxResults = 8,
    bool IncludeCrossWorkspace = false,
    Guid? SourceIdFilter = null, Guid? DocumentIdFilter = null);
```

`RetrievedPassage` carries `LexicalScore`, `VectorScore`, `FusedScore`, and `RetrievalReason`
alongside full provenance (`SourceId`, `DocumentId`, `PageNumber`, `ChunkIndex`).

### `IVectorIndex`

```csharp
Task UpsertAsync(Guid chunkId, Guid workspaceId, float[] vector, CancellationToken ct = default);
Task<IReadOnlyList<(Guid ChunkId, double Similarity)>> QueryAsync(Guid workspaceId, float[] vector, int topK, CancellationToken ct = default);
Task DeleteForDocumentAsync(Guid documentId, CancellationToken ct = default);
```

### `IAiProviderResolver`

```csharp
Task<ResolvedChatProvider?> ResolveChatAsync(Guid workspaceId, CancellationToken ct = default);
Task<ResolvedEmbeddingProvider?> ResolveEmbeddingAsync(Guid workspaceId, CancellationToken ct = default);
Task<ResolvedReranker?> ResolveRerankerAsync(Guid workspaceId, CancellationToken ct = default);
```

This is the single place where "may this leave the machine?" is decided. `ResolveRerankerAsync`
always returns `null` — reranking is not implemented.

### `ISecretStore`

```csharp
string? Get(string name);
void Set(string name, string value);
void Remove(string name);
bool Contains(string name);
```

Synchronous by design: DPAPI is a local file operation, and making it async would imply I/O
that does not exist.

### `IBackgroundJobQueue`

```csharp
ValueTask EnqueueIngestionAsync(Guid sourceVersionId, Guid jobId, CancellationToken ct = default);
Task RequestCancellationAsync(Guid jobId, CancellationToken ct = default);
```

### `IJobNotifier`

```csharp
Task JobChangedAsync(Guid jobId, CancellationToken ct = default);
Task ResearchRunChangedAsync(Guid researchRunId, CancellationToken ct = default);
```

### `IFileStorage`

```csharp
Task<string> SaveAsync(Stream content, string suggestedFileName, CancellationToken ct = default);
Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);
Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
Task DeleteAsync(string relativePath, CancellationToken ct = default);
string ResolveAbsolutePath(string relativePath);
```

`ResolveAbsolutePath` rejects traversal attempts. It is the only sanctioned way to turn a
stored relative path into an absolute one.

### `ITextExtractor` / `IOcrProvider` / `ITextTokenizer` / `ILanguageDetector`

```csharp
bool CanHandle(SourceKind kind, string? fileName);
Task<ExtractionResult> ExtractAsync(Stream content, SourceKind kind, string? fileName, CancellationToken ct = default);

public sealed record ExtractedPage(int PageNumber, string Text, bool OcrApplied);
public sealed record ExtractionResult(
    IReadOnlyList<ExtractedPage> Pages, string? DetectedLanguage, bool UsedOcr, string? Warning = null);

IReadOnlyList<string> Tokenize(string text);
string Normalize(string text);
IReadOnlyDictionary<string, int> CountTerms(string text);
string Detect(string text);   // "fa" | "en" | "mixed" | "unknown"
```

`IOcrProvider.IsAvailable` is `false` in the shipped implementation. `ImageTextExtractor`
returns a `Warning` instead of pretending to have read the image.
