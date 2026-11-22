# Acceptance scenario — "AI Agent Architecture and Production Readiness"

This document records the end-to-end scenario that was actually executed against a running
instance, the exact observations made, and the parts that could not be exercised without an
AI provider. It is a verification record, not a plan.

Everything below was observed in the browser at `http://localhost:5199` against the local
SQL Server database `PersonalResearchLab`. Where a number appears, it was read from the UI or
from the database, not estimated.

---

## 1. The workspace

| Field | Value |
| --- | --- |
| Name | `AI Agent Architecture and Production Readiness` |
| `DataRoute` | `LocalOnly` (enum value `0`) |
| `PreferredLanguage` | `fa-IR` |
| `IsArchived` | `false` |
| Description | تعیین اینکه یک عامل هوشمند برای استفاده در محیط عملیاتی به چه مؤلفه‌هایی نیاز دارد و کدام شکاف‌ها پیش از استقرار باید بسته شوند. |

`LocalOnly` is the important part. It means `AiProviderResolver` will refuse to serve this
workspace from any provider whose `DataRoute` is `CloudAllowed`, even if that provider is the
configured default. The scenario therefore also proves the routing guard, because the
companion and the research run both had to fail closed rather than silently call out.

---

## 2. Sources ingested

Three manual notes (`SourceKind.ManualNote`, enum value `9`), all detected as `fa`:

| Title | Characters | Pages | Chunks |
| --- | --- | --- | --- |
| یادداشت نمونه — مؤلفه‌های یک عامل هوشمند عملیاتی | 1042 | 1 | 1 |
| یادداشت دوم — سنجش کیفیت بازیابی | 338 | 1 | 1 |
| یادداشت سوم — مسیریابی داده محلی | 284 | 1 | 1 |

`UsedOcr` is `0` on all three, which is correct: they are text, not images.

The lexical index built 221 `ChunkTerms` rows across those 3 chunks. That number matters
because it is the evidence that the Persian tokenizer actually produced terms — SQL Server
full-text has no Persian word breaker, so without this table Persian search would return
nothing.

The first note deliberately opens with the sentence
«این یک یادداشت نمونه برای سناریوی پذیرش است و یافته پژوهشی واقعی نیست.» so that no reader
can mistake the fixture for a real research finding.

---

## 3. Ingestion pipeline

The monitor page (`/monitor`) shows four completed jobs, all `موفق` (Succeeded) at `۱۰۰٪`,
each ending on the stage `پایان`:

- `پردازش: یادداشت نمونه — مؤلفه‌های یک عامل هوشمند عملیاتی`
- `پردازش: یادداشت دوم — سنجش کیفیت بازیابی`
- `پردازش: یادداشت سوم — مسیریابی داده محلی`
- `پردازش دوباره: یادداشت سوم — مسیریابی داده محلی` (a re-process, proving the job is
  re-runnable and that a second run does not corrupt the first)

Each job records eight named stages in `ProcessingJobSteps.Name`. The stage column is `Name`,
not `StageName` — worth stating because it is an easy mistake when querying the table
directly.

The stage statuses are not uniformly `Succeeded`. On the re-processed job the embedding stage
is stored as `Skipped` (`5`), because no embedding provider is configured:

| Order | Stage | Status |
| --- | --- | --- |
| 0 | اعتبارسنجی فایل | `2` Succeeded |
| 1 | درهم‌سازی و تشخیص تکرار | `2` Succeeded |
| 2 | استخراج متن | `2` Succeeded |
| 3 | تشخیص زبان | `2` Succeeded |
| 4 | قطعه‌بندی متن | `2` Succeeded |
| 5 | ساخت نمایه واژگانی | `2` Succeeded |
| 6 | تولید تعبیه برداری | `5` **Skipped** |
| 7 | پایان | `2` Succeeded |

The monitor page renders that stage with a `–` marker and the label `رد‌شده`, next to the
detail `رد شد: ارائه‌دهنده تعبیه برداری تنظیم نشده است (جست‌وجوی واژگانی همچنان کار می‌کند)`.
A skipped stage is therefore visually distinct from a completed one, and the job as a whole
still reports `موفق` because extraction and the lexical index did succeed.

---

## 4. Hybrid retrieval

Query: `مسیریابی داده` (data routing), result count 10, current workspace only.

Two passages were returned. Both were retrieved by lexical match; the vector score was `0`
for both because no embedding provider is configured, so `ChunkEmbeddings` is empty and the
vector arm of the fusion contributed nothing.

