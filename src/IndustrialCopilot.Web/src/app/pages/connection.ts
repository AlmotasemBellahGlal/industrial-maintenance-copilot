import { TranslatePipe } from '../core/language';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Session } from '../core/session';
import { WorkflowApi, failure } from '../core/api';
import { focusInvalid, requiredText } from '../shared/ui';
@Component({
  imports: [TranslatePipe, ReactiveFormsModule],
  template: ` <p class="eyebrow">{{ 'Host access' | t }}</p>
    <h1>{{ 'Connect your workspace' | t }}</h1>
    <p class="lede">{{ 'Use a credential issued by your trusted maintenance host.' | t }}</p>
    @if (demoMinutes() !== null) {
      <section class="panel narrow">
        <h2>{{ 'Try the demo' | t }}</h2>
        <p>
          {{
            'Start a temporary session with synthetic data only. Your work is private to this session and is deleted after it expires. Reloading this page discards the session in this browser; the server keeps it until it expires.'
              | t
          }}
        </p>
        <button class="primary" [disabled]="demoBusy()" (click)="startDemo()">
          {{ (demoBusy() ? 'Starting…' : 'Start demo session') | t }}
        </button>
        @if (demoMessage()) {
          <p role="status">{{ demoMessage() | t }}</p>
        }
      </section>
    }
    <section class="panel narrow">
      <h2>{{ 'Session credential' | t }}</h2>
      <p>
        {{
          'Credentials stay in memory until you clear them or reload. Your actor identity and equipment permissions are determined by the server.'
            | t
        }}
      </p>
      <form (submit)="$event.preventDefault(); connect()">
        <label for="credential"> {{ 'Host bearer credential' | t }} </label
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
          {{
            'Paste the credential configured by your administrator. Never enter an actor ID as a substitute.'
              | t
          }}
        </p>
        @if (token.touched && token.invalid) {
          <p class="field-error" role="alert">
            {{ 'Enter a credential between 32 and 512 characters.' | t }}
          </p>
        }
        <div class="actions">
          <button class="primary" type="submit">{{ 'Use credential' | t }}</button
          ><button type="button" (click)="clear()">{{ 'Clear session' | t }}</button>
        </div>
      </form>
      <p role="status">{{ message() | t }}</p>
    </section>
    <section class="panel narrow">
      <h2>{{ 'API readiness' | t }}</h2>
      <p>
        {{
          'Checks the configured database schemas. It does not validate your credential or prove an LLM provider is available.'
            | t
        }}
      </p>
      <button [disabled]="busy()" (click)="check()">
        {{ (busy() ? 'Checking…' : 'Check readiness') | t }}
      </button>
      <p role="status">{{ readiness() | t }}</p>
    </section>`,
})
export class Connection implements OnInit {
  private session = inject(Session);
  private api = inject(WorkflowApi);
  token = new FormControl('', {
    nonNullable: true,
    validators: [requiredText, Validators.minLength(32), Validators.maxLength(512)],
  });
  message = signal('');
  demoMinutes = signal<number | null>(null);
  demoBusy = signal(false);
  demoMessage = signal('');
  async ngOnInit(): Promise<void> {
    const seconds = await this.session.demoAvailable();
    this.demoMinutes.set(seconds === null ? null : Math.round(seconds / 60));
  }
  async startDemo(): Promise<void> {
    this.demoBusy.set(true);
    const result = await this.session.startDemo();
    this.demoBusy.set(false);
    this.demoMessage.set(
      result.ok
        ? 'Demo session started. It expires automatically; start another if it ends.'
        : result.reason === 'limit'
          ? 'Too many demo sessions from your network. Try again later.'
          : result.reason === 'full'
            ? 'The demo is full right now. Try again in a few minutes.'
            : 'Could not start a demo session.',
    );
  }
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
      this.readiness.set('Database schemas ready');
    } catch (e) {
      this.readiness.set(failure(e));
    } finally {
      this.busy.set(false);
    }
  }
}
