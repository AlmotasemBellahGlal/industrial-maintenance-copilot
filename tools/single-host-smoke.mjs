// Demo-only single host smoke. Needs BASE (https) and the host running with DEMO_TRUSTED_PROXY_CIDR=127.0.0.0/8 and DEMO_MAX_LIVE_SESSIONS=12.
// Not proof of IIS or of any hosting provider. It checks the host's own boundary and pooled-database behaviour.
import assert from 'node:assert/strict';
const base=process.env.BASE,eq='11111111-1111-1111-1111-111111111111',doc='22222222-2222-2222-2222-222222222222',rev='33333333-3333-3333-3333-333333333333';
const call=async(p,{token,method='GET',body,headers={}}={})=>{const r=await fetch(base+p,{method,headers:{...(token?{Authorization:'Bearer '+token}:{}),...(body?{'Content-Type':'application/json'}:{}),...headers},body:body?JSON.stringify(body):undefined});const t=await r.text();let j;try{j=JSON.parse(t)}catch{}return{s:r.status,j,h:r.headers}};
const mint=async ip=>{const r=await call('/api/demo/session',{method:'POST',headers:{'X-Forwarded-For':ip}});assert.equal(r.s,201);return r.j.token};
const ta=await mint('198.51.100.1'),tb=await mint('198.51.100.2');
for(const p of['/health/live','/health/ready','/openapi/v1.json','/scalar','/api/usage',`/api/documents/${doc}/revisions/${rev}/ingestion?equipmentId=${eq}`,`/api/dispatch-attempts/${eq}`])assert.equal((await call(p,{token:ta})).s,404,p);
for(const p of[`/api/documents/${doc}/revisions/${rev}/ingest`,`/api/work-orders/${eq}/dispatch`])assert.equal((await call(p,{token:ta,method:'POST',body:{}})).s,404,p);
assert.equal((await call('/api/identity')).s,401);assert.equal((await call('/api/nothing-here')).s,404);
const id=(await call('/api/identity',{token:ta})).j;assert.deepEqual(id.permissions,['read','start']);
const k=crypto.randomUUID();const sub=await call('/api/jobs',{token:ta,method:'POST',headers:{'Idempotency-Key':k},body:{equipmentId:eq,symptom:'pump vibration '+k}});assert.equal(sub.s,202);
let f;for(let i=0;i<60;i++){await new Promise(r=>setTimeout(r,500));f=await call('/api/jobs/'+sub.j.jobId,{token:ta});if(['Succeeded','Failed'].includes(f.j?.status))break}
assert.equal(f.j.status,'Succeeded');
for(const s of[`/api/jobs/${sub.j.jobId}`,`/api/jobs/${sub.j.jobId}/events`])assert.equal((await call(s,{token:tb})).s,403,s);
assert.equal((await call(`/api/jobs/${sub.j.jobId}/cancel`,{token:tb,method:'POST'})).s,403);
const wid=f.j.result.workOrderId;assert.equal((await call('/api/work-orders/'+wid,{token:ta})).s,200);
const target={revision:1,concurrencyToken:'x'};
for(const [t,p,b] of[[ta,'decisions',{target,decision:'Approve'}],[tb,'decisions',{target,decision:'Approve'}],[ta,'verifications',{target,prerequisiteId:eq,evidence:'x',satisfied:true}]])assert.equal((await call(`/api/work-orders/${wid}/${p}`,{token:t,method:'POST',body:b})).s,403,p);
const cv=await call('/api/conversations',{token:ta,method:'POST',body:{equipmentId:eq,documentId:doc,manualRevisionId:rev}});assert.equal(cv.s,200);
assert.equal((await call('/api/conversations/'+cv.j.id,{token:ta})).s,200);assert.equal((await call('/api/conversations/'+cv.j.id,{token:tb})).s,404);
// Concurrent minting must never pass the live cap (advisory-lock path under the pooler).
const r=await Promise.all(Array.from({length:40},(_,i)=>call('/api/demo/session',{method:'POST',headers:{'X-Forwarded-For':'192.0.2.'+(i+1)}})));
assert.ok(r.filter(x=>x.s===201).length<=10,'live cap exceeded');assert.ok(r.some(x=>x.s===503),'cap never reached');
const spa=await fetch(base+'/diagnose/x');assert.equal(spa.status,200);assert.match(spa.headers.get('content-type'),/html/);assert.equal(spa.headers.get('x-frame-options'),'DENY');
console.log('single-host smoke passed');
