import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { WorkflowEvidenceSourcePicker, type WorkflowEvidenceSourceKind } from './workflow-evidence-source-picker';

const firmId = '11111111-1111-4111-8111-111111111111';
const engagementId = '22222222-2222-4222-8222-222222222222';
const otherEngagementId = '66666666-6666-4666-8666-666666666666';
const selectedId = '33333333-3333-4333-8333-333333333333';
const firstId = '44444444-4444-4444-8444-444444444444';
const secondId = '55555555-5555-4555-8555-555555555555';
const kind: WorkflowEvidenceSourceKind = 'PBC_ACCEPTED_OR_CLOSED';
const stepTitle = 'Step 2: Send request';
const requirementKey = 'request-evidence';
const pickerId = 'workflow-evidence-step-2-request-evidence';

function page(pageNumber: number, items: unknown[], selected: unknown, hasMore = false) {
  return { engagementId, kind, page: pageNumber, pageSize: 25, hasMore, items, selected };
}

describe('workflow evidence source picker', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkflowEvidenceSourcePicker],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '1', staff: true });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  function setup(selected = selectedId) {
    const fixture = TestBed.createComponent(WorkflowEvidenceSourcePicker);
    fixture.componentRef.setInput('engagementId', engagementId);
    fixture.componentRef.setInput('kind', kind);
    fixture.componentRef.setInput('stepTitle', stepTitle);
    fixture.componentRef.setInput('requirementKey', requirementKey);
    fixture.componentRef.setInput('pickerId', pickerId);
    fixture.componentRef.setInput('selectedId', selected);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }

  it('loads only on open, retains the exact selected source across search and paging, and emits a result id', () => {
    const fixture = setup();
    const http = TestBed.inject(HttpTestingController);
    http.expectNone(r => r.url.includes('/workflow-evidence-sources'));

    const host = fixture.nativeElement as HTMLElement;
    const open = host.querySelector<HTMLButtonElement>(
      '[aria-label="Open evidence source search for request — Step 2: Send request — request-evidence"]',
    )!;
    open.click();
    fixture.detectChanges();
    TestBed.tick();
    expect(open.isConnected).toBe(true);
    expect(open.getAttribute('aria-expanded')).toBe('true');
    expect(open.getAttribute('aria-controls')).toBe(`${pickerId}-panel`);

    const url = `/api/ui/engagements/${engagementId}/workflow-evidence-sources?kind=${kind}&query=&page=0&pageSize=25&selectedId=${selectedId}`;
    http.expectOne({ method: 'GET', url }).flush(page(0, [
      { id: firstId, label: 'Quarterly support', state: 'SENT', revision: '9223372036854775807' },
    ], { id: selectedId, label: 'Existing request', state: 'CLOSED', revision: null }, true));
    fixture.detectChanges();

    expect(host.textContent).toContain('Current selection: Existing request · Recorded state CLOSED');
    expect(host.textContent).toContain('Revision 9223372036854775807');
    expect(host.textContent).toContain(firstId);
    expect(host.querySelector(`label[for="${pickerId}-query"]`)?.textContent?.trim())
      .toBe(`Find evidence source for request — ${stepTitle} — ${requirementKey}`);
    expect(host.querySelector('input[type="search"]')?.getAttribute('maxlength')).toBe('120');
    const emitted: string[] = [];
    fixture.componentInstance.sourceSelected.subscribe(id => emitted.push(id));
    const staleItem = fixture.componentInstance.sources.data()!.items[0];

    const search = host.querySelector<HTMLInputElement>(`#${pickerId}-query`)!;
    search.value = '  quarterly  ';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.componentInstance.select(staleItem);
    expect(emitted).toEqual([]);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne({ method: 'GET', url: `/api/ui/engagements/${engagementId}/workflow-evidence-sources?kind=${kind}&query=quarterly&page=0&pageSize=25&selectedId=${selectedId}` })
      .flush(page(0, [{ id: firstId, label: 'Quarterly support', state: 'ACCEPTED', revision: '7' }],
        { id: selectedId, label: 'Existing request', state: 'CLOSED', revision: null }, true));
    fixture.detectChanges();

    const next = host.querySelector<HTMLButtonElement>(`nav[aria-label="Evidence source pages for ${stepTitle} — ${requirementKey}"] button:last-child`)!;
    next.click();
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne({ method: 'GET', url: `/api/ui/engagements/${engagementId}/workflow-evidence-sources?kind=${kind}&query=quarterly&page=1&pageSize=25&selectedId=${selectedId}` })
      .flush(page(1, [{ id: secondId, label: 'Quarterly close evidence', state: 'CLOSED', revision: '8' }],
        { id: selectedId, label: 'Existing request', state: 'CLOSED', revision: null }));
    fixture.detectChanges();

    const result = host.querySelector<HTMLButtonElement>(`button[aria-label="Select evidence source for request: Quarterly close evidence (${secondId}) — ${stepTitle} — ${requirementKey}"]`)!;
    result.click();
    expect(emitted).toEqual([secondId]);
    open.click();
    fixture.detectChanges();
    TestBed.tick();
    expect(open.isConnected).toBe(true);
    expect(open.getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector<HTMLElement>(`#${pickerId}-panel`)?.hidden).toBe(true);
    fixture.componentRef.setInput('engagementId', otherEngagementId);
    fixture.detectChanges();
    TestBed.tick();
    expect(fixture.componentInstance.opened()).toBe(false);
    expect(fixture.componentInstance.query()).toBe('');
    expect(fixture.componentInstance.searchDraft()).toBe('');
    expect(fixture.componentInstance.page()).toBe(0);
    http.expectNone(r => r.method === 'GET' && r.url.includes('/workflow-evidence-sources'));
    http.expectNone(r => r.method === 'POST');
  });

  it('rejects a response from another engagement or evidence kind', () => {
    const fixture = setup();
    const http = TestBed.inject(HttpTestingController);
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('[aria-label^="Open evidence source search"]')!.click();
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(r => r.method === 'GET' && r.url.includes('/workflow-evidence-sources'))
      .flush({ ...page(0, [{ id: firstId, label: 'Request', state: 'SENT', revision: '1' }],
        { id: selectedId, label: 'Existing request', state: 'CLOSED', revision: null }), engagementId: firmId });
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Unsupported response');
    expect(host.querySelector(`button[aria-label^="Select evidence source"]`)).toBeNull();

    fixture.componentInstance.sources.reload();
    TestBed.tick();
    http.expectOne(r => r.method === 'GET' && r.url.includes('/workflow-evidence-sources'))
      .flush({ ...page(0, [{ id: firstId, label: 'Request', state: 'SENT', revision: '1' }],
        { id: selectedId, label: 'Existing request', state: 'CLOSED', revision: null }), kind: 'CURRENT_PROCEDURE_REVIEWED' });
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Unsupported response');
  });

  it('rejects noncanonical revision strings without converting them to numbers', () => {
    const fixture = setup();
    const http = TestBed.inject(HttpTestingController);
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('[aria-label^="Open evidence source search"]')!.click();
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(r => r.method === 'GET' && r.url.includes('/workflow-evidence-sources'))
      .flush(page(0, [{ id: firstId, label: 'Request', state: 'SENT', revision: '01' }],
        { id: selectedId, label: 'Existing request', state: 'CLOSED', revision: null }));
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Unsupported response');
    expect(host.querySelector('button[aria-label^="Select evidence source"]')).toBeNull();
  });

  it('omits an invalid manual reference from selectedId while keeping source lookup available', () => {
    const fixture = setup('manual-reference');
    const http = TestBed.inject(HttpTestingController);
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('[aria-label^="Open evidence source search"]')!.click();
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne({ method: 'GET', url:
      `/api/ui/engagements/${engagementId}/workflow-evidence-sources?kind=${kind}&query=&page=0&pageSize=25` })
      .flush(page(0, [{ id: firstId, label: 'Request', state: 'SENT', revision: '1' }], null));
    fixture.detectChanges();
    expect(host.querySelector('.current-selection strong')?.textContent).toBe('Current source reference:');
    expect(host.querySelector('.current-selection code')?.textContent).toBe('manual-reference');
  });
});
