# Glossary

The Persian terms this product uses, what they mean here, and the code identifier each one maps
to. Persian is the UI language; code, identifiers, and logs are English. This file is the bridge
between the two.

Every Persian string below is copied from `src/PersonalResearchLab.Web/Formatting/Labels.cs` or
`PersianFormat.cs`. If you change a label, change it there — never inline in a page.

---

## 1. Core concepts

| Persian | English | Code | Meaning |
| --- | --- | --- | --- |
| رصدخانه دانش | Knowledge Observatory | — | The product codename |
| فضای کاری | Workspace | `ResearchWorkspace` | One research topic. Everything is scoped to one |
| منبع | Source | `Source` | A document, note, or page you added |
| نسخه منبع | Source version | `SourceVersion` | An immutable snapshot of a source's content |
| سند | Document | `Document` | Extracted text from one source version |
| صفحه | Page | `DocumentPage` | A page boundary inside a document |
| قطعه | Chunk | `DocumentChunk` | A retrievable passage, roughly 1100 characters |
| شاهد / شواهد | Evidence | `ClaimEvidence` | A passage that bears on a claim |
| ادعا | Claim | `Claim` | A statement whose truth is being tracked |
| گره | Node | `KnowledgeNode` | A concept, person, technology, or event in the graph |
| رابطه | Relationship | `KnowledgeRelationship` | A typed edge between two nodes |
| گزارش | Report | `ResearchReport` | A versioned markdown document |
| خط زمان | Timeline | `ResearchTimelineEvent` | The chronological record of what happened |
| کار پردازش | Processing job | `ProcessingJob` | One ingestion run |
| مرحله | Stage | `ProcessingJobStep` | One of the eight fixed ingestion stages |

---

## 2. Epistemic status

The most important vocabulary in the product. These categories must never be blurred, because
the whole value of the lab depends on the user knowing what kind of knowledge they hold.

| Persian | English | Enum | Meaning |
| --- | --- | --- | --- |
| تأییدشده | Verified | `EpistemicStatus.Verified` | You checked this against a primary source |
| به‌نقل از منبع | Source-reported | `EpistemicStatus.SourceReported` | The source said it; truth depends on the source |
| تفسیر هوش مصنوعی | AI interpretation | `EpistemicStatus.AiInterpretation` | A model inferred this from retrieved material |
| استنتاج منطقی | Logical inference | `EpistemicStatus.LogicalInference` | Derived from explicit premises |
| فرضیه | Hypothesis | `EpistemicStatus.Hypothesis` | A proposed explanation with no evidence yet |
| نامعلوم | Unknown | `EpistemicStatus.Unknown` | Known to be unknown; evidence is insufficient |

The UI shows a hint next to each badge explaining what the category claims. For example
`نامعلوم` renders as "دانسته‌شده‌ی نادانسته: شواهد کافی برای بیان آن وجود ندارد."

There is **no** `Disputed` member. Contested material is expressed through `ClaimStatus.Contested`
and `ClaimConflict`, not by inventing an epistemic category.

---

## 3. Claim status

| Persian | English | Enum |
| --- | --- | --- |
| بررسی‌نشده | Unreviewed | `ClaimStatus.Unreviewed` |
| پشتیبانی‌شده | Supported | `ClaimStatus.Supported` |
| تا حدی پشتیبانی‌شده | Partially supported | `ClaimStatus.PartiallySupported` |
| مورد مناقشه | Contested | `ClaimStatus.Contested` |
| رد‌شده | Contradicted | `ClaimStatus.Contradicted` |
| کهنه | Outdated | `ClaimStatus.Outdated` |
| غیرقابل بررسی | Unverifiable | `ClaimStatus.Unverifiable` |

`Claim.Status` is mutable only through `ClaimsService.SetStatusAsync` (D-015). Setting
`Supported` also promotes `EpistemicStatus` to `Verified`.

---

## 4. Claim type

| Persian | English | Enum |
| --- | --- | --- |
| واقعی | Factual | `ClaimType.Factual` |
| آماری | Statistical | `ClaimType.Statistical` |
| علّی | Causal | `ClaimType.Causal` |
| پیش‌بینی | Predictive | `ClaimType.Predictive` |
| تفسیری | Interpretive | `ClaimType.Interpretive` |
| هنجاری | Normative | `ClaimType.Normative` |
| فرضی | Hypothetical | `ClaimType.Hypothetical` |

---

## 5. Evidence stance

Deliberately ordinal and coarse. The product must not present subjective judgement as
mathematical precision.

| Persian | English | Enum |
| --- | --- | --- |
| پشتیبانی می‌کند | Supports | `EvidenceStance.Supports` |
| تا حدی پشتیبانی می‌کند | Partially supports | `EvidenceStance.PartiallySupports` |
| بی‌طرف | Neutral | `EvidenceStance.Neutral` |
| تا حدی رد می‌کند | Partially contradicts | `EvidenceStance.PartiallyContradicts` |
| رد می‌کند | Contradicts | `EvidenceStance.Contradicts` |

