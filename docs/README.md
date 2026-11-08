# Project docs — navigation

| File | What it is | When to read |
| --- | --- | --- |
| `architecture.md` | architectural style (D15), components, DB schema, guarantees, roadmap | to understand "how it's built" |
| `database.md` | the DB model explained: per-table/index rationale + measured load behavior (pool boundary, channel-full path) | "how does the schema handle load" |
| `guide.md` | onboarding to the **real code**: event path, statuses, boundary map, config | first file after README |
| `tasks.md` | live backlog — only `todo`/`review` | "what's next" |
| `done-log.md` | archive of closed tasks with files and commits | "what's already done" |
| `incidents.md` | review findings: before/after code, why it was broken | "show me real bugs you fixed" |
| `diagrams/system.md` | mermaid diagrams of components and data flow | the visual picture |
| `next.md` | construction queue — what's next and why | planning |
| `guides/task_guide.md` | concepts behind the hands-on tasks (bucket, heap) without code | before TASK-004/005 |
| `guides/task004_walkthrough.md` | step-by-step TokenBucket path | during TASK-004 |
| `guides/task005_walkthrough.md` | step-by-step RetryScheduler path | during TASK-005 |
| `guides/h1_walkthrough.md` | step-by-step H-1 dedup-window path | during H-1 (done — DoD closed) |
| `guides/h2_walkthrough.md` | step-by-step H-2 client `Idempotency-Key` path | during H-2 |
| `guides/h3_walkthrough.md` | step-by-step H-3 lease-versioning path | during H-3 |
| `screenshots/` | rendered images of the live UI | proof it works |
| `guides/concepts.md` | "what, why, when": CORS, CQRS, pattern heuristics | background reading |
| `guides/open_questions.md` | all open questions: reviews, decisions, trainer | answer by voice when there's time |

Root level: `README.md` (overview) · `PLAN.md` (decision journal +
metrics + failure-boundary map) · `CHANGELOG.md` (commit → files →
task) · `AGENTS.md` (rules) · `CONTRIBUTING.md` (ground rules for
changes + local verification).
