import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'translate',
    loadComponent: () => import('./subtitle-dashboard/subtitle-dashboard.component')
      .then(mod => mod.SubtitleDashboardComponent)
  }
];
