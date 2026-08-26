# Fashion Accessory Digital Assets Pipeline — Solution Plan

> **Status:** Draft v1.0 — Pre-Architecture  
> **Date:** 2026-03-29  
> **Brand / Business:** "Latkans and Laces" (Family Fashion Accessories)  
> **Target:** B2B → B2C Evolution  

---

## 1. Executive Summary

This document outlines a phased plan to digitalize and automate the fashion accessory marketing and customer experience workflow. The solution leverages Generative AI to produce high-quality images and short promotional videos, and ultimately enables customers to virtually try accessories on their chosen fabric or suit design — all without stepping into a physical shop.

---

## 2. Current State Analysis

### 2.1 Manual Workflow (As-Is)

```
[Accessory Photo] 
      ↓ 
[Prompt 1 → AI: Extract Color & Design Features]
      ↓
[Prompt 2 → AI: Generate Image Prompts]
      ↓
[Gemini + NanoBanana: Generate Model Images]
      ↓
[Manual Capture of Generated Image]
      ↓
[Meta AI / Grok: Generate Short Video]
      ↓
[Add Music Manually]
      ↓
[Share via WhatsApp + Facebook Page]
```

### 2.2 Pain Points

| Pain Point | Impact |
|---|---|
| Fully manual, step-by-step | High time cost per accessory |
| No structured prompt management | Inconsistent image quality |
| Manual image capture between tools | Error-prone, slow handoff |
| Video generation is isolated | Cannot be batched or scheduled |
| No customer self-service | Lost sales opportunity |
| No digital catalog or search | Hard to cross-sell or up-sell |

---

## 3. Vision & Goals

### Phase 1 — Automated Multimedia Pipeline (Internal Tool)
Automate the end-to-end journey from a photo of an accessory to a shareable image and video. Reduce the time from hours to minutes per accessory.

### Phase 2 — Customer Facing App (B2C)
Allow boutique owners and customers to browse the digital catalog, pick an accessory, upload a dress image, and see an AI-generated preview of how the accessory looks on the dress.

### Phase 3 — Personalized Try-On (Advanced B2C)
Allow customers to upload a photo of a person and their measurements. The system generates a personalised image or short video of that individual wearing the chosen dress with the accessory applied.

---

## 4. Proposed Solution Architecture (High Level)

### 4.1 Core Modules

```
┌──────────────────────────────────────────────────────────────────────┐
│                     Fashion Accessory AI Pipeline                    │
├──────────────┬──────────────┬─────────────────┬──────────────────────┤
│  Accessory   │  AI Image    │  Video          │  Distribution /      │
│  Ingestion   │  Generation  │  Generation     │  Customer App        │
│  Module      │  Module      │  Module         │  Module              │
└──────────────┴──────────────┴─────────────────┴──────────────────────┘
```

#### Module A – Accessory Ingestion
- Upload photo of accessory (image file)
- Auto-extract features: color, texture, design pattern, dimensions
- Store structured metadata (name, type, color, size, collection tag)
- Generate a clean, tagged entry in the accessory catalog

#### Module B – AI Image Generation
- Use extracted features to auto-build image generation prompts
- Generate multiple variations (different suit styles, colors, settings)
- Store generated images linked to the accessory entry
- Human review / approval gate (optional toggle)

#### Module C – Video Generation
- Take approved images and auto-submit to video generation AI
- Optionally add background music from a royalty-free library
- Store final MP4 linked to the accessory entry

#### Module D – Distribution & Customer App
- Internal dashboard to review, manage and publish assets
- Customer-facing catalog (Phase 2)
- Virtual try-on feature (Phase 3)

---

## 5. Technology Stack (.NET-First, Open-Source, Free)

> **Principle:** .NET 8 everywhere (in sync with DotnetFastMCP), fully open-source, free tier to start — pay only when revenue justifies it.

> [!NOTE]
> **Why .NET 8?** The pipeline is built to match the current DotnetFastMCP version (`net8.0`), which is still actively supported (LTS until November 2026). Both projects will be upgraded to **.NET 10 LTS** simultaneously in a future sprint. See [DOTNET10_UPGRADE_PLAN.md](./DOTNET10_UPGRADE_PLAN.md) for the full upgrade analysis and plan.

### 5.1 AI & ML Services