---

## 6. Source kind

| Persian | English | Enum |
| --- | --- | --- |
| PDF | PDF | `SourceKind.Pdf` |
| Word | Word | `SourceKind.Docx` |
| متن | Plain text | `SourceKind.Text` |
| Markdown | Markdown | `SourceKind.Markdown` |
| HTML | HTML | `SourceKind.Html` |
| CSV | CSV | `SourceKind.Csv` |
| JSON | JSON | `SourceKind.Json` |
| تصویر | Image | `SourceKind.Image` |
| صفحه وب | Web page | `SourceKind.WebPage` |
| یادداشت دستی | Manual note | `SourceKind.ManualNote` |
| ورود ساختاریافته | Structured import | `SourceKind.StructuredImport` |

---

## 7. Processing status

| Persian | English | Enum | Marker |
| --- | --- | --- | --- |
| در انتظار | Pending | `ProcessingStatus.Pending` | `○` |
| در حال اجرا | Running | `ProcessingStatus.Running` | `•` |
| موفق | Succeeded | `ProcessingStatus.Succeeded` | `✓` |
| ناموفق | Failed | `ProcessingStatus.Failed` | `✕` |
| لغو‌شده | Cancelled | `ProcessingStatus.Cancelled` | `–` |
| رد‌شده | Skipped | `ProcessingStatus.Skipped` | `–` |

`Skipped` exists because a stage can legitimately do no work — for example the embedding stage
when no provider is configured. It renders as `–`, never as a green check (D-021).

---

## 8. Ingestion stages

Fixed, ordered, and stored in `ProcessingJobSteps.Name`.

| # | Persian | English |
| --- | --- | --- |
| 1 | اعتبارسنجی فایل | Validate file |
| 2 | درهم‌سازی و تشخیص تکرار | Hash and detect duplicates |
| 3 | استخراج متن | Extract text |
| 4 | تشخیص زبان | Detect language |
| 5 | قطعه‌بندی متن | Chunk text |
| 6 | ساخت نمایه واژگانی | Build lexical index |
| 7 | تولید تعبیه برداری | Generate embeddings |
| 8 | پایان | Finish |

---

## 9. Knowledge graph

### Node kinds

| Persian | English | Enum |
| --- | --- | --- |
| مفهوم | Concept | `KnowledgeNodeKind.Concept` |
| شخص | Person | `KnowledgeNodeKind.Person` |
| سازمان | Organization | `KnowledgeNodeKind.Organization` |
| فناوری | Technology | `KnowledgeNodeKind.Technology` |
| رویداد | Event | `KnowledgeNodeKind.Event` |
| سند | Document | `KnowledgeNodeKind.Document` |
| ادعا | Claim | `KnowledgeNodeKind.Claim` |
| پرسش پژوهشی | Research question | `KnowledgeNodeKind.ResearchQuestion` |
| موضوع | Topic | `KnowledgeNodeKind.Topic` |

### Relation kinds

| Persian | English | Enum |
| --- | --- | --- |
| پشتیبانی می‌کند | Supports | `KnowledgeRelationKind.Supports` |
| رد می‌کند | Contradicts | `KnowledgeRelationKind.Contradicts` |
| وابسته است به | Depends on | `KnowledgeRelationKind.DependsOn` |
| مرتبط است با | Related to | `KnowledgeRelationKind.RelatedTo` |
| برگرفته از | Derived from | `KnowledgeRelationKind.DerivedFrom` |
| اشاره می‌کند به | Mentions | `KnowledgeRelationKind.Mentions` |
| توضیح می‌دهد | Explains | `KnowledgeRelationKind.Explains` |
| باعث می‌شود | Causes | `KnowledgeRelationKind.Causes` |

---

## 10. Research plan steps

| Persian | English | Enum |
| --- | --- | --- |
| شفاف‌سازی هدف | Clarify objective | `ResearchPlanStepKind.ClarifyObjective` |
| تجزیه به پرسش‌ها | Decompose questions | `ResearchPlanStepKind.DecomposeQuestions` |
| فهرست‌برداری از دانش موجود | Inventory knowledge | `ResearchPlanStepKind.InventoryKnowledge` |
| بازیابی شواهد | Retrieve evidence | `ResearchPlanStepKind.RetrieveEvidence` |
| ارزیابی شواهد | Evaluate evidence | `ResearchPlanStepKind.EvaluateEvidence` |
| تشخیص تناقض‌ها | Detect contradictions | `ResearchPlanStepKind.DetectContradictions` |
| پرسش‌های پیگیری | Follow-up questions | `ResearchPlanStepKind.FollowUpQuestions` |
| ترکیب و جمع‌بندی | Synthesize | `ResearchPlanStepKind.Synthesize` |
| نگارش پیش‌نویس گزارش | Draft report | `ResearchPlanStepKind.DraftReport` |

### Run status

