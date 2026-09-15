import test from 'node:test';
import assert from 'node:assert/strict';
import { smokeFetch } from './smoke-http.mjs';
test('429 retry preserves idempotency body and gets a fresh attempt deadline', async t => {
  const requests=[];
  t.mock.method(globalThis,'fetch',async (_url,options)=>{
    requests.push(options);
    return new Response(null,{status:requests.length===1?429:202,headers:{'Retry-After':'1'}});
  });
  const options={method:'POST',body:'fixed-payload',headers:{'Idempotency-Key':'fixed-key'}};
  assert.equal((await smokeFetch('http://localhost/test',options)).status,202);
  assert.equal(requests.length,2);assert.equal(requests[1].body,requests[0].body);
  assert.deepEqual(requests[1].headers,requests[0].headers);assert.notEqual(requests[1].signal,requests[0].signal);
});
test('conflict and uncertain server failures are never automatically retried',async t=>{
  let count=0;t.mock.method(globalThis,'fetch',async()=>{count++;return new Response(null,{status:503});});
  assert.equal((await smokeFetch('http://localhost/test',{})).status,503);assert.equal(count,1);
});
test('unbounded Retry-After is rejected',async t=>{
  t.mock.method(globalThis,'fetch',async()=>new Response(null,{status:429,headers:{'Retry-After':'3600'}}));
  await assert.rejects(()=>smokeFetch('http://localhost/test',{}),/excessive/);
});
