import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { CompaniesService, CompanyResponse } from './companies.service';

@Component({
  selector: 'app-companies',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <h2>Companies</h2>

    <ul>
      @for (company of companies(); track company.id) {
        <li>{{ company.name }}@if (company.domain) { — {{ company.domain }}}</li>
      } @empty {
        <li>No companies yet.</li>
      }
    </ul>

    <h3>Add company</h3>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <div>
        <label>
          Name
          <input formControlName="name" />
        </label>
        @if (form.controls.name.invalid && form.controls.name.touched) {
          <span>Name is required.</span>
        }
      </div>
      <div>
        <label>
          Domain
          <input formControlName="domain" />
        </label>
      </div>
      <div>
        <label>
          Industry
          <input formControlName="industry" />
        </label>
      </div>
      <button type="submit" [disabled]="form.invalid || submitting()">Add</button>
    </form>

    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
  `
})
export class CompaniesComponent implements OnInit {
  private readonly service = inject(CompaniesService);
  private readonly fb = inject(FormBuilder);

  readonly companies = signal<CompanyResponse[]>([]);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    domain: [''],
    industry: [''],
  });

  ngOnInit(): void {
    this.loadCompanies();
  }

  private loadCompanies(): void {
    this.service.list().subscribe({
      next: (companies) => this.companies.set(companies),
      error: () => this.error.set('Failed to load companies.'),
    });
  }

  submit(): void {
    if (this.form.invalid) return;
    this.submitting.set(true);
    this.error.set(null);
    const { name, domain, industry } = this.form.getRawValue();
    this.service.create({
      name,
      domain: domain || null,
      industry: industry || null,
      ownerId: '00000000-0000-0000-0000-000000000001',
    }).subscribe({
      next: () => {
        this.form.reset();
        this.submitting.set(false);
        this.loadCompanies();
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(err?.error?.title ?? 'Create failed.');
      },
    });
  }
}
