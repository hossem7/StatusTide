# Contributing to StatusTide

Thanks for helping. This guide covers how work moves from an issue to `main`. For how the code is organized, see [CLAUDE.md](CLAUDE.md); it applies to people and AI agents alike.

## Workflow

1. **Start from an issue.** Every change has one. Use the feature or bug template so the goal and "done when" are clear before code starts.
2. **Branch from `main`**, named `<type>/<issue>-<short-description>`:
   - `feat/5-statuspage-reader`
   - `fix/12-rate-limit-headers`
   - `chore/ci-baseline`
3. **Keep PRs small**: one issue per PR, ideally under 400 changed lines. Split larger work into several PRs.
4. **Open a PR** using the template. Link the issue with `Closes #N`, or `Related to #N` when the PR only covers part of it.
5. **CI must pass** before merging: .NET build and tests, frontend lint and build, and Docker image builds.
6. **Merge** once CI is green and the checklist is done. Delete the branch afterwards.

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
feat: add Statuspage reader with recorded fixtures
fix: honor Retry-After on 429 responses
```

Keep the subject under 72 characters and the whole message to 2 or 3 lines. Commits are authored by the person making the change; do not add co-author trailers for tools.

## Before opening a PR

```bash
dotnet build
dotnet test
cd WebUI && npm run lint && npm run build
```

- New behavior has tests. Readers are tested against recorded vendor responses, including malformed and oversized ones.
- New message types go in `StatusTide.Contracts`, and their consumers are idempotent.
- No secrets, real credentials or AWS keys.
- UI changes include a screenshot in the PR.

## Reporting bugs and requesting features

Use the issue templates on GitHub. For security problems, do not open a public issue; contact the maintainer directly instead.
