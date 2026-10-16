/** jsdom has no viewport geometry or IntersectionObserver. Keep viewport defers pending
 * until a spec explicitly emits an intersection or renders its Angular defer fixture.
 * Never replace a real browser observer and never change production defer triggers.
 */
export class TestIntersectionObserver implements IntersectionObserver {
  readonly root: Element | Document | null;
  readonly rootMargin: string;
  readonly scrollMargin: string;
  readonly thresholds: readonly number[];
  private readonly targets = new Set<Element>();

  constructor(private readonly callback: IntersectionObserverCallback, options: IntersectionObserverInit = {}) {
    this.root = options.root ?? null;
    this.rootMargin = options.rootMargin ?? '0px 0px 0px 0px';
    this.scrollMargin = options.scrollMargin ?? '0px 0px 0px 0px';
    const threshold = options.threshold ?? 0;
    this.thresholds = [...new Set(Array.isArray(threshold) ? threshold : [threshold])].sort((a, b) => a - b);
  }
  observe(target: Element): void { this.targets.add(target); }
  unobserve(target: Element): void { this.targets.delete(target); }
  disconnect(): void { this.targets.clear(); }
  takeRecords(): IntersectionObserverEntry[] { return []; }

  /** Explicit test control; observation alone never implies visibility in jsdom. */
  emit(target: Element, isIntersecting = true): void {
    if (!this.targets.has(target)) return;
    const bounds = target.getBoundingClientRect();
    this.callback([{
      target, isIntersecting, intersectionRatio: isIntersecting ? 1 : 0,
      time: performance.now(), boundingClientRect: bounds, rootBounds: null,
      intersectionRect: isIntersecting ? bounds : new DOMRect(),
    }], this);
  }
}

if (typeof globalThis.IntersectionObserver === 'undefined') {
  globalThis.IntersectionObserver = TestIntersectionObserver;
}
