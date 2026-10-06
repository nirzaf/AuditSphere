import { DestroyRef, Injectable, Signal, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subscription, firstValueFrom, map, timeout } from 'rxjs';
import { guidPattern } from './contracts';
import { SessionService } from './session';
import { Decoder, decode } from './decode';

export type CommandOutcome<T = unknown> =
  | { ok: true; value: T }
  | { ok: false; unknown: false; code: string; message: string; status: number }
  | { ok: false; unknown: true; code: 'outcome.unknown'; message: string; status: number };

export const UNKNOWN_OUTCOME =
  'The outcome is not confirmed. Refresh to see the persisted state before repeating this action.';

export interface Resource<T> {
  readonly data: () => T | null;
  readonly error: () => string;
  readonly loading: () => boolean;
  reload(): void;
}

/**
 * Reads are cancellable, session-bound and decoded; commands send exactly once with antiforgery (via HttpClient's
 * XSRF interceptor), are never retried automatically, and report network/timeout/5xx results as "outcome unknown"
 * rather than failure. The server remains the authority for every authorization and business decision.
 */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);

  /** Must be called in an injection context (field initializer or constructor). Null URL means "nothing to load". */
  resource<T>(url: () => string | null, decoder: Decoder<T>, unavailable = 'This information is unavailable in your current scope.'): Resource<T> {
    const data = signal<T | null>(null);
    const error = signal('');
    const loading = signal(false);
    let sub: Subscription | undefined;
    const load = (target: string | null) => {
      sub?.unsubscribe();
      data.set(null);
      error.set('');
      if (!target || !this.session.current()?.staff && !target.startsWith('/api/ui/portal')) { loading.set(false); return; }
      const generation = this.session.invalidation();
      loading.set(true);
      sub = this.http.get<unknown>(target).pipe(timeout(20000)).subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          loading.set(false);
          try { data.set(decode(decoder, value)); } catch { error.set('Unsupported response. Refresh the page; if this persists the workspace version may be out of date.'); }
        },
        error: (e: unknown) => {
          if (generation !== this.session.invalidation()) return;
          loading.set(false);
          const status = e instanceof HttpErrorResponse ? e.status : 0;
          if (status === 401) { error.set('Your session ended. Sign in again.'); this.session.refresh(); }
          else error.set(status === 403 || status === 404 ? unavailable : status === 400 || status === 409 ? Api.message(e, unavailable) : 'Temporarily unavailable. Retry shortly.');
        },
      });
    };
    let current: string | null = null;
    effect(() => {
      const target = url();
      this.session.invalidation();
      this.session.current();
      untracked(() => { current = target; load(target); });
    });
    inject(DestroyRef).onDestroy(() => sub?.unsubscribe());
    return { data, error, loading, reload: () => load(current) };
  }

  /** On-demand read (e.g. a user-triggered calculation). Throws an Error whose message is safe to display. */
  async get<T>(url: string, decoder: Decoder<T>): Promise<T> {
    const generation = this.session.invalidation();
    let value: unknown;
    try {
      value = await firstValueFrom(this.http.get<unknown>(url).pipe(timeout(30000)));
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      if (status === 401) this.session.refresh();
      throw new Error(status === 400 || status === 403 ? Api.message(e, 'Not available in your current scope.') : 'Temporarily unavailable. Retry shortly.');
    }
    if (generation !== this.session.invalidation()) throw new Error('Your session changed. Load this information again after signing in.');
    try { return decode(decoder, value); } catch { throw new Error('Unsupported response.'); }
  }

  async command<T = unknown>(url: string, body: unknown = {}, method: 'POST' | 'PUT' | 'DELETE' = 'POST'): Promise<CommandOutcome<T>> {
    const generation = this.session.invalidation();
    try {
      const result = await firstValueFrom(this.http.request<{ value?: T }>(method, url, { body }).pipe(timeout(30000)));
      if (generation !== this.session.invalidation()) return { ok: false, unknown: true, code: 'outcome.unknown', message: UNKNOWN_OUTCOME, status: 0 };
      return { ok: true, value: (result?.value ?? (result as T)) as T };
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      if (generation !== this.session.invalidation()) return { ok: false, unknown: true, code: 'outcome.unknown', message: UNKNOWN_OUTCOME, status };
      if (status === 0 || status >= 500 || !(e instanceof HttpErrorResponse)) return { ok: false, unknown: true, code: 'outcome.unknown', message: UNKNOWN_OUTCOME, status };
      if (status === 401) this.session.refresh();
      const code = typeof e.error?.code === 'string' ? e.error.code : 'request.failed';
      const message = code === 'library.duplicate-code'
        ? 'That library code already exists. Choose another code or open the entry to prepare a new version.'
        : status === 409
          ? 'This record changed since you loaded it. Refresh and review the current revision.'
          : Api.message(e, 'The request was not accepted.');
      return { ok: false, unknown: false, code, status, message };
    }
  }

  /**
   * Generated-file download through an unsafe request (the server rechecks scope and may record the export). Saved via
   * an object URL; sent once. Returns the outcome so the page can state that the file is an instruction, not evidence.
   */
  async download(url: string, body: unknown = {}, accept?: (metadata: { fileName: string; headers: Record<string, string>; byteCount: number; contentType: string }) => boolean): Promise<CommandOutcome<{ fileName: string; headers: Record<string, string> }>> {
    const generation = this.session.invalidation();
    try {
      const response = await firstValueFrom(this.http.post(url, body, { observe: 'response', responseType: 'blob' }).pipe(timeout(60000)));
      if (generation !== this.session.invalidation()) return { ok: false, unknown: true, code: 'outcome.unknown', message: 'Your session changed. No file was saved.', status: 0 };
      const disposition = response.headers.get('Content-Disposition') ?? '';
      const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
      const fileName = match ? decodeURIComponent(match[1]) : 'download';
      const headers: Record<string, string> = {};
      for (const key of response.headers.keys()) headers[key.toLowerCase()] = response.headers.get(key) ?? '';
      if (!response.body || accept && !accept({ fileName, headers, byteCount: response.body.size, contentType: headers['content-type'] ?? '' }))
        return { ok: false, unknown: false, code: 'download.context', message: 'The file did not match the current reviewed source. No file was saved. Refresh before exporting.', status: 0 };
      const link = document.createElement('a');
      link.href = URL.createObjectURL(response.body!);
      link.download = fileName;
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 1000);
      return { ok: true, value: { fileName, headers } };
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      if (status === 0 || status >= 500) return { ok: false, unknown: true, code: 'outcome.unknown', message: 'The download did not complete. Retry when ready.', status };
      if (status === 401) await this.session.refresh();
      let message = 'The download was refused.';
      if (e instanceof HttpErrorResponse && e.error instanceof Blob) {
        try { const parsed = JSON.parse(await e.error.text()); if (typeof parsed?.message === 'string') message = parsed.message; } catch { /* keep generic */ }
      }
      return { ok: false, unknown: false, code: 'download.refused', message, status };
    }
  }

  /** Multipart upload; sent once, same outcome semantics as command. */
  upload<T = unknown>(url: string, form: FormData): Promise<CommandOutcome<T>> {
    return this.command<T>(url, form);
  }

  static message(e: unknown, fallback: string): string {
    const m = e instanceof HttpErrorResponse ? e.error?.message : undefined;
    return typeof m === 'string' && m.length > 0 && m.length < 1000 ? m : fallback;
  }
}

