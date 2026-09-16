import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FormControl } from '@angular/forms';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DispatchApi, hostHeaders, ReviewApi, WorkflowApi, errorText } from './api';
import { Session } from './session';
import { decodeEvent, readEvents, StreamEvent } from './workflow-stream';
import { guid, requiredText } from '../shared/ui';
const id = '11111111-1111-1111-1111-111111111111';
describe('Host API contracts', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([hostHeaders])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(Session).connect('test-only-credential'.repeat(2));
  });
  afterEach(() => http.verify());
  it('binds edited approval to target and comparison-only authoritative requirements without actor authority', async () => {
    const target = { revision: 2, concurrencyToken: 'version' },
      content = {
        equipmentId: id,
        manualId: id,
        manualRevisionId: id,
        reportedSymptom: 'noise',
        description: 'repair',
        actions: [{ order: 1, instruction: 'isolate' }],
      },
      requirements = [{ id, description: 'isolation', mandatory: true }];
    const promise = TestBed.inject(ReviewApi).decide(
      id,
      target,
      'EditAndApprove',
      content,
      requirements,
    );
    const r = http.expectOne(`/api/work-orders/${id}/decisions`);
    expect(r.request.body).toEqual({
      target,
      decision: 'EditAndApprove',
      editedContent: content,
      reviewedRequirements: requirements,
    });
    expect(r.request.headers.get('Authorization')).toContain('Bearer test-only');
    expect(r.request.headers.get('X-Correlation-ID')).toMatch(/^[0-9a-f-]{36}$/);
    r.flush({ outcome: 'Applied', review: null });
    await promise;
  });
  it.each(['Approve', 'Reject'] as const)(
    'sends %s without edited scope or actor fields',
    async (decision) => {
      const target = { revision: 4, concurrencyToken: 'c4' };
      const p = TestBed.inject(ReviewApi).decide(id, target, decision);
      const r = http.expectOne(`/api/work-orders/${id}/decisions`);
      expect(r.request.body).toEqual({ target, decision });
      r.flush({ outcome: 'Applied', review: null });
      await p;
    },
  );
  it('records physical verification with target and evidence', async () => {
    const target = { revision: 3, concurrencyToken: 'c3' };
    const p = TestBed.inject(ReviewApi).verify(id, target, id, 'meter zero', true);
    const r = http.expectOne(`/api/work-orders/${id}/verifications`);
    expect(r.request.body).toEqual({
      target,
      prerequisiteId: id,
      evidence: 'meter zero',
      satisfied: true,
    });
    r.flush({ outcome: 'Ready' });
    await p;
  });
  it('dispatch inspection is a GET against the existing attempt', async () => {
    const p = TestBed.inject(DispatchApi).get(id);
    const r = http.expectOne(`/api/dispatch-attempts/${id}`);
    expect(r.request.method).toBe('GET');
    r.flush({ attemptId: id, state: 'Uncertain' });
    expect((await p).state).toBe('Uncertain');
  });
  it('does not forward bearer credentials to readiness endpoint', async () => {
    const p = TestBed.inject(WorkflowApi).ready();
    const r = http.expectOne('/health/ready');
    expect(r.request.headers.has('Authorization')).toBe(false);
    r.flush({ status: 'ready' });
    await p;
  });
  it('distinguishes authentication, permission and stale review errors without raw error payloads', () => {
    expect(errorText(401)).toContain('Authentication');
    expect(errorText(403)).toContain('Permission');
    expect(errorText(409)).toContain('Reload');
    expect(errorText(503)).toContain('unknown');
  });
});
describe('Request-owned SSE framing', () => {
  const progress = `event: AgentStarted\ndata: ${JSON.stringify({ CorrelationId: id, ExecutionId: id, progress: { Kind: 1, RunId: id, Role: 2, Allowed: null, WorkOrderId: null } })}\n\n`;
  const result = `event: Result\ndata: ${JSON.stringify({ RunId: id, ExecutionId: id, CorrelationId: id, WorkOrderId: null, Outcome: 'Blocked' })}\n\n`;
  it('decodes real PascalCase envelope and numeric role; ignores unrelated fields', () => {
    const e = decodeEvent('AgentStarted', progress.split('data: ')[1].trim());
    expect(e?.type).toBe('progress');
    if (e?.type === 'progress') expect(e.value.role).toBe(2);
  });
  it('handles fragmented CRLF and UTF-8, preserving result outcome', async () => {
    const bytes = new TextEncoder().encode((progress + result).replaceAll('\n', '\r\n'));
    const events: StreamEvent[] = [];
    const body = new ReadableStream<Uint8Array>({
      start(c) {
        for (const b of bytes) c.enqueue(new Uint8Array([b]));
        c.close();
      },
    });
    await readEvents(body, (e) => events.push(e), new AbortController().signal);
    expect(events.map((e) => e.type)).toEqual(['progress', 'result']);
    expect(events[1].value).toHaveProperty('outcome', 'Blocked');
  });
  it('treats truncated streams as unknown outcome instead of success', async () => {
    const body = new ReadableStream<Uint8Array>({
      start(c) {
        c.enqueue(new TextEncoder().encode(progress));
        c.close();
      },
    });
    await expect(readEvents(body, () => {}, new AbortController().signal)).rejects.toThrow();
  });
  it('bounds unfinished frames', async () => {
    const body = new ReadableStream<Uint8Array>({
      start(c) {
        c.enqueue(new TextEncoder().encode('x'.repeat(65537)));
      },
    });
    await expect(readEvents(body, () => {}, new AbortController().signal)).rejects.toThrow();
  });
  it('cancels a blocked reader on disconnect', async () => {
    let cancelled = false;
    const controller = new AbortController();
    const body = new ReadableStream<Uint8Array>({
      cancel() {
        cancelled = true;
      },
    });
    const p = readEvents(body, () => {}, controller.signal);
    controller.abort();
    await expect(p).rejects.toThrow();
    expect(cancelled).toBe(true);
  });
  it('rejects mismatched event kind', () =>
    expect(() => decodeEvent('AgentCompleted', progress.split('data: ')[1].trim())).toThrow());
});
describe('Session and required values', () => {
  it('bounds session observations, replaces duplicates and clears on credential change', () => {
    const s = new Session();
    for (let i = 0; i < 50; i++) s.remember('runs', String(i), 'Running');
    expect(s.activity()).toHaveLength(30);
    s.remember('runs', '49', 'Blocked');
    expect(s.activity()).toHaveLength(30);
    expect(s.activity()[0].status).toBe('Blocked');
    s.connect('credential');
    expect(s.activity()).toEqual([]);
    s.clear();
    expect(s.token()).toBe('');
  });
  it('rejects whitespace, empty and malformed IDs', () => {
    expect(requiredText(new FormControl('  '))).not.toBeNull();
    expect(guid(new FormControl('00000000-0000-0000-0000-000000000000'))).not.toBeNull();
    expect(guid(new FormControl('wrong'))).not.toBeNull();
    expect(guid(new FormControl(id))).toBeNull();
  });
});

it('preserves exact fallback citations and structured retry observations', () => {
  const result = decodeEvent(
    'Result',
    JSON.stringify({
      RunId: id,
      ExecutionId: id,
      CorrelationId: id,
      Outcome: 'Degraded',
      WorkOrderId: null,
      Narrative: 'advisory',
      DegradationReason: 'transient_exhausted',
      Citations: [
        {
          DocumentId: id,
          ManualRevisionId: id,
          ChunkId: id,
          Locator: 'section 2',
          Snippet: 'source verbatim',
        },
      ],
    }),
  );
  expect(result?.type).toBe('result');
  if (result?.type === 'result') {
    expect(result.value.citations?.[0].snippet).toBe('source verbatim');
    expect(result.value.degradationReason).toBe('transient_exhausted');
    expect(result.value.workOrderId).toBeNull();
  }
  const retry = decodeEvent(
    'RetryScheduled',
    JSON.stringify({
      ExecutionId: id,
      CorrelationId: id,
      progress: { Kind: 9, RunId: id, Attempt: 2, ReasonCode: 'transient_dependency' },
    }),
  );
  expect(retry?.type).toBe('progress');
  if (retry?.type === 'progress') expect(retry.value.attempt).toBe(2);
});