| Rank | Source | Page | Chunk | Lexical | Vector | Fused | Reason shown |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | یادداشت سوم — مسیریابی داده محلی | 1 | 0 | 1.867 | 0 | 0.0164 | تطابق واژگانی |
| 2 | یادداشت نمونه — مؤلفه‌های یک عامل هوشمند عملیاتی | 1 | 0 | 1.386 | 0 | 0.0161 | تطابق واژگانی |

Two things are worth noting, because they are the honest behaviour rather than the flattering
one:

1. The fused score is small (`0.0164`) because reciprocal rank fusion with `RrfK = 60`
   produces `1 / (60 + rank)`. It is a rank-fusion score, not a similarity, and the UI shows
   the lexical and vector components separately so the number is interpretable.
2. The dashboard reports `قطعه‌های تعبیه‌شده ۰` and `۰٪ پوشش برداری`. The product does not
   hide the missing vector arm behind a green checkmark.

The ranking is also correct on the merits: the note that is *about* data routing outranks the
note that merely *mentions* it.

---

## 5. Companion refusal

Question asked: `چه شواهدی درباره مسیریابی داده وجود دارد؟`

The companion answered:

> هیچ ارائه‌دهنده هوش مصنوعی برای این فضای کاری تنظیم نشده است، بنابراین پاسخ نوشته نمی‌شود.
> قطعه‌های بازیابی‌شده در ادامه شواهد معتبری هستند که می‌توانید مستقیم بخوانید.

followed by the error line `هیچ ارائه‌دهنده گفت‌وگویی برای این فضای کاری یافت نشد.`

This is the required behaviour. With no provider, the companion does not invent an answer and
does not fall back to a cloud model. It states the reason, keeps the retrieved evidence
available for the user to read directly, and stops. The send button returns to `disabled`
after submission.

---

## 6. Deep research run

Goal: `معیارهای آمادگی تولید برای عامل‌های هوشمند چیست؟`

The run completed all nine plan steps (`۹ از ۹ تکمیل‌شده`), each reporting a real outcome:

| # | Step | Reported detail |
| --- | --- | --- |
| 0 | روشن‌سازی هدف | ارائه‌دهنده گفت‌وگویی تنظیم نشده است؛ هدف بدون روشن‌سازی و به‌صورت عین عبارت استفاده شد. |
| 1 | تجزیه به پرسش‌های پژوهشی | ۳ پرسش |
| 2 | فهرست‌برداری از دانش موجود | ۳ منبع و ۳ قطعه نمایه‌شده |
| 3 | بازیابی شواهد | ۶ قطعه در ۳ پرسش |
| 4 | ارزیابی شواهد | ۳ از ۳ پرسش قطعه پشتیبان دارد |
| 5 | تشخیص تناقض | تناقض ادعایی ثبت‌شده‌ای وجود ندارد |
| 6 | تولید پرسش‌های پیگیری | هر پرسش حداقل یک قطعه پشتیبان داشت |
| 7 | ترکیب یافته‌ها | ۳ یافته با پیوند شواهد ثبت شد |
| 8 | نگارش پیش‌نویس گزارش | پیش‌نویس گزارش ساخته شد |

Step 0 is the one to read closely. Without a provider the goal is **not** clarified, and the
step says so instead of pretending a model rewrote it.

All three findings carry `EpistemicStatus.SourceReported` (`به‌نقل از منبع`) and the body text
`ارائه‌دهنده هوش مصنوعی تنظیم نشده است؛ این یک اشاره به شواهد است، نه ترکیب و نتیجه‌گیری.`
Each finding is linked to 2 evidence chunks. No finding claims `Verified`, and none claims
`AiInterpretation`, because no model ran.

The run also records `UsedWebSearch = false` and states in the UI that web search is disabled
and all evidence is local.

---

## 7. Claims and evidence

A claim was created manually:

> یک عامل هوشمند عملیاتی باید مسیریابی داده را در یک نقطه واحد اعمال کند.

It was stored as `Unreviewed` / `Hypothesis` / `Factual`, which is the documented default for
a manual claim. It was then reviewed with the note
`بررسی دستی: با متن یادداشت نمونه هم‌خوان است.` and the status was set to `Supported`.

The result: status `پشتیبانی‌شده`, epistemic status promoted to `تأییدشده` (`Verified`). The
dashboard counters moved from `بررسی‌نشده ۱` to `پشتیبانی‌شده ۱` in the same render.

This confirms the rule stated in the UI: only `Supported` promotes epistemic status to
`Verified`, and the promotion happens because a human clicked, not because a model decided.