| Task | Tool | Cost | Notes |
|---|---|---|---|
| Image Feature Extraction | **Google Gemini 2.0 Flash** | Free (15 req/min, 1M tokens/day) | Multimodal; best for fashion image analysis |
| Prompt Engineering / Generation | **Google Gemini 2.0 Flash** | Free tier | Chain-of-thought prompt generation |
| High-Quality Image Generation | **Google Imagen 3 via Gemini API** | Free tier with limits | Photo-realistic fashion model images |
| Open-Source Image Gen (fallback) | **Stable Diffusion XL** via Hugging Face Inference API | Free OSS | Self-hostable, no vendor lock-in |
| Video Generation (Phase 1 – semi-auto) | **Meta AI / Kling AI web UI** | Free | Manual upload; API automation added later |
| Video Generation (Phase 1+ – API) | **Kling AI API** / **Wan2.1 (open-source)** | Free OSS or minimal | Wan2.1 fully open-source and self-hostable |
| Accessory-on-Dress Compositing | **Gemini Image Editing API** / **SDXL Inpainting** | Free | Inpainting to blend accessory onto dress fabric |
| Personalized Try-On (Phase 3) | **IP-Adapter + ControlNet** (Hugging Face) | Free OSS | Preserves face identity |
| Background Music | **Pixabay API / Free Music Archive API** | Free | Royalty-free, API-accessible |

### 5.2 Backend (.NET Stack)

| Layer | Technology | Library / Framework | Cost |
|---|---|---|---|
| **MCP Tool Server** | .NET 8 | **DotnetFastMCP** (existing) | Free OSS |
| **REST API / Pipeline Controller** | .NET 8 | **ASP.NET Core Minimal APIs** | Free OSS |
| **Agentic AI Orchestration** | .NET 8 | **Microsoft Semantic Kernel** | Free OSS |
| **Background Job Queue** | .NET 8 | **Hangfire** (SQLite backend) | Free OSS |
| **ORM / Data Access** | .NET 8 | **Entity Framework Core** | Free OSS |
| **HTTP Client (AI APIs)** | .NET 8 | `HttpClientFactory` + **Polly** (retry/circuit breaker) | Free OSS |
| **Image Processing** | .NET 8 | **ImageSharp** (SixLabors) | Free OSS |
| **Video/Audio Overlay** | Local CLI | **FFmpeg** (called via `Process`) | Free OSS |
| **Configuration & Secrets** | .NET 8 | `Microsoft.Extensions.Configuration` + User Secrets | Free OSS |
| **Logging & Observability** | .NET 8 | **Serilog** + **OpenTelemetry** (already in DotnetFastMCP) | Free OSS |
| **Health Checks** | .NET 8 | **ASP.NET Core Health Checks** (already in DotnetFastMCP) | Free OSS |
| **Testing** | .NET 8 | **xUnit** + **Moq** + **Testcontainers** | Free OSS |
| **Containerization** | Docker | **Docker Desktop** / **Podman** | Free OSS |

### 5.3 Data Storage

| Store | Technology | Free Tier | Notes |
|---|---|---|---|
| **Relational DB (Catalog)** | **SQLite** → **PostgreSQL** | SQLite free forever; PostgreSQL on Supabase (500 MB free) | EF Core migrations handle both |
| **Object Storage (Images/Videos)** | **Cloudflare R2** | 10 GB free, zero egress cost | Best free option; S3-compatible API |
| **Caching** | **FusionCache** (.NET) | Free OSS | In-process + optional Redis backing |
| **Vector Store (Phase 3 – semantic search)** | **Qdrant** (open-source) | Free (self-hosted) | For matching accessories by color/pattern |

### 5.4 Frontend

| Layer | Technology | Cost | Notes |
|---|---|---|---|
| **Admin Dashboard** | **Blazor WebAssembly** (.NET) | Free OSS | Keeps stack pure .NET; no JS required |
| **Customer App (Phase 2)** | **Next.js** (PWA) | Free OSS | Better SEO; deployed on Vercel free tier |
| **Authentication** | **Supabase Auth** | Free (50K users) | Google, phone OTP, magic link |
| **Hosting (API)** | **Any Container Host** (GCP/Azure/Railway/Render) | Free Tiers | Docker container deployment prevents vendor lock-in |
| **Hosting (Frontend)** | **Vercel** / **Cloudflare Pages** | Free | Global CDN included |

