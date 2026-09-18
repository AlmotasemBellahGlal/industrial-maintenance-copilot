import { smokeFetch } from './smoke-http.mjs';
// Run from the repository root after starting IndustrialCopilot.Demo.
// All workflow state is created by the real HTTP API; no database seeding of approvals.
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
const credential=readFileSync(process.env.DEMO_CREDENTIAL_FILE??'artifacts/issue25/credential.txt','utf8').trim();
const base=process.env.DEMO_API_BASE??'http://127.0.0.1:5000/api';
if(!['127.0.0.1','localhost','web'].includes(new URL(base).hostname))throw Error('Use the isolated local demo host.');
async function call(path,body,culture='en-US',authenticated=true) {
  const response=await smokeFetch(base+path,{method:body===undefined?'GET':'POST',headers:{
    'Content-Type':'application/json','Accept-Language':culture,...(authenticated?{Authorization:`Bearer ${credential}`}:{})
  },body:body===undefined?undefined:JSON.stringify(body)});
  return {status:response.status,body:await response.json()};
}
let canonicalRequirements,canonicalEvidence;let scenarios=0;
for(const culture of ['en-US','ar-EG']) {
  const unauthorized=await call('/runs',{equipmentId:'11111111-1111-1111-1111-111111111111',symptom:'pump vibration'},culture,false);
  assert.equal(unauthorized.status,401);assert.equal(unauthorized.body.error,'unauthenticated');
  assert.equal(/[\u0600-\u06ff]/.test(unauthorized.body.title),culture==='ar-EG');
  for(const decision of ['Approve','Reject','EditAndApprove']) {
    let run;
    if(process.argv.includes('--jobs')) {
      const response=await smokeFetch(base+'/jobs',{method:'POST',headers:{'Content-Type':'application/json',Authorization:`Bearer ${credential}`,'Accept-Language':culture,'Idempotency-Key':crypto.randomUUID()},body:JSON.stringify({equipmentId:'11111111-1111-1111-1111-111111111111',symptom:'pump vibration'})});
      assert.equal(response.status,202);let job=await response.json();
      for(let poll=0;poll<60 && !['Succeeded','Failed','Cancelled'].includes(job.status);poll++) {
        await new Promise(resolve=>setTimeout(resolve,500));job=(await call(`/jobs/${job.jobId}`)).body;
      }
      assert.equal(job.status,'Succeeded');
      run={status:201,body:{...job.result,executionId:job.attempts.at(-1).executionId,correlationId:job.correlationId}};
    } else run=await call('/runs',{equipmentId:'11111111-1111-1111-1111-111111111111',symptom:'pump vibration'},culture);
    assert.equal(run.status,201);assert.equal(run.body.outcome,'Proposed');
    assert.equal(/[\u0600-\u06ff]/.test(run.body.narrative),culture==='ar-EG');
    const path=`/work-orders/${run.body.workOrderId}`;
    let review=(await call(path,undefined,culture)).body;
    const evidence=(await call(path+'/evidence',undefined,culture)).body.evidence;
    canonicalRequirements??=review.requirements;canonicalEvidence??=evidence;
    assert.deepEqual(review.requirements,canonicalRequirements);assert.deepEqual(evidence,canonicalEvidence);
    assert.equal((await call(path+'/dispatch',review.target,culture)).status,422);
    assert.equal((await call(path+'/decisions',{target:review.target,decision,actorId:'spoof'},culture)).status,400);
    const previous=review.target;
    let edit={};
    if(decision==='EditAndApprove') {
      const content={...review.content,reportedSymptom:'pump vibration observed during demo review'};
      const preview=await call(path+'/edited-safety-preview',content,culture);
      assert.equal(preview.status,200);assert.equal(preview.body.canProceed,true);
      edit={editedContent:content,reviewedRequirements:preview.body.requirements};
      assert.equal((await call(path+'/edited-safety-preview',{...content,description:'unreviewed expanded repair'},culture)).status,422);
    }
    assert.equal((await call(path+'/decisions',{target:previous,decision,...edit},culture)).status,200);
    assert.equal((await call(path+'/decisions',{target:previous,decision:'Approve'},culture)).status,409);
    review=(await call(path,undefined,culture)).body;
    assert.equal((await call(path+'/dispatch',review.target,culture)).status,422);
    if(decision==='Reject') { assert.equal(review.status,'Rejected');scenarios++;continue; }
    assert.equal(review.status,'Approved');assert.equal(review.requirements[0].status,'Unverified');
    assert.equal((await call(path+'/verifications',{target:review.target,prerequisiteId:review.requirements[0].id,evidence:'SIMULATED demo zero-energy observation',satisfied:true},culture)).status,200);
    review=(await call(path,undefined,culture)).body;
    const dispatch=await call(path+'/dispatch',review.target,culture);
    if(process.argv.includes('--uncertain')) {
      assert.equal(dispatch.status,202);assert.equal(dispatch.body.state,'Uncertain');
      let current=dispatch.body;
      for(let poll=0;poll<30 && current.state!=='Confirmed';poll++) {
        await new Promise(resolve=>setTimeout(resolve,1000));
        current=(await call(`/dispatch-attempts/${dispatch.body.attemptId}`,undefined,culture)).body;
      }
      assert.equal(current.state,'Confirmed');assert.equal(current.attemptId,dispatch.body.attemptId);
      assert.equal(current.externalReference,dispatch.body.attemptId);
    } else {assert.equal(dispatch.status,200);assert.equal(dispatch.body.state,'Confirmed');}
    assert.equal((await call(path,undefined,culture)).body.status,'Dispatched');
    assert.equal((await call(`/runs/${run.body.runId}`,undefined,culture)).body.status,'Completed');
    const trace=(await call(`/traces/${run.body.executionId}`,undefined,culture)).body;
    assert.equal(trace.steps.filter(s=>s.kind==='Agent').length,3);
    assert.ok(trace.steps.filter(s=>s.kind==='Agent').every(s=>s.error===null));
    assert.equal(trace.correlationId,run.body.correlationId);
    scenarios++;
  }
}
console.log(`PASS: ${scenarios} real HTTP/PostgreSQL workflows; bilingual narrative, original citations, identical prerequisites, all approval decisions, stale conflicts, spoof rejection, unverified safety gate, verified dispatch and trace.`);
