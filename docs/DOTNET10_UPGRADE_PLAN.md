# DotnetFastMCP + Pipeline Upgrade to .NET 10 — Planning Document

> **Status:** Deferred — to be done simultaneously for both projects after Phase 1 pipeline is stable  
> **Priority:** Medium  
> **Trigger:** When DotnetFastMCP and the Multimedia Pipeline are both ready to upgrade together

---

## 1. Background & Decision

The Multimedia Pipeline was initially built targeting **net8.0** to stay in sync with the current DotnetFastMCP framework version. The upgrade to **.NET 10 LTS** (released November 2025, supported until November 2028) will be done simultaneously for both projects once the pipeline reaches a stable state.

**Why .NET 10 and not .NET 9?**
- .NET 9 is STS and reaches end-of-life May 2026 — avoid for long-lived projects
- .NET 10 LTS is supported until November 2028 — the right target for B2C growth

---

## 2. Scope

Both repositories must be upgraded in the same sprint:

| Project | Current Target | Upgrade Target |
|---|---|---|
| `DotnetFastMCP` (framework) | `net8.0` | `net10.0` |
| `FashionAccessoryPipeline` (new pipeline) | `net8.0` | `net10.0` |

---

## 3. DotnetFastMCP Upgrade Analysis (Analysed March 2026)

### 3.1 Project Inventory

All 22 projects currently target `net8.0`:
- 3 core projects (`FastMCP`, `FastMCP.OpenApi`, `FastMCP.CLI`)
- 18 example projects
- 1 integration test project

### 3.2 ASP.NET Core Code Assessment

The entire codebase uses **standard, stable ASP.NET Core patterns** with no deprecated APIs:
- `WebApplicationBuilder` / `WebApplication`
- Minimal APIs (`MapGet`, `MapPost`)
- Middleware pipeline (`UseAuthentication`, `UseAuthorization`, `UseRateLimiter`, `UseCors`)
- `AddAuthentication`, `AddAuthorization`, `AddRateLimiter`, `AddCors`, `AddHostedService`
- JWT Bearer, OIDC, Google OAuth, GitHub OAuth
- **No breaking changes** are expected from .NET 8 → 10 in any of these APIs

### 3.3 NuGet Package Changes Required

| Package | Current | Target | Risk |
|---|---|---|---|
| `Microsoft.AspNetCore.Authentication.Google` | `8.0.0` | `10.0.x` | 🟢 Low |
| `AspNet.Security.OAuth.GitHub` | `8.0.0` | `10.0.x` | 🟢 Low |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | `8.0.0` | `10.0.x` | 🟢 Low |
| `Microsoft.Identity.Web` | `2.15.2` | `3.x` | 🟡 Medium — major version; review `AddMicrosoftIdentityWebApp()` |
| `Microsoft.Extensions.Configuration.Binder` | `8.0.0` | `10.0.x` | 🟢 Low |
| `System.IdentityModel.Tokens.Jwt` | `8.0.0` | `8.x latest` | 🟢 Low — independent track |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | `8.0.0` | `8.x latest` | 🟢 Low — independent track |
| `Microsoft.Extensions.Http.Polly` | `8.0.0` | `10.0.x` | 🟢 Low |
| `OpenTelemetry` | `1.9.0` | `1.11.x+` | 🟢 Low — semver-independent |
| `Microsoft.OpenApi` / `.Readers` | `1.6.11` | `1.6.x latest` | 🟢 Low — independent track |
| `System.CommandLine` | `2.0.0` | `2.0.0` | 🟢 No change |

> [!IMPORTANT]
> **`Microsoft.Identity.Web` v2 → v3** is the only meaningful risk. v3 changed some `AddMicrosoftIdentityWebApp()` overloads. The `AddAzureAd()` method in `McpAuthenticationExtensions.cs` (line ~144–218) must be reviewed against the v3 migration guide.

### 3.4 File-by-File Code Changes

| File | Change | Effort |
|---|---|---|
| `src/FastMCP/FastMCP.csproj` | `<TargetFramework>net10.0</TargetFramework>` + bump 6 package versions | ⏱ 5 min |
| `src/FastMCP.OpenApi/FastMCP.OpenApi.csproj` | `<TargetFramework>net10.0</TargetFramework>` | ⏱ 2 min |
| `src/FastMCP.CLI/FastMCP.CLI.csproj` | `<TargetFramework>net10.0</TargetFramework>`, remove `<LangVersion>12.0</LangVersion>` (net10 defaults to C# 13) | ⏱ 2 min |
| `Hosting/McpAuthenticationExtensions.cs` | Review `AddMicrosoftIdentityWebApp()` for MS.Identity.Web v3 API changes | ⏱ 1–2 hrs |
| All 18 example `.csproj` files | `<TargetFramework>net10.0</TargetFramework>` (scriptable) | ⏱ 20 min |
| `tests/McpIntegrationTest/McpIntegrationTest.csproj` | `<TargetFramework>net10.0</TargetFramework>` | ⏱ 2 min |

### 3.5 Effort Estimate

| Category | Estimated Effort |
|---|---|
| `.csproj` target framework updates (all 22) | 0.5 days (scriptable) |
| NuGet version bumps + restore | 0.5 days |
| `Microsoft.Identity.Web` v3 review & fix | 0.5–1 day |
| Build verification + smoke testing | 0.5 days |
| **Total** | **1.5–2.5 days** |

---

## 4. Pipeline Project Upgrade

The pipeline project will be trivially upgraded alongside DotnetFastMCP — just change `<TargetFramework>net8.0</TargetFramework>` to `net10.0` in all pipeline project files and update any 8.x-pinned packages.

---

## 5. Multi-Targeting Option

Instead of dropping `net8.0` support from the DotnetFastMCP NuGet package entirely, consider:

```xml
<TargetFrameworks>net8.0;net10.0</TargetFrameworks>
```

This allows existing consumers on .NET 8 to continue using the package while new projects target .NET 10. The pipeline itself should target `net10.0` only.

---

## 6. Recommended Upgrade Order

1. ✅ Upgrade `FastMCP.csproj` (core) first — get it compiling on `net10.0`
2. ✅ Upgrade `FastMCP.OpenApi` and `FastMCP.CLI`
3. ✅ Run `dotnet build` — catch any breaking compile errors
4. ✅ Review `McpAuthenticationExtensions.cs` for `Microsoft.Identity.Web` v3 changes
5. ✅ Script-upgrade all 18 example `.csproj` files (`net8.0` → `net10.0`)
6. ✅ Upgrade all pipeline `.csproj` files
7. ✅ Run integration tests to confirm all transports work
8. ✅ Tag DotnetFastMCP as `v2.0.0` (major version = .NET 10 LTS baseline)
9. ✅ Update both READMEs to reflect .NET 10 requirement

---

## 7. Risk Assessment

| Risk | Likelihood | Mitigation |
|---|---|---|
| `Microsoft.Identity.Web` v3 API breaks | Medium | Follow official migration guide |
| OpenTelemetry exporter incompatibility | Low | OTel versioning independent of .NET runtime |
| Runtime behavioral changes (.NET 8→10) | Very Low | No known breaking changes in APIs used |
| Existing DotnetFastMCP consumers broken | Low | Multi-targeting (`net8.0;net10.0`) in NuGet package |

---

*Deferred from main PLAN.md on 2026-03-29. Revisit when Phase 1 of the pipeline is stable.*