### 5.5 Distribution & Integration

| Channel | Tool | Cost | Notes |
|---|---|---|---|
| **WhatsApp Integration** | **Manual (Ph 1)** → **Meta Cloud API (Ph 2)** | Free → Pay per convo | Start with personal WA link → Upgrade to Business API for automation |
| **Facebook Page Posts** | **Facebook Graph API** | Free | Auto-post new arrivals |
| **Email** | **Brevo (Sendinblue)** | Free (300 emails/day) | Marketing newsletters |

### 5.6 DevOps

| Task | Tool | Cost |
|---|---|---|
| **Source Control** | **GitHub** (public or private) | Free |
| **CI/CD** | **GitHub Actions** | Free (2000 min/month) |
| **Container Registry** | **GitHub Container Registry (GHCR)** | Free |
| **Secrets in CI** | **GitHub Secrets** | Free |

---

## 6. Phased Rollout Plan

### Phase 1 — Automated Pipeline (Weeks 1–4)

**Goal:** From accessory photo → shareable image & video in one click.

**Steps:**
1. **Ingestion Service** — ASP.NET Core Minimal API endpoint accepts accessory photo + metadata (name, type, size)
2. **Feature Extraction MCP Tool** — Calls Gemini 2.0 Flash with Prompt 1; returns structured feature JSON
3. **Prompt Generation MCP Tool** — Uses features + prompt templates to produce 2–5 image generation prompts
4. **Image Generation MCP Tool** — Submits prompts + original image to Gemini Imagen 3 API via HttpClientFactory
5. **Hangfire Job Queue** — All AI steps run as background jobs; dashboard shows progress
6. **Review Dashboard** — Blazor or simple HTML page to approve/reject generated images
7. **Video Generation Step** — Hybrid: Start with free API quotas (Kling/Meta) → auto-fallback to prompt user for manual web UI upload when quota exhausted
8. **Music Overlay** — FFmpeg attaches auto-selected audio track (with optional overrides for mood/genre/custom upload)
9. **Output Store** — ImageSharp for any resizing/WebP conversion; final assets saved to Cloudflare R2; metadata to SQLite via EF Core

**Deliverable:** A pipeline CLI or simple web UI where you upload a photo and get back images + a video.

---

### Phase 2 — Customer Digital Catalog (Weeks 5–10)

**Goal:** Boutique owners browse catalog, pick accessory, see AI-generated previews.

**Steps:**
1. Build catalog API (CRUD for accessories + assets)
2. Customer-facing PWA: browse by category, color, collection (English first, architected for easy i18n expansion to Hindi/Urdu later)
3. **Virtual "Apply" Feature:**
   - Customer uploads dress image
   - System calls Gemini image editing API to composite the accessory onto the dress
   - Preview is shown in-browser
4. WhatsApp / Facebook auto-posting of new arrivals
5. Authentication (Firebase / Supabase) for boutique owner accounts

---

### Phase 3 — Personalised Try-On (Weeks 11–20)

**Goal:** Customer uploads their own photo + measurements → personalized video try-on.

**Steps:**
1. Accept person photo + body measurements input
2. Use ControlNet / IP-Adapter (Hugging Face) to preserve facial identity in generated image
3. Generate image: person + dress + accessory applied
4. Generate personalized video
5. Optionally notify customer via WhatsApp with their personalized video

> **Note:** Phase 3 involves facial images requiring GDPR/privacy compliance. Images must not be stored without consent.

---

## 7. Automation Testing Strategy

> **Philosophy:** Testing is built in from Day 1, not bolted on later. Simple, fast, and robust — with a radical twist: AI is used to test AI.

### 7.1 Testing Pyramid (Layered Approach)

```
              ┌────────────────────────┐
              │  Layer 5: Chaos Tests  │  (resilience, fault injection)
            ┌─┴────────────────────────┴─┐
            │  Layer 4: AI Quality Tests │  (LLM-as-Judge, visual hash)
          ┌─┴────────────────────────────┴─┐
          │  Layer 3: Integration Tests    │  (Testcontainers, WireMock)
        ┌─┴──────────────────────────────────┴─┐
        │  Layer 2: Contract / Snapshot Tests   │  (MCP tools, prompts)
      ┌─┴────────────────────────────────────────┴─┐
      │         Layer 1: Unit Tests                 │  (xUnit + Moq, <5s)
      └─────────────────────────────────────────────┘
```

