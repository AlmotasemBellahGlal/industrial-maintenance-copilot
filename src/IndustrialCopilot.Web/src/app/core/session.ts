import { Injectable, signal } from '@angular/core';
import { Activity, RecordKind } from './contracts';

@Injectable({ providedIn: 'root' })
export class Session {
  // Deliberately memory-only: neither bearer credentials nor maintenance data survive reload.
  private credential = '';
  readonly connected = signal(false);
  readonly activity = signal<Activity[]>([]);
  token(): string {
    return this.credential;
  }
  connect(token: string): void {
    this.clear();
    this.credential = token.trim();
    this.connected.set(!!this.credential);
  }
  clear(): void {
    this.credential = '';
    this.connected.set(false);
    this.activity.set([]);
  }
  remember(kind: RecordKind, id: string, status: string): void {
    this.activity.update((items) =>
      [
        { kind, id, status, observedAt: new Date().toISOString() },
        ...items.filter((i) => i.kind !== kind || i.id !== id),
      ].slice(0, 30),
    );
  }
}
