import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Session } from '../core/session';
import { WorkflowApi, failure } from '../core/api';
import { focusInvalid, requiredText } from '../shared/ui';
@Component({
  imports: [ReactiveFormsModule],
  template: ` <p class="eyebrow">Host access</p>
    <h1>Connect your workspace</h1>
    <p class="lede">Use a credential issued by your trusted maintenance host.</p>
    <section class="panel narrow">
      <h2>Session credential</h2>
      <p>
        Credentials stay in memory until you clear them or reload. Your actor identity and equipment
        permissions are determined by the server.
      </p>
      <form (submit)="$event.preventDefault(); connect()">
        <label for="credential">Host bearer credential</label
        ><input
          id="credential"
          type="password"
          autocomplete="current-password"
          [formControl]="token"
          aria-describedby="credential-help"
          [attr.aria-invalid]="token.touched && token.invalid"
          maxlength="512"
        />
        <p id="credential-help" class="muted">
          Paste the credential configured by your administrator. Never enter an actor ID as a
          substitute.
        </p>
        @if (token.touched && token.invalid) {
          <p class="field-error" role="alert">Enter a credential between 32 and 512 characters.</p>
        }
        <div class="actions">
          <button class="primary" type="submit">Use credential</button
          ><button type="button" (click)="clear()">Clear session</button>
        </div>
      </form>
      <p role="status">{{ message() }}</p>
    </section>
    <section class="panel narrow">
      <h2>API readiness</h2>
      <p>
        Checks the configured database schemas. It does not validate your credential or prove an LLM
        provider is available.
      </p>
      <button [disabled]="busy()" (click)="check()">
        {{ busy() ? 'Checking…' : 'Check readiness' }}
      </button>
      <p role="status">{{ readiness() }}</p>
    </section>`,
})
export class Connection {
  private session = inject(Session);
  private api = inject(WorkflowApi);
  token = new FormControl('', {
    nonNullable: true,
    validators: [requiredText, Validators.minLength(32), Validators.maxLength(512)],
  });
  message = signal('');
  readiness = signal('Not checked');
  busy = signal(false);
  connect(): void {
    this.token.markAsTouched();
    if (this.token.invalid) {
      focusInvalid();
      return;
    }
    this.session.connect(this.token.value);
    this.token.reset();
    this.message.set(
      'Credential configured for this session. The server will authenticate each request.',
    );
  }
  clear(): void {
    this.session.clear();
    this.token.reset();
    this.message.set('Credential and session activity cleared.');
  }
  async check(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.ready();
      this.readiness.set('Database schemas ready at ' + new Date().toLocaleTimeString());
    } catch (e) {
      this.readiness.set(failure(e));
    } finally {
      this.busy.set(false);
    }
  }
}
