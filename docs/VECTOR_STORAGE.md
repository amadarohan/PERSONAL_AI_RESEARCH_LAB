# Vector storage

## 1. The constraint

The verified database is **SQL Server 2019 (RTM-GDR) 15.0.2210.4 Enterprise Edition**.

SQL Server 2019 has **no native vector type**. The `VECTOR` data type and `VECTOR_DISTANCE`
function were introduced in SQL Server 2025. There is also no `pgvector` equivalent, because
this is not PostgreSQL.

So the choice is not "native vectors or blobs". It is "which external vector store do we add,
or do we keep everything in one database".

---

## 2. The decision

**Store embeddings as packed little-endian float32 in a `varbinary` column and score them
in-process.**

`Domain/Documents/ChunkEmbedding.cs`:

```csharp
public byte[] Vector { get; set; } = Array.Empty<byte>();
public int Dimensions { get; set; }

public static byte[] Pack(IReadOnlyList<float> values)   // float[] -> byte[]
public static float[] Unpack(byte[] bytes)               // byte[] -> float[]
```

`Infrastructure/Retrieval/SqlVectorIndex.cs` implements `IVectorIndex`:

- `UpsertAsync(chunkId, workspaceId, vector)` — insert or replace one embedding
- `QueryAsync(workspaceId, vector, topK)` — load candidates, cosine-score, return top K
- `DeleteForDocumentAsync(documentId)` — remove embeddings for a document's chunks

`HybridRetrievalService` uses the same approach inline for fusion, filtering on
`Dimensions == queryVector.Length` so vectors from a different model are never compared.

---

## 3. Why this is the right trade-off here

| Consideration | Assessment |
| --- | --- |
| Deployment complexity | One database. No extra service to install, secure, back up, or monitor. |
| Operational surface | No second connection string, no second failure mode, no version skew. |
| Backup and restore | A single database backup captures documents, indexes, and vectors together. |
| Transactional consistency | Chunks and their embeddings commit in the same transaction. |
| Latency for a personal corpus | Tens of thousands of chunks score in well under a second. |
| Storage overhead | 4 bytes per dimension plus row overhead. A 1536-dimension vector is 6 KB. |

For a single-user research tool on a local machine, adding a vector database would be
strictly worse: more moving parts, more to break, and no benefit at this scale.

---

## 4. The cost, stated plainly

**Query cost is O(n) in the number of embeddings in the workspace.** Every candidate row is
loaded and scored on every query. There is no approximate nearest-neighbour index.

Concretely:

| Chunks in workspace | Rows loaded per query | Approximate behaviour |
| --- | --- | --- |
| 1,000 | 1,000 | Instant |
| 10,000 | 10,000 | Fast, noticeable memory churn |
| 100,000 | 100,000 | Slow; several hundred MB of transient allocation |
| 1,000,000 | 1,000,000 | Not viable |

The `Dimensions` filter reduces the set when models have changed, but it does not reduce the
scan when they have not.

This is a real limit, not a theoretical one. It is documented here so nobody discovers it in
production.

---

## 5. What would change if the corpus grew

In rough order of effort:

1. **Pre-filter by lexical candidates.** Score vectors only for chunks that already matched
   lexically, plus a bounded random sample. Cheap, loses recall on purely semantic queries.
2. **Add a `Hierarchical Navigable Small World` index in-process.** A library such as
   HNSWlib or a managed ANN implementation, persisted alongside the blobs. Moderate effort,
   keeps the single-database property.
3. **Move to SQL Server 2025** and use the native `VECTOR` type with `VECTOR_DISTANCE`.
   Requires a database upgrade; the schema change is small because the column already holds
   the raw payload.
4. **Add an external vector store** (Qdrant, Milvus, pgvector on a side PostgreSQL). Largest
   operational cost, best scaling.

None of these are implemented. The `IVectorIndex` abstraction exists so option 2 or 4 can be
added without touching `HybridRetrievalService`.

---

## 6. Correctness details that matter

### Endianness

`BitConverter.TryWriteBytes` writes in platform byte order. On x86/x64 and ARM64 this is
little-endian, which is consistent across every platform this application targets. If the
blobs were ever moved between architectures with different endianness, they would need
conversion. This is noted rather than handled, because it cannot occur in the supported
deployment.

### Dimension mismatch

Two embeddings are only comparable if they came from the same model. The `Dimensions` column
is checked before scoring. A model change therefore degrades to "old vectors are ignored"
rather than "old vectors produce garbage scores".

### Zero vectors

`CosineSimilarity` returns 0 when either norm is zero, rather than dividing by zero. A
provider that returns a zero vector produces no vector hits instead of a crash.

### Threshold

Vector hits at or below 0.01 similarity are discarded before fusion. Without a floor, a large
corpus would fill the candidate list with near-orthogonal noise that outranks nothing but
consumes the 60-candidate budget.

### Failure isolation

If embedding the *query* throws, the exception is caught, logged at warning, and vector search
returns empty. Lexical results are still returned. A broken embedding provider degrades
search quality; it does not break search.

---

## 7. Storage sizing

A 1536-dimension embedding is `1536 * 4 = 6144` bytes. With row overhead, budget roughly
6.5 KB per chunk.

| Chunks | Vector storage |
| --- | --- |
| 10,000 | ~65 MB |
| 100,000 | ~650 MB |
| 1,000,000 | ~6.5 GB |

Chunk text and the lexical index add to this. A 300-page PDF typically produces 400–900
chunks depending on density, so 100,000 chunks is on the order of 150–250 documents.

---

## 8. Verification performed

- `ChunkEmbedding.Pack` / `Unpack` round-trip is exercised by the ingestion pipeline on every
  document: vectors are packed on write and unpacked on every search.
- `SqlVectorIndex.QueryAsync` filters on `Dimensions` and returns ordered results.
- `HybridRetrievalService` degrades to lexical-only when no embedding provider resolves,
  which is the default state of a fresh installation.
