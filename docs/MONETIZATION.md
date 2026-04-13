# SharpClaw Code — Monetization Strategy

## Document Info

| Field | Value |
|-------|-------|
| Status | Draft |
| Author | telli |
| Date | 2026-04-10 |
| Model | Open Core |

---

## 1. Strategy Summary

SharpClaw Code follows an **open-core model** with phased revenue introduction:

| Phase | Focus | Revenue | Timeline |
|-------|-------|---------|----------|
| **Phase 1** | Adoption & category ownership | $0 (investment phase) | Months 0-12 |
| **Phase 2** | Enterprise & ISV monetization | Enterprise licenses + support | Months 6-18 |
| **Phase 3** | Consumer & ecosystem monetization | Pro tier + marketplace | Months 12-24 |

The core runtime remains MIT-licensed permanently. Revenue comes from proprietary extensions, managed services, and ecosystem fees that serve segments willing to pay for capabilities the open-source core intentionally doesn't include.

---

## 2. What Stays Free (Forever)

The open-source core includes everything needed to build and run a coding agent:

| Capability | Package |
|------------|---------|
| All protocol contracts and DTOs | `SharpClaw.Code.Protocol` |
| Full runtime orchestration | `SharpClaw.Code.Runtime` |
| Anthropic and OpenAI-compatible providers | `SharpClaw.Code.Providers.*` |
| Built-in tools (read, write, edit, grep, glob, bash) | `SharpClaw.Code.Tools` |
| Permission policy engine and approval gates | `SharpClaw.Code.Permissions` |
| Session persistence (file-backed) | `SharpClaw.Code.Sessions` |
| MCP client integration | `SharpClaw.Code.Mcp` |
| Plugin system | `SharpClaw.Code.Plugins` |
| Structured telemetry and event publishing | `SharpClaw.Code.Telemetry` |
| CLI with REPL, slash commands, JSON output | `SharpClaw.Code.Cli` |
| Spec workflow mode | `SharpClaw.Code.Runtime` |
| All documentation and examples | `docs/` |

**Principle:** A single developer or small team should never hit a paywall for core agent functionality. The free tier must be genuinely useful, not a crippled demo.

---

## 3. Phase 1 — Investment Phase (Months 0-12)

### Revenue: $0

### Goal: Category Ownership

All effort goes into adoption, community, and establishing SharpClaw as the default .NET agent runtime.

### Investment Activities

| Activity | Purpose | Cost |
|----------|---------|------|
| NuGet package publishing | Frictionless adoption | CI/CD time |
| Documentation + tutorials | Reduce time-to-value | Author time |
| Conference talks / blog posts | .NET community visibility | Travel + time |
| Discord community | Developer engagement | Moderation time |
| GitHub Sponsors | Signal legitimacy, collect early support | $0 cost |
| "Built with SharpClaw" showcase | Social proof | Curation time |

### Early Revenue Signals (Not Revenue)

- **GitHub Sponsors:** Accept individual and corporate sponsorships. Not a business model, but validates willingness to pay and builds a mailing list of engaged users.
- **Consulting:** Offer paid architecture reviews for teams adopting SharpClaw. This generates revenue, but more importantly surfaces enterprise requirements for Phase 2.
- **Training workshops:** Paid half-day workshops ("Building Production Agents with SharpClaw") at .NET conferences. Revenue is modest but builds authority.

**Target:** $5K-$15K in consulting/workshop revenue. Primary purpose is learning, not profit.

---

## 4. Phase 2 — Enterprise & ISV Monetization (Months 6-18)

### Revenue Target: $10K-$50K MRR by month 18

### 4.1 Tier Structure

#### Free (Open Source)

Everything in the MIT-licensed core. No limits, no telemetry, no registration required.

#### Team — $500/month (per organization)

For teams embedding SharpClaw in internal tools or products with <50 users.

| Feature | Description |
|---------|-------------|
| **Priority support** | 48-hour response SLA via dedicated channel |
| **Office hours** | Monthly group call with maintainers |
| **Early access** | Pre-release builds and roadmap input |
| **Logo rights** | "Powered by SharpClaw" badge for marketing |

**Why teams pay:** Support SLA and early access. These teams have adopted the open-source runtime and need confidence they won't get stuck.

#### Enterprise — $2,500/month (per organization)

