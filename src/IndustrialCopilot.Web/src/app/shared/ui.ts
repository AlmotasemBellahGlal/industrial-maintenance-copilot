import {
  Component,
  ElementRef,
  input,
  viewChild,
  signal,
  afterNextRender,
  inject,
  Injector,
} from '@angular/core';
import { AbstractControl, ValidationErrors } from '@angular/forms';
export function requiredText(c: AbstractControl): ValidationErrors | null {
  return typeof c.value === 'string' && c.value.trim() ? null : { required: true };
}
export function guid(c: AbstractControl): ValidationErrors | null {
  return typeof c.value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(c.value) &&
    c.value !== '00000000-0000-0000-0000-000000000000'
    ? null
    : { guid: true };
}
export function focusInvalid(): void {
  queueMicrotask(() =>
    document
      .querySelector<HTMLElement>('input.ng-invalid, textarea.ng-invalid, select.ng-invalid')
      ?.focus(),
  );
}
@Component({
  selector: 'app-status',
  template: '<span class="badge" [attr.data-tone]="tone()">{{ text() }}</span>',
})
export class Status {
  readonly text = input.required<string>();
  tone(): string {
    const v = this.text().toLowerCase();
    if (/uncertain|pending|waiting|unverified|not verified/.test(v)) return 'warning';
    if (/blocked|failed|rejected|unsatisfied/.test(v)) return 'danger';
    if (/approved/.test(v)) return 'approval';
    if (/satisfied|verified/.test(v)) return 'safety';
    if (/confirmed|dispatched|completed/.test(v)) return 'success';
    return 'neutral';
  }
}
@Component({
  selector: 'app-confirm',
  template: `<dialog
    #dialog
    aria-labelledby="confirm-title"
    (cancel)="finish(false)"
    (close)="closed()"
  >
    <p class="eyebrow">Consequential action</p>
    <h2 id="confirm-title">{{ title() }}</h2>
    <p>{{ detail() }}</p>
    <p class="muted">
      The server validates the current revision and your permission. This confirmation does not
      bypass safety checks.
    </p>
    <div class="actions">
      <button type="button" autofocus (click)="finish(false)">Go back</button
      ><button type="button" class="primary" (click)="finish(true)">{{ label() }}</button>
    </div>
  </dialog>`,
})
export class Confirm {
  private injector = inject(Injector);
  private trigger: HTMLElement | null = null;
  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  title = signal('');
  detail = signal('');
  label = signal('');
  private resolve?: (value: boolean) => void;
  ask(title: string, detail: string, label: string): Promise<boolean> {
    this.trigger = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.title.set(title);
    this.detail.set(detail);
    this.label.set(label);
    return new Promise((resolve) => {
      this.resolve = resolve;
      this.dialog().nativeElement.showModal();
    });
  }
  finish(value: boolean): void {
    const wasOpen = this.dialog().nativeElement.open;
    const trigger = this.trigger;
    this.resolve?.(value);
    this.resolve = undefined;
    this.dialog().nativeElement.close();
    // The invoking button is disabled while the confirmation awaits a result.
    // Restore only after Angular has rendered its enabled state on cancellation.
    if (wasOpen && !value)
      afterNextRender(
        () => {
          if (trigger?.isConnected && !trigger.matches(':disabled')) trigger.focus();
        },
        { injector: this.injector },
      );
  }
  closed(): void {
    this.resolve?.(false);
    this.resolve = undefined;
  }
  ngOnDestroy(): void {
    this.closed();
  }
}
