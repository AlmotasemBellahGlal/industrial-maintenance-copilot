import { TranslatePipe, LanguageService } from './core/language';
import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Session } from './core/session';
@Component({
  selector: 'app-root',
  imports: [TranslatePipe, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  readonly language = inject(LanguageService);
  readonly session = inject(Session);
  readonly menu = signal(false);
  readonly links = [
    { path: '/', label: 'Dashboard', number: '01' },
    { path: '/diagnosis', label: 'New diagnosis', number: '02' },
    { path: '/runs', label: 'Maintenance runs', number: '03' },
    { path: '/work-orders', label: 'Work orders', number: '04' },
    { path: '/dispatch', label: 'Dispatch', number: '05' },
    { path: '/traces', label: 'Trace / activity', number: '06' },
  ];
  constructor() {
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((e) => {
        if (e instanceof NavigationEnd) {
          document.getElementById('main')?.focus();
          window.scrollTo(0, 0);
        }
      });
  }
}
