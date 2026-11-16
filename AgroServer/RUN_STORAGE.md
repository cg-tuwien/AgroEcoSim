# Stored runs and temporary write access

All server mutations require one shared password. Configure `AGRO_WRITE_PASSWORD` or
`AGRO_WritePassword` for a direct server process; if both are set, `AGRO_WritePassword` takes
precedence. With Docker Compose, set `AGRO_WRITE_PASSWORD` in its deployment environment;
Compose passes it to the server as `AGRO_WritePassword`. An empty or absent password leaves
writes disabled. Reads, stored playback and downloads remain public.

Use HTTPS and WSS at the production reverse proxy. Do not expose its plaintext backend port
directly to external clients. Both frontends start read-only and offer a masked **Unlock writes**
control. Unlocking lasts until locking, reloading or closing the client. Credentials are held
only in memory, outside run requests, archives and preferences. Lock disconnects live control
without sending Abort; the server's existing unattended-run timeout still applies.

## Protection and deletion

- New runs, including forks and synchronous API runs, start **protected**.
- Existing runs with no protection setting remain **unprotected**.
- A password holder can Protect or Unprotect a run in either frontend's Runs menu, including
  during computation. Resume preserves the setting. Protection has no expiry.
- Automatic cleanup never deletes a protected run. Its uploaded model also remains referenced
  and retained. Protection is recorded in the run directory as well as the metadata database,
  so rebuilding the database or adopting copied directories preserves it. An unreadable
  protection file is treated as protected and reported in the store log.
- A password holder can deliberately delete a protected run after the existing permanent-delete
  confirmation. Deletion has no trash/recovery period. Runs still being written cannot be deleted.
- Unprotecting makes a run eligible for the next normal cleanup; it does not immediately run cleanup.

Run cleanup is oldest-first when total run storage exceeds `AGRO_RunStoreRetentionBytes`
(64 GiB default; the supplied Compose configuration sets 1 TiB). It runs at new-run registration
and every `AGRO_RunStoreSweepMinutes` (30 by default). Setting that interval to zero disables
the timer, not new-run cleanup. Live and branched-from runs are also retained. If these and
protected runs alone exceed the budget, cleanup refuses to delete the remaining history.

Uploaded models not referenced by any stored run are collected separately after
`AGRO_RunStoreModelGraceHours` (6 by default), or earlier above `AGRO_RunStoreModelBytes`
(8 GiB), with a 15-minute minimum grace. Abandoned temporary files are also cleaned up.
These policies do not delete models referenced by protected or unprotected stored runs.

Protection still consumes disk space. The default 2 GiB free-space reserve rejects new work or
stops active writing safely, retaining committed frames and attempting a final checkpoint.
Storage failure is an error; simulations no longer silently continue without being saved.

## API

HTTP mutations carry `X-Agro-Write-Password`. `POST /Simulation/write-access` verifies the
credential and returns 204. Invalid credentials return 401; absent server configuration returns
503. The credential is a separate final string argument on every operational SignalR method
(`Run`, `Start`, `Abort`, `Terrain`, `Subscribe`, `Preview`), never part of `SimulationRequest`.
Old clients without this credential can still read stored data, but cannot write.

Run listings and details include `protected`. Change it with
`PUT /Simulation/run/{id}/protection` and JSON `{ "protected": true }` or
`{ "protected": false }`. The explicit boolean is required; the response is the updated run.
Missing runs return 404. Every deletion and protection change is logged without credentials.

`POST /Simulation` still waits for completion and returns its original result fields, now with
`runId`. It uses the same storage/checkpoint pipeline as socket runs and continues if the HTTP
caller disconnects. `ExactPreview` does not pace this synchronous endpoint. Invalid/expired
terrain keys are rejected rather than replaced with a plain field. Unavailable storage returns
503; insufficient storage returns 507. Mid-run failures include the retained run's ID and stop
reason. A successful response waits for queued frame/checkpoint writes to finish.

## Deployment

The supplied Compose file retains the fixed `agroeco-runstore` volume at
`/data/AgroEcoStorage`, with the store in `/data/AgroEcoStorage/runstore`. Preserve that volume
across upgrades; do not use `docker compose down --volumes` or prune data volumes.

After deployment, verify `GET /Simulation/store` reports the intended root, volume, budgets
and free space. Verify anonymous reads and write rejection without creating or deleting
production runs. Also check host cleanup jobs and backups separately: application protection
cannot prevent an administrator or host job from removing the volume.