For organizations running SharpClaw in production with compliance, scale, or multi-team requirements.

| Feature | Description |
|---------|-------------|
| Everything in Team | — |
| **Multi-tenant session store** | Pluggable session backends (Azure CosmosDB, SQL Server, PostgreSQL) with tenant isolation |
| **Enterprise SSO integration** | Microsoft Entra ID, SAML, and OIDC for approval workflows — tie permission approvals to corporate identity |
| **Audit log export** | Compliance-ready export of all tool executions, approvals, and provider calls |
| **Advanced telemetry sinks** | Azure Monitor, OpenTelemetry, Datadog, and Splunk exporters |
| **Session encryption at rest** | AES-256 encryption for session snapshots and event logs |
| **Role-based access control** | Define who can approve dangerous operations, manage MCP servers, install plugins |
| **Dedicated support** | 8-hour response SLA, named account engineer |
| **Custom SLA** | Uptime and response guarantees |

**Why enterprises pay:** Compliance (audit logs, encryption, SSO), scale (multi-tenant), and support guarantees. These are table-stakes for enterprise procurement.

#### ISV / OEM — Custom pricing

For companies embedding SharpClaw as the runtime inside a commercial product.

| Feature | Description |
|---------|-------------|
| Everything in Enterprise | — |
| **White-label rights** | Remove SharpClaw branding from end-user surfaces |
| **Embedded runtime SDK** | Optimized for hosting inside ASP.NET Core, worker services, or desktop apps |
| **Usage metering API** | Per-tenant token and tool usage tracking for ISV billing |
| **Custom provider integration** | Assistance building proprietary model provider adapters |
| **Roadmap influence** | Direct input on feature prioritization |
| **Indemnification** | IP indemnity for the proprietary components |

**Pricing model:** Base platform fee ($5K-$15K/month) + per-seat or per-usage component negotiated per deal.

### 4.2 What's Proprietary vs. Open Source

The boundary is drawn at a clear principle: **the open-source core runs a single-user, single-workspace agent with full functionality. Proprietary features serve multi-user, multi-tenant, compliance, and enterprise-scale needs.**

| Capability | Open Source | Proprietary |
|------------|:-----------:|:-----------:|
| Runtime orchestration | x | |
| File-backed session storage | x | |
| SQL/cloud session backends | | x |
| Permission policy engine | x | |
| SSO-backed approval workflows | | x |
| Structured telemetry (ring buffer) | x | |
| OpenTelemetry/Datadog/Splunk sinks | | x |
| Event log (NDJSON) | x | |
| Compliance audit export | | x |
| Session encryption at rest | | x |
| Single-workspace MCP | x | |
| Multi-tenant MCP orchestration | | x |
| Plugin system | x | |
| Plugin marketplace hosting | | x |
| CLI + REPL | x | |
| Admin REST API | | x |
| Role-based access control | | x |
| Usage metering | | x |

### 4.3 Packaging the Proprietary Extensions

Proprietary features ship as separate NuGet packages under a commercial license:

```
SharpClaw.Code.Enterprise.Sessions.CosmosDb
SharpClaw.Code.Enterprise.Sessions.SqlServer
SharpClaw.Code.Enterprise.Telemetry.AzureMonitor
SharpClaw.Code.Enterprise.Telemetry.OpenTelemetry
SharpClaw.Code.Enterprise.Telemetry.Datadog
SharpClaw.Code.Enterprise.Auth.EntraId
SharpClaw.Code.Enterprise.Auth.Oidc
SharpClaw.Code.Enterprise.Audit
SharpClaw.Code.Enterprise.Encryption
SharpClaw.Code.Enterprise.Admin
```

These packages depend on the open-source core and plug in via the existing DI extension pattern (`services.AddSharpClawEnterpriseSessions(configuration)`). No fork, no separate build — enterprise customers add packages and configure.

**License enforcement:** Package-level license key validation at startup. No runtime phone-home; offline validation with periodic renewal.

---

## 5. Phase 3 — Consumer & Ecosystem Monetization (Months 12-24)

### Revenue Target: $50K-$150K MRR by month 24

### 5.1 SharpClaw Pro — $20/month (individual)

A personal tier for developers using SharpClaw as their daily coding agent.

