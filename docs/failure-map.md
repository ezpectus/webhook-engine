# Failure-boundary map: where duplicates, where loss (proven live)

The project's final criterion — every row backed by a live run
(evidence: 9 chaos failures on the running system; three LoadGen
runs — 2,055 / ~4,800 / 803 accepted events, zero loss after accept).

| Stage | Duplicate possible? | Loss possible? | Mechanism / guard | Evidence |
| --- | --- | --- | --- | --- |
| POST /events, PG down | no | **no** — client gets a 5xx, not a 202 | honest refusal before accept | chaos #1: HTTP 500 |
| POST /events, Redis down | no | no | fail-open → PG unique `IdempotencyKey` catches the dup | chaos #2: 2nd POST = duplicate with Redis dead |
| Ack after commit, client never heard it | **yes** — client retries → a dup on our ingest | no | client sees a timeout, but the event is in PG; client retry → `duplicate` response, not a second event | T5 analysed |
| Same content after the dedup window | **yes — now a new event by design** | no | bucketed PG key differs per window; Redis marker TTL = window. Pre-H-1 this row read "PG unique is permanent" — chaos #9 proved that a bug | e2e 07.11: POST3 in next bucket → new eventId |
| Claim → channel (channel full) | no | no | `Pending` stays in PG, sweeper picks it up next tick | chaos #4: cap=1, 3/3 delivered |
| Worker died after claim, before send | no | no | `InFlightTimeout` lease → sweeper reclaim → re-enqueue | chaos #7 reclaim stage |
| Send succeeded, ack/status never written (crash) | **yes** — reclaim → repeated send | no | at-least-once: a dup is legal, loss is not | Q7: two attempt#1 rows observed live |
| Slow channel > lease (Q7) | **yes** — a second worker sends in parallel | no | accepted as a boundary; the fix — lease versioning (hands-on task H-3) | chaos #7: attemptCount=2 across 3 sends |
| Retry in heap, process killed | no | no | `NextAttemptAt` in PG is truth; the heap is an accelerator, rebuilt by scan | chaos #3: retries continued after kill |
| Endpoint always 500 | no | **no — quarantine, not loss** | MaxAttempts → DLQ + `DeadLetterItem.Reason` | chaos #5: 5 attempts → DLQ |
| Endpoint flaps 500↔200 | yes (a retry is a repeated send by design) | no | equal-jitter backoff: a chance at 200 within 5 attempts | chaos #6: [500,200] → Delivered |
| Replay from DLQ | by operator's will (not a failure) | no | bypasses dedup, AttemptCount→0, history kept as audit | chaos #8: second delivery cycle |
| Client resends with a new body | no | no | different content-hash = different key = a new event (correct) | by construction |

**Bottom line:** no loss on any stage after accept (202) — the only
"refusal" before accept honestly returns a 5xx. Failure-caused
duplicates live on three boundaries — client retry after a lost
ack, crash between send and ack write, slow channel past the lease
(Q7) — all legal under at-least-once. Flap retries and DLQ replay are
repeated sends by design, not failures. The only "dirty" dup is Q7
(documented).

