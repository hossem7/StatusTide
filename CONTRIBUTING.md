# Contributing to StatusTide

Thanks for helping. This guide covers how work moves from an issue to `main`. For how the code is organized, see [CLAUDE.md](CLAUDE.md); it applies to people and AI agents alike.

## Workflow

1. **Start from an issue.** Every change has one. Use the feature or bug template so the goal and "done when" are clear before code starts. Comment on the issue to take it, so two people don't build the same thing.
2. **Branch from `main`**, named `<type>/<issue>-<short-description>`:
   - `feat/5-statuspage-reader`
   - `fix/12-rate-limit-headers`
   - `chore/ci-baseline`
3. **Keep PRs small**: one issue per PR, ideally under 400 changed lines. Split larger work into several PRs.
4. **Open a PR** using the template, and fill in every section. Link the issue with `Closes #N`, or `Related to #N` when the PR only covers part of it. If the issue needs both backend and frontend work, open the backend PR first: the frontend should call the real API, not sample data.
5. **CI must pass** before merging: .NET build and tests, frontend lint, tests and build, and Docker image builds.
6. **Review.** `main` needs one approving review, and a new push dismisses earlier approvals. Reply to every comment, either with a fix or a reason.
7. **Merge** once CI is green, the PR is approved and the checklist is done. The maintainer merges; delete the branch afterwards.

Changes made by AI coding agents follow exactly the same path: an issue, a branch, a PR with the template, green CI and a review before merge.

## Commit messages

We use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <what changed, in the imperative>

<optional body: why the change was needed, in one or two lines>
```

| Type | Use for |
| --- | --- |
| `feat` | A new feature or behavior |
| `fix` | A bug fix |
| `refactor` | A code change that neither fixes a bug nor adds a feature |
| `test` | Adding or fixing tests |
| `docs` | Documentation only |
| `chore` | Build, tooling, dependencies, configuration |
| `ci` | The CI workflow |

Examples:

```
feat: add Statuspage reader with sample fixtures
fix: honor Retry-After on 429 responses
```

Keep the subject under 72 characters and the whole message to 2 or 3 lines. Commits are authored by the person making the change. Do not add co-author trailers for AI tools, and do not add "Generated with" lines to commits or PR descriptions.

## Before opening a PR

```bash
dotnet build
dotnet test
cd WebUI && npm run lint && npm test && npm run build
```

- New behavior has tests. Readers are tested against the sample responses in `tests/fixtures`, including malformed and oversized ones.
- New message types go in `StatusTide.Contracts`, and their consumers are idempotent.
- No secrets, real credentials or AWS keys.
- UI changes include a screenshot in the PR, at desktop and phone width.
- Sample data uses the demo vendor (Acme Cloud) or made-up `.example` vendors. Never show a made-up outage for a real company.

## Reporting bugs and requesting features

Use the issue templates on GitHub. For security problems, do not open a public issue; contact the maintainer directly instead.
