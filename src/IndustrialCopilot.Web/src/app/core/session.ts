import { Injectable, signal } from '@angular/core';
import { Activity, RecordKind } from './contracts';

@Injectable({ providedIn: 'root' })
export class Session {
  // Deliberately memory-only: neither bearer credentials nor maintenance data survive reload.
  private credential = '';
  readonly connected = signal(false);
  readonly identity = signal<{
    actor: string;
    role: string;
    permissions: string[];
    equipmentIds: string[];
  } | null>(null);
  can(permission: string): boolean {
    return this.identity()?.permissions.includes(permission) === true;
  }
  async identify(): Promise<void> {
    const token = this.credential;
    try {
      const response = await fetch('/api/identity', {
        headers: { Authorization: `Bearer ${token}` },
        redirect: 'error',
        credentials: 'omit',
      });
      if (!response.ok) throw new Error();
      const value = await response.json();
      if (this.credential === token) this.identity.set(value);
    } catch {
      if (this.credential === token) this.identity.set(null);
    }
  }
  /** Whether this host offers anonymous temporary demo sessions (synthetic data only). */
  async demoAvailable(): Promise<number | null> {
    try {
      const response = await fetch('/api/demo/session', { redirect: 'error', credentials: 'omit' });
      if (!response.ok) return null;
      const value = await response.json();
      return value?.enabled === true && Number.isFinite(value.lifetimeSeconds)
        ? value.lifetimeSeconds
        : null;
    } catch {
      return null;
    }
  }
  /** Asks the server for a temporary session. The token lives in memory only; reload discards it. */
  async startDemo(): Promise<{ ok: true; expiresAt: string } | { ok: false; reason: string }> {
    try {
      const response = await fetch('/api/demo/session', {
        method: 'POST',
        redirect: 'error',
        credentials: 'omit',
      });
      if (response.status === 429) return { ok: false, reason: 'limit' };
      if (response.status === 503) return { ok: false, reason: 'full' };
      if (!response.ok) return { ok: false, reason: 'error' };
      const value = await response.json();
      if (typeof value?.token !== 'string' || typeof value?.expiresAt !== 'string')
        return { ok: false, reason: 'error' };
      this.connect(value.token);
      return { ok: true, expiresAt: value.expiresAt };
    } catch {
      return { ok: false, reason: 'error' };
    }
  }
  readonly activity = signal<Activity[]>([]);
  token(): string {
    return this.credential;
  }
  connect(token: string): void {
    this.clear();
    this.credential = token.trim();
    this.connected.set(!!this.credential);
    void this.identify();
  }
  clear(): void {
    this.credential = '';
    this.identity.set(null);
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
