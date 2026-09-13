import { inject, Injectable, InjectionToken } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Session } from './session';
import {
  Content,
  DecisionResult,
  Dispatch,
  Evidence,
  Preview,
  Requirement,
  Review,
  Run,
  Target,
  Trace,
} from './contracts';

// Same-origin /api deployment avoids bearer forwarding to arbitrary destinations.
export const API_BASE = new InjectionToken<string>('API_BASE', {
  providedIn: 'root',
  factory: () => '/api',
});
export const hostHeaders: HttpInterceptorFn = (request, next) => {
  const base = inject(API_BASE),
    token = inject(Session).token();
  return next(
    request.url.startsWith(base + '/')
      ? request.clone({
          setHeaders: {
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
            'X-Correlation-ID': crypto.randomUUID(),
          },
        })
      : request,
  );
};
export class ApiFailure extends Error {
  constructor(
    readonly status: number,
    readonly correlation: string | null = null,
  ) {
    super(errorText(status));
  }
}
export function errorText(status: number): string {
  switch (status) {
    case 400:
      return 'The request is invalid. Check the required fields and identifiers.';
    case 401:
      return 'Authentication required. Update your host credential in Connection.';
    case 403:
      return 'Permission denied. Your host account cannot perform this operation for this equipment.';
    case 404:
      return 'Record not found or unavailable to this account. Check the identifier.';
    case 409:
      return 'Review changed or lifecycle conflict. Reload the current record and review again. Your request was not applied.';
    case 422:
      return 'The server did not accept this operation. Review the current lifecycle and authoritative safety requirements.';
    case 504:
      return 'The operation timed out. Inspect the known record before attempting another operation.';
    default:
      return 'Connection or dependency unavailable. The outcome may be unknown. Inspect the record before retrying a consequential operation.';
  }
}
export function failure(error: unknown): string {
  if (error instanceof HttpErrorResponse)
    return (
      errorText(error.status) +
      (error.headers.get('X-Correlation-ID')
        ? ` Correlation: ${error.headers.get('X-Correlation-ID')}`
        : '')
    );
  return error instanceof ApiFailure ? error.message : errorText(0);
}
@Injectable({ providedIn: 'root' })
export class WorkflowApi {
  private http = inject(HttpClient);
  private base = inject(API_BASE);
  run(id: string): Promise<Run> {
    return firstValueFrom(this.http.get<Run>(`${this.base}/runs/${encodeURIComponent(id)}`));
  }
  trace(id: string): Promise<Trace> {
    return firstValueFrom(this.http.get<Trace>(`${this.base}/traces/${encodeURIComponent(id)}`));
  }
  ready(): Promise<unknown> {
    return firstValueFrom(this.http.get('/health/ready'));
  }
}
@Injectable({ providedIn: 'root' })
export class ReviewApi {
  private http = inject(HttpClient);
  private base = inject(API_BASE);
  private url(id: string): string {
    return `${this.base}/work-orders/${encodeURIComponent(id)}`;
  }
  get(id: string): Promise<Review> {
    return firstValueFrom(this.http.get<Review>(this.url(id)));
  }
  evidence(id: string): Promise<Evidence> {
    return firstValueFrom(this.http.get<Evidence>(this.url(id) + '/evidence'));
  }
  preview(id: string, content: Content): Promise<Preview> {
    return firstValueFrom(
      this.http.post<Preview>(this.url(id) + '/edited-safety-preview', content),
    );
  }
  submit(id: string, target: Target): Promise<DecisionResult> {
    return firstValueFrom(this.http.post<DecisionResult>(this.url(id) + '/submit', target));
  }
  decide(
    id: string,
    target: Target,
    decision: 'Approve' | 'Reject' | 'EditAndApprove',
    content?: Content,
    requirements?: Requirement[],
  ): Promise<DecisionResult> {
    return firstValueFrom(
      this.http.post<DecisionResult>(this.url(id) + '/decisions', {
        target,
        decision,
        ...(content ? { editedContent: content, reviewedRequirements: requirements } : {}),
      }),
    );
  }
  verify(
    id: string,
    target: Target,
    prerequisiteId: string,
    evidence: string,
    satisfied: boolean,
  ): Promise<unknown> {
    return firstValueFrom(
      this.http.post(this.url(id) + '/verifications', {
        target,
        prerequisiteId,
        evidence,
        satisfied,
      }),
    );
  }
}
@Injectable({ providedIn: 'root' })
export class DispatchApi {
  private http = inject(HttpClient);
  private base = inject(API_BASE);
  send(id: string, target: Target): Promise<Dispatch> {
    return firstValueFrom(
      this.http.post<Dispatch>(
        `${this.base}/work-orders/${encodeURIComponent(id)}/dispatch`,
        target,
      ),
    );
  }
  get(id: string): Promise<Dispatch> {
    return firstValueFrom(
      this.http.get<Dispatch>(`${this.base}/dispatch-attempts/${encodeURIComponent(id)}`),
    );
  }
}