| Feature | Description |
|---------|-------------|
| **IDE extensions** | VS Code and Rider extensions with rich integration |
| **Cross-session memory** | Persistent project knowledge that survives session boundaries |
| **Workspace indexing** | Semantic code search, symbol navigation, dependency graph |
| **Priority model routing** | Automatic provider selection optimized for cost/quality/speed |
| **Session sync** | Sync sessions across machines via cloud storage |
| **Custom slash commands** | Visual editor for creating and sharing custom commands |
| **Pro badge** | Community recognition in Discord and GitHub |

**Why individuals pay:** The free CLI is fully functional. Pro adds convenience and power-user features that save time daily. The $20 price point is impulse-buy territory for professional developers.

### 5.2 Plugin Marketplace

A curated registry where third parties publish and optionally sell SharpClaw plugins.

| Revenue Stream | Model |
|----------------|-------|
| **Free plugins** | Listed for free; drives ecosystem growth |
| **Paid plugins** | 70/30 revenue split (developer/SharpClaw) |
| **Verified publisher** | $99/year badge for trust signal |
| **Featured placement** | $500/month for homepage visibility |

**Marketplace economics:** The marketplace is a flywheel — more plugins attract more users, which attract more plugin developers. The 30% take rate is standard (Apple, Stripe, etc.) and funds curation, security review, and infrastructure.

### 5.3 Managed Cloud (Optional)

A hosted SharpClaw runtime for teams that don't want to self-host.

| Tier | Price | Includes |
|------|-------|----------|
| **Starter** | $99/month | 5 seats, 100K tokens/month, file-backed sessions |
| **Growth** | $499/month | 25 seats, 1M tokens/month, SQL-backed sessions, SSO |
| **Scale** | $1,999/month | Unlimited seats, 10M tokens/month, full enterprise features |

**Build-or-buy decision:** The managed cloud is the highest-effort revenue stream. It requires infrastructure, ops, and support investment. Consider partnering with a hosting provider (Azure, Railway) rather than building from scratch. Evaluate at month 12 based on demand signals.

---

## 6. Pricing Philosophy

### Principles

1. **Free must be genuinely useful.** A developer should be able to build and ship a real product on the free tier. If the free tier feels crippled, adoption dies.

2. **Paid tiers solve real problems the free tier can't.** Enterprise features (compliance, multi-tenancy, SSO) are genuinely different requirements, not artificial limitations.

3. **Price on value, not cost.** The Enterprise tier costs $2,500/month but saves an enterprise team months of building session backends, audit infrastructure, and SSO integration.

4. **No per-seat pricing on the core runtime.** Per-seat pricing on an open-source runtime feels extractive. Charge per organization, not per developer.

5. **Annual discounts.** 20% discount for annual commitment (Team: $4,800/year, Enterprise: $24,000/year). Improves cash flow predictability.

### Competitive Pricing Context

| Competitor | Pricing | SharpClaw Comparison |
|------------|---------|---------------------|
| GitHub Copilot Business | $19/user/month | SharpClaw Pro at $20/month is comparable but includes the full runtime |
| Cursor Business | $40/user/month | SharpClaw is cheaper and open-source at the core |
| Semantic Kernel | Free (framework only) | SharpClaw builds on Agent Framework with production runtime; complementary, not competing |
| LangChain / LangSmith | Free core + $39/user for platform | Similar open-core model; SharpClaw's .NET/Agent Framework foundation is differentiated |

### Microsoft Partnership Value

The monetization strategy is designed to be **friendly to Microsoft's ecosystem interests**:

- **Free tier grows Agent Framework adoption.** Every SharpClaw user is a `Microsoft.Agents.AI` NuGet consumer. Microsoft benefits from SharpClaw's success.
- **Enterprise tier drives Azure alignment.** Multi-tenant session backends (CosmosDB, SQL Server), Azure Monitor telemetry sinks, and Entra ID SSO integration all drive Azure consumption.
- **No competition with Microsoft's own monetization.** SharpClaw monetizes the runtime/operational layer. Microsoft monetizes Azure infrastructure and AI model APIs. The incentives are aligned.
- **Co-marketing reduces CAC.** If Microsoft features SharpClaw in Agent Framework docs or conference talks, customer acquisition cost for all tiers drops significantly.

---

## 7. Revenue Projections

### Conservative Scenario