**Rule:** Every new feature must include tests from at least Layer 1 and Layer 2. Layers 3–5 run in CI on every PR.

---

### 7.2 Layer 1 — Unit Tests (Fast, Isolated)

**Goal:** Test all business logic in complete isolation from AI APIs, databases, and file systems.

| Aspect | Detail |
|---|---|
| **Framework** | **xUnit** + **Moq** |
| **Target** | All domain logic, prompt builders, feature extractors, metadata mappers |
| **Speed target** | Full suite runs in < 5 seconds |
| **Principle** | No network, no disk, no AI calls — ever |
| **Coverage gate** | 80% line coverage minimum enforced in CI |

**Key things to unit test:**
- Prompt template rendering (given a feature JSON, assert the exact generated prompt text)
- Feature extraction response parsing (JSON → domain model)
- Catalog metadata mapping logic
- Storage path and filename generation rules
- Hangfire job arguments serialization

---

### 7.3 Layer 2 — Contract & Snapshot Tests (Prompt Integrity)

> **Radical element:** Lock every AI prompt as a versioned golden file. Any prompt change is a breaking contract that must be reviewed — preventing silent quality regressions.

| Aspect | Detail |
|---|---|
| **MCP Tool Contracts** | **Pact.NET** — consumer-driven contract tests for every MCP tool (input schema + output schema) |
| **Prompt Snapshot Tests** | **Verify** (VerifyTests NuGet) — golden file snapshots of generated prompts |
| **Prompt Drift Detection** | Any change to a prompt template fails CI until the snapshot is explicitly approved and committed |
| **JSON Schema Validation** | EF Core model output validated against JSON Schema on every test run |

**How Prompt Snapshot Testing works:**
```
Given: accessory feature JSON (fixed, checked-in input)
When: PromptGenerator.Generate() is called
Then: the output is byte-for-byte identical to "approved" snapshot file
      → if different, test FAILS and shows a diff
      → developer must explicitly approve the change with: `dotnet verify accept`
```
This means no one can accidentally change a prompt silently — every prompt change is a deliberate, reviewed, committed decision.

---

### 7.4 Layer 3 — Integration Tests (Real Infra, Mocked AI)

**Goal:** Test that components wire together correctly without hitting real AI APIs.

| Aspect | Detail |
|---|---|
| **DB Integration** | **Testcontainers** spins up a real PostgreSQL container per test run |
| **Gemini API Mock** | **WireMock.NET** — pre-recorded Gemini responses replayed in tests (VCR pattern) |
| **Hangfire Jobs** | Test job execution in-process using Hangfire's in-memory storage |
| **Cloudflare R2 Mock** | WireMock or `aws-sdk` fake S3 endpoint for storage tests |
| **MCP Endpoint Tests** | `WebApplicationFactory<Program>` — spin up the real ASP.NET Core pipeline in memory |

**VCR Pattern for AI APIs:**
- First run: real Gemini calls are made and responses recorded to JSON cassette files
- All subsequent runs: cassettes are replayed — no API cost, fully deterministic
- Cassettes are committed to source control alongside tests

---

### 7.5 Layer 4 — AI Quality Tests ⚡ (Radical)

> **The radical approach: use AI to test AI.** A cheap, fast LLM (Gemini 2.0 Flash) acts as an automated quality judge evaluating outputs — catching subtle regressions that no rule-based test ever could.

#### 7.5.1 LLM-as-Judge (Output Quality Scoring)

```
[Generated Image Prompt]
        ↓
[Gemini 2.0 Flash — Judge Agent]
        ↓
[Quality Score: 0–100 + Pass/Fail Verdict]
        ↓
[CI Gate: score must be ≥ 80 to pass]
```

**Judge evaluates:**
- Does the prompt correctly reference the accessory's extracted color?
- Does it mention the correct suit type from the template?
- Does it include all mandatory photography directives (full body, studio setting)?
- Does it avoid any banned/ambiguous terms?

**Cost:** Gemini 2.0 Flash is free tier (15 req/min) — quality tests run on PR preview, not on every commit.

#### 7.5.2 Perceptual Hash Testing for Images

