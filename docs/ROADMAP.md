# Roadmap — what is deliberately not implemented

Every item below is absent from the codebase. None of them is stubbed to look finished. Where
the UI touches an unimplemented capability, it says so in Persian rather than showing a dead
control.

---

## 1. Web search

**Status** Not implemented. No `IWebSearchProvider` is registered.

**What exists** `ResearchRun.UsedWebSearch` is always `false` and `WebSearchProvider` is
always `null`. The research page states that evidence is local-only.

**Why** Web search would introduce an uncontrolled egress path that bypasses `DataRoute`. It
needs its own policy decision (which queries may leave the machine, whether results are cached
locally, how a fetched page is attributed) before it can be added safely.

**To add** Define `IWebSearchProvider` in `Application/Abstractions`, gate it behind an
explicit per-workspace flag, record every query in `AiExecution` with `DataRoute`, and store
fetched pages as `Source` rows with `SourceKind.WebPage` so they are citable.

---

## 2. Reranking

**Status** Not implemented. `IAiProviderResolver.ResolveRerankerAsync` always returns `null`.

**What exists** The interface, the `AiCapability.Reranking` enum value, and the settings page
statement that reranking is unavailable. Retrieval falls back to fused lexical + vector scores.

**Why** A reranker needs a cross-encoder model. None is available locally, and calling a cloud
reranker would send every candidate passage off the machine — a much larger egress than a
single chat call.

**To add** Implement `IReranker` against a local cross-encoder, return it from
`ResolveRerankerAsync`, and apply it to the top 60 fused candidates before truncating to the
final result count.

---

## 3. OCR

**Status** Not implemented. `UnavailableOcrProvider.IsAvailable` is `false`.

**What exists** `IOcrProvider`, the `AiCapability.Ocr` enum value, and `ImageTextExtractor`,
which returns an `ExtractionResult` carrying a warning instead of pretending to have read the
image. `DocumentPage.OcrApplied` exists so a future implementation can mark pages.

**Why** Tesseract requires a native binary and Persian language data. Neither is bundled, and
shipping a native dependency silently would be worse than an honest warning.

**To add** Implement `IOcrProvider` over Tesseract with `fas` traineddata, register it, and
flip `IsAvailable`. The extraction pipeline already handles `OcrApplied`.

---

## 4. Automatic claim extraction

**Status** Not implemented. Claims are created by the user through `ClaimsService.CreateAsync`.

**What exists** `Claim.IsAiExtracted` exists as a provenance flag, and `AiProposal` exists as
the human-in-the-loop mechanism. Both are unused by any extraction pipeline.

**Why** Auto-extracting claims from documents would fill the database with unreviewed
assertions that look authoritative. The product's value is that a claim in the system has been
looked at.

**To add** A pipeline that writes `AiProposal` rows of kind "claim", surfaced on a review page,
accepted one at a time into `Claim` with `IsAiExtracted = true` and `Status = Unreviewed`.

---

## 5. Automatic knowledge-graph extraction

**Status** Not implemented. Nodes and relationships are created by the user through
`KnowledgeGraphService.CreateNodeAsync` / `CreateRelationshipAsync`.

**What exists** `KnowledgeNode.IsUserConfirmed`, `KnowledgeRelationship.IsAiSuggested`, and
`KnowledgeRelationshipEvidence` — the schema is ready for machine-suggested edges.

**Why** Same reason as claims: an auto-generated graph of unconfirmed edges is noise that
looks like knowledge.

**To add** Entity and relation extraction producing `AiProposal` rows, with
`IsAiSuggested = true` and `IsUserConfirmed = false` until a human confirms.

---

## 6. Automatic report generation from findings

**Status** Not implemented. `ReportsService.CreateAsync` takes markdown the caller supplies.

**What exists** `ResearchRun` produces `ResearchFinding` rows with evidence chunk IDs. The
report page lets a user write and version markdown manually.

**Why** Generating prose from findings would require the model to compose sentences that are
not directly quoted from evidence. That is exactly where fabrication enters. The current
design keeps the human as the author and the findings as the material.

**To add** A "draft from findings" action that produces a *proposal* — a new
`ResearchReportVersion` marked as machine-drafted, with every sentence linked to a finding ID,
requiring explicit acceptance.

