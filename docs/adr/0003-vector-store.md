# ADR 0003 — Vector store: pgvector on PostgreSQL, behind a port

- **Status:** Accepted
- **Date:** 2026-09-12
- **Decision needed:** Where do dense embeddings live, and how serialisable is the choice?
- **Decision:** The vector store is a **port** (`IVectorStore` in `HR.Application`) with two
  adapters: `PostgresVectorStore` (pgvector, HNSW cosine index, production target) and
  `InMemoryVectorStore` (brute-force cosine over in-memory arrays; dev/tests and the
  docker-less eval path). Selection is configuration, not code.
- **Rationale:**
  - One database for relational + vector means one backup, one migration story, one
    credential domain — right-sized for 30-doc / 150-page corpora.
  - pgvector is operated by the project already (prior work) and runs in Docker today,
    so the estimate is internal.
  - HNSW gives `ORDER BY embedding <=> $1` at a few ms at our scale; no external service to
    fail or leak data to.
- **Alternatives considered and rejected:**
  - *Managed vector DB (Pinecone/Qdrant/Weaviate):* excellent at scale; adds a SaaS
    dependency, pricing and a second data plane for no benefit at this corpus size. Tracked
    as a scale-out option in `SYSTEM-DESIGN.md` Part A.
  - *Elastic/OpenSearch:* heavy for this; keyword search is better served by an in-process
    BM25 index.
  - *In-memory only:* too lossy across restarts; embeddings must survive in the store.
- **Consequences:** Embedding dimension is pinned at migration time (`DB__EmbeddingDimension`,
  default 768 = nomic-embed-text). Switching embedding model to a different dimension needs a
  new migration — documented in README and the SDD gap table.