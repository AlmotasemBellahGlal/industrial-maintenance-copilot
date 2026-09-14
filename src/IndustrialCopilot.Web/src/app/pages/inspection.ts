import { Component, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest } from 'rxjs';
import { DispatchApi, WorkflowApi, failure } from '../core/api';
import { Dispatch, RecordKind, Run, Trace } from '../core/contracts';
import { Session } from '../core/session';
import { guid, focusInvalid, Status } from '../shared/ui';
@Component({
  imports: [ReactiveFormsModule, RouterLink, DatePipe, Status],
  template: ` <p class="eyebrow">Record inspection</p>
    <h1>{{ title() }}</h1>
    <p class="lede">Open a known identifier. The host checks your access on every request.</p>
    <section class="panel">
      <form (submit)="$event.preventDefault(); open()" class="lookup">
        <div>
          <label for="lookup">{{ title() }} ID</label
          ><input id="lookup" [formControl]="id" [attr.aria-invalid]="id.touched && id.invalid" />
          @if (id.touched && id.invalid) {
            <p class="field-error" role="alert">Enter a nonempty UUID.</p>
          }
        </div>
        <button class="primary" [disabled]="busy()">Open record</button>
      </form>
      @if (error()) {
        <div class="notice danger" role="alert">
          {{ error() }} <a routerLink="/connection">Connection settings</a>
        </div>
      }
      @if (busy()) {
        <p role="status">Loading current server record…</p>
      }
      @if (!run() && !dispatch() && !trace() && !busy()) {
        <p class="muted">
          No record loaded. There is no global inventory endpoint; use a known ID or session
          activity below.
        </p>
      }
    </section>
    @if (run(); as r) {
      <section class="panel">
        <div class="section-heading">
          <h2>Maintenance run</h2>
          <app-status [text]="r.status" />
        </div>
        <dl>
          <dt>Run</dt>
          <dd>{{ r.runId }}</dd>
          <dt>Equipment</dt>
          <dd>{{ r.equipmentId }}</dd>
          <dt>Reported symptom</dt>
          <dd>{{ r.symptom }}</dd>
          <dt>Cancellation intent</dt>
          <dd>
            {{
              r.cancellationRequested
                ? 'Requested — inspect lifecycle status for acknowledgement'
                : 'Not requested'
            }}
          </dd>
        </dl>
        <h3>Work orders</h3>
        @for (id of r.workOrderIds; track id) {
          <p>
            <a [routerLink]="['/work-orders', id]">{{ id }}</a>
          </p>
        } @empty {
          <p>No work order published.</p>
        }
        <h3>Execution traces</h3>
        @for (id of r.executionIds; track id) {
          <p>
            <a [routerLink]="['/traces', id]">{{ id }}</a>
          </p>
        } @empty {
          <p>No accessible trace linked.</p>
        }
        <button (click)="load(r.runId)" [disabled]="busy()">Refresh run</button>
      </section>
    }
    @if (dispatch(); as d) {
      <section class="panel">
        <div class="section-heading">
          <h2>External dispatch</h2>
          <app-status [text]="d.state ?? d.outcome" />
        </div>
        <p class="notice">{{ dispatchExplanation(d) }}</p>
        <dl>
          <dt>Attempt</dt>
          <dd>{{ d.attemptId }}</dd>
          <dt>Revision</dt>
          <dd>{{ d.revision }}</dd>
          <dt>External reference</dt>
          <dd>{{ d.externalReference ?? 'Not confirmed' }}</dd>
          <dt>Gate outcome</dt>
          <dd>{{ d.outcome }}</dd>
        </dl>
        <div class="actions">
          <button (click)="load(d.attemptId!)" [disabled]="busy()">Refresh dispatch status</button>
          @if (d.workOrderId) {
            <a [routerLink]="['/work-orders', d.workOrderId]">Inspect work order</a>
          }
        </div>
        <p class="muted">
          Refresh only queries the existing attempt. It never sends another dispatch.
        </p>
      </section>
    }
    @if (trace(); as t) {
      <section class="panel">
        <h2>Execution activity</h2>
        <dl>
          <dt>Execution</dt>
          <dd>{{ t.executionId }}</dd>
          <dt>Correlation</dt>
          <dd>{{ t.correlationId }}</dd>
        </dl>
        <p class="muted">
          Safe stage metadata from the host. Token/cost details and hidden reasoning are not exposed
          by this endpoint.
        </p>
        <ol class="timeline">
          @for (s of t.steps; track $index) {
            <li>
              <div class="section-heading">
                <strong>{{ s.name }}</strong
                ><app-status [text]="s.status" />
              </div>
              <span class="muted">{{ s.kind }} · {{ s.startedAt | date: 'medium' }}</span>
              @if (s.error) {
                <p class="field-error">Code: {{ s.error }}</p>
              }
            </li>
          } @empty {
            <li>No recorded steps.</li>
          }
        </ol>
      </section>
    }
    <section class="panel">
      <h2>Known in this session</h2>
      <ul class="record-list">
        @for (item of session.activity(); track item.kind + item.id) {
          @if (item.kind === kind()) {
            <li>
              <a [routerLink]="['/', item.kind, item.id]">{{ item.id }}</a
              ><app-status [text]="item.status" />
            </li>
          }
        }
      </ul>
      <p class="muted">
        Visit records or start a diagnosis to populate this list. These are observations, not live
        server totals.
      </p>
    </section>`,
})
export class Inspection implements OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private workflow = inject(WorkflowApi);
  private deliveries = inject(DispatchApi);
  readonly session = inject(Session);
  kind = signal<RecordKind>('runs');
  id = new FormControl('', { nonNullable: true, validators: [guid] });
  run = signal<Run | null>(null);
  dispatch = signal<Dispatch | null>(null);
  trace = signal<Trace | null>(null);
  error = signal('');
  busy = signal(false);
  private generation = 0;
  constructor() {
    combineLatest([this.route.data, this.route.paramMap])
      .pipe(takeUntilDestroyed())
      .subscribe(([data, params]) => {
        this.kind.set(data['kind'] as RecordKind);
        this.clear();
        const id = params.get('id');
        if (id) {
          this.id.setValue(id);
          void this.load(id);
        }
      });
  }
  title(): string {
    return {
      runs: 'Maintenance runs',
      'work-orders': 'Work orders',
      dispatch: 'Dispatch',
      traces: 'Trace / activity',
    }[this.kind()];
  }
  open(): void {
    this.id.markAsTouched();
    if (this.id.invalid) {
      focusInvalid();
      return;
    }
    void this.router.navigate(['/', this.kind(), this.id.value]);
  }
  private clear(): void {
    this.generation++;
    this.run.set(null);
    this.dispatch.set(null);
    this.trace.set(null);
    this.error.set('');
    this.busy.set(false);
  }
  async load(id: string): Promise<void> {
    if (this.id.invalid) {
      this.error.set('Enter a valid record UUID.');
      return;
    }
    const generation = ++this.generation;
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.kind() === 'runs') {
        const r = await this.workflow.run(id);
        if (generation !== this.generation) return;
        this.run.set(r);
        this.session.remember('runs', id, r.status);
      } else if (this.kind() === 'dispatch') {
        const d = await this.deliveries.get(id);
        if (generation !== this.generation) return;
        this.dispatch.set(d);
        this.session.remember('dispatch', id, d.state ?? d.outcome);
      } else if (this.kind() === 'traces') {
        const t = await this.workflow.trace(id);
        if (generation !== this.generation) return;
        this.trace.set(t);
        this.session.remember('traces', id, 'Inspected');
      }
    } catch (e) {
      if (generation === this.generation) this.error.set(failure(e));
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  dispatchExplanation(d: Dispatch): string {
    return d.state === 'Uncertain'
      ? 'External outcome is uncertain. This is neither success nor failure. The Worker reconciles the existing attempt; do not submit another delivery.'
      : d.state === 'Pending'
        ? 'Delivery remains pending. The Worker can inspect unresolved attempts using the same durable key.'
        : d.state === 'Confirmed'
          ? 'External acceptance confirmed by the trusted host.'
          : d.state === 'DefinitivelyFailed'
            ? 'External delivery definitively failed. No automatic browser retry will be made.'
            : 'Inspect the gate outcome. Delivery has not been confirmed.';
  }
  ngOnDestroy(): void {
    this.generation++;
  }
}
