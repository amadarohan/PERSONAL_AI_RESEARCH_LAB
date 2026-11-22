# Retrieval

Retrieval is the core of the product. Everything the companion says, every research finding,
and every claim's evidence comes from this pipeline. It is designed so that a result can
always be explained and a refusal is always justified.

---

## 1. Pipeline overview

```
query text
   |
   +--> PersianEnglishTokenizer.Tokenize
   |       |
   |       +--> lexical search over ChunkTerm  -> up to 60 candidates
   |
   +--> embedding provider (if resolvable)
           |
           +--> cosine scan over ChunkEmbedding -> up to 60 candidates
   |
   +--> reciprocal rank fusion (k = 60)
           |
           +--> top N RetrievedPassage, each with LexicalScore, VectorScore,
                FusedScore, and RetrievalReason
```

Both branches are optional. If no embedding provider is resolvable, or embedding the query
throws, lexical results are still returned. Vector search is an enhancement, never a
prerequisite.

---

## 2. Tokenization

`Application/Text/PersianEnglishTokenizer.cs`

Persian needs explicit normalization before tokenizing. Without it, the same word written
two ways indexes as two different terms and recall collapses.

| Input | Normalized to | Why |
| --- | --- | --- |
| `\u064A` (Arabic yeh), `\u0649` (alef maksura) | `\u06CC` (Persian yeh) | Same letter, different code points |
| `\u0643` (Arabic kaf) | `\u06A9` (Persian keheh) | Same letter, different code points |
| `\u0622`, `\u0623`, `\u0625` | `\u0627` (bare alef) | Alef variants are not distinguished in search |
| `\u0629` (teh marbuta) | `\u0647` (heh) | Common in borrowed words |
| `\u0640` (tatweel) | `\u200C` (ZWNJ) | Tatweel is decorative |
| `\u0660`–`\u0669` (Arabic-Indic digits) | `0`–`9` | Digit unification |
| `\u06F0`–`\u06F9` (Extended Arabic-Indic digits) | `0`–`9` | Digit unification |
| `\u064B`–`\u0652` (harakat) | removed | Diacritics are not meaningful for search |

ZWNJ (`\u200C`) is treated as a **word separator**, not a joiner. Persian compound words
written with ZWNJ then index as their component words, which matches how users search.

Tokens shorter than two characters are dropped, as are stop words from a fixed Persian and
English list.

---

## 3. Chunking

`Application/Ingestion/TextChunker.cs`

| Parameter | Value |
| --- | --- |
| Target chunk size | 1100 characters |
| Maximum chunk size | 1600 characters |
| Overlap | 180 characters |

Chunking is **sentence-aware**. When a chunk would exceed the target, the splitter searches
backwards from the limit for a sentence terminator (`.`, `!`, `?`, `۔`, `؟`, newline). If
none is found it falls back to the last whitespace so words are never split. Only if both
fail does it hard-cut at the maximum.

Overlap exists so a fact that straddles a boundary is still retrievable from at least one
chunk.

`StartOffset` and `EndOffset` are preserved on every chunk so the reader can highlight the
exact span a citation refers to.

`EstimateTokens` uses a script-aware ratio: roughly 4 characters per token for Latin text,
dropping toward 2.5 as the Persian-character proportion rises. It is an estimate for
budgeting, not a billing figure.

---

## 4. Lexical search

`HybridRetrievalService.LexicalSearchAsync`

1. Tokenize the query, deduplicate, take at most 24 terms.
2. Fetch postings from `ChunkTerm` where `WorkspaceId` is in scope and `Term` is in the set.
3. Compute document frequency per term.
4. Score each chunk with a TF-IDF variant:

```
idf          = ln(1 + totalChunks / documentFrequency)
contribution = (1 + ln(frequency)) * idf
chunkScore   = sum of contributions over matched terms
```

5. Take the top 60 chunks.
6. Load chunk context (source title, document title, page number, text) in one query.

Rarer terms weigh more: a term appearing in 2 chunks is more informative than one appearing
in 200. The `1 + ln(frequency)` term damps repetition so a chunk that repeats a word ten
times does not dominate.

### Why not SQL Server full-text search