---

## 8. Knowledge graph

One node was created manually: `مسیریابی داده`, type `مفهوم` (Concept). It was stored as
`تأییدشده` with `۰ منبع پشتیبان`, and the graph correctly reports it as an isolated node
(`گره‌های بی‌رابطه ۱`).

The zero-support count is displayed rather than hidden. A node with no supporting source is
visibly weaker than one with sources, which is the point.

---

## 9. Reports and timeline

The research run produced one report, `گزارش پژوهش: معیارهای آمادگی تولید برای عامل‌های
هوشمند چیست؟`, at version 1 with 1 version total. The reports page states that each edit
creates a new version and previous versions are never overwritten.

The timeline recorded 6 events, newest first, with Jalali timestamps:

1. اجرای پژوهش کامل شد — ۱ آذر ۱۴۰۵، ساعت ۰۸:۳۳
2. طرح پژوهش ساخته شد — ۱ آذر ۱۴۰۵، ساعت ۰۸:۳۳
3. منبع افزوده شد: یادداشت سوم — ۱ آذر ۱۴۰۵، ساعت ۰۷:۵۳
4. منبع افزوده شد: یادداشت دوم — ۱ آذر ۱۴۰۵، ساعت ۰۷:۴۹
5. منبع افزوده شد: یادداشت نمونه — ۳۰ آبان ۱۴۰۵، ساعت ۱۶:۴۳
6. فضای کاری ساخته شد — ۳۰ آبان ۱۴۰۵، ساعت ۱۶:۴۱

The dates are Jalali and the times are Tehran local. The database stores UTC; conversion
happens at the display boundary only.

---

## 10. Learning

The learning page reports 0 paths, 0 quizzes, and 0 due cards, with the explicit statements
that automatic path generation from sources is not implemented and that no card is due. The
SM-2 review control is present but has nothing to review, and it says so rather than showing
an empty form.

---

## 11. What this scenario does **not** prove

Stated plainly, because a verification record that only lists successes is not a verification
record:

| Not exercised | Why | Where it is documented |
| --- | --- | --- |
| Vector retrieval | No embedding provider configured, so `ChunkEmbeddings` is empty | `docs/VECTOR_STORAGE.md`, `docs/RETRIEVAL.md` |
| Model-generated answers | No chat provider configured; `LocalOnly` blocks cloud providers | `docs/AI_PROVIDERS.md` |
| Reranking | Not implemented; `ResolveRerankerAsync` returns `null` | `docs/ROADMAP.md` §2 |
| OCR | Not implemented; `UnavailableOcrProvider.IsAvailable` is `false` | `docs/ROADMAP.md` §3 |
| PDF / DOCX / HTML extraction | Only manual notes were ingested in this run | `docs/ARCHITECTURE.md` |
| Automatic claim and graph extraction | Not implemented; both are manual by design | `docs/ROADMAP.md` §4 |
| Multi-user authentication | Single-user by design | `docs/SECURITY.md` |
| Automated test suite | No test project exists yet | `docs/ROADMAP.md` |

To exercise the vector arm and model-generated answers, add a provider of kind
**Deterministic (offline)** on `/settings`, then re-process a source so embeddings are
generated. That path stays entirely on the machine and is compatible with `LocalOnly`.

---

## 12. Reproducing this scenario

```powershell
# 1. Schema
dotnet ef database update `
  --project src\PersonalResearchLab.Infrastructure `
  --startup-project src\PersonalResearchLab.Infrastructure

# 2. Run
dotnet run --project src\PersonalResearchLab.Web
```

Then, in the UI:

1. `/workspaces` — create a workspace named `AI Agent Architecture and Production Readiness`.
   Leave `DataRoute` at `LocalOnly`.
2. `/sources` — create three manual notes with the titles in §2.
3. `/monitor` — confirm each job reaches `پایان` at `۱۰۰٪`.
4. `/search` — search `مسیریابی داده` and confirm the routing note ranks first.
5. `/companion` — ask a question and confirm the explicit refusal.
6. `/research` — build a plan for the goal in §6, approve it, and confirm nine steps.
7. `/claims` — create a claim, review it as `Supported`, confirm promotion to `تأییدشده`.
8. `/knowledge` — create a node and confirm it is reported as isolated.
9. `/reports`, `/timeline` — confirm one report at version 1 and six timeline events.

Expected counts after step 8: 1 workspace, 3 sources, 3 documents, 3 chunks, 221 chunk terms,
1 claim, 1 knowledge node, 1 research run, 1 report, 6 timeline events.
