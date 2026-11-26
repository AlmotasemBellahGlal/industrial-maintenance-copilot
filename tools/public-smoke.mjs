// Public-overlay proof through Caddy -> nginx -> API (Production). Run by .github/workflows/public-demo.yml.
// Usage: node tools/public-smoke.mjs <phase>   phases: flow | after-expiry
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
const base = process.env.PUBLIC_DEMO_URL ?? 'https://localhost';
const equipmentId = '11111111-1111-1111-1111-111111111111';
const phase = process.argv[2] ?? 'flow';
const limit = Number(process.env.DEMO_MAX_SESSIONS_PER_CLIENT_HOUR ?? '4');
async function call(path, { token, method = 'GET', body, headers = {} } = {}) {
  const response = await fetch(base + path, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body ? JSON.stringify(body) : undefined,
    redirect: 'manual',
  });
  const text = await response.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* not JSON */ }
  return { status: response.status, headers: response.headers, json, text };
}
const mint = (headers) => call('/api/demo/session', { method: 'POST', headers });
const submit = (token, key) =>
  call('/api/jobs', { token, method: 'POST', headers: { 'Idempotency-Key': key }, body: { equipmentId, symptom: 'pump vibration ' + key } });

if (phase === 'flow') {
  // 1. Production behaviour through the real proxy chain.
  const offer = await call('/api/demo/session');
  assert.equal(offer.status, 200); assert.equal(offer.json.enabled, true);
  for (const path of ['/openapi/v1.json', '/api/openapi/v1.json', '/scalar', '/health/ready', '/health/live'])
    assert.equal((await call(path)).status, 404, path + ' must not be public');
  // 2. Anonymous sessions are separate, least privilege, and isolated.
  const a = await mint({ 'X-Forwarded-For': '203.0.113.1' });
  assert.equal(a.status, 201); const tokenA = a.json.token;
  const b = await mint({ 'X-Forwarded-For': '203.0.113.2' });
  assert.equal(b.status, 201); const tokenB = b.json.token;
  assert.notEqual(tokenA, tokenB); assert.match(tokenA, /^dms_[A-Za-z0-9_-]{43}$/);
  const identity = await call('/api/identity', { token: tokenA });
  assert.equal(identity.status, 200);
  assert.deepEqual(identity.json.permissions.map((x) => String(x).toLowerCase()).sort(), ['read', 'start']);
  const job = await submit(tokenA, crypto.randomUUID());
  assert.equal(job.status, 202);
  const id = job.json.jobId;
  assert.equal((await call(`/api/jobs/${id}`, { token: tokenA })).status, 200);
  assert.equal((await call(`/api/jobs/${id}`, { token: tokenB })).status, 403);
  assert.equal((await call(`/api/jobs/${id}/cancel`, { token: tokenB, method: 'POST' })).status, 403);
  assert.equal((await call('/api/jobs/' + id)).status, 401);
  // The worker finishes the visitor's job with the synthetic provider.
  let state = '';
  for (let i = 0; i < 120 && state !== 'Succeeded'; i++) {
    state = (await call(`/api/jobs/${id}`, { token: tokenA })).json.status;
    if (state !== 'Succeeded') await new Promise((r) => setTimeout(r, 500));
  }
  if (state !== 'Succeeded') {
    const body = (await call(`/api/jobs/${id}`, { token: tokenA })).json;
    console.error('Job did not succeed. status=' + state + ' body=' + JSON.stringify(body).slice(0, 1500));
  }
  assert.equal(state, 'Succeeded');
  // 3. The mint limit is keyed on the real client address, not on a header the visitor controls.
  const codes = [];
  for (let i = 0; i < limit + 2; i++) codes.push((await mint({ 'X-Forwarded-For': `198.51.100.${i + 10}` })).status);
  assert.ok(codes.includes(429), 'rotating X-Forwarded-For must not evade the per-client mint limit: ' + codes);
  console.log('PASS flow: hidden docs/health, isolated least-privilege sessions, job finished, mint limit ignores spoofed headers');
  const stateFile = process.env.PUBLIC_SMOKE_STATE_FILE;
  assert.ok(stateFile, 'PUBLIC_SMOKE_STATE_FILE required');
  writeFileSync(stateFile, JSON.stringify({ tokenA, jobId: id }));
} else if (phase === 'after-expiry') {
  const stateFile = process.env.PUBLIC_SMOKE_STATE_FILE;
  assert.ok(stateFile, 'PUBLIC_SMOKE_STATE_FILE required');
  const raw = readFileSync(stateFile, 'utf8');
  assert.ok(raw.trim().length > 0, 'state file is empty: the flow phase did not complete');
  const { tokenA, jobId } = JSON.parse(raw);
  assert.ok(tokenA && jobId, 'state file lacks token or job id');
  assert.equal((await call(`/api/jobs/${jobId}`, { token: tokenA })).status, 401, 'expired token must stop authenticating');
  console.log('PASS after-expiry: expired visitor token is refused');
}
