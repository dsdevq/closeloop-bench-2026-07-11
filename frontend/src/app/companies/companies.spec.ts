import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CompaniesComponent } from './companies.component';
import { environment } from '../../environments/environment';

const BASE = `${environment.apiBaseUrl}/companies`;

const acme = { id: 'aa', name: 'Acme Corp', domain: 'acme.com', industry: 'Technology', ownerId: 'owner-1' };
const beta = { id: 'bb', name: 'Beta Ltd', domain: null, industry: null, ownerId: 'owner-2' };
const gamma = { id: 'cc', name: 'Gamma Inc', domain: 'gamma.io', industry: 'Finance', ownerId: 'owner-1' };

describe('CompaniesComponent', () => {
  let controller: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CompaniesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('renders fetched companies in the list', () => {
    const fixture = TestBed.createComponent(CompaniesComponent);
    fixture.detectChanges();

    controller.expectOne(BASE).flush([acme, beta]);
    fixture.detectChanges();

    const items = (fixture.nativeElement as HTMLElement).querySelectorAll('li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Acme Corp');
    expect(items[1].textContent).toContain('Beta Ltd');
  });

  it('shows empty-state message when list is empty', () => {
    const fixture = TestBed.createComponent(CompaniesComponent);
    fixture.detectChanges();
    controller.expectOne(BASE).flush([]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No companies yet.');
  });

  it('posts to create and refreshes the list on success', () => {
    const fixture = TestBed.createComponent(CompaniesComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();
    controller.expectOne(BASE).flush([]);
    fixture.detectChanges();

    comp.form.setValue({ name: 'Gamma Inc', domain: 'gamma.io', industry: '' });
    comp.submit();

    const postReq = controller.expectOne({ method: 'POST', url: BASE });
    expect(postReq.request.body).toEqual({
      name: 'Gamma Inc',
      domain: 'gamma.io',
      industry: null,
      ownerId: '00000000-0000-0000-0000-000000000001',
    });
    postReq.flush(gamma);

    controller.expectOne(BASE).flush([gamma]);
    fixture.detectChanges();

    const items = (fixture.nativeElement as HTMLElement).querySelectorAll('li');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('Gamma Inc');
    expect(comp.form.pristine).toBe(true);
    expect(comp.submitting()).toBe(false);
  });

  it('displays the API error title on a 422 validation error', () => {
    const fixture = TestBed.createComponent(CompaniesComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();
    controller.expectOne(BASE).flush([]);
    fixture.detectChanges();

    comp.form.setValue({ name: 'Bad Corp', domain: '', industry: '' });
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
