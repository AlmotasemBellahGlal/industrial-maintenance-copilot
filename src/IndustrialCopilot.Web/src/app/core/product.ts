import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE, ApiFailure } from './api';
import { Session } from './session';
import { LanguageService } from './language';
export interface Conversation {
  id: string;
  equipmentId: string;
  documentId: string;
  manualRevisionId: string;
  culture: string;
  createdAt: string;
  updatedAt: string;
}
export interface Citation {
  documentId: string;
  manualRevisionId: string;
  chunkId: string;
  locator: string;
  snippet: string;
}
export interface Turn {
  id: string;
  sequence: number;
  question: string;
  answer: string;
  state: number;
  citations: Citation[];
  correlationId: string;
}
export interface History {
  conversation: Conversation;
  turns: Turn[];
}
export interface Ingestion {
  attemptId: string;
  state: number;
  stage: number;
  pages: number | null;
  chunks: number | null;
  failure: number | null;
}
export interface AnswerEvent {
  type: string;
  delta?: string;
  citations?: Citation[];
  state?: string;
  correlationId?: string;
}
export async function readAnswer(
  body: ReadableStream<Uint8Array>,
  receive: (event: AnswerEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  const reader = body.getReader(),
    decoder = new TextDecoder();
  let buffer = '',
    completed = false,
    total = 0;
  const abort = (): void => {
    void reader.cancel().catch(() => undefined);
  };
  signal.addEventListener('abort', abort, { once: true });
  try {
    while (!completed) {
      signal.throwIfAborted();
      const { value, done } = await reader.read();
      signal.throwIfAborted();
      buffer += done ? decoder.decode() : decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n/g, '\n');
      let end: number;
      while ((end = buffer.indexOf('\n\n')) >= 0) {
        const frame = buffer.slice(0, end);
        buffer = buffer.slice(end + 2);
        if (frame.length > 262144) throw new ApiFailure(0);
        const type = frame
          .split('\n')
          .find((l) => l.startsWith('event:'))
          ?.slice(6)
          .trim();
        const data = frame
          .split('\n')
          .filter((l) => l.startsWith('data:'))
          .map((l) => l.slice(5).trimStart())
          .join('\n');
        if (!type || !data) continue;
        if (!['meta', 'evidence', 'delta', 'citation', 'completed', 'error'].includes(type))
          throw new ApiFailure(0);
        const raw = JSON.parse(data) as {
          delta?: unknown;
          citations?: Citation[];
          state?: string;
          correlationId?: string;
        };
        if (type === 'error') throw new ApiFailure(503, raw.correlationId ?? null);
        if (type === 'delta') {
          if (typeof raw.delta !== 'string') throw new ApiFailure(0);
          total += raw.delta.length;
          if (total > 32000) throw new ApiFailure(0);
        }
        receive({ type, ...raw, delta: typeof raw.delta === 'string' ? raw.delta : undefined });
        if (type === 'completed') {
          completed = true;
          break;
        }
      }
      if (buffer.length > 262144) throw new ApiFailure(0);
      if (done && !completed) throw new ApiFailure(0);
    }
  } finally {
    signal.removeEventListener('abort', abort);
    await reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}
@Injectable({ providedIn: 'root' })
export class ProductApi {
  private http = inject(HttpClient);
  private base = inject(API_BASE);
  private session = inject(Session);
  private language = inject(LanguageService);
  list(offset = 0): Promise<Conversation[]> {
    return firstValueFrom(
      this.http.get<Conversation[]>(`${this.base}/conversations?offset=${offset}&limit=20`),
    );
  }
  history(id: string, after = 0): Promise<History> {
    return firstValueFrom(
      this.http.get<History>(`${this.base}/conversations/${id}?after=${after}&limit=20`),
    );
  }
  create(equipmentId: string, documentId: string, manualRevisionId: string): Promise<Conversation> {
    return firstValueFrom(
      this.http.post<Conversation>(this.base + '/conversations', {
        equipmentId,
        documentId,
        manualRevisionId,
      }),
    );
  }
  async ask(
    id: string,
    question: string,
    receive: (event: AnswerEvent) => void,
    signal: AbortSignal,
  ): Promise<void> {
    const response = await fetch(`${this.base}/conversations/${id}/ask`, {
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
      body: JSON.stringify({ question }),
    });
    if (
      !response.ok ||
      !response.body ||
      !response.headers.get('Content-Type')?.includes('text/event-stream')
    )
      throw new ApiFailure(response.status);
    await readAnswer(response.body, receive, signal);
  }
  upload(
    equipment: string,
    document: string,
    revision: string,
    title: string,
    number: number,
    file: File,
  ): Promise<Ingestion[]> {
    const q = new URLSearchParams({
      equipmentId: equipment,
      filename: file.name,
      title,
      revisionNumber: String(number),
    });
    return firstValueFrom(
      this.http.post<Ingestion[]>(
        `${this.base}/documents/${document}/revisions/${revision}/ingest?${q}`,
        file,
        {
          headers: {
            'Content-Type': file.name.toLowerCase().endsWith('.pdf')
              ? 'application/pdf'
              : 'text/plain',
          },
        },
      ),
    );
  }
  ingestion(equipment: string, document: string, revision: string): Promise<Ingestion[]> {
    return firstValueFrom(
      this.http.get<Ingestion[]>(
        `${this.base}/documents/${document}/revisions/${revision}/ingestion?equipmentId=${equipment}`,
      ),
    );
  }
}