| Month | Phase | Free Users | Team Orgs | Enterprise Orgs | Pro Users | MRR |
|-------|-------|-----------|-----------|-----------------|-----------|-----|
| 6 | 1 | 500 | 0 | 0 | 0 | $0 |
| 12 | 1-2 | 2,000 | 5 | 1 | 0 | $5,000 |
| 18 | 2 | 5,000 | 15 | 5 | 100 | $22,000 |
| 24 | 2-3 | 10,000 | 25 | 10 | 500 | $47,500 |

### Optimistic Scenario

| Month | Phase | Free Users | Team Orgs | Enterprise Orgs | Pro Users | MRR |
|-------|-------|-----------|-----------|-----------------|-----------|-----|
| 6 | 1 | 1,500 | 0 | 0 | 0 | $0 |
| 12 | 1-2 | 5,000 | 10 | 3 | 0 | $12,500 |
| 18 | 2 | 15,000 | 30 | 10 | 500 | $50,000 |
| 24 | 2-3 | 30,000 | 50 | 20 | 2,000 | $115,000 |

### Revenue Mix at Month 24 (Conservative)

```
Team:        $12,500  (26%)
Enterprise:  $25,000  (53%)
Pro:         $10,000  (21%)
─────────────────────────
Total MRR:   $47,500
ARR:         $570,000
```

---

## 8. Go-to-Market Channels

### Phase 1 (Adoption)

| Channel | Action | Expected Impact |
|---------|--------|-----------------|
| **NuGet** | Publish packages with clear README and getting-started | Primary discovery channel for .NET developers |
| **GitHub** | Optimize repo (README, badges, examples, issue templates) | Social proof and contribution funnel |
| **Microsoft partnership** | Engage Agent Framework team; offer SharpClaw as reference implementation | Ecosystem credibility, docs listing, co-marketing |
| **.NET blogs** | "Building a production coding agent on Microsoft Agent Framework" | Direct reach to target persona, friendly to Microsoft |
| **Conference talks** | .NET Conf, NDC, Build — joint sessions with Agent Framework team if possible | Authority positioning |
| **Discord** | Developer community with channels for help, showcase, RFC | Engagement and retention |
| **Twitter/X + Bluesky** | Regular updates, demos, Agent Framework integration highlights | Awareness |
| **YouTube** | "Build an agent in 15 minutes with Agent Framework + SharpClaw" | Long-tail discovery |

### Phase 2 (Enterprise)

| Channel | Action |
|---------|--------|
| **Direct outreach** | Identify companies using Semantic Kernel for agent work; offer migration assistance |
| **Case studies** | Publish 2-3 production deployment stories |
| **Partner program** | .NET consultancies who recommend SharpClaw to enterprise clients |
| **Enterprise landing page** | Separate page with compliance, security, and ROI messaging |

### Phase 3 (Consumer)

| Channel | Action |
|---------|--------|
| **VS Code Marketplace** | IDE extension as primary distribution |
| **Product Hunt launch** | Consumer awareness burst |
| **Developer influencers** | Sponsored reviews and walkthroughs |
| **Plugin marketplace** | Self-reinforcing ecosystem growth |

---

## 9. Cost Structure

### Phase 1 (Months 0-12)

| Item | Monthly Cost | Notes |
|------|-------------|-------|
| GitHub Actions CI/CD | $0-50 | Free tier covers most OSS needs |
| NuGet hosting | $0 | Free for public packages |
| Domain + hosting (docs site) | $20 | Static site on Cloudflare/Vercel |
| Discord (Nitro for branding) | $10 | Optional |
| Conference travel (amortized) | $500 | 2-3 conferences per year |
| **Total** | **~$580/month** | |

### Phase 2 (Months 6-18)

| Item | Monthly Cost | Notes |
|------|-------------|-------|
| Phase 1 costs | $580 | Continuing |
| License server infrastructure | $100 | Simple key validation service |
| Enterprise package CI/CD | $200 | Private build pipelines |
| Support tooling (Intercom/Linear) | $200 | Customer communication |
| Part-time support engineer | $3,000 | Contractor or part-time hire |
| **Total** | **~$4,080/month** | |

### Breakeven

At conservative projections, MRR exceeds costs by **month 14-15** (Enterprise tier covers the burn).

---

