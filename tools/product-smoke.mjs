// Opt-in loopback/Compose proof; never logs credentials. Run with the deterministic Demo host.
import { readFileSync, writeFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { smokeFetch } from './smoke-http.mjs';
const base=process.env.DEMO_API_BASE??'http://127.0.0.1:5000/api';
if(!['127.0.0.1','localhost','web'].includes(new URL(base).hostname))throw Error('Use the isolated local demo host.');
const token=readFileSync(process.env.DEMO_CREDENTIAL_FILE??'artifacts/issue25/credential.txt','utf8').trim();
const technician=readFileSync(process.env.DEMO_TECHNICIAN_FILE??'artifacts/issue25/technician-credential.txt','utf8').trim();
const equipmentId='11111111-1111-1111-1111-111111111111';
const headers=(culture='en-US',credential=token)=>({Authorization:`Bearer ${credential}`,'Accept-Language':culture,'Content-Type':'application/json'});
async function json(path,body,culture='en-US',credential=token){const r=await smokeFetch(base+path,{method:body===undefined?'GET':'POST',headers:headers(culture,credential),body:body===undefined?undefined:JSON.stringify(body)});return{status:r.status,body:await r.json()};}
if(process.argv.includes('--verify-history')){
 const proof=JSON.parse(readFileSync('artifacts/product-proof.json','utf8'));
 for(const run of proof.runs){const result=await json(`/conversations/${run.conversationId}`);assert.equal(result.status,200);assert.equal(result.body.turns[0].answer,run.answer);assert.deepEqual(result.body.turns[0].citations,run.citations);}
 proof.restartVerified=true;writeFileSync('artifacts/product-proof.json',JSON.stringify(proof,null,2));console.log('PASS: history and exact citations survived API process restart.');process.exit(0);
}
const proof={runs:[],restartVerified:false};
async function ask(id,question,culture,cancel=false){
 const controller=new AbortController();const started=performance.now();
 let response;
 for(let attempt=0;attempt<3;attempt++){
  response=await fetch(`${base}/conversations/${id}/ask`,{method:'POST',headers:headers(culture),body:JSON.stringify({question}),signal:AbortSignal.any([controller.signal,AbortSignal.timeout(70000)])});
  if(response.status!==429||attempt===2)break;
  const seconds=Number(response.headers.get('Retry-After'));assert.ok(seconds>=1&&seconds<=65);await response.body?.cancel();await new Promise(r=>setTimeout(r,seconds*1000));
 }
 assert.equal(response.status,200);
 const reader=response.body.getReader(),decoder=new TextDecoder();let buffer='',answer='',citations=[],state,correlationId;const events=[];
 try{while(true){const {value,done}=await reader.read();if(done)break;buffer+=decoder.decode(value,{stream:true});let end;
 while((end=buffer.indexOf('\n\n'))>=0){const frame=buffer.slice(0,end);buffer=buffer.slice(end+2);const type=frame.split('\n').find(l=>l.startsWith('event:'))?.slice(6).trim();const data=JSON.parse(frame.split('\n').find(l=>l.startsWith('data:')).slice(5));events.push({type,elapsedMs:Math.round(performance.now()-started)});
 assert.ok(['meta','evidence','delta','citation','completed','error'].includes(type));assert.notEqual(type,'error');
 if(type==='meta')correlationId=data.correlationId;if(type==='delta'){answer+=data.delta;if(cancel){controller.abort();return{events,cancelled:true,correlationId};}}
 if(type==='citation')citations=data.citations;if(type==='completed')state=data.state;
 }}}finally{await reader.cancel().catch(()=>{});reader.releaseLock();}
 return{events,answer,citations,state,correlationId};
}
for(const culture of ['en-US','ar-EG']){
 const documentId=randomUUID(),manualRevisionId=randomUUID();
 const content='SYNTHETIC PRODUCT PROOF ONLY.\nPump vibration requires inspection.\nاهتزاز المضخة يتطلب الفحص.\nIsolate before checking the pump seal.\nIndependent human approval and verified safety are mandatory.';
 const q=new URLSearchParams({equipmentId,filename:'product.txt',title:'Synthetic product manual',revisionNumber:'1'});
 const upload=await smokeFetch(`${base}/documents/${documentId}/revisions/${manualRevisionId}/ingest?${q}`,{method:'POST',headers:{...headers(culture),'Content-Type':'text/plain'},body:content});assert.equal(upload.status,200);const report=(await upload.json())[0];assert.equal(report.state,2);assert.ok(report.chunks>0);
 const created=await json('/conversations',{equipmentId,documentId,manualRevisionId},culture);assert.equal(created.status,200);const id=created.body.id;
 const result=await ask(id,culture==='ar-EG'?'اهتزاز':'vibration',culture);assert.equal(result.state,'Completed');assert.ok(result.events.filter(e=>e.type==='delta').length>=2);const completion=result.events.find(e=>e.type==='completed').elapsedMs;assert.ok(result.events.filter(e=>e.type==='delta').slice(0,2).every(e=>e.elapsedMs<completion));
 assert.ok(result.citations.length>0);for(const c of result.citations){assert.equal(c.documentId,documentId);assert.equal(c.manualRevisionId,manualRevisionId);assert.ok(content.includes(c.snippet));assert.ok(c.locator.startsWith('text:lines'));}
 const refused=await ask(id,'quasar astrophysics',culture);assert.equal(refused.state,'InsufficientEvidence');assert.equal(refused.events.filter(e=>e.type==='delta').length,0);
 const cancelled=await ask(id,'vibration',culture,true);assert.equal(cancelled.cancelled,true);let history;
 for(let n=0;n<100;n++){history=(await json(`/conversations/${id}`)).body;if(history.turns.at(-1).state===3)break;await new Promise(r=>setTimeout(r,100));}
 assert.equal(history.turns.at(-1).state,3);assert.equal(history.turns.at(-1).answer,'');
 assert.equal((await json(`/conversations/${id}`,undefined,culture,technician)).status,404);
 const denied=await fetch(`${base}/documents/${documentId}/revisions/${manualRevisionId}/ingest?${q}`,{method:'POST',headers:{...headers(culture,technician),'Content-Type':'text/plain'},body:content});assert.equal(denied.status,403);
 proof.runs.push({culture,conversationId:id,ingestion:report,documentId,manualRevisionId,...result,refusal:refused.state,cancelledState:history.turns.at(-1).state,technicianHistoryStatus:404,technicianUploadStatus:403});
}
writeFileSync('artifacts/product-proof.json',JSON.stringify(proof,null,2));console.log(JSON.stringify(proof,null,2));
