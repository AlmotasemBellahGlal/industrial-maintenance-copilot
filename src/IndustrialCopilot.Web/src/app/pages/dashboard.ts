import { TranslatePipe, LanguageService } from '../core/language';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { Session } from '../core/session';
import { Status } from '../shared/ui';
@Component({
  imports: [TranslatePipe, RouterLink, DatePipe, Status],
  template: ` <div class="page-heading">
      <div>
        <p class="eyebrow">{{ 'Operations overview' | t }}</p>
        <h1>{{ 'Maintenance, with evidence.' | t }}</h1>
        <p class="lede">
          {{ 'A clear path from reported symptom to a safely reviewed work order.' | t }}
        </p>
      </div>
      <a class="button primary" routerLink="/diagnosis">
        {{ 'Start a diagnosis' | t }} <span aria-hidden="true">↗</span></a
      >
    </div>
    <div class="overview-grid">
      <section class="panel workflow-map">
        <p class="eyebrow">{{ 'The maintenance workflow' | t }}</p>
        <h2>{{ 'Three agents. One human decision.' | t }}</h2>
        <ol class="stages">
          <li>
            <span>01</span>
            <div>
              <strong> {{ 'Match the symptom' | t }} </strong>
              <p>{{ 'Use the selected equipment and applicable manual revision.' | t }}</p>
            </div>
          </li>
          <li>
            <span>02</span>
            <div>
              <strong> {{ 'Plan diagnostics & safety' | t }} </strong>
              <p>
                {{ 'Ground instructions in evidence. Trusted policy determines requirements.' | t }}
              </p>
            </div>
          </li>
          <li>
            <span>03</span>
            <div>
              <strong> {{ 'Review the proposal' | t }} </strong>
              <p>
                {{
                  'A supervisor reviews the exact scope before safety verification and dispatch.'
                    | t
                }}
              </p>
            </div>
          </li>
        </ol>
      </section>
      <aside class="panel trust-panel" [attr.aria-label]="'Safety boundary' | t">
        <p class="eyebrow">{{ 'Safety boundary' | t }}</p>
        <h2>{{ 'A proposal is not permission.' | t }}</h2>
        <p>
          {{
            'Supervisor approval and verified mandatory prerequisites are separate gates. The server rechecks both when dispatch is requested.'
              | t
          }}
        </p>
        <a routerLink="/work-orders">
          {{ 'Open a work order' | t }} <span aria-hidden="true">→</span></a
        >
      </aside>
    </div>
    <section class="panel">
      <h2>{{ 'Recent session activity' | t }}</h2>
      <p class="muted">
        {{
          'Records visited in this browser session. Observations may be stale; open a record to refresh. This is not a system-wide inventory.'
            | t
        }}
      </p>
      @if (session.activity().length) {
        <ul class="record-list">
          @for (item of session.activity(); track item.kind + item.id) {
            <li>
              <div>
                <span class="eyebrow">{{ item.kind | t }}</span
                ><a class="identifier" [routerLink]="['/', item.kind, item.id]">{{ item.id }}</a
                ><span class="muted">
                  {{ 'Observed' | t }}
                  {{ item.observedAt | date: 'shortTime' : undefined : language.language() }}</span
                >
              </div>
              <app-status [text]="item.status" />
            </li>
          }
        </ul>
      } @else {
        <div class="empty">
          <span class="empty-mark" aria-hidden="true">—</span>
          <h3>{{ 'Your workspace is ready' | t }}</h3>
          <p>
            {{
              'Start a diagnosis or look up a known run, work order, dispatch attempt, or trace.'
                | t
            }}
          </p>
          <a routerLink="/runs"> {{ 'Look up a maintenance run' | t }} </a>
        </div>
      }
    </section>`,
})
export class Dashboard {
  readonly session = inject(Session);
  readonly language = inject(LanguageService);
}
