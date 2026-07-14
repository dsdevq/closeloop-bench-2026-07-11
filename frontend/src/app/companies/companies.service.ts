import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface CompanyResponse {
  id: string;
  name: string;
  domain: string | null;
  industry: string | null;
  ownerId: string;
}

export interface CreateCompanyRequest {
  name: string;
  domain: string | null;
  industry: string | null;
  ownerId: string;
}

@Injectable({ providedIn: 'root' })
export class CompaniesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/companies`;

  list(): Observable<CompanyResponse[]> {
    return this.http.get<CompanyResponse[]>(this.base);
  }

  create(req: CreateCompanyRequest): Observable<CompanyResponse> {
    return this.http.post<CompanyResponse>(this.base, req);
  }
}
