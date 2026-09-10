import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'translate',
    loadComponent: () => import('./subtitle-dashboard/subtitle-dashboard')
      .then(mod => mod.SubtitleDashboard)
  }
];
