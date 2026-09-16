import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'ask',
    title: 'Ask | Maintenance Copilot',
    loadComponent: () => import('./pages/ask').then((m) => m.AskPage),
  },
  {
    path: 'ingest',
    title: 'Ingest | Maintenance Copilot',
    loadComponent: () => import('./pages/ingest').then((m) => m.IngestPage),
  },
  {
    path: '',
    title: 'Operations dashboard | Maintenance Copilot',
    loadComponent: () => import('./pages/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'connection',
    title: 'Host connection | Maintenance Copilot',
    loadComponent: () => import('./pages/connection').then((m) => m.Connection),
  },
  {
    path: 'diagnosis',
    title: 'New diagnosis | Maintenance Copilot',
    loadComponent: () => import('./pages/diagnosis').then((m) => m.Diagnosis),
  },
  {
    path: 'work-orders/:id',
    title: 'Work order review | Maintenance Copilot',
    loadComponent: () => import('./pages/review').then((m) => m.ReviewPage),
  },
  ...['runs', 'work-orders', 'dispatch', 'traces'].flatMap((kind) => [
    {
      path: kind,
      data: { kind },
      title: `${kind} | Maintenance Copilot`,
      loadComponent: () => import('./pages/inspection').then((m) => m.Inspection),
    },
    ...(kind === 'work-orders'
      ? []
      : [
          {
            path: kind + '/:id',
            data: { kind },
            title: `${kind} | Maintenance Copilot`,
            loadComponent: () => import('./pages/inspection').then((m) => m.Inspection),
          },
        ]),
  ]),
  { path: '**', redirectTo: '' },
];