## 10. Key Risks to Monetization

| Risk | Mitigation |
|------|------------|
| Enterprise features built by community (defeating proprietary value) | Keep proprietary features integration-heavy (Azure backends, Entra ID, audit export) — hard to replicate without infrastructure |
| Microsoft builds competing production layer into Agent Framework | Stay close to the team, contribute upstream, position as community complement. If they build it, pivot to hosting/tooling layer. The incentive alignment (SharpClaw drives Agent Framework adoption) makes this unlikely |
| .NET agent market doesn't materialize | Phase 1 positioning as production runtime layer doesn't require a large agent-specific market |
| Free tier is too good, nobody upgrades | Monitor conversion at Phase 2 launch; adjust boundary if needed, but err on the side of generous free tier |
| Price resistance at $2,500/month for Enterprise | Offer quarterly billing and proof-of-value pilots (30-day trial with migration assistance) |
| Microsoft relationship doesn't materialize | Product stands alone regardless; Microsoft alignment is accelerant, not dependency |

---

## 11. Microsoft Partnership Strategy

SharpClaw's success is accelerated by — but not dependent on — a strong relationship with the Microsoft Agent Framework team. The strategy is to make SharpClaw so useful to Microsoft's ecosystem goals that partnership is a natural outcome.

### Why Microsoft Benefits

| Microsoft Goal | How SharpClaw Helps |
|---------------|---------------------|
| Agent Framework adoption | Every SharpClaw user downloads `Microsoft.Agents.AI` from NuGet |
| Azure consumption | Enterprise tier drives CosmosDB, Azure Monitor, Entra ID, and Azure OpenAI usage |
| .NET ecosystem competitiveness | SharpClaw demonstrates .NET is a first-class platform for AI agents (vs. Python/TypeScript narrative) |
| Agent Framework credibility | A production-grade, open-source project validates the framework for real workloads |
| Community engagement | SharpClaw's contributor community feeds Agent Framework issue reports, feature requests, and real-world usage patterns |

### Partnership Playbook

| Timeline | Action | Ask |
|----------|--------|-----|
| **Month 1** | File well-crafted Agent Framework issues from real SharpClaw usage | Build visibility with the team |
| **Month 2** | Publish blog post: "Building a Production Agent Runtime on Microsoft Agent Framework" | Ask to be retweeted / shared by .NET team accounts |
| **Month 3** | Submit PR to Agent Framework docs: "Community Projects" section featuring SharpClaw | Get listed in official docs |
| **Month 4** | Propose .NET Conf community talk: "From Agent Framework to Production" | Conference visibility |
| **Month 6** | Request meeting with Agent Framework PM to share usage data and feature requests | Establish direct relationship |
| **Month 8** | Propose joint blog post or case study | Co-marketing |
| **Month 12** | Explore Microsoft for Startups or ISV partnership program | Formal partnership, potential Azure credits |

### What Not to Do

- Don't position against Semantic Kernel or AutoGen — they serve different use cases
- Don't ask for special treatment or early access before proving value
- Don't depend on Microsoft for distribution — NuGet and GitHub are the primary channels
- Don't build on unstable Agent Framework APIs — pin to released versions
- Don't gate features behind Microsoft-specific infrastructure — Azure backends are one option, not the only option

---

## 12. Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| MIT license for core | Already shipped; builds trust; maximizes adoption | 2026-04-10 |
| Open-core model | Best fit for MIT base + enterprise upsell | 2026-04-10 |
| No per-seat pricing on core | Feels extractive for OSS runtime; charge per org instead | 2026-04-10 |
| Phase 1 = $0 revenue | Category ownership > early revenue; consulting covers costs | 2026-04-10 |
| Enterprise features as separate NuGet packages | Clean separation; no fork; existing DI patterns | 2026-04-10 |
| Managed cloud as Phase 3 optional | High effort; evaluate based on demand signals at month 12 | 2026-04-10 |
| Position as Agent Framework complement, not competitor | Microsoft partnership accelerates all phases; aligned incentives (SharpClaw drives AF adoption, enterprise tier drives Azure consumption) | 2026-04-10 |
| Azure-first enterprise backends (CosmosDB, Entra ID, Azure Monitor) | Aligns with Microsoft ecosystem; enterprise .NET teams are already on Azure | 2026-04-10 |