/** Busy/message state for a page's command buttons. One command at a time; never retried automatically. */
export class CommandState {
  readonly busy = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly uncertain = signal(false);
  constructor(private readonly api: Api) {}
  async run<T>(url: string, body: unknown, success: string, after?: (value: T) => void, method: 'POST' | 'PUT' | 'DELETE' = 'POST'): Promise<boolean> {
    if (this.busy() || this.uncertain()) return false;
    this.busy.set(true);
    this.message.set('');
    try {
      const r = await this.api.command<T>(url, body, method);
      this.failed.set(!r.ok);
      if (!r.ok && r.unknown) this.uncertain.set(true);
      this.message.set(r.ok ? success : r.message);
      if (r.ok) after?.(r.value);
      return r.ok;
    } finally {
      this.busy.set(false);
    }
  }
}

/** Route parameter as a signal; non-GUID values become null so pages never request malformed identifiers. */
export function routeGuid(name = 'id'): Signal<string | null> {
  const route = inject(ActivatedRoute);
  return toSignal(route.paramMap.pipe(map((p) => { const v = p.get(name); return v && guidPattern.test(v) ? v.toLowerCase() : null; })), { initialValue: null });
}
export function queryParam(name: string): Signal<string | null> {
  const route = inject(ActivatedRoute);
  return toSignal(route.queryParamMap.pipe(map((p) => p.get(name))), { initialValue: null });
}
