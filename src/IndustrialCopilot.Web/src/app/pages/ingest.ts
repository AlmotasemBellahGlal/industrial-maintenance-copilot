import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../core/language';
import { ProductApi, Ingestion } from '../core/product';
import { Session } from '../core/session';
import { failure } from '../core/api';
export function validUpload(file: File): boolean {
  return (
    file.size > 0 &&
    file.size <= 16000000 &&
    /\.(txt|pdf)$/i.test(file.name) &&
    !/[\\/:]/.test(file.name)
  );
}
@Component({
  imports: [FormsModule, TranslatePipe],
  template: ` <p class="eyebrow">{{ 'Knowledge base' | t }}</p>
    <h1>{{ 'Ingest manual' | t }}</h1>
    <p class="lede">
      {{ 'Text or PDF, up to 16 MB. Uploading a manual never authorizes maintenance work.' | t }}
    </p>
    @if (!session.can('ingest')) {
      <p role="status">
        {{ 'Ingestion permission required. Connect with an authorized account.' | t }}
      </p>
    }
    @if (error()) {
      <p role="alert" class="field-error">{{ error() | t }}</p>
    }
    <section class="panel">
      <form ngNativeValidate (ngSubmit)="upload()">
        <fieldset [disabled]="busy() || !session.can('ingest')">
          <div class="form-grid">
            <label
              >{{ 'Equipment ID' | t
              }}<input name="equipment" [(ngModel)]="equipment" required pattern="[0-9a-fA-F-]{36}"
            /></label>
            <label
              >{{ 'Document ID' | t
              }}<input name="document" [(ngModel)]="document" required pattern="[0-9a-fA-F-]{36}"
            /></label>
            <label
              >{{ 'Revision ID' | t
              }}<input name="revision" [(ngModel)]="revision" required pattern="[0-9a-fA-F-]{36}"
            /></label>
            <label
              >{{ 'Title' | t }}<input name="title" [(ngModel)]="title" required maxlength="512"
            /></label>
            <label
              >{{ 'Revision number' | t
              }}<input name="number" type="number" [(ngModel)]="number" required min="1"
            /></label>
            <label
              >{{ 'Manual file' | t
              }}<input
                type="file"
                accept=".txt,.pdf,text/plain,application/pdf"
                (change)="choose($event)"
                required
            /></label>
          </div>
          <p>
            {{
              'Keep the same document and revision IDs when retrying. Re-ingestion replaces that revision without duplicates.'
                | t
            }}
          </p>
          <button type="submit" class="primary" [disabled]="!file">
            {{ 'Upload and index' | t }}
          </button>
        </fieldset>
      </form>
      <button (click)="refresh()" [disabled]="!document || !revision || !equipment">
        {{ 'Refresh ingestion status' | t }}
      </button>
      <p role="status">{{ (busy() ? 'Processing' : 'Ready') | t }}</p>
      @for (report of reports(); track report.attemptId) {
        <article>
          <h2>{{ state(report.state) | t }}</h2>
          <p>
            {{ 'Pages' | t }}: {{ report.pages ?? '—' }} · {{ 'Chunks' | t }}:
            {{ report.chunks ?? '—' }}
          </p>
          @if (report.failure) {
            <p>
              {{ 'Document could not be processed. Check format, limits and source content.' | t }}
            </p>
          }
          <p>
            <bdi>{{ report.attemptId }}</bdi>
          </p>
        </article>
      }
    </section>`,
})
export class IngestPage {
  private api = inject(ProductApi);
  readonly session = inject(Session);
  equipment = '';
  document = '';
  revision = '';
  title = '';
  number = 1;
  file: File | null = null;
  busy = signal(false);
  error = signal('');
  reports = signal<Ingestion[]>([]);
  choose(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    this.file = file && validUpload(file) ? file : null;
    this.error.set(this.file ? '' : 'Select a text or PDF file up to 16 MB.');
  }
  async upload(): Promise<void> {
    if (!this.file || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.reports.set(
        await this.api.upload(
          this.equipment,
          this.document,
          this.revision,
          this.title,
          this.number,
          this.file,
        ),
      );
    } catch (e) {
      this.error.set(failure(e));
      await this.refresh();
    } finally {
      this.busy.set(false);
    }
  }
  async refresh(): Promise<void> {
    try {
      this.reports.set(await this.api.ingestion(this.equipment, this.document, this.revision));
    } catch (e) {
      this.error.set(failure(e));
    }
  }
  state(n: number): string {
    return ['', 'Processing', 'Completed', 'Failed', 'Interrupted'][n] ?? 'Failed';
  }
}
