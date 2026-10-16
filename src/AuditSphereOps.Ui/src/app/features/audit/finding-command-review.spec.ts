import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { FindingCommandReview } from './finding-command-review';
import { FindingCommandState } from './finding-command-state';
import { SessionService } from '../../core/session';
import { decodeFindingFields } from './finding-command-contracts';
import { findingTestId as id, findingTestPreview as preview, findingTestReceipt as receipt } from './finding-command-test-data';

describe('Exact finding review display', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('renders structured before/after and exact identity, with fresh explicit assent', async () => {
    const p = preview({ requestId: id(50), fields: { action: 'RECORD_RESPONSE', response: { managementResponse: 'Proposed exact response', corrected: false } } });
    const flow = { preview: signal(p), receipt: signal(null), pending: signal([]), absent: signal([]), reviewed: signal(false), busy: signal(false), uncertain: signal(false),
      message: signal(''), failed: signal(false), execute: vi.fn<() => void>() };
    TestBed.configureTestingModule({ imports: [FindingCommandReview], providers: [{ provide: SessionService, useValue: { current: signal({ userId: id(90) }) } }] });
    const fixture = TestBed.createComponent(FindingCommandReview); fixture.componentRef.setInput('flow', flow as unknown as FindingCommandState); await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.textContent).toContain('Response 1'); expect(host.textContent).toContain('Proposed exact response'); expect(host.textContent).toContain(p.requestHash);
    expect(host.textContent).toContain('does not establish an independent review'); expect(host.querySelector('button')!.disabled).toBe(true);
    const assent = host.querySelector<HTMLInputElement>('input[type=checkbox]')!; assent.click(); await fixture.whenStable();
    expect(flow.reviewed()).toBe(true); expect(host.querySelector('button')!.disabled).toBe(false); host.querySelector('button')!.click(); expect(flow.execute).toHaveBeenCalledTimes(1);
  });
  it('renders designation and removal effects without inventing a reviewer or commit timestamp', () => {
    TestBed.configureTestingModule({ imports: [FindingCommandReview], providers: [{ provide: SessionService, useValue: { current: signal({ userId: id(90) }) } }] });
    const fixture = TestBed.createComponent(FindingCommandReview), component = fixture.componentInstance;
    const p = preview({ requestId: id(50), fields: { action: 'DESIGNATE_MANAGEMENT_LETTER', designation: { recommendation: 'Exact letter recommendation' } } });
    expect(component.rows(p)).toContainEqual({ label: 'Designation recorded by', before: 'None', after: id(90) });
    expect(component.rows(p)).toContainEqual({ label: 'Letter recommendation', before: 'None', after: 'Exact letter recommendation' });
    const r = receipt(p);
    const removal = { ...p, before: r.amendment.after, canonicalInput: decodeFindingFields({ action: 'UNDESIGNATE_MANAGEMENT_LETTER' }, '') };
    expect(component.rows(removal)).toContainEqual({ label: 'Letter recommendation', before: 'Exact letter recommendation', after: 'None' });
  });
});
