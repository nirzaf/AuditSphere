import { DestroyRef, inject, signal } from '@angular/core';
import { SessionService } from '../../core/session';
import { DraftScope, TabDrafts } from '../../core/tab-drafts';

/** Pre-dispatch fields only. No review assent, executable request, file, or calculation result is retained. */
export class CommercialFormDraft<T> {
  private readonly store = inject(TabDrafts);
  private readonly session = inject(SessionService);
  readonly scope = signal<DraftScope | null>(null);
  private baseline: string | null = null;
  private sequence = 0;
  private alive = true;
  constructor(
    private readonly fields: () => T,
    private readonly validate: (value: unknown) => T | null,
  ) {
    const unload = (e: BeforeUnloadEvent) => {
      if (this.dirty()) {
        e.preventDefault();
        e.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', unload);
    inject(DestroyRef).onDestroy(() => {
      this.alive = false;
      ++this.sequence;
      window.removeEventListener('beforeunload', unload);
    });
  }
  reset(): void {
    ++this.sequence;
    this.scope.set(null);
    this.baseline = null;
  }
  async bind(entity: string, basis: unknown): Promise<void> {
    const sequence = ++this.sequence,
      epoch = this.session.invalidation();
    this.scope.set(null);
    if (this.baseline === null) this.baseline = JSON.stringify(this.fields());
    const bytes = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode(JSON.stringify(basis)),
    );
    if (!this.alive || sequence !== this.sequence || epoch !== this.session.invalidation()) return;
    this.scope.set({
      entity,
      baseRevision: Array.from(new Uint8Array(bytes), (x) => x.toString(16).padStart(2, '0')).join(
        '',
      ),
    });
  }
  dirty(): boolean {
    return this.baseline !== null && this.baseline !== JSON.stringify(this.fields());
  }
  save(): boolean {
    const scope = this.scope();
    return !!scope && this.store.save(scope, this.fields(), this.validate);
  }
  recover(): T | null {
    const scope = this.scope();
    if (!scope) return null;
    const r = this.store.read(scope, this.validate);
    return r.state === 'ready' && !r.draft.submissionPending ? r.draft.value : null;
  }
  discard(): boolean {
    const scope = this.scope();
    return !scope || this.store.clear(scope.entity);
  }
  submitted(): void {
    this.baseline = JSON.stringify(this.fields());
    this.discard();
  }
}
export function textFields<const K extends string>(
  value: unknown,
  limits: Record<K, number>,
): Record<K, string> | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const v = value as Record<string, unknown>,
    result = {} as Record<K, string>;
  for (const key of Object.keys(limits) as K[]) {
    if (typeof v[key] !== 'string' || v[key].length > limits[key]) return null;
    result[key] = v[key];
  }
  return result;
}
