import { Component, DestroyRef, ElementRef, afterNextRender, inject, input, signal } from '@angular/core';

export interface PageSection { id: string; label: string }

/**
 * A sticky tab bar for long record pages. Every section stays rendered on the page; a tab scrolls to its section,
 * moves focus there for keyboard users and highlights the section currently in view.
 */
@Component({
  selector: 'audit-section-tabs',
  template: `
    <nav class="section-tabs" [attr.aria-label]="label()">
      @for (s of sections(); track s.id) {
        <a [attr.href]="'#' + s.id" [attr.aria-current]="active() === s.id ? 'location' : null" (click)="jump($event, s.id)">{{ s.label }}</a>
      }
    </nav>
  `,
  styles: `
    :host { position: sticky; top: var(--as-shell-header-height, 4rem); z-index: 6; display: block; margin: 0 0 1.25rem;
      background: var(--as-canvas, #f3f6f9); }
    .section-tabs { display: flex; gap: 0; margin: 0; overflow-x: auto; overflow-y: hidden; scrollbar-width: none;
      border-bottom: 1px solid var(--as-line-strong, #c7d2de); }
    a { flex: none; display: inline-flex; align-items: center; min-height: 44px; padding: 0 .9rem; color: var(--as-ink-soft, #315476);
      font-size: .875rem; font-weight: 550; text-decoration: none; white-space: nowrap; border-bottom: 2px solid transparent;
      margin-bottom: -1px; transition: color .15s, border-color .15s; }
    a:hover { color: var(--as-ink, #12385d); border-bottom-color: var(--as-line-strong, #c7d2de); }
    a[aria-current] { color: var(--as-accent-ink, #1e4fa8); border-bottom-color: var(--as-accent-ink, #1e4fa8); font-weight: 650; }
    a:focus-visible { outline-offset: -3px; }
    @media (max-width: 700px) { :host { top: 0; } }
    @media (prefers-reduced-motion: reduce) { a { transition: none; } }
  `,
})
export class SectionTabs {
  readonly sections = input.required<readonly PageSection[]>();
  readonly label = input('Sections on this page');
  readonly active = signal<string | null>(null);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private frame = 0;

  constructor() {
    const onScroll = () => {
      if (this.frame) return;
      this.frame = requestAnimationFrame(() => { this.frame = 0; this.track(); });
    };
    afterNextRender(() => {
      window.addEventListener('scroll', onScroll, { passive: true });
      this.track();
    });
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('scroll', onScroll);
      if (this.frame) cancelAnimationFrame(this.frame);
    });
  }

  /**
   * Jumps straight to the section, then briefly re-aligns it while sections that load on idle above it finish
   * rendering. An instant jump never passes through sections that load on view, so it does not trigger them.
   */
  jump(event: Event, id: string): void {
    const target = document.getElementById(id);
    event.preventDefault();
    if (!target) return;
    const align = () => {
      const offset = this.host.nativeElement.getBoundingClientRect().height + this.stickyTop() + 12;
      const top = target.getBoundingClientRect().top + window.scrollY - offset;
      if (Math.abs(top - window.scrollY) > 4) window.scrollTo({ top, behavior: 'auto' });
    };
    align();
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus({ preventScroll: true });
    this.active.set(id);
    let settles = 0;
    const stop = () => { settles = Number.MAX_SAFE_INTEGER; };
    for (const name of ['wheel', 'touchstart', 'keydown'] as const) window.addEventListener(name, stop, { once: true, passive: true });
    const settle = () => { if (settles++ < 6) { align(); setTimeout(settle, 200); } };
    setTimeout(settle, 200);
  }

  private stickyTop(): number {
    return parseFloat(getComputedStyle(this.host.nativeElement).top) || 0;
  }

  /** The last section whose top has passed the bottom of the tab bar is the one being read. */
  private track(): void {
    const line = this.host.nativeElement.getBoundingClientRect().bottom + 24;
    let current: string | null = null;
    for (const s of this.sections()) {
      const el = document.getElementById(s.id);
      if (!el) continue;
      if (current === null || el.getBoundingClientRect().top <= line) current = s.id;
      else break;
    }
    this.active.set(current);
  }
}