For generated images (when Imagen 3 is called in a staging environment), use **perceptual hashing** (`ImageSharp` + `Shipwreck.Phash`) to:
- Store a reference hash of a "known good" generated image
- On regression: regenerate and compare hash — flag if visual similarity drops below threshold
- Much faster and cheaper than human review for batch regression checks

#### 7.5.3 Property-Based Testing (Random Input Exploration)

**FsCheck** (NuGet: `FsCheck.Xunit`) generates hundreds of random accessory feature combinations and asserts:
- Prompt generator never throws an exception
- Output always contains required keywords
- Output length is always within Imagen 3's token limit

---

### 7.6 Layer 5 — Chaos & Resilience Tests

**Goal:** Deliberately break things to prove the system recovers gracefully.

| Scenario | Test Approach |
|---|---|
| Gemini API returns 429 (rate limit) | WireMock injects 429 → assert Polly retries 3× with exponential backoff |
| Gemini API times out | WireMock delays 30s → assert timeout + fallback message |
| Cloudflare R2 upload fails | Mock throws IOException → assert Hangfire job retries, not silently dropped |
| Database connection lost mid-job | Testcontainers stop container mid-test → assert graceful error and no partial data |
| Concurrent accessory uploads | 50 parallel requests → assert no race conditions, all jobs complete |

**Tool:** **Simmy** (Polly chaos extension for .NET) — inject faults declaratively in test code.

---

### 7.7 AI-Assisted Test Generation ⚡ (Radical)

> **Radical concept:** A Semantic Kernel agent reads new MCP tool code and auto-generates a starter test file — dramatically lowering the friction of writing tests for new tools.

**How it works:**
1. Developer writes a new `McpTool` method
2. A `GenerateTestsAgent` (SK agent) reads the method signature + XML doc comments
3. Agent generates xUnit test stubs covering: happy path, null inputs, boundary values, error cases
4. Developer reviews and fills in assertions

**Benefit:** No excuse for missing test coverage — the boilerplate is always pre-generated.

---

### 7.8 Mutation Testing (Verify Your Tests Actually Work)

**Stryker.NET** (`dotnet stryker`) automatically mutates your source code (flips `>` to `>=`, removes `if` conditions, etc.) and runs your tests against each mutation. If your tests don't catch the mutation, the test is considered **weak**.

| Metric | Target |
|---|---|
| Mutation score | ≥ 75% for core pipeline logic |
| Run frequency | Weekly in CI (slow but high value) |
| Scope | Layer 1 (unit tests) only — too slow for integration tests |

---

### 7.9 CI/CD Integration

```
[Git Push / PR]
      ↓
┌─────────────────────────────────────────────┐
│  GitHub Actions Pipeline                    │
│                                             │
│  1. Layer 1: Unit Tests          (<30s)     │
│  2. Layer 2: Contract/Snapshot   (<60s)     │
│  3. Layer 3: Integration Tests   (<3 min)   │
│  4. Layer 4: AI Quality Tests    (<5 min)   │  ← PR only
│  5. Layer 5: Chaos Tests         (<5 min)   │  ← PR only
│                                             │
│  Layers 1–3: Run on EVERY push              │
│  Layers 4–5: Run on PR and nightly only     │
│                                             │
│  Weekly: Mutation Testing (Stryker.NET)     │
└─────────────────────────────────────────────┘
```

**Fast feedback:** Layers 1–3 complete in < 5 minutes total. A developer gets a pass/fail verdict before their coffee is ready.

---

### 7.10 Testing Summary

| Layer | Tool(s) | Speed | When Runs | Radical? |
|---|---|---|---|---|
| 1 — Unit | xUnit + Moq | < 5s | Every push | No |
| 2 — Contract/Snapshot | Pact.NET + Verify | < 60s | Every push | Semi (prompt locks) |
| 3 — Integration | Testcontainers + WireMock.NET | < 3 min | Every push | No |
| 4 — AI Quality | LLM-as-Judge + Phash + FsCheck | < 5 min | PR + nightly | **Yes** |
| 5 — Chaos | Simmy (Polly chaos) | < 5 min | PR + nightly | Semi |
| Weekly — Mutation | Stryker.NET | ~20 min | Weekly | **Yes** |
| On-demand — AI Gen | SK GenerateTestsAgent | Instant | On new tools | **Yes** |

