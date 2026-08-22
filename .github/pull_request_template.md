<!--
ARCHITECTURE.md's skill table names "PR template + review checklist in /docs/" as the artifact for
Agile/Scrum practice — this lives at .github/pull_request_template.md instead, the location GitHub
actually auto-populates a PR description from. A real, working artifact beats a literally-followed
suggestion when the two diverge, same reasoning as every Table C choice in this project.
-->

## What changed and why

<!-- One or two sentences. Link the phase/issue this continues if relevant. -->

## How this was verified

<!-- Be specific: which tests ran, which manual/browser check happened, what a screenshot showed.
     "It builds" is not verification on its own — this project's own discipline (see
     LEARNING_GUIDE.md) is that a passing test suite and a working feature are different claims. -->

- [ ] `dotnet test` passes
- [ ] Any touched frontend has been exercised in a real browser (not just compiled/type-checked)
- [ ] New external dependencies (HTTP calls, caches, queues) degrade gracefully if unreachable,
      not just on the happy path

## Review checklist

- [ ] No secrets, connection strings, or API keys committed (check diff, not just intent)
- [ ] New public API surface (endpoint, event, config key) is documented where a future reader
      would actually look for it
- [ ] Deliberate scope decisions (a gap left open, a resume-tool comparison, an unverified path)
      are called out explicitly, not left for a reviewer to guess at
