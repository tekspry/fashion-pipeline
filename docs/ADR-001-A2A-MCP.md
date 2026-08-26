# ADR-001: Phase 1 uses A2A agents + 4 DotnetFastMCP servers

## Decision
Implement ARCHITECTURE_PHASE1.md: Hangfire → A2A Orchestrator (:5050) → sub-agents (:5101–5401) → MCP servers (:5100–5400).

## Rejects
In-process Semantic Kernel AgentGroupChat as production orchestration (retained only in Path 2 interim appendix).

## Storage
Cloudflare R2 (S3-compatible) for object storage; EF SQLite dev / PostgreSQL prod.