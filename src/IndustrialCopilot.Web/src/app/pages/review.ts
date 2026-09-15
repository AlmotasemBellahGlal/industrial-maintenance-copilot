import { TranslatePipe, UiText, phrase } from '../core/language';
import { Component, inject, signal, viewChild, OnDestroy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReviewApi, DispatchApi, failure } from '../core/api';
import { Content, Dispatch, Evidence, Preview, Review } from '../core/contracts';
import { Session } from '../core/session';
import { Confirm, focusInvalid, guid, requiredText, Status } from '../shared/ui';

@Component({
  imports: [TranslatePipe, ReactiveFormsModule, RouterLink, Confirm, Status],
  templateUrl: './review.html',
})
export class ReviewPage implements OnDestroy {
  private route = inject(ActivatedRoute);
  private api = inject(ReviewApi);
  private deliveries = inject(DispatchApi);
  private session = inject(Session);
  private confirmation = viewChild(Confirm);
  private id = '';
  private generation = 0;
  review = signal<Review | null>(null);
  evidence = signal<Evidence | null>(null);
  evidenceError = signal('');
  error = signal('');
  message = signal<UiText>('');
  busy = signal(false);
  editing = signal(false);
  stale = signal(false);
  preview = signal<Preview | null>(null);
  dispatch = signal<Dispatch | null>(null);
  dispatchRequested = signal(false);
  private reviewedContent: Content | null = null;
  readonly form = new FormGroup({
    manualId: new FormControl('', { nonNullable: true, validators: [guid] }),
    manualRevisionId: new FormControl('', { nonNullable: true, validators: [guid] }),
    reportedSymptom: new FormControl('', {
      nonNullable: true,
      validators: [requiredText, Validators.maxLength(2000)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [requiredText, Validators.maxLength(4000)],
    }),
    actions: new FormArray<FormControl<string>>([]),
  });
  readonly verification = new FormGroup({
    prerequisiteId: new FormControl('', { nonNullable: true, validators: [guid] }),
    evidence: new FormControl('', {
      nonNullable: true,
      validators: [requiredText, Validators.maxLength(4000)],
    }),
    satisfied: new FormControl(false, { nonNullable: true }),
  });
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.id = params.get('id') ?? '';
      this.review.set(null);
      this.dispatch.set(null);
      this.dispatchRequested.set(false);
      void this.load();
    });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.invalidatePreview());
  }
  invalidatePreview(): void {
    this.preview.set(null);
    this.reviewedContent = null;
  }
  async load(): Promise<void> {
    const generation = ++this.generation;
    this.confirmation()?.finish(false);
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.evidenceError.set('');
    this.evidence.set(null);
    this.editing.set(false);
    this.invalidatePreview();
    try {
      const r = await this.api.get(this.id);
      if (generation !== this.generation) return;
      this.install(r);
      this.stale.set(false);
      try {
        const evidence = await this.api.evidence(this.id);
        if (generation === this.generation) this.evidence.set(evidence);
      } catch (e) {
        if (generation === this.generation) this.evidenceError.set(failure(e));
      }
    } catch (e) {
      if (generation === this.generation) {
        this.error.set(failure(e));
        this.stale.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  private install(r: Review): void {
    this.review.set(r);
    this.session.remember('work-orders', r.workOrderId, r.status);
    this.verification.reset();
  }
  edit(): void {
    const r = this.review();
    if (!r || this.busy()) return;
    this.form.patchValue({
      manualId: r.content.manualId,
      manualRevisionId: r.content.manualRevisionId,
      reportedSymptom: r.content.reportedSymptom,
      description: r.content.description,
    });
    this.form.controls.actions.clear();
    for (const action of r.content.actions) this.addAction(action.instruction);
    this.editing.set(true);
    this.invalidatePreview();
  }
  addAction(value = ''): void {
    if (this.form.controls.actions.length < 32)
      this.form.controls.actions.push(
        new FormControl(value, {
          nonNullable: true,
          validators: [requiredText, Validators.maxLength(2000)],
        }),
      );
  }
  removeAction(index: number): void {
    if (this.form.controls.actions.length > 1) this.form.controls.actions.removeAt(index);
  }
  cancelEdit(): void {
    this.editing.set(false);
    this.invalidatePreview();
  }
  content(): Content {
    const r = this.review()!;
    const v = this.form.getRawValue();
    return {
      equipmentId: r.content.equipmentId,
      manualId: v.manualId,
      manualRevisionId: v.manualRevisionId,
      reportedSymptom: v.reportedSymptom,
      description: v.description,
      actions: v.actions.map((instruction, i) => ({ order: i + 1, instruction })),
    };
  }
  async assess(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || !this.form.controls.actions.length) {
      focusInvalid();
      return;
    }
    if (this.busy() || this.stale()) return;
    const generation = this.generation;
    this.busy.set(true);
    this.error.set('');
    this.invalidatePreview();
    const content = this.content();
    try {
      const p = await this.api.preview(this.id, content);
      if (generation !== this.generation) return;
      if (p.canProceed) {
        this.preview.set(p);
        this.reviewedContent = structuredClone(content);
      } else
        this.error.set(
          'Trusted safety policy blocked this edited scope. No approval was recorded.',
        );
    } catch (e) {
      if (generation === this.generation) this.error.set(failure(e));
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async decide(decision: 'Approve' | 'Reject' | 'EditAndApprove'): Promise<void> {
    const r = this.review();
    if (!r || this.busy() || this.stale()) return;
    const p = this.preview(),
      content = this.reviewedContent;
    if (decision === 'EditAndApprove' && (!p || !content)) return;
    const generation = this.generation;
    this.busy.set(true);
    const detail =
      decision === 'EditAndApprove'
        ? phrase(
            'Approve the displayed edited scope and authoritative requirements as the final revision after revision {0}. Old safety verifications do not transfer.',
            r.target.revision,
          )
        : phrase(
            decision === 'Approve'
              ? 'Approve work order revision {0}: {1}. Approval does not verify safety or dispatch the work order.'
              : 'Reject work order revision {0}: {1}. No dispatch is authorized.',
            r.target.revision,
            r.content.description,
          );
    try {
      if (
        !(await this.confirmation()!.ask(
          decision === 'EditAndApprove'
            ? 'Approve final edited scope?'
            : phrase(
                decision === 'Approve' ? 'Approve revision {0}?' : 'Reject revision {0}?',
                r.target.revision,
              ),
          detail,
          decision === 'EditAndApprove'
            ? 'Confirm edit & approval'
            : decision === 'Approve'
              ? 'Confirm approve'
              : 'Confirm reject',
        )) ||
        generation !== this.generation
      )
        return;
      this.error.set('');
      const result = await this.api.decide(
        r.workOrderId,
        r.target,
        decision,
        decision === 'EditAndApprove' ? (content ?? undefined) : undefined,
        decision === 'EditAndApprove' ? p?.requirements : undefined,
      );
      if (generation !== this.generation) return;
      if (result.review) this.install(result.review);
      else {
        this.stale.set(true);
        this.message.set('Decision returned no current review. Reload before continuing.');
        return;
      }
      this.cancelEdit();
      this.message.set(
        phrase(
          'Server decision: {0}. Review the resulting revision and safety state below.',
          result.outcome,
        ),
      );
    } catch (e) {
      if (generation === this.generation) {
        this.error.set(failure(e));
        this.stale.set(true);
        this.invalidatePreview();
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async submit(): Promise<void> {
    const r = this.review();
    if (!r || this.busy() || this.stale()) return;
    const generation = this.generation;
    this.busy.set(true);
    try {
      const result = await this.api.submit(r.workOrderId, r.target);
      if (generation !== this.generation) return;
      if (result.review) this.install(result.review);
      this.message.set(phrase('Review submission: {0}', result.outcome));
    } catch (e) {
      if (generation === this.generation) {
        this.error.set(failure(e));
        this.stale.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async verify(): Promise<void> {
    const r = this.review();
    this.verification.markAllAsTouched();
    if (this.verification.invalid) {
      focusInvalid();
      return;
    }
    if (!r || this.busy() || this.stale()) return;
    const v = this.verification.getRawValue();
    const requirement = r.requirements.find((p) => p.id === v.prerequisiteId);
    if (!requirement) return;
    const generation = this.generation;
    this.busy.set(true);
    try {
      if (
        !(await this.confirmation()!.ask(
          'Record human safety verification?',
          phrase(
            v.satisfied
              ? '{0}: record satisfied for revision {1}, with the evidence you entered. Only attest to checks actually performed.'
              : '{0}: record not satisfied for revision {1}, with the evidence you entered. Only attest to checks actually performed.',
            requirement.description,
            r.target.revision,
          ),
          'Record verification',
        )) ||
        generation !== this.generation
      )
        return;
      await this.api.verify(r.workOrderId, r.target, v.prerequisiteId, v.evidence, v.satisfied);
      if (generation !== this.generation) return;
      const current = await this.api.get(r.workOrderId);
      if (generation !== this.generation) return;
      this.install(current);
      this.message.set('Verification saved by the trusted host. Current safety state refreshed.');
    } catch (e) {
      if (generation === this.generation) {
        this.error.set(failure(e));
        this.stale.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async send(): Promise<void> {
    const r = this.review();
    if (!r || this.busy() || this.stale() || this.dispatchRequested()) return;
    const generation = this.generation;
    this.busy.set(true);
    try {
      if (
        !(await this.confirmation()!.ask(
          'Request external dispatch?',
          phrase(
            'Request dispatch of revision {0}: {1}. This can create an external maintenance ticket. The server must recheck approval and all mandatory safety prerequisites.',
            r.target.revision,
            r.content.description,
          ),
          'Request dispatch',
        )) ||
        generation !== this.generation
      )
        return;
      this.dispatchRequested.set(true);
      this.error.set('');
      const d = await this.deliveries.send(r.workOrderId, r.target);
      if (generation !== this.generation) return;
      this.dispatch.set(d);
      if (d.attemptId) this.session.remember('dispatch', d.attemptId, d.state ?? d.outcome);
      const current = await this.api.get(r.workOrderId);
      if (generation !== this.generation) return;
      this.install(current);
    } catch (e) {
      if (generation === this.generation) {
        // A definitive gate rejection created no attempt. Reload remains required;
        // unknown outcomes and existing delivery attempts remain protected from retry.
        if (e instanceof HttpErrorResponse && e.status === 422 && e.error?.attemptId === null)
          this.dispatchRequested.set(false);
        this.error.set(failure(e));
        this.stale.set(true);
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  ngOnDestroy(): void {
    this.generation++;
    // Child destruction resolves the pending confirmation without scheduling a render.
    this.confirmation()?.closed();
  }
}
