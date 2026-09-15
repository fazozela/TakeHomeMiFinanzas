import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./components/game/game') },
  { path: 'rules', loadComponent: () => import('./components/rules/rules') },
  { path: 'stats', loadComponent: () => import('./components/stats/stats') },
  { path: '**', redirectTo: '' }
];
