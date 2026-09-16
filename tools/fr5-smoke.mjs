// Explicit loopback deterministic fault proof. Run Demo with --transient-reasoning.
import { readFileSync, writeFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';
import { smokeFetch } from './smoke-http.mjs';
const base = 'http://127.0.0.1:5000/api';
const token = readFileSync('artifacts/issue25/credential.txt', 'utf8').trim();
const technician = readFileSync('artifacts/issue25/technician-credential.txt', 'utf8').trim();
const equipmentId = '11111111-1111-1111-1111-111111111111';
const headers = (credential = token) => ({ Authorization: `Bearer ${credential}`, 'Content-Type': 'application/json', 'Accept-Language': 'ar-EG' });
async function usage(correlation, credential = token) {
  const r = await smokeFetch(`${base}/usage?correlationId=${correlation}`, { headers: headers(credential) });
  assert.equal(r.status, 200); return r.json();
}
if (process.argv.includes('--verify-restart')) {
  const proof = JSON.parse(readFileSync('artifacts/fr5-live-proof.json', 'utf8'));
  for (const run of proof.runs) {
    const page = await usage(run.correlation);
    assert.deepEqual(page.records.map(r => r.callId).sort(), run.callIds.sort());
  }
  proof.restartVerified = true;
  writeFileSync('artifacts/fr5-live-proof.json', JSON.stringify(proof, null, 2));
  console.log('PASS: identical usage call IDs survive host restart.'); process.exit(0);
}
const proof = { runs: [], restartVerified: false };
for (const [symptom, outcome] of [['pump vibration', 'Degraded'], ['quasar astrophysics', 'DegradedRefused']]) {
  const correlation = randomUUID();
  const r = await smokeFetch(`${base}/runs/stream`, { method: 'POST', headers: { ...headers(), 'X-Correlation-ID': correlation }, body: JSON.stringify({ equipmentId, symptom }), signal: AbortSignal.timeout(90000) });
  assert.equal(r.status, 200); const text = await r.text();
  const events = text.split('\n\n').filter(x => x.includes('data:')).map(frame => ({ name: frame.split('\n').find(x => x.startsWith('event:')).slice(6).trim(), data: JSON.parse(frame.split('\n').find(x => x.startsWith('data:')).slice(5)) }));
  assert.ok(events.some(e => e.name === 'RetryScheduled' && e.data.progress.Attempt === 2));
  assert.ok(events.some(e => e.name === 'FallbackStarted'));
  const result = events.find(e => e.name === 'Result').data;
  assert.equal(result.Outcome, outcome); assert.equal(result.WorkOrderId, null); assert.equal(result.DegradationReason, 'transient_exhausted');
  if (outcome === 'Degraded') {
    assert.ok(result.Citations.length > 0);
    assert.equal(result.Citations[0].DocumentId, '22222222-2222-2222-2222-222222222222');
    assert.equal(result.Citations[0].ManualRevisionId, '33333333-3333-3333-3333-333333333333');
    assert.ok(result.Citations[0].Snippet.includes('vibration')); assert.match(result.Narrative, /معلومات استشارية/);
  } else assert.equal(result.Citations.length, 0);
  const page = await usage(correlation);
  assert.equal(page.records.filter(x => x.operation === 1).length, 2);
  assert.ok(page.records.every(x => x.context.runId === result.RunId && x.tokens === null && x.estimatedCost === null));
  assert.equal(page.summary.totalTokens, null);
  assert.equal((await usage(correlation, technician)).records.length, 0);
  proof.runs.push({ correlation, runId: result.RunId, outcome, events: events.map(e => e.name), citations: result.Citations, callIds: page.records.map(x => x.callId), calls: page.summary.calls, knownTokenCalls: page.summary.knownTokenCalls });
}
writeFileSync('artifacts/fr5-live-proof.json', JSON.stringify(proof, null, 2));
console.log('PASS: bounded retries, Arabic grounded fallback, exact citations, refusal, no work order, scoped persisted unknown usage.');
