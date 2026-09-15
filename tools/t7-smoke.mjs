// Real PostgreSQL/API plus child-process production Worker services in the opt-in deterministic demo host.
// Start the demo API first. Do not run another Worker during this isolated recovery test.
import { readFileSync, openSync, closeSync, writeFileSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import assert from 'node:assert/strict';
const token=readFileSync('artifacts/issue25/credential.txt','utf8').trim();
const base='http://127.0.0.1:5000/api';
const equipmentId='11111111-1111-1111-1111-111111111111';
const proof=[];let worker;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function call(path,body,culture='en-US',key) {
  const response=await fetch(base+path,{method:body===undefined?'GET':'POST',signal:AbortSignal.timeout(10000),headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json','Accept-Language':culture,...(key?{'Idempotency-Key':key}:{})},body:body===undefined?undefined:JSON.stringify(body)});
  return {status:response.status,body:await response.json()};
}
async function submit(culture='en-US',symptom='pump vibration') {
  const key=crypto.randomUUID();const started=Date.now();const result=await call('/jobs',{equipmentId,symptom},culture,key);
  assert.equal(result.status,202);assert.equal(result.body.status,'Queued');
  const duplicate=await call('/jobs',{equipmentId,symptom},culture,key);assert.equal(duplicate.body.jobId,result.body.jobId);
  proof.push({submission:result.body.jobId,status:202,elapsedMs:Date.now()-started});return result.body;
}
async function until(id,predicate) {
  for(let i=0;i<120;i++) {const j=(await call(`/jobs/${id}`)).body;if(predicate(j))return j;await sleep(500);}
  throw Error('Durable job did not reach expected boundary: '+id);
}
function start(delay=0) {
  const fd=openSync('artifacts/t7-worker.log','a');
  worker=spawn('dotnet',['tools/IndustrialCopilot.Demo/bin/Debug/net10.0/IndustrialCopilot.Demo.dll','--demo','--worker'],{env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',DEMO_MODEL_DELAY_MS:String(delay)},stdio:['ignore',fd,fd]});closeSync(fd);
}
async function stop() {if(worker && worker.exitCode===null){const exited=once(worker,'exit');worker.kill('SIGKILL');await exited;}worker=undefined;}
try {
  const queued=await submit();assert.equal((await call(`/jobs/${queued.jobId}/cancel`,{})).body.status,'Cancelled');
  const recover=await submit('ar-EG');
  const observer=new AbortController();
  const live=await fetch(base+`/jobs/${recover.jobId}/events`,{headers:{Authorization:`Bearer ${token}`},signal:observer.signal});
  const reader=live.body.getReader();let liveText='';
  const liveProgress=(async()=>{while(!liveText.includes('event: AgentStarted')){const item=await reader.read();if(item.done)throw Error('Live stream ended early');liveText+=new TextDecoder().decode(item.value);}})();
  start(30000);await Promise.race([liveProgress,sleep(30000).then(()=>{throw Error('No pushed live progress');})]);
  observer.abort();await reader.cancel().catch(()=>{});
  await until(recover.jobId,j=>j.phase==='MatchingSymptoms');await stop();
  const interrupted=(await call(`/jobs/${recover.jobId}`)).body;assert.equal(interrupted.status,'Running');
  start();const finished=await until(recover.jobId,j=>j.status==='Succeeded');
  assert.equal(finished.attempts.length,2);assert.notEqual(finished.attempts[0].executionId,finished.attempts[1].executionId);
  assert.match(finished.result.narrative,/[\u0600-\u06ff]/);
  const progress=await fetch(base+`/jobs/${recover.jobId}/events`,{headers:{Authorization:`Bearer ${token}`},signal:AbortSignal.timeout(10000)});
  const events=await progress.text();assert.ok(events.includes('event: AgentStarted'));assert.ok(events.includes('event: Result'));assert.ok(events.includes(finished.attempts[1].executionId));
  const run=(await call(`/runs/${finished.maintenanceRunId}`)).body;assert.equal(run.status,'WaitingForApproval');assert.equal(run.workOrderIds.length,1);
  const order=(await call(`/work-orders/${finished.result.workOrderId}`)).body;assert.equal(order.status,'PendingApproval');assert.equal(order.requirements[0].status,'Unverified');
  assert.equal((await call(`/work-orders/${finished.result.workOrderId}/dispatch`,order.target)).status,422);
  proof.push({recoveryJob:finished.jobId,attempts:finished.attempts,progressReplayed:true,liveProgressBeforeDisconnect:true,runStatus:run.status,workOrders:run.workOrderIds.length,approvalBypassed:false});
  await stop();const cancelling=await submit();start(30000);await until(cancelling.jobId,j=>j.phase==='MatchingSymptoms');
  const intent=(await call(`/jobs/${cancelling.jobId}/cancel`,{})).body;assert.equal(intent.cancellationRequested,true);
  const cancelled=await until(cancelling.jobId,j=>j.status==='Cancelled');assert.equal((await call(`/runs/${cancelled.maintenanceRunId}`)).body.status,'Cancelled');
  proof.push({runningCancellation:cancelled.jobId,status:cancelled.status});await stop();
  const blocked=await submit('en-US','unknown synthetic symptom');const good=await submit();start();
  const badDone=await until(blocked.jobId,j=>j.status==='Failed');assert.equal(badDone.phase,'Blocked');
  const goodDone=await until(good.jobId,j=>j.status==='Succeeded');assert.equal(goodDone.attempts.length,1);
  proof.push({blockedJob:badDone.jobId,unrelatedJob:goodDone.jobId,workerContinued:true});
  // Existing full human approval / verification / dispatch regression now runs through durable submissions.
  const smoke=spawn(process.execPath,['tools/demo-smoke.mjs','--jobs'],{env:process.env,stdio:'inherit'});
  assert.equal((await once(smoke,'exit'))[0],0);
  writeFileSync('artifacts/t7-proof.json',JSON.stringify(proof,null,2));console.log('T7 live proof passed: 202, replay, real process kill/restart, SSE, cancellation, failure isolation, bilingual safety/approval/dispatch.');
} finally {await stop();}
