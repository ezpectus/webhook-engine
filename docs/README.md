# Project docs — navigation

| File | What it is | When to read |
| --- | --- | --- |
| `architecture.md` | architectural style (D15), components, DB schema, guarantees, roadmap | to understand "how it's built" |
| `database.md` | the DB model explained: per-table/index rationale + measured load behavior (pool boundary, channel-full path) | "how does the schema handle load" |
| `guide.md` | onboarding to the **real code**: event path, statuses, boundary map, config | first file after README |
| `failure-map.md` | stage-by-stage table: where a duplicate is legal, where loss is impossible — every row proven by a live chaos run | the project's final criterion |
| `diagrams/system.md` | mermaid diagrams of components and data flow | the visual picture |
| `screenshots/` | rendered images of the live UI | proof it works |
