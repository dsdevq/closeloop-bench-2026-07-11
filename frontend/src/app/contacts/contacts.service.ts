import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface ContactResponse {
  id: string;
  name: string;
  email: string;
  phone: string | null;
  companyId: string | null;
}

export interface CreateContactRequest {
  name: string;
  email: string;
  phone: string | null;
  companyId: string | null;
}

@Injectable({ providedIn: 'root' })
export class ContactsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/contacts`;

  list(): Observable<ContactResponse[]> {
    return this.http.get<ContactResponse[]>(this.base);
  }

  create(req: CreateContactRequest): Observable<ContactResponse> {
    return this.http.post<ContactResponse>(this.base, req);
  }
}
