import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ReviewPointCommandReview } from './review-point-command-review';
import { ReviewPointCommandState } from './review-point-command-state';
import { reviewPointTestId as id, reviewPointTestPreview as preview } from './review-point-command-test-data';

describe('Review point exact administrative review display', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('renders before/after, separate revisions and target identity with fresh assent', async () => {
    const p = preview({ requestId: id(50), fields: { action: 'CLEAR' } });
    const flow = { preview: signal(p), receipt: signal(null), pending: signal([]), absent: signal([]), reviewed: signal(false), busy: signal(false), uncertain: signal(false),
      message: signal(''), failed: signal(false), execute: vi.fn<() => void>() };
    TestBed.configureTestingModule({ imports: [ReviewPointCommandReview] });
    const f = TestBed.createComponent(ReviewPointCommandReview); f.componentRef.setInput('flow', flow as unknown as ReviewPointCommandState); await f.whenStable();
    const host: HTMLElement = f.nativeElement;
    expect(host.textContent).toContain(p.before.targetId); expect(host.textContent).toContain('target revision 7');
    expect(host.textContent).toContain('Disposition revision'); expect(host.textContent).toContain('BLOCKING'); expect(host.textContent).toContain('CLEARED');
    expect(host.textContent).toContain('does not establish independent review'); expect(host.querySelector('button')!.disabled).toBe(true);
    host.querySelector<HTMLInputElement>('input[type=checkbox]')!.click(); await f.whenStable(); expect(flow.reviewed()).toBe(true);
    expect(host.querySelector('button')!.disabled).toBe(false); host.querySelector('button')!.click(); expect(flow.execute).toHaveBeenCalledTimes(1);
  });
});
