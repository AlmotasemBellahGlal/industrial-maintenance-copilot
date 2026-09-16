import { describe, it, expect } from 'vitest';
import { readAnswer, AnswerEvent } from './product';
import { validUpload } from '../pages/ingest';
const frame = (event: string, value: unknown) =>
  new TextEncoder().encode(`event: ${event}\ndata: ${JSON.stringify(value)}\n\n`);
describe('request-owned Ask stream', () => {
  it('renders deltas before completion without collecting a full response', async () => {
    let controller!: ReadableStreamDefaultController<Uint8Array>;
    const stream = new ReadableStream<Uint8Array>({
      start(c) {
        controller = c;
      },
    });
    const seen: AnswerEvent[] = [];
    const pending = readAnswer(stream, (e) => seen.push(e), new AbortController().signal);
    controller.enqueue(frame('delta', { delta: 'أول' }));
    await new Promise((r) => setTimeout(r, 0));
    expect(seen.map((e) => e.delta)).toEqual(['أول']);
    controller.enqueue(frame('delta', { delta: 'second' }));
    controller.enqueue(frame('completed', { state: 'Completed' }));
    await pending;
    expect(seen.map((e) => e.type)).toEqual(['delta', 'delta', 'completed']);
  });
  it('cancels the reader and rejects an incomplete stream', async () => {
    const ct = new AbortController();
    let cancelled = false;
    const stream = new ReadableStream<Uint8Array>({
      cancel() {
        cancelled = true;
      },
    });
    const pending = readAnswer(stream, () => {}, ct.signal);
    ct.abort();
    await expect(pending).rejects.toBeDefined();
    expect(cancelled).toBe(true);
    await expect(
      readAnswer(
        new ReadableStream({
          start(c) {
            c.close();
          },
        }),
        () => {},
        new AbortController().signal,
      ),
    ).rejects.toBeDefined();
  });
  it('rejects oversized frames and unknown private events', async () => {
    for (const chunk of [
      new TextEncoder().encode('x'.repeat(262145)),
      frame('reasoning', { text: 'private' }),
    ]) {
      const stream = new ReadableStream<Uint8Array>({
        start(c) {
          c.enqueue(chunk);
          c.close();
        },
      });
      await expect(
        readAnswer(stream, () => {}, new AbortController().signal),
      ).rejects.toBeDefined();
    }
  });
});
it('restricts upload size, type and path-like names before HTTP', () => {
  expect(validUpload(new File(['pump'], 'manual.txt'))).toBe(true);
  expect(validUpload(new File(['pdf'], 'manual.pdf'))).toBe(true);
  expect(validUpload(new File(['x'], 'a.exe'))).toBe(false);
  expect(validUpload(new File(['x'], '../manual.txt'))).toBe(false);
  expect(validUpload(new File([], 'empty.txt'))).toBe(false);
  expect(validUpload({ name: 'large.pdf', size: 16000001 } as File)).toBe(false);
});
