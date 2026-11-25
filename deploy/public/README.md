# Public demo overlay (UNTESTED on a real host)

Goal: anyone with the link can try the synthetic demo, each in a private temporary session.
Synthetic data only. Not a production deployment. Nothing here has run on a public host yet.

## How visitors get access
- The Connection page shows "Try the demo" when the host enables `Demo__PublicSessions`.
  One click calls `POST /api/demo/session`, which returns a random 256-bit bearer token
  (`dms_...`, 47 chars) and an expiry. The server stores only its SHA-256.
- The token lives in browser memory only. Reload or a new tab discards it in that browser
  (a tab opened from a page may or may not share state; do not rely on it). The server keeps the
  session until it expires (default 120 minutes, not sliding).
- Rights are fixed: `read` and `start` on the demo equipment. Never approve, verify, dispatch or ingest.
- Isolation: conversations, jobs, runs, work orders, evidence and traces are owned by the session's
  actor id (migration 008 and the owner checks), so one visitor cannot read another's items by ID.

## Limits (all configurable; defaults shown)
- Mint: 10 sessions per client per hour, 200 live sessions, refuse when stored jobs+conversations >= 20000
  (`Demo__MaxSessionsPerClientPerHour`, `Demo__MaxLiveSessions`, `Demo__MaxStoredRows`). Checked and
  inserted under one Postgres advisory lock, so concurrent mints cannot overshoot.
- Mint HTTP limiter, separate from actor budgets: 6/min per client address and 120/min overall
  (`Security__MintPermitsPerClientPerMinute`, `Security__MintPermitsPerMinute`).
- Per actor: 30 mutating POST/min, 3 active jobs, 25 active total, 200 jobs per session lifetime.
- Containers: api and worker 768 MB / 1 CPU, db 1 GB / 1 CPU, web and caddy 128 MB, pids limits.
- Client address: `X-Forwarded-For` is trusted only from `Demo__TrustedProxyCidr` (the fixed compose
  subnet 172.29.0.0/24); Caddy overwrites the header with the real peer. Otherwise it is ignored.

## Cleanup
The worker removes expired sessions every 60 s and deletes only data owned by those actors
(never seeded equipment, manuals or knowledge chunks). Sessions with queued/running jobs or any
dispatch history are skipped. There is no volume wipe and no cron.

## Mode
The API, worker and setup run as `Production` through `DEMO_PUBLIC=true`: no OpenAPI, HSTS and
HTTPS enforcement on. Caddy still returns 404 for `/openapi*`, `/scalar*`, `/health/*` as a second layer.
The local and CI demo keeps its Development requirement.

## Known gaps
- Not run on a real host: TLS issuance, the Caddy header handling, Production startup inside the
  container, and the compose limits are untested. Test them before sharing a link.
- IP limits do not stop a distributed flood. A captcha or edge protection would be the next layer.
- Conversations have no per-session row cap (the global stored-row guard bounds new sessions only).
- A hosting plan has no hard spend cap; synthetic mode removes AI provider cost, not server or
  bandwidth cost.
