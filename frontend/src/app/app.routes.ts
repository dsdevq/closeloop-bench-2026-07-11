import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'contacts',
    loadComponent: () =>
      import('./contacts/contacts.component').then((m) => m.ContactsComponent),
  },
  {
    path: 'companies',
    loadComponent: () =>
      import('./companies/companies.component').then((m) => m.CompaniesComponent),
  },
];
