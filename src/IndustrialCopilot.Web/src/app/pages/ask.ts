import { Component, inject, signal, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../core/language';
import { ProductApi, Conversation, Turn, Citation } from '../core/product';
import { failure } from '../core/api';
@Component({ imports: [FormsModule, TranslatePipe], templateUrl: './ask.html' })
export class AskPage implements OnDestroy {
  private api = inject(ProductApi);
  private abort?: AbortController;
  private selection = 0;
  private listing = 0;
  loading = signal(false);
  conversations = signal<Conversation[]>([]);
  current = signal<Conversation | null>(null);
  turns = signal<Turn[]>([]);
  busy = signal(false);
  error = signal('');
  answer = signal('');
  citations = signal<Citation[]>([]);
  status = signal('');
  correlation = signal('');
  equipment = '';
  document = '';
  revision = '';
  question = '';
  offset = 0;
  moreTurns = signal(false);
  constructor() {
    void this.list();
  }
  async list(offset = 0): Promise<void> {
    const version = ++this.listing;
    try {
      const rows = await this.api.list(offset);
      if (version !== this.listing) return;
      this.conversations.set(rows);
      this.offset = offset;
      this.error.set('');
    } catch (e) {
      if (version === this.listing) this.error.set(failure(e));
    }
  }
  async select(c: Conversation): Promise<void> {
    if (this.busy()) return;
    const version = ++this.selection;
    this.loading.set(true);
    try {
      const h = await this.api.history(c.id);
      if (version !== this.selection) return;
      this.current.set(c);
      this.turns.set(h.turns);
      this.moreTurns.set(h.turns.length === 20);
      this.answer.set('');
      this.citations.set([]);
      this.status.set('');
      this.correlation.set('');
      this.error.set('');
    } catch (e) {
      if (version === this.selection) this.error.set(failure(e));
    } finally {
      if (version === this.selection) this.loading.set(false);
    }
  }
  async more(): Promise<void> {
    const c = this.current();
    if (!c || this.loading()) return;
    const version = this.selection;
    this.loading.set(true);
    try {
      const h = await this.api.history(c.id, this.turns().at(-1)?.sequence ?? 0);
      if (version !== this.selection) return;
      this.turns.update((t) => [...t, ...h.turns]);
      this.moreTurns.set(h.turns.length === 20);
    } catch (e) {
      if (version === this.selection) this.error.set(failure(e));
    } finally {
      if (version === this.selection) this.loading.set(false);
    }
  }
  async create(): Promise<void> {
    try {
      const c = await this.api.create(this.equipment, this.document, this.revision);
      await this.list();
      await this.select(c);
    } catch (e) {
      this.error.set(failure(e));
    }
  }
  async ask(): Promise<void> {
    const c = this.current();
    if (!c || this.busy() || this.loading() || !this.question.trim()) return;
    this.busy.set(true);
    this.error.set('');
    this.answer.set('');
    this.citations.set([]);
    this.status.set('Streaming');
    this.abort = new AbortController();
    try {
      await this.api.ask(
        c.id,
        this.question,
        (e) => {
          if (e.delta) this.answer.update((a) => a + e.delta);
          if (e.citations) this.citations.set(e.citations);
          if (e.state) this.status.set(e.state);
          if (e.correlationId) this.correlation.set(e.correlationId);
        },
        this.abort.signal,
      );
    } catch (e) {
      if (this.abort.signal.aborted) this.status.set('Cancelled');
      else {
        this.status.set('Failed');
        this.error.set(failure(e));
      }
    } finally {
      this.busy.set(false);
      this.abort = undefined;
    }
  }
  cancel(): void {
    this.abort?.abort();
  }
  state(n: number): string {
    return ['Streaming', 'Completed', 'InsufficientEvidence', 'Cancelled', 'Failed'][n] ?? 'Failed';
  }
  ngOnDestroy(): void {
    this.selection++;
    this.listing++;
    this.cancel();
  }
}
