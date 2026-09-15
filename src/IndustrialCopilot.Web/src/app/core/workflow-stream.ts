import { LanguageService } from './language';
import { inject, Injectable } from '@angular/core';
import { API_BASE, ApiFailure } from './api';
import { Session } from './session';
import { WorkflowResult } from './contracts';

export interface Progress {
  kind: string;
  runId: string;
  executionId: string;
  correlationId: string;
  role: number | null;
  allowed: boolean | null;
  workOrderId: string | null;
}
export type StreamEvent =
  { type: 'progress'; value: Progress } | { type: 'result'; value: WorkflowResult };
const kinds = [
  'WorkflowStarted',
  'AgentStarted',
  'AgentCompleted',
  'SafetyEvaluated',
  'WorkOrderReady',
  'WaitingForApproval',
  'Blocked',
  'Cancelled',
  'Failed',
];
export function decodeEvent(name: string, data: string): StreamEvent | null {
  const raw: unknown = JSON.parse(data);
  if (!raw || typeof raw !== 'object') throw new ApiFailure(0);
  const v = raw as Record<string, unknown>;
  const str = (o: Record<string, unknown>, k: string): string => {
    if (typeof o[k] !== 'string') throw new ApiFailure(0);
    return o[k];
  };
  if (name === 'Result')
    return {
      type: 'result',
      value: {
        runId: str(v, 'RunId'),
        workOrderId: typeof v['WorkOrderId'] === 'string' ? v['WorkOrderId'] : null,
        executionId: str(v, 'ExecutionId'),
        correlationId: str(v, 'CorrelationId'),
        outcome: str(v, 'Outcome'),
        narrative: typeof v['Narrative'] === 'string' ? v['Narrative'] : null,
      },
    };
  if (!kinds.includes(name)) return null;
  const p = v['progress'];
  if (!p || typeof p !== 'object') throw new ApiFailure(0);
  const q = p as Record<string, unknown>;
  if (q['Kind'] !== kinds.indexOf(name)) throw new ApiFailure(0);
  return {
    type: 'progress',
    value: {
      kind: name,
      runId: str(q, 'RunId'),
      executionId: str(v, 'ExecutionId'),
      correlationId: str(v, 'CorrelationId'),
      role: typeof q['Role'] === 'number' ? q['Role'] : null,
      allowed: typeof q['Allowed'] === 'boolean' ? q['Allowed'] : null,
      workOrderId: typeof q['WorkOrderId'] === 'string' ? q['WorkOrderId'] : null,
    },
  };
}
// Incremental UTF-8 framing, CRLF-safe; no automatic reconnection of this mutating POST.
export async function readEvents(
  body: ReadableStream<Uint8Array>,
  receive: (e: StreamEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  const reader = body.getReader(),
    decoder = new TextDecoder();
  let buffer = '';
  let result = false;
  const abort = (): void => {
    void reader.cancel().catch(() => undefined);
  };
  signal.addEventListener('abort', abort, { once: true });
  try {
    while (true) {
      signal.throwIfAborted();
      const { value, done } = await reader.read();
      signal.throwIfAborted();
      buffer += done ? decoder.decode() : decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n/g, '\n');
      let boundary: number;
      while ((boundary = buffer.indexOf('\n\n')) >= 0) {
        const frame = buffer.slice(0, boundary);
        buffer = buffer.slice(boundary + 2);
        if (frame.length > 65536) throw new ApiFailure(0);
        const lines = frame.split('\n'),
          name =
            lines
              .find((l) => l.startsWith('event:'))
              ?.slice(6)
              .trim() ?? 'message';
        const data = lines
          .filter((l) => l.startsWith('data:'))
          .map((l) => l.slice(5).trimStart())
          .join('\n');
        if (data) {
          const event = decodeEvent(name, data);
          if (event) {
            if (result) continue;
            result = event.type === 'result';
            receive(event);
          }
        }
      }
      if (buffer.length > 65536) throw new ApiFailure(0);
      if (done) {
        if (!result) throw new ApiFailure(0);
        break;
      }
    }
  } finally {
    signal.removeEventListener('abort', abort);
    await reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}
@Injectable({ providedIn: 'root' })
export class WorkflowStream {
  private language = inject(LanguageService);
  private session = inject(Session);
  private base = inject(API_BASE);
  async start(
    equipmentId: string,
    symptom: string,
    receive: (e: StreamEvent) => void,
    signal: AbortSignal,
  ): Promise<void> {
    const response = await fetch(this.base + '/runs/stream', {
      method: 'POST',
      signal,
      redirect: 'error',
      credentials: 'omit',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
        Authorization: `Bearer ${this.session.token()}`,
        ...this.language.requestHeaders(),
        'X-Correlation-ID': crypto.randomUUID(),
      },
      body: JSON.stringify({ equipmentId, symptom }),
    });
    if (
      !response.ok ||
      !response.body ||
      !response.headers.get('Content-Type')?.includes('text/event-stream')
    )
      throw new ApiFailure(response.status, response.headers.get('X-Correlation-ID'));
    await readEvents(response.body, receive, signal);
  }
}