| Persian | English | Enum |
| --- | --- | --- |
| پیش‌نویس | Draft | `ResearchRunStatus.Draft` |
| در انتظار تأیید | Awaiting approval | `ResearchRunStatus.AwaitingApproval` |
| در حال اجرا | Running | `ResearchRunStatus.Running` |
| متوقف | Paused | `ResearchRunStatus.Paused` |
| تکمیل‌شده | Completed | `ResearchRunStatus.Completed` |
| لغو‌شده | Cancelled | `ResearchRunStatus.Cancelled` |
| ناموفق | Failed | `ResearchRunStatus.Failed` |

---

## 11. AI and data routing

| Persian | English | Enum | Meaning |
| --- | --- | --- | --- |
| فقط محلی | Local only | `DataRoute.LocalOnly` | No call may leave the machine |
| ابر مجاز | Cloud allowed | `DataRoute.CloudAllowed` | A cloud provider may be used |

`LocalOnly` is the default for every new workspace. A `LocalOnly` workspace is never served by a
`CloudAllowed` provider — `AiProviderResolver` is the single enforcement point.

### Provider kinds

| Persian | English | Enum |
| --- | --- | --- |
| سازگار با OpenAI | OpenAI-compatible | `AiProviderKind.OpenAiCompatible` |
| Ollama (محلی) | Ollama (local) | `AiProviderKind.Ollama` |
| Azure OpenAI | Azure OpenAI | `AiProviderKind.AzureOpenAi` |
| آزمایشی قطعی | Deterministic (offline) | `AiProviderKind.DeterministicFake` |

### Capabilities

| Persian | English | Enum |
| --- | --- | --- |
| گفت‌وگو | Chat | `AiCapability.Chat` |
| تعبیه برداری | Embedding | `AiCapability.Embedding` |
| بازچینش | Reranking | `AiCapability.Reranking` |
| تشخیص متن تصویر | OCR | `AiCapability.Ocr` |
| تبدیل گفتار | Transcription | `AiCapability.Transcription` |

`Reranking`, `Ocr`, and `Transcription` are declared but not implemented. The UI says so rather
than offering a control that does nothing.

---

## 12. Retrieval reasons

The retrieval layer emits stable English tokens; the UI translates them. Never show the raw
token.

| Token | Persian |
| --- | --- |
| `lexical match` | تطابق واژگانی |
| `semantic similarity` | شباهت معنایی |

When both arms contribute, the reason is the two tokens joined by ` + `, and the UI renders
`تطابق واژگانی + شباهت معنایی`. An empty reason renders `دلیل بازیابی ثبت نشده`.

---

## 13. Timeline event types

Event types are stable machine keys stored in the database. The mapping lives in the
presentation layer.

| Key | Persian |
| --- | --- |
| `workspace.created` | ساخت فضای کاری |
| `source.added` | افزودن منبع |
| `research.planned` | طرح پژوهش |
| `research.completed` | پایان پژوهش |
| `claim.created` | ثبت ادعا |
| `claim.statusChanged` | تغییر وضعیت ادعا |
| `claim.conflictResolved` | حل تناقض |
| `knowledge.nodeCreated` | ساخت گره دانش |
| `knowledge.relationshipCreated` | ساخت رابطه دانش |
| `report.created` | ساخت گزارش |
| `report.versionAdded` | نسخه جدید گزارش |
| `learning.reviewed` | مرور کارت |

Job type `DocumentIngestion` renders as `پردازش سند`.

---

## 14. Navigation labels

| Route | Persian | Short label |
| --- | --- | --- |
| `/` | مرکز فرمان | مرکز |
| `/workspaces` | فضاهای کاری | فضاها |
| `/sources` | کتابخانه منابع | منابع |
| `/search` | جست‌وجوی شواهد | جست‌وجو |
| `/companion` | همراه پژوهش | همراه |
| `/research` | پژوهش عمیق | پژوهش |
| `/knowledge` | گراف دانش | گراف |
| `/claims` | ادعاها و شواهد | ادعاها |
| `/timeline` | خط زمان | زمان |
| `/reports` | گزارش‌ها | گزارش |
| `/learning` | یادگیری | یادگیری |
| `/monitor` | پایش پردازش | پایش |
| `/settings` | تنظیمات | تنظیمات |

---

## 15. Writing rules

1. **Persian for the user, English for the machine.** UI strings, validation messages, and
   empty-state text are Persian. Identifiers, log messages, exception messages, and commit
   messages are English.
2. **Never hardcode a Persian enum name in a page.** Use `Labels`. A page that invents its own
   wording for an epistemic category is a defect.
3. **Never format a date inline.** Use `JalaliDate`. `ToString("yyyy/MM/dd")` is a defect.
4. **Use Persian digits in the UI.** `PersianFormat.Digits` and `JalaliDate.ToPersianDigits`
   convert. Counts, percentages, and dates all render with Persian numerals.
5. **ZWNJ matters.** `می‌کند` is not `می کند`. Copy the exact strings from `Labels.cs`.
