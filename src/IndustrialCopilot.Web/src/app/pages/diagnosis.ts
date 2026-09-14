import { Component, inject, OnDestroy, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { WorkflowStream, Progress, StreamEvent } from '../core/workflow-stream';
import { WorkflowResult } from '../core/contracts';
import { Session } from '../core/session';
import { failure } from '../core/api';
import { focusInvalid, guid, requiredText, Status } from '../shared/ui';
@Component({
  imports: [ReactiveFormsModule, RouterLink, Status],
  template: ` <p class="eyebrow">Technician workspace</p>
    <h1>New diagnosis</h1>
    <p class="lede">Describe the symptom. Follow the grounded workflow to a reviewable proposal.</p>
    @if (!session.connected()) {
      <div class="notice">
        A host credential is required. <a routerLink="/connection">Configure connection</a>
      </div>
    }
    <div class="detail-grid">
      <section class="panel">
        <h2>Equipment & symptom</h2>
        <form [formGroup]="form" (ngSubmit)="start()">
          <fieldset [disabled]="busy()">
            <label for="equipment">Equipment ID</label
            ><input
              id="equipment"
              formControlName="equipmentId"
              aria-describedby="equipment-help"
              [attr.aria-invalid]="
                form.controls.equipmentId.touched && form.controls.equipmentId.invalid
              "
            />
            <p id="equipment-help" class="muted">
              Use a supported equipment UUID from your deployment's reviewed procedure
              configuration.
            </p>
            @if (form.controls.equipmentId.touched && form.controls.equipmentId.invalid) {
              <p class="field-error" role="alert">Enter a nonempty equipment UUID.</p>
            }
            <label for="symptom">Reported symptom</label
            ><textarea
              id="symptom"
              rows="6"
              formControlName="symptom"
              maxlength="2000"
              placeholder="Describe the observed condition, operating context and changes."
              [attr.aria-invalid]="form.controls.symptom.touched && form.controls.symptom.invalid"
            ></textarea>
            @if (form.controls.symptom.touched && form.controls.symptom.invalid) {
              <p class="field-error" role="alert">Describe the symptom using 1–2,000 characters.</p>
            }
            <button class="primary" type="submit" [disabled]="busy() || !session.connected()">
              {{ busy() ? 'Workflow running…' : 'Start grounded diagnosis' }}
            </button>
          </fieldset>
        </form>
        @if (busy()) {
          <button class="danger" (click)="stop()">Stop listening & request cancellation</button>
        }
        <p class="muted">
          Disconnecting requests cancellation; it does not prove durable execution stopped. Inspect
          any known run before starting again.
        </p>
      </section>
      <section class="panel">
        <div class="section-heading">
          <h2>Live workflow progress</h2>
          <app-status [text]="busy() ? 'Running' : (result()?.outcome ?? 'Ready')" />
        </div>
        <p class="muted">
          Safe execution events only. No hidden reasoning or estimated percentage.
        </p>
        <p role="status">{{ message() }}</p>
        @if (events().length) {
          <ol class="timeline">
            @for (event of events(); track event.kind + ':' + event.role) {
              <li>
                <strong>{{ label(event) }}</strong
                ><span class="muted">{{ event.kind }}</span>
              </li>
            }
          </ol>
        } @else {
          <div class="empty"><p>Progress will appear when the host starts the workflow.</p></div>
        }
        @if (runId()) {
          <a class="button" [routerLink]="['/runs', runId()]">Inspect run</a>
        }
        @if (result()?.workOrderId) {
          <a class="button primary" [routerLink]="['/work-orders', result()!.workOrderId]"
            >Review proposed work order</a
          >
        }
        @if (executionId()) {
          <p><a [routerLink]="['/traces', executionId()]">Inspect safe trace</a></p>
        }
        @if (correlation()) {
          <p class="muted identifier">Correlation {{ correlation() }}</p>
        }
      </section>
    </div>`,
})
export class Diagnosis implements OnDestroy {
  readonly session = inject(Session);
  private stream = inject(WorkflowStream);
  private controller?: AbortController;
  form = new FormGroup({
    equipmentId: new FormControl('', { nonNullable: true, validators: [guid] }),
    symptom: new FormControl('', {
      nonNullable: true,
      validators: [requiredText, Validators.maxLength(2000)],
    }),
  });
  busy = signal(false);
  events = signal<Progress[]>([]);
  result = signal<WorkflowResult | null>(null);
  message = signal('');
  runId = signal('');
  executionId = signal('');
  correlation = signal('');
  async start(): Promise<void> {
    if (this.busy()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      focusInvalid();
      return;
    }
    this.controller = new AbortController();
    this.busy.set(true);
    this.events.set([]);
    this.result.set(null);
    this.runId.set('');
    this.executionId.set('');
    this.correlation.set('');
    this.message.set('Connecting to the workflow…');
    try {
      await this.stream.start(
        this.form.getRawValue().equipmentId,
        this.form.getRawValue().symptom,
        (e) => this.receive(e),
        this.controller.signal,
      );
    } catch (e) {
      this.message.set(
        this.controller.signal.aborted
          ? 'Disconnected; cancellation requested. Inspect the run to confirm its durable state.'
          : failure(e) +
              ' This stream cannot be resumed. Inspect the known run; do not automatically restart.',
      );
    } finally {
      this.busy.set(false);
    }
  }
  receive(event: StreamEvent): void {
    const v = event.value;
    this.runId.set(v.runId);
    this.executionId.set(v.executionId);
    this.correlation.set(v.correlationId);
    if (event.type === 'result') {
      this.result.set(event.value);
      this.message.set(
        event.value.outcome === 'Proposed'
          ? 'Proposal ready for human review. No dispatch has been authorized.'
          : `Workflow outcome: ${event.value.outcome}. Inspect the durable run for details.`,
      );
      this.session.remember('runs', v.runId, event.value.outcome);
      if (v.workOrderId)
        this.session.remember('work-orders', v.workOrderId, 'Proposal — review required');
    } else {
      this.events.update((items) =>
        items.some((i) => i.kind === event.value.kind && i.role === event.value.role)
          ? items
          : [...items, event.value].slice(-100),
      );
      this.message.set(this.label(event.value));
      this.session.remember('runs', v.runId, event.value.kind);
    }
  }
  label(e: Progress): string {
    const roles: Record<number, string> = {
      1: 'Symptom Matcher',
      2: 'Diagnostic & Safety Planner',
      3: 'Work Order Generator',
    };
    return e.kind === 'SafetyEvaluated'
      ? e.allowed
        ? 'Trusted safety policy accepted the proposed scope'
        : 'Trusted safety policy blocked continuation'
      : e.role
        ? `${roles[e.role] ?? 'Agent'} — ${e.kind === 'AgentStarted' ? 'started' : 'completed'}`
        : e.kind.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
  stop(): void {
    this.controller?.abort();
  }
  ngOnDestroy(): void {
    this.stop();
  }
}
