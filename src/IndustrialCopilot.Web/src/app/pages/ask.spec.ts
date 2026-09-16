import { TestBed } from '@angular/core/testing';
import { it, expect } from 'vitest';
import { AskPage } from './ask';
import { ProductApi, AnswerEvent, Conversation } from '../core/product';
const conversation: Conversation = {
  id: 'c',
  equipmentId: 'e',
  documentId: 'd',
  manualRevisionId: 'r',
  culture: 'en-US',
  createdAt: 'now',
  updatedAt: 'now',
};
it('restores history, displays incremental safe text and cancels on destruction', async () => {
  let receive!: (e: AnswerEvent) => void;
  let signal!: AbortSignal;
  TestBed.configureTestingModule({
    imports: [AskPage],
    providers: [
      {
        provide: ProductApi,
        useValue: {
          list: async () => [conversation],
          history: async () => ({
            conversation,
            turns: [
              { id: 't', question: 'old question', answer: 'old answer', state: 1, citations: [] },
            ],
          }),
          ask: (_id: string, _question: string, r: (e: AnswerEvent) => void, ct: AbortSignal) => {
            receive = r;
            signal = ct;
            return new Promise<void>((_, reject) =>
              ct.addEventListener('abort', () => reject(new Error('cancelled'))),
            );
          },
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(AskPage),
    page = fixture.componentInstance;
  await page.select(conversation);
  page.question = 'pump';
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('old answer');
  const pending = page.ask();
  receive({ type: 'delta', delta: '<script>alert(1)</script>' });
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('script')).toBeNull();
  expect(fixture.nativeElement.textContent).toContain('<script>');
  expect(page.busy()).toBe(true);
  fixture.destroy();
  expect(signal.aborted).toBe(true);
  await pending;
  expect(page.status()).toBe('Cancelled');
});
it('represents insufficient evidence without an answer', async () => {
  TestBed.configureTestingModule({
    imports: [AskPage],
    providers: [
      {
        provide: ProductApi,
        useValue: {
          list: async () => [],
          ask: async (_id: string, _question: string, receive: (e: AnswerEvent) => void) =>
            receive({ type: 'completed', state: 'InsufficientEvidence' }),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(AskPage),
    page = fixture.componentInstance;
  page.current.set(conversation);
  page.question = 'unknown';
  await page.ask();
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Not enough matching evidence');
  expect(page.answer()).toBe('');
});

it('ignores a late history response after selecting a different conversation', async () => {
  let first!: (value: unknown) => void, second!: (value: unknown) => void;
  TestBed.configureTestingModule({imports:[AskPage],providers:[{provide:ProductApi,useValue:{
    list:async()=>[], history:(id:string)=>new Promise(resolve=>{if(id==='first')first=resolve;else second=resolve;})
  }}]});
  const page=TestBed.createComponent(AskPage).componentInstance;
  const a=page.select({...conversation,id:'first'}),b=page.select({...conversation,id:'second'});
  second({turns:[],conversation:{...conversation,id:'second'}});await b;
  first({turns:[{id:'stale'}],conversation:{...conversation,id:'first'}});await a;
  expect(page.current()?.id).toBe('second');expect(page.turns()).toEqual([]);
});
