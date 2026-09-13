import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { expect, it } from 'vitest';
import { Diagnosis } from './diagnosis';
import { WorkflowStream, StreamEvent } from '../core/workflow-stream';
it('bounds duplicate progress and aborts the active request when the page is destroyed', async () => {
  let signal: AbortSignal | undefined;
  TestBed.configureTestingModule({
    imports: [Diagnosis],
    providers: [
      provideRouter([]),
      {
        provide: WorkflowStream,
        useValue: {
          start: (
            _equipment: string,
            _symptom: string,
            receive: (e: StreamEvent) => void,
            ct: AbortSignal,
          ) => {
            signal = ct;
            for (let i = 0; i < 150; i++)
              receive({
                type: 'progress',
                value: {
                  kind: 'AgentStarted',
                  runId: 'r',
                  executionId: 'e',
                  correlationId: 'c',
                  role: i,
                  allowed: null,
                  workOrderId: null,
                },
              });
            receive({
              type: 'progress',
              value: {
                kind: 'AgentStarted',
                runId: 'r',
                executionId: 'e',
                correlationId: 'c',
                role: 149,
                allowed: null,
                workOrderId: null,
              },
            });
            return new Promise<void>((_, reject) =>
              ct.addEventListener(
                'abort',
                () => reject(new DOMException('Aborted', 'AbortError')),
                { once: true },
              ),
            );
          },
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(Diagnosis),
    page = fixture.componentInstance;
  page.form.setValue({ equipmentId: '11111111-1111-1111-1111-111111111111', symptom: 'noise' });
  const running = page.start();
  expect(page.events()).toHaveLength(100);
  expect(page.busy()).toBe(true);
  fixture.destroy();
  await running;
  expect(signal?.aborted).toBe(true);
  expect(page.busy()).toBe(false);
  expect(page.result()).toBeNull();
  expect(page.message()).toContain('Inspect the run');
});