---

## 8. Security Considerations

### B2B Phase (Phase 1)
- API key authentication (rate-limited)
- All Gemini API keys stored in environment variables / secrets manager (never hardcoded)
- HTTPS-only endpoints
- Role-based access: only you can trigger pipeline

### B2C Phase (Phase 2+)

| Concern | Mitigation |
|---|---|
| Customer Authentication | OAuth 2.0 via Firebase/Supabase (Google, phone OTP) |
| Customer Image Privacy | Images stored encrypted; deleted after 24–48 hours unless user opts to save |
| API Abuse Prevention | Rate limiting per user, CAPTCHA on public endpoints |
| Payment (future) | Stripe (PCI-DSS compliant), never handle card data directly |
| Data Residency | Choose storage region matching customer base (India: Mumbai) |
| Facial Image Consent | Explicit consent modal before Phase 3 upload; lawful basis documented |
| Secrets Management | Azure Key Vault / HashiCorp Vault (or GCP Secret Manager free tier) |

---

## 9. Performance Considerations

| Area | Strategy |
|---|---|
| Image Generation Latency | Async job queue (Hangfire / Celery); show progress bar to user |
| Video Generation | Offload to background worker; notify via WhatsApp when done |
| CDN for Serving Assets | Cloudflare CDN (free tier) in front of storage |
| Caching | Cache Gemini feature extraction results per unique image hash |
| Parallel Processing | Generate multiple image variations in parallel (Gemini batch API) |
| Mobile Performance | Serve compressed WebP images; lazy load in catalog |

---

## 10. Scalability Considerations

| Dimension | Approach |
|---|---|
| Pipeline Throughput | **Volume: ~20 items/week.** Stateless Hangfire workers easily handle this load. |
| Storage | Object storage (Cloudflare R2, GCP Cloud Storage, or Azure Blob) scales automatically |
| Database | Start SQLite → migrate to PostgreSQL (Supabase free tier) |
| API Layer | Containerize with Docker; deploy on best active free tier (GCP Cloud Run / Railway / Render / Azure) |
| Multi-tenant (B2C) | **Scale: 5 boutiques → 30 in 6 months.** Tenant isolation at DB level (tenant_id). |
| AI Cost at Scale | Cache repeated prompts; negotiate Gemini committed use discount at scale |

---

## 11. Agentic AI Strategy

> **Short Answer: Yes — Agentic AI is strongly recommended, especially for Phase 2+ scale.**

### 10.1 Why Agentic AI?

As the pipeline grows beyond a single accessory per run, manually chaining AI calls becomes fragile. An **AI Agent** can:
- Decide which tools to call in what order
- Retry failed steps autonomously
- Handle conditional logic (e.g., "if extracted color is unclear, ask for clarification")
- Scale to process dozens of accessories in parallel
- Be prompted in natural language ("Process all new accessories added this week")

### 10.2 Recommended Approach: Microsoft Semantic Kernel (not AutoGen)

**Semantic Kernel (SK)** is Microsoft's open-source .NET SDK for building AI agents and pipelines. It integrates directly with Gemini, OpenAI, and other LLMs and supports MCP natively.

> [!NOTE]
> **Why not AutoGen?** Microsoft AutoGen is Python-first; its .NET port is still maturing. More importantly, AutoGen has no native MCP support, while SK integrates with DotnetFastMCP tools out of the box. SK's built-in `AgentGroupChat` handles all multi-agent scenarios needed for this project. **Decision: Semantic Kernel only — no AutoGen needed.**


