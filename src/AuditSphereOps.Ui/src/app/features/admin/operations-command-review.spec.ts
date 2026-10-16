import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OperationsCommandReview } from './operations-command-review';
import { decodeOperationPreview, decodeOperationReceipt } from './operations-command-contracts';

const id = '11111111-1111-4111-8111-111111111111', hash = 'a'.repeat(64), epoch = '9007199254740993';
const before = { operatingMode: 'RECOVERY_QUARANTINE', deploymentEpoch: epoch, recoveryEpoch: '0', policyGeneration: '1', operation: null, recoverySession: null };
const preview = { requestId: id, requestHash: hash, reviewBasis: hash, canonicalInput: { action: 'LIFT_QUARANTINE' }, before,
  canProceed: true, blockers: [], summary: 'Lift only this exact quarantine.' };

describe('Operator review and original event evidence', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('renders meaningful before/effect/hash evidence and requires explicit assent', () => {
    TestBed.configureTestingModule({ imports: [OperationsCommandReview] });
    const flow = { preview: signal(decodeOperationPreview(preview, '')), receipt: signal(null), busy: signal(false), reviewed: signal(false),
      uncertain: signal(false), pending: signal(null), absent: signal(false), failed: signal(false), message: signal(''), execute: vi.fn() };
    const f = TestBed.createComponent(OperationsCommandReview); f.componentRef.setInput('flow', flow); f.detectChanges();
    const root = f.nativeElement as HTMLElement; expect(root.textContent).toContain(epoch); expect(root.textContent).toContain(hash);
    expect(root.textContent).toContain('RECOVERY_QUARANTINE'); expect(root.textContent).toContain('Before this command'); expect(root.textContent).not.toContain('StateJson');
    expect((root.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
    flow.reviewed.set(true); f.detectChanges(); expect((root.querySelector('button') as HTMLButtonElement).disabled).toBe(false);
  });
  it('moves keyboard focus to the review heading when a new exact preview is presented', async () => {
    TestBed.configureTestingModule({ imports: [OperationsCommandReview] });
    const flow = { preview: signal<ReturnType<typeof decodeOperationPreview> | null>(null), receipt: signal(null), busy: signal(false), reviewed: signal(false),
      uncertain: signal(false), pending: signal(null), absent: signal(false), failed: signal(false), message: signal(''), execute: vi.fn() };
    const f = TestBed.createComponent(OperationsCommandReview); f.componentRef.setInput('flow', flow); f.detectChanges();
    flow.preview.set(decodeOperationPreview(preview, ''));
    f.detectChanges(); await f.whenStable();
    const heading = (f.nativeElement as HTMLElement).querySelector('h2[tabindex="-1"]');
    expect(heading?.textContent).toContain('Review operator command');
    expect(document.activeElement).toBe(heading);
  });
  it('renders original before/after events and warns that queue advancement is not provider success', () => {
    TestBed.configureTestingModule({ imports: [OperationsCommandReview] });
    const receipt = decodeOperationReceipt({ id, actorId: id, actorEpoch: epoch, requestId: id, requestHash: hash, action: 'LIFT_QUARANTINE', createdAt: '2026-10-16T00:00:00Z', preview,
      commandEvent: { id, actorId: id, actorEpoch: epoch, action: 'LIFT_QUARANTINE', operationEventId: null, createdAt: '2026-10-16T00:00:00Z', before,
        after: { ...before, operatingMode: 'LOCAL_ONLY', deploymentEpoch: '9007199254740994' } } }, '');
    const flow = { preview: signal(null), receipt: signal(receipt), uncertain: signal(false), message: signal('') };
    const f = TestBed.createComponent(OperationsCommandReview); f.componentRef.setInput('flow', flow); f.detectChanges();
    const text = (f.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('Recorded before'); expect(text).toContain('Recorded after'); expect(text).toContain('RECOVERY_QUARANTINE'); expect(text).toContain('LOCAL_ONLY');
    expect(text).toContain(epoch); expect(text).toContain('9007199254740994'); expect(text).toContain('provider success is not implied');
  });
});