SQL Server 2019 ships English and Arabic word breakers only. Neither segments Persian
correctly, so `CONTAINS` and `FREETEXT` give poor recall on Persian text. A maintained
inverted index built by our own tokenizer is both more accurate and predictable. The cost is
that the index must be rebuilt when a document is reprocessed — which the ingestion pipeline
already does as stage 6.

---

## 5. Vector search

`HybridRetrievalService.VectorSearchAsync`

1. Resolve an embedding provider for the workspace. If none, return empty.
2. Embed the query text. If this throws, log a warning and return empty — lexical results
   must survive.
3. Load all embeddings in scope with matching `Dimensions`.
4. Compute cosine similarity in-process.
5. Discard anything at or below 0.01.
6. Take the top 60.

The `Dimensions` filter matters: if the embedding model changes, old vectors have a different
length and are silently incomparable. Filtering prevents a meaningless score from entering
the fusion.

---

## 6. Fusion

Reciprocal rank fusion, `k = 60`:

```
fused(chunk) = sum over branches of  1 / (k + rank_in_branch)
```

RRF is used instead of adding raw scores because lexical TF-IDF scores and cosine
similarities live on incomparable scales. Adding them would let whichever branch happens to
produce larger numbers dominate. RRF only uses rank, so it is scale-free.

Each result records which branches contributed:

- `"lexical match"` — found by the inverted index
- `"semantic similarity"` — found by the vector scan
- `"lexical match + semantic similarity"` — found by both, which is the strongest signal

This string is surfaced in the UI as `RetrievalReason`, so a user can see *why* a passage
appeared rather than trusting a black box.

---

## 7. The refusal policy

`ResearchCompanionService.AskAsync` refuses in two situations, and both are deliberate:

### No evidence

If retrieval returns zero passages, the companion returns
`CompanionAnswer.Refused("...")` with a Persian explanation. It does not call the model with
an empty context, because a model asked to answer without evidence will invent one.

### No compatible provider

If `IAiProviderResolver.ResolveChatAsync` returns `null` — because no provider is enabled, or
because every enabled provider is `CloudAllowed` while the workspace is `LocalOnly` — the
companion refuses with a different Persian message.

Neither path falls back to a cloud call. That is the whole point of `DataRoute`.

### Passage budget

At most 8 passages are retrieved, each truncated to `MaxPassageChars = 1200`. This bounds the
prompt size and keeps the model focused on the strongest evidence rather than a long tail.

---

## 8. Cross-workspace retrieval

Off by default. `RetrievalQuery.IncludeCrossWorkspace` adds workspaces where
`AllowCrossWorkspaceAccess` is true and `IsArchived` is false. The current workspace is
always included. This is an explicit opt-in per workspace, not a global setting.

---

## 9. Tuning knobs

| Constant | Location | Value | Effect |
| --- | --- | --- | --- |
| `TargetChunkChars` | `TextChunker` | 1100 | Larger chunks mean fewer, broader passages |
| `OverlapChars` | `TextChunker` | 180 | Larger overlap means more boundary recall, more storage |
| `LexicalCandidateLimit` | `HybridRetrievalService` | 60 | Candidates entering fusion |
| `VectorCandidateLimit` | `HybridRetrievalService` | 60 | Candidates entering fusion |
| `RrfK` | `HybridRetrievalService` | 60 | Higher flattens rank differences |
| `MaxPassageChars` | `ResearchCompanionService` | 1200 | Per-passage prompt budget |
| passage count | `ResearchCompanionService` | 8 | Total prompt evidence |
| `EvidencePerQuestion` | `DeepResearchService` | 5 | Passages per research question |

These are compile-time constants, not configuration. They were chosen for a personal corpus
and are documented here so the choice is visible rather than buried.

---

## 10. Known limitations

- **No reranking.** `IAiProviderResolver.ResolveRerankerAsync` always returns `null`. The
  interface exists so a reranker can be added without changing callers, but nothing
  implements it. The UI states this on the settings page.
- **Exact vector scan.** Every embedding in the workspace is loaded and scored. This is
  O(n) per query. Fine for tens of thousands of chunks; not for millions. See
  `docs/VECTOR_STORAGE.md`.
- **No query expansion.** The query is used as written. Persian synonyms are not expanded.
- **No stemming.** Persian morphological variants (`کتاب` / `کتابها`) are separate terms.
  ZWNJ splitting helps but does not solve it.