```
┌─────────────────────────────────────────────────────────┐
│               Semantic Kernel Agent                     │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Planner / Orchestrator (Gemini 2.0 Flash LLM)  │   │
│  └────────────────┬─────────────────────────────────┘   │
│                   │ decides which tools to call          │
│  ┌────────────────▼─────────────────────────────────┐   │
│  │          SK Plugin ≡ MCP Tool (via DotnetFastMCP) │   │
│  │  extract_accessory_features                       │   │
│  │  generate_image_prompts                           │   │
│  │  generate_accessory_images                        │   │
│  │  generate_accessory_video                         │   │
│  │  apply_accessory_to_dress                         │   │
│  │  publish_to_catalog                               │   │
│  │  post_to_social                                   │   │
│  └───────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### 10.3 Agent Types by Phase

| Phase | Agent | Responsibility |
|---|---|---|
| Phase 1 | **Pipeline Agent** | Orchestrates: ingest → extract → prompt → image → video → store |
| Phase 2 | **Catalog Agent** | Monitors new uploads, auto-publishes to catalog, posts to WhatsApp/Facebook |
| Phase 2 | **Customer Service Agent** | Answers questions about accessories (color match, availability) in chat |
| Phase 3 | **Try-On Agent** | Manages personalized image/video generation per customer request |

### 10.4 SK + MCP Integration

DotnetFastMCP tools are exposed as **SK Plugins** — the agent can call any MCP tool as a native SK function. This means:
- No duplicate code: the same MCP tool serves both API consumers AND the AI agent
- Semantic Kernel's built-in **ChatCompletionAgent** handles tool-use loop automatically
- Supports parallel tool calls for generating multiple image variations simultaneously

### 10.5 Agentic AI Decision

| Scenario | Recommendation |
|---|---|
| Phase 1 (internal, single user) | Simple sequential pipeline is sufficient; SK agent is optional but good to design for |
| Phase 2 (multiple boutique owners) | SK agent becomes essential for parallel processing and reliability |
| Phase 3 (many personalized requests) | Multi-agent system required; agents run as Hangfire jobs with retry |

---

## 12. MCP Integration Strategy

Since the existing **DotnetFastMCP** framework is already available, the pipeline will be exposed as a set of **MCP Tools** that can be orchestrated by any MCP-compatible LLM client (Claude, Gemini, custom agent):

| MCP Tool | Description |
|---|---|
| `extract_accessory_features` | Takes image URL, returns structured feature JSON |
| `generate_image_prompts` | Takes feature JSON + style preferences, returns 2–5 prompts |
| `generate_accessory_images` | Takes prompts + image, calls Imagen API, returns image URLs |
| `generate_accessory_video` | Takes image URL + music preference, returns video URL |
| `publish_to_catalog` | Stores asset metadata + file URLs in catalog DB |
| `post_to_social` | Posts image + caption to Facebook / WhatsApp |
| `apply_accessory_to_dress` | Takes dress image + accessory image, returns composite image |

This makes the pipeline **AI-agent orchestratable** — a single prompt to an LLM like Gemini or Claude can trigger the entire pipeline end-to-end via MCP. Combined with **Semantic Kernel**, each MCP tool is also a native SK Plugin callable by the AI agent.

---

## 13. Key Decisions & Parameters (Confirmed)

Based on business discussions, the following parameters are locked in:
- **Brand Name:** "Latkans and Laces"
- **Volume:** ~20 accessories per week (low concurrent load, predictable queue sizing)
- **B2C Scale:** Starting with 5–6 boutiques, expanding to ~30 within 6 months (requires rapid expansion headroom)
- **Video Strategy:** Hybrid approach — utilize free API quotas (Kling/Sora) first, then gracefully degrade to prompt for manual web UI generation when quotas hit.
- **WhatsApp:** Start manual with Personal WhatsApp → upgrade to WhatsApp Business API when automation is required in Phase 2.
- **Language:** English first, but UI components must be i18n-ready for Hindi/Urdu expansion.
- **Hosting:** Cloud-agnostic and free-tier focused. Core services will be containerized (Docker) to easily deploy and move between the best free tiers (GCP, Azure, Railway, or Render) without vendor lock-in.
- **Music:** Auto-selected by default, but system must support optional overrides (mood, genre, or specific audio upload).

---

## 14. Recommended Next Steps

1. ✅ Review this plan and answer the open questions above
2. 📐 **Architecture Document** — Detailed system architecture with component diagrams
3. 📋 **Implementation Plan** — File-by-file breakdown of what to build, in what order
4. 🏗️ **Phase 1 Prototype** — End-to-end pipeline (extract → prompt → image → catalog)
5. 🎬 **Phase 1 Video** — Semi-automated video generation step
6. 🛍️ **Phase 2 Customer App** — Catalog + virtual try-on
7. 👤 **Phase 3 Personalized Try-On** — Identity-preserving generation

---

*This plan was generated based on the described business workflow. It will evolve as we finalize architecture decisions and get answers to the open questions.*
