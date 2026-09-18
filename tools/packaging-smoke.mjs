// Container-only proof driver. Reuses existing product/D5 smoke contracts; no privileged Docker socket.
import {readFileSync,writeFileSync} from 'node:fs';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import assert from 'node:assert/strict';
import {smokeFetch} from './smoke-http.mjs';
const base=process.env.DEMO_API_BASE;
assert.equal(new URL(base).hostname,'web');
const token=readFileSync(process.env.DEMO_CREDENTIAL_FILE,'utf8').trim();
const equipmentId='11111111-1111-1111-1111-111111111111';
async function call(path,body,key){
  const r=await smokeFetch(base+path,{method:body===undefined?'GET':'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json','Accept-Language':'ar-EG',...(key?{'Idempotency-Key':key}:{})},body:body===undefined?undefined:JSON.stringify(body)});
  return {status:r.status,body:await r.json()};
}
async function run(file,...args){const child=spawn(process.execPath,[file,...args],{stdio:'inherit'});assert.equal((await once(child,'exit'))[0],0);}
async function until(id,predicate){for(let i=0;i<180;i++){const job=(await call(`/jobs/${id}`)).body;if(predicate(job))return job;await new Promise(r=>setTimeout(r,500));}throw Error('Job boundary timeout');}
if(process.argv.includes('--submit-recovery')){
  // The documented delayed Worker makes cancellation deterministic, not a race with instant completion.
  const cancelled=await call('/jobs',{equipmentId,symptom:'pump vibration'},crypto.randomUUID());assert.equal(cancelled.status,202);await until(cancelled.body.jobId,j=>j.status==='Running');await call(`/jobs/${cancelled.body.jobId}/cancel`,{});await until(cancelled.body.jobId,j=>j.status==='Cancelled');
  const key=crypto.randomUUID();const body={equipmentId,symptom:'pump vibration'};const submitted=await call('/jobs',body,key);assert.equal(submitted.status,202);
  const id=submitted.body.jobId;assert.equal((await call('/jobs',body,key)).body.jobId,id);assert.equal((await call('/jobs',{...body,symptom:'changed'},key)).status,409);
  const response=await fetch(base+`/jobs/${id}/events`,{headers:{Authorization:`Bearer ${token}`},signal:AbortSignal.timeout(60000)});assert.equal(response.status,200);
  const reader=response.body.getReader();let events='';while(!events.includes('event: AgentStarted')){const item=await reader.read();assert.equal(item.done,false);events+=new TextDecoder().decode(item.value);}await reader.cancel();
  const active=await until(id,j=>j.status==='Running');assert.equal(active.cancellationRequested,false);
  writeFileSync('artifacts/packaging-recovery.json',JSON.stringify({id,attempts:active.attempts.length}));
  console.log('PASS: 202, duplicate convergence, changed-payload 409; SSE disconnect left job Running. Kill the delayed test Worker now.');
} else if(process.argv.includes('--verify-recovery')){
  const proof=JSON.parse(readFileSync('artifacts/packaging-recovery.json','utf8'));const finished=await until(proof.id,j=>j.status==='Succeeded');assert.equal(finished.attempts.length,proof.attempts+1);assert.match(finished.result.narrative,/[\u0600-\u06ff]/);
  const replay=await fetch(base+`/jobs/${proof.id}/events`,{headers:{Authorization:`Bearer ${token}`},signal:AbortSignal.timeout(10000)});assert.equal(replay.status,200);const events=await replay.text();assert.ok(events.includes('event: Result'));for(const attempt of finished.attempts)assert.ok(events.includes(attempt.executionId));
  const runState=(await call(`/runs/${finished.maintenanceRunId}`)).body;assert.equal(runState.workOrderIds.length,1);
  const order=(await call(`/work-orders/${finished.result.workOrderId}`)).body;assert.equal(order.status,'PendingApproval');assert.equal((await call(`/work-orders/${order.workOrderId}/dispatch`,order.target)).status,422);
  writeFileSync('artifacts/packaging-recovery.json',JSON.stringify({...proof,recovered:true,attempts:finished.attempts.length,workOrders:1}));console.log('PASS: killed Worker recovered with a new attempt and exactly one unapproved work order; dispatch remains blocked.');
} else if(process.argv.includes('--verify-restart')){
  await run('tools/product-smoke.mjs','--verify-history');
  const proof=JSON.parse(readFileSync('artifacts/packaging-proof.json','utf8'));
  for(const saved of proof.usage){const current=(await call(`/usage?correlationId=${saved.correlation}`)).body;assert.deepEqual(current.records.map(r=>r.callId).sort(),saved.ids.sort());}
  const status=await call(`/documents/22222222-2222-2222-2222-222222222222/revisions/33333333-3333-3333-3333-333333333333/ingestion?equipmentId=${equipmentId}`);assert.equal(status.body[0].state,2);
  writeFileSync('artifacts/packaging-proof.json',JSON.stringify({...proof,restartVerified:true},null,2));console.log('PASS: database restart retained history, exact citations, ingestion and usage IDs.');
} else {
  const root=new URL('/',base);const page=await fetch(root);assert.equal(page.status,200);const html=await page.text();assert.match(html,/<app-root/);
  // CSP forbids inline script handlers. Critical-CSS extraction must not leave
  // the production stylesheet stuck at media=print behind a blocked onload.
  assert.doesNotMatch(html,/onload\s*=/i);
  const route=await fetch(new URL('/diagnosis',base));assert.equal(route.status,200);assert.match(await route.text(),/<app-root/);
  assert.equal((await fetch(base+'/usage')).status,401);
  await run('tools/product-smoke.mjs');await run('tools/demo-smoke.mjs','--jobs');
  const product=JSON.parse(readFileSync('artifacts/product-proof.json','utf8'));const usage=[];
  for(const item of product.runs){const result=(await call(`/usage?correlationId=${item.correlationId}`)).body;assert.ok(result.records.length>0);assert.ok(result.records.every(r=>r.tokens===null && r.estimatedCost===null));usage.push({correlation:item.correlationId,ids:result.records.map(r=>r.callId)});}
  writeFileSync('artifacts/packaging-proof.json',JSON.stringify({usage,restartVerified:false,ui:true,spa:true,roles:true,streaming:true,d5:true,t7:true},null,2));
  console.log('PASS: packaged SPA/proxy, bilingual streaming/citations, history/IDOR, roles, ingestion, D5/T7, Ask cancellation and usage.');
}
