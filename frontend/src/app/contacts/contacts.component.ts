import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ContactsService, ContactResponse } from './contacts.service';

@Component({
  selector: 'app-contacts',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <h2>Contacts</h2>

    <ul>
      @for (contact of contacts(); track contact.id) {
        <li>{{ contact.name }} — {{ contact.email }}</li>
      } @empty {
        <li>No contacts yet.</li>
      }
    </ul>

    <h3>Add contact</h3>
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
          Email
          <input formControlName="email" type="email" />
        </label>
        @if (form.controls.email.invalid && form.controls.email.touched) {
          <span>Valid email is required.</span>
        }
      </div>
      <div>
        <label>
          Phone
          <input formControlName="phone" />
        </label>
      </div>
      <button type="submit" [disabled]="form.invalid || submitting()">Add</button>
    </form>

    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
  `
})
export class ContactsComponent implements OnInit {
  private readonly service = inject(ContactsService);
  private readonly fb = inject(FormBuilder);

  readonly contacts = signal<ContactResponse[]>([]);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phone: [''],
  });

  ngOnInit(): void {
    this.loadContacts();
  }

  private loadContacts(): void {
    this.service.list().subscribe({
      next: (contacts) => this.contacts.set(contacts),
      error: () => this.error.set('Failed to load contacts.'),
    });
  }

  submit(): void {
    if (this.form.invalid) return;
    this.submitting.set(true);
    this.error.set(null);
    const { name, email, phone } = this.form.getRawValue();
    this.service.create({
      name,
      email,
      phone: phone || null,
      companyId: null,
    }).subscribe({
      next: () => {
        this.form.reset();
        this.submitting.set(false);
        this.loadContacts();
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(err?.error?.title ?? 'Create failed.');
      },
    });
  }
}
