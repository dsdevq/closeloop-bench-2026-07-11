import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ContactsComponent } from './contacts.component';
import { environment } from '../../environments/environment';

const BASE = `${environment.apiBaseUrl}/contacts`;

const alice = { id: 'aa', name: 'Alice', email: 'alice@example.com', phone: null, companyId: null };
const bob = { id: 'bb', name: 'Bob', email: 'bob@example.com', phone: null, companyId: null };
const carol = { id: 'cc', name: 'Carol', email: 'carol@example.com', phone: null, companyId: null };

describe('ContactsComponent', () => {
  let controller: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContactsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('renders fetched contacts in the list', () => {
    const fixture = TestBed.createComponent(ContactsComponent);
    fixture.detectChanges();

    controller.expectOne(BASE).flush([alice, bob]);
    fixture.detectChanges();

    const items = (fixture.nativeElement as HTMLElement).querySelectorAll('li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Alice');
    expect(items[1].textContent).toContain('Bob');
  });

  it('shows empty-state message when list is empty', () => {
    const fixture = TestBed.createComponent(ContactsComponent);
    fixture.detectChanges();
    controller.expectOne(BASE).flush([]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No contacts yet.');
  });

  it('posts to create and refreshes the list on success', () => {
    const fixture = TestBed.createComponent(ContactsComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();
    controller.expectOne(BASE).flush([]);
    fixture.detectChanges();

    comp.form.setValue({ name: 'Carol', email: 'carol@example.com', phone: '' });
    comp.submit();

    const postReq = controller.expectOne({ method: 'POST', url: BASE });
    expect(postReq.request.body).toEqual({
      name: 'Carol',
      email: 'carol@example.com',
      phone: null,
      companyId: null,
    });
    postReq.flush(carol);

    // loadContacts() called again after success
    controller.expectOne(BASE).flush([carol]);
    fixture.detectChanges();

    const items = (fixture.nativeElement as HTMLElement).querySelectorAll('li');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('Carol');
    expect(comp.form.pristine).toBe(true);
    expect(comp.submitting()).toBe(false);
  });

  it('displays the API error title on a 422 validation error', () => {
    const fixture = TestBed.createComponent(ContactsComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();
    controller.expectOne(BASE).flush([]);
    fixture.detectChanges();

    comp.form.setValue({ name: 'Dave', email: 'dave@example.com', phone: '' });
    comp.submit();

    const postReq = controller.expectOne({ method: 'POST', url: BASE });
    postReq.flush(
      { title: 'One or more validation errors occurred.', status: 422 },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('One or more validation errors occurred.');
    expect(comp.submitting()).toBe(false);
  });
});
