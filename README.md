# StatusTide

One dashboard for the status of every service your stack depends on, with independent checks running alongside each vendor's official status page.

> **Project status: in development.** The plan, screen designs and backlog are done, and code starts with [milestone 1](https://github.com/hossem7/StatusTide/milestone/1). Nothing is deployed yet. Progress is tracked in [milestones](https://github.com/hossem7/StatusTide/milestones) and [issues](https://github.com/hossem7/StatusTide/issues).

## The problem

When production breaks, the first question is usually "is it us, or something we depend on?" Answering it means opening GitHub's status page, then Slack's, then AWS's, one tab at a time. And official status pages tend to lag: a vendor posts an incident once its own engineers have confirmed it, which is often after customers have already started seeing errors.

## What StatusTide does

- **Follow only what you use.** Pick services from a catalog, then pick components within them (for example only "Actions" on GitHub). Alerts only fire for those.
- **One live dashboard.** Status changes are pushed to the browser over SignalR, so there's nothing to refresh.
- **Our checks next to theirs.** StatusTide calls each vendor's public health endpoint every minute and shows the result beside the official status. When our checks fail before the vendor posts, the incident page says so: "caught 4 minutes early".
- **Quiet alerts.** One message when an incident starts and one when it ends, by email, Slack or Discord.
- **Short incident summaries.** A plain-English summary written by an LLM, from facts only (see design decisions below).
- **Try it without signing up.** Guest mode gives you a real dashboard, including a demo vendor, Acme Cloud, that you can break on purpose to watch the whole pipeline react.

## How it works

```mermaid
flowchart LR
  S[Scheduler<br/>Quartz.NET] --> Q[Poll queue<br/>RabbitMQ]
  Q --> W[Poll workers<br/>fetch, parse, compare]
  W --> F[(Vendor feeds<br/>Statuspage JSON,<br/>RSS, Atom)]
  W --> C{Changed?}
  C -- no --> X[Stop]
  C -- yes --> DB[(PostgreSQL)]
  C -- yes --> E[VendorStatusChanged<br/>event]
  E --> H[SignalR hub] --> D[React dashboards]
  E --> AI[Incident summary<br/>Amazon Nova Lite]
  E --> A[Alert worker] --> I[Email, Slack, Discord]
  AI --> A
```

The scheduler queues one poll per vendor per interval, however many people follow that vendor. A worker fetches the feed with a conditional request (ETag or If-Modified-Since), normalizes the response, hashes it and compares the hash with the last one stored in Redis. Most polls end right there. Only a real change is written to PostgreSQL and published as an event, which then fans out to dashboards, alerts and the summarizer.

The result is that load and cost grow with the number of vendors in the catalog, not the number of users.

## Design decisions

**One poll per vendor, not per user.** A thousand people following GitHub still means one request to GitHub per minute. This is the core of the architecture and the reason for the queue and the fingerprinting step above.

**Read published feeds, never scrape.** StatusTide only reads formats vendors publish for programs: Atlassian Statuspage JSON, Slack's status API, Google Cloud's Atom feed and plain RSS or Atom. There is no HTML scraping and no LLM reading arbitrary pages. The trade-off is that some vendors can't be supported, and that's accepted. Before a vendor joins the catalog, its format, terms of service, API terms and robots.txt are reviewed and the decision is recorded in the database, and reviewed again every six months.

**Show facts and link to the source.** StatusTide stores and displays statuses, component names, incident titles, timestamps and a link back to the vendor. It never stores or shows the vendor's written incident updates. That keeps the app on the right side of vendors' content, and it means the AI never paraphrases someone else's words. Vendors appear by name with initials tiles, no logos.

**Treat the LLM as a formatter, not a source.** The summarizer gets statuses, component names, timestamps and our own check data. Incident titles are text from outside the system, so all input is wrapped as untrusted data to blunt prompt injection. The model must return strict JSON, which is validated before saving; anything invalid falls back to a template sentence. Summaries are generated only when an incident opens, changes or resolves, so the cost is a few cents a month. They're labeled "AI-generated" in the UI.

**Confirm before alarming.** A component is marked down by our checks only after three failures in a row. That makes our detection about two minutes later than the first failed check, but one slow response shouldn't wake anyone up, and noisy alerts are the fastest way to get an alert tool muted.

**Guests are real users.** A first visit creates a guest user on the server and a secure HttpOnly cookie, instead of keeping state in the browser. Visitors exercise the same backend as account holders, and signing up just transfers ownership of the guest's data. Guests inactive for 30 days are deleted by a cleanup job. Guests get in-app alerts only and lower limits, so the open door can't be used to send email.

**The server fetches URLs, so SSRF is designed in.** Account holders can add their own status page by URL, which means the server will fetch addresses users give it. Only http and https on ports 80 and 443 are allowed, DNS is resolved and private, loopback and link-local ranges are blocked (including the cloud metadata address 169.254.169.254), and every redirect is checked again. The EC2 instance also requires IMDSv2. Webhook URLs are limited to official Slack and Discord domains.

**Sized to run for about $21 a month.** Production is one EC2 t3.small (2 GB of memory) running k3s. That budget ruled out SQL Server, Elasticsearch and a managed RDS database, so PostgreSQL runs in the cluster with daily backups to S3. CPU credits are set to "standard" so a traffic spike slows the server down instead of adding to the bill, and an AWS spend limit sits on top. No AWS access keys exist anywhere; the instance's IAM role grants access to S3, SES, Bedrock and CloudWatch.

## Tech stack

| Layer | Choice |
| --- | --- |
| Frontend | React with TypeScript (Vite), served from S3 through CloudFront |
| API | ASP.NET Core Web API, SignalR, ASP.NET Core Identity with JWT |
| Background work | .NET worker services, Quartz.NET scheduler |
| Messaging | RabbitMQ |
| Data | PostgreSQL with EF Core migrations, Redis for locks, fingerprints, caching and rate limits |
| AI | Amazon Nova Lite on Bedrock |
| Alerts | Amazon SES, Slack and Discord webhooks |
| Infrastructure | Docker, k3s on EC2, CloudFront, Route 53, ACM |
| Logging | Serilog to CloudWatch Logs, with correlation IDs from the API through the workers |
| Testing and CI | xUnit, Testcontainers, Playwright, GitHub Actions |

## Roadmap

Each milestone ends with something that runs and can be demonstrated.

| Milestone | What it delivers |
| --- | --- |
| [M1: Foundation](https://github.com/hossem7/StatusTide/milestone/1) | Solution structure, Docker Compose, CI, database schema and migrations |
| [M2: Ingestion pipeline](https://github.com/hossem7/StatusTide/milestone/2) | Statuspage reader, scheduler, poll queue, workers and change detection |
| [M3: Core app](https://github.com/hossem7/StatusTide/milestone/3) | Live dashboard, demo vendor, guest mode, accounts, catalog, rate limits |
| [M4: Checks, readers and AI](https://github.com/hossem7/StatusTide/milestone/4) | Our own checks, incident page, more readers, custom status pages, AI summaries |
| [M5: Alerts and vendor vetting](https://github.com/hossem7/StatusTide/milestone/5) | Email and webhook alerts, format and terms review for every catalog vendor |
| [M6: Launch](https://github.com/hossem7/StatusTide/milestone/6) | AWS deployment, final documentation and a demo video |

## Running it locally

Setup instructions arrive with milestone 1. The plan:

- Requirements: .NET 10 SDK, Node.js 24 and Docker.
- Everything runs in containers, and each AWS service sits behind an interface with a local stand-in: MinIO for S3, Mailpit for email, a fake summarizer for Bedrock and console logging for CloudWatch. Going to production is a configuration change, not a code change.
- No real vendor is polled during development. Readers are built and tested against recorded sample responses, plus the demo vendor.

## About the data

Statuses shown in StatusTide come from each vendor's public status page or feed, with a link back to the source. StatusTide is an independent project and isn't affiliated with or endorsed by any of the vendors it lists.