---

## 7. Automatic learning-path generation

**Status** Not implemented. Paths, topics, cards, and quizzes are created by the user.

**What exists** The full SM-2 scheduler (`LearningService.ReviewCardAsync`), quiz attempts,
and `QuizQuestion.DocumentChunkId` linking a question to its source passage.

**Why** Generating cards from documents produces low-quality prompts that waste review time.
The scheduler is the valuable part and it works.

**To add** Card generation as `AiProposal` rows, reviewed before entering a path.

---

## 8. Multi-user authentication and authorization

**Status** Not implemented, by design. There is no users table, no login, no roles.

**What exists** `AllowedHosts = "localhost;127.0.0.1"`, and workspace scoping on every query.

**Why** The application is a personal tool. A login screen would imply a security boundary
that does not exist behind it.

**To add** ASP.NET Core Identity, a `UserId` on `ResearchWorkspace`, and an authorization
filter on every service call. This is a prerequisite for any hosted deployment.

---

## 9. Streaming model responses

**Status** Not implemented. Provider calls are request/response.

**What exists** `IChatCompletionProvider.CompleteAsync` returns a complete string.

**Why** Streaming complicates the citation and refusal logic, which must decide *after* the
full response whether evidence was sufficient.

**To add** A streaming overload plus incremental rendering in the companion chat, with the
citation set fixed before the first token is emitted.

---

## 10. Token counting and cost estimation

**Status** Partially implemented. `AiExecution` stores `PromptTokens` and `CompletionTokens`
when a provider reports them. There is no local tokenizer and no cost model.

**Why** A local tokenizer would be an approximation that disagrees with the provider's own
count. Storing what the provider reports is honest; estimating is not.

---

## 11. Content-Security-Policy header

**Status** Not implemented.

**Why** For a localhost-only single-user app the marginal benefit is small, and a wrong CSP
silently breaks Blazor's script loading.

**To add** A `Content-Security-Policy` header with a nonce for `blazor.web.js`, tested against
every page.

---

## 12. Health endpoint

**Status** Not implemented. There is no `/health`.

**Why** Nothing consumes it. `docs/OPERATIONS.md` documents a manual route check instead.

---

## 13. Test project

**Status** Not implemented. There is no `*.Tests` project in the solution.

**What exists** Manual verification: clean builds with zero warnings, a route smoke test
across all thirteen pages, a 404 check, and a database table-count check. These are recorded
in `docs/ACCEPTANCE_SCENARIO.md`.

**Why** This is the most significant gap in the repository. The verification performed is real
but it is not regression protection.

**To add** `PersonalResearchLab.Tests` with xUnit covering: `PersianEnglishTokenizer`
normalization, `TextChunker` boundary behaviour, `ChunkEmbedding.Pack`/`Unpack` round-trip,
`HybridRetrievalService` fusion ordering, `AiProviderResolver` data-route enforcement (the
security-critical one), `LearningService` SM-2 scheduling, and `ClaimsService` status
transitions.

---

## 14. Structured logging and telemetry

**Status** Console logging only. No Serilog, no OpenTelemetry, no metrics endpoint.

**Why** A single-user local app does not need a telemetry pipeline. `AiExecution` and
`AuditLog` provide the audit trail that matters.

---

## Summary table

| Capability | Status | Honest disclosure in UI |
| --- | --- | --- |
| Web search | Absent | Research page states local-only |
| Reranking | Absent | Settings page states unavailable |
| OCR | Absent | Image extraction returns a warning |
| Transcription | Absent | — |
| Azure OpenAI | Absent | Settings page states unsupported |
| Claim extraction | Absent | — |
| Graph extraction | Absent | — |
| Report drafting | Absent | — |
| Learning generation | Absent | — |
| Authentication | Absent by design | — |
| Streaming | Absent | — |
| Local token counting | Absent | — |
| CSP header | Absent | — |
| Health endpoint | Absent | — |
| Test project | Absent | — |
| Telemetry | Absent | — |

The pattern is deliberate: where a user could reasonably expect a feature, the interface says
it is not there. Where a feature is simply not surfaced, no control exists to mislead.
