# StatusTide: agent instructions

Context for AI coding agents (and people) working in this repo. Read this before changing code. The product, architecture and design decisions are in [README.md](README.md); the workflow is in [CONTRIBUTING.md](CONTRIBUTING.md).

## Commands

```bash
dotnet build                          # whole solution; warnings are errors
dotnet test                           # unit + integration tests (Microsoft Testing Platform); Docker must be running
dotnet tool restore                   # once; installs dotnet-ef from dotnet-tools.json
dotnet ef migrations add <Name> --project StatusTide.Data   # after any entity change
dotnet run --project StatusTide.Api   # http://localhost:5080, health at /health

cd WebUI && npm ci                    # frontend dependencies
npm run lint                          # oxlint
npm test                              # Vitest and React Testing Library
npm run build                         # type check + production build
npm run dev                           # http://localhost:5173, proxies /api and /hubs to the API

cp .env.example .env                  # once; local values only
docker compose up --build             # whole stack; API on 5080, web app on 8080
docker compose up -d postgres redis rabbitmq storage mailpit   # infrastructure only
kubectl kustomize k8s/local           # render the k3d manifests (full k3d steps in README)
```

Infrastructure lives in `compose.yaml` and `k8s/`. `k8s/base` is shared with production k3s, so keep local-only changes in `k8s/local`. Secrets come from `.env` (Compose) or the `statustide-secrets` secret (Kubernetes), never from files in the repo.

CI (`.github/workflows/ci.yml`) runs the same build, test, lint and build steps, then builds a Docker image for each app. Run them locally before opening a PR.

## Architecture

Publishers and subscribers that only talk through RabbitMQ, using MassTransit.

| Project | Holds | May reference |
| --- | --- | --- |
| `StatusTide.Contracts` | Message records: commands (`PollVendor`) and events (`VendorStatusChanged`) | Nothing |
| `StatusTide.Common` | Shared plumbing: `AddStatusTideLogging`, `AddStatusTideMessaging`, `AddStatusTideRedis`, retry policy, CorrelationId in consumer logs, `DemoOutageStore` (the demo vendor's outage in Redis, written by the Api and read by the Subscriber) | Contracts |
| `StatusTide.Data` | `StatusTideDbContext`, entities and their configurations (one folder per feature), migrations, the Acme Cloud seed | Nothing |
| `StatusTide.Api` | Controllers, SignalR hub, query handlers for screens | Contracts, Common, Data |
| `StatusTide.Publisher` | Quartz.NET jobs: publishing commands on a schedule, and nightly housekeeping such as deleting inactive guests. Always one replica. | Contracts, Common, Data |
| `StatusTide.Subscriber` | `IConsumer<T>` implementations, grouped by feature | Contracts, Common, Data |
| `WebUI` | React + TypeScript (Vite) | Calls the API only |

Rules:

- **Executables never reference each other.** Api, Publisher and Subscriber share only Contracts, Common and Data. They communicate through messages, never direct calls.
- **Contracts are plain records with no dependencies** (no MassTransit, no EF Core). Commands are named as instructions (`PollVendor`), events in the past tense (`VendorStatusChanged`). Never change a message's meaning in place; add a new one instead.
- **Never rename or move a message type.** MassTransit identifies a message by its full type name, including the namespace, so renaming `PollVendor` or moving it to another namespace silently stops delivery to running consumers. Add a new message type instead, migrate publishers and consumers to it, then remove the old one.
- **Group code by feature, not by type**: `StatusTide.Subscriber/Features/Polling/PollVendorConsumer.cs`, `StatusTide.Api/Features/Follows/FollowsController.cs`. Keep a feature's consumer, logic and data access together.
- **Separate writes from reads (light CQRS).** Commands enforce rules and change state. Queries change nothing, use `AsNoTracking`, and return DTOs shaped for the screen. One PostgreSQL database; no separate read store or event sourcing.
- **Schema changes go through migrations.** Snake_case names come from the naming convention, and enums are stored as text. Only the API applies migrations on startup (`Database:MigrateOnStartup`, on in development and the local stacks). Seed data uses fixed ids and dates; a test fails if the model and migrations drift apart.
- **Consumers must be idempotent.** MassTransit retries a failing message 5 times (exponential backoff, 1 to 30 s, see `MessagingDefaults`), then moves it to the `_error` queue. A message can arrive more than once.
- **Interfaces only where an implementation really swaps**: `IStatusReader` (one per feed format), `IIncidentSummarizer` (Bedrock or fake), `IAlertSender` (SES, Mailpit, webhooks). Do not add an interface for a class with one implementation.
- **API endpoints are controllers** (`[ApiController]`), not minimal APIs.
- **Frontend**: feature folders under `WebUI/src/features/`, small components, server data through one typed API client. Never use `dangerouslySetInnerHTML`.

## Hard rules

- **MassTransit stays on 8.x.** Version 9 and later need a commercial license. Do not accept upgrade PRs that move it to 9.
- **No real vendor polling in development or tests.** Use the sample responses in `tests/fixtures` and the demo vendor (Acme Cloud). Fixtures are written by hand in the documented format, never copied from a real vendor, since real responses carry update text we must not store. Only vendors approved through the verification process in the plan go live.
- **Store and show facts only**: statuses, component names, incident titles, timestamps and source links. Never store or display a vendor's written update text. AI summaries use only those facts plus our own check data.
- **Treat every fetched URL and every incident title as untrusted.** URLs go through the SSRF guard (http/https, ports 80 and 443, no private, loopback or link-local addresses, every redirect re-checked). Incident titles are wrapped as data in AI prompts.
- **No secrets in the repo and no AWS access keys anywhere.** Production uses the EC2 IAM role. Local values live in `appsettings.Development.json` (fake development credentials only) or `.env`, never committed.
- **Commits and PRs are authored by the person making the change.** No AI co-author trailers and no "Generated with" lines in commits or PR descriptions, whoever opens the PR. When you take over someone else's work, keep their commits under their name and add yours on top.

## Writing style for docs, comments and PRs

Plain, readable language. Short sentences. No em dashes. Explain why, not just what.
