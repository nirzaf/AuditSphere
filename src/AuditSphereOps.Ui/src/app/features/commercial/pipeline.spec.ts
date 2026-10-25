import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { CommercialPipeline, decodePipeline } from './pipeline';

const id = (n: number) => `00000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const card = (n: number, currency = 'QAR', fee = '100.00') => ({
  id: id(n), leadId: id(900), leadName: 'Pipeline lead', clientId: null, clientName: null, serviceRoute: 'FinancialStatementAudit',
  periodStart: '2026-01-01', periodEnd: '2026-12-31', expectedFee: fee, currency, probability: null, ownerUserId: null, ownerName: null,
  ownerState: 'UNASSIGNED', nextAction: null, createdAt: `2026-10-${(25 - n % 20).toString().padStart(2, '0')}T08:00:00+00:00`,
  proposalId: null, proposalRevision: null, proposalStatus: null, proposalRevisions: 0,
});
const empty = (stage: string) => ({ stage, count: 0, totals: [], unavailableCurrencyCount: 0, items: [], hasMore: false });
const board = () => ({
  pageSize: 2,
  stages: [
    { stage: 'DISCOVERY', count: 4, totals: [{ currency: 'QAR', count: 3, expectedFee: '300.00' }, { currency: 'USD', count: 1, expectedFee: '2500.50' }],
      unavailableCurrencyCount: 0, items: [card(1), card(2)], hasMore: true },
    { stage: 'PROPOSAL', count: 1, totals: [{ currency: 'QAR', count: 1, expectedFee: '15000.00' }], unavailableCurrencyCount: 0,
      items: [{ ...card(5, 'QAR', '15000.00'), proposalId: id(50), proposalRevision: '2', proposalStatus: 'DRAFT', proposalRevisions: 2 }], hasMore: false },
    empty('NEGOTIATION'), empty('WON'), empty('LOST'),
  ],
});

describe('pipeline decoder', () => {
  it('accepts a coherent board and keeps amounts as exact strings', () => {
    const decoded = decodePipeline(board(), 'pipeline');
    expect(decoded.stages[0].totals[1]).toEqual({ currency: 'USD', count: 1, expectedFee: '2500.50' });
  });

  it('refuses totals that do not account for the count, merged currencies or a short page that claims more', () => {
    const value = board();
    expect(() => decodePipeline({ ...value, stages: [{ ...value.stages[0], count: 5 }, ...value.stages.slice(1)] }, 'p')).toThrow();
    expect(() => decodePipeline({ ...value, stages: [{ ...value.stages[0], totals: [{ currency: 'QAR', count: 3, expectedFee: '300.00' },
      { currency: 'QAR', count: 1, expectedFee: '1.00' }] }, ...value.stages.slice(1)] }, 'p')).toThrow();
    expect(() => decodePipeline({ ...value, stages: [{ ...value.stages[0], items: [card(1)] }, ...value.stages.slice(1)] }, 'p')).toThrow();
    expect(() => decodePipeline({ ...value, stages: [{ ...value.stages[0], totals: [{ currency: 'qar', count: 3, expectedFee: '300.00' },
      { currency: 'USD', count: 1, expectedFee: '2500.50' }] }, ...value.stages.slice(1)] }, 'p')).toThrow();
  });
});

describe('CommercialPipeline', () => {
  function render() {
    TestBed.configureTestingModule({ imports: [CommercialPipeline], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: true });
    const fixture = TestBed.createComponent(CommercialPipeline);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); TestBed.tick();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('shows complete per-currency totals and offers no stage-changing control', () => {
    const { fixture, http, host } = render();
    try {
      http.expectOne('/api/ui/pipeline').flush(board());
      fixture.detectChanges();
      const discovery = host.querySelector('section[aria-labelledby="pipeline-DISCOVERY"]')!;
      expect(discovery.textContent).toContain('300.00 QAR · 3 opportunities');
      expect(discovery.textContent).toContain('2,500.50 USD · 1 opportunity');
      expect(discovery.textContent).toContain('Showing 2 of 4');
      const proposal = host.querySelector('section[aria-labelledby="pipeline-PROPOSAL"]')!;
      expect(proposal.textContent).toMatch(/Proposal revision 2\s*· Draft · 2 revisions/);
      expect(host.textContent).toContain('nothing is converted or weighted by probability');
      const buttons = [...host.querySelectorAll('button')].map(b => b.textContent?.trim());
      expect(buttons).toEqual(['Apply filters', 'Clear filters', 'Show more discovery']);
      expect(host.querySelector('[draggable="true"]')).toBeNull();
      const before = [...host.querySelectorAll('[data-opportunity-id]')].map(row => row.getAttribute('data-opportunity-id'));
      fixture.componentInstance.display.set('table');
      fixture.detectChanges();
      expect([...host.querySelectorAll('[data-opportunity-id]')].map(row => row.getAttribute('data-opportunity-id'))).toEqual(before);
      expect(host.querySelector('table caption')?.textContent).toContain('same loaded records as the board');
      fixture.componentInstance.stageScope.set('open');
      fixture.detectChanges();
      expect(host.querySelector('section[aria-labelledby="pipeline-WON"]')).toBeNull();
      http.expectNone(r => r.method !== 'GET');
      http.verify();
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('continues a stage from its last card and reloads instead of mixing a refused continuation', async () => {
    const { fixture, http, host } = render();
    try {
      http.expectOne('/api/ui/pipeline').flush(board());
      fixture.detectChanges();
      const component = fixture.componentInstance;
      const discovery = component.columns()![0];
      const pending = component.loadMore(discovery);
      const next = http.expectOne(request => request.url.startsWith('/api/ui/pipeline?stage=DISCOVERY'));
      const params = new URL(next.request.url, 'http://localhost').searchParams;
      expect([params.get('beforeId'), params.get('beforeCreatedAt')]).toEqual([id(2), card(2).createdAt]);
      next.flush({ pageSize: 2, stages: [{ ...board().stages[0], items: [card(3), card(4, 'USD', '2500.50')], hasMore: false }] });
      await pending;
      fixture.detectChanges();
      expect(component.columns()![0].items.map(item => item.id)).toEqual([id(1), id(2), id(3), id(4)]);
      expect(host.textContent).toContain('Showing 4 of 4');

      component.draft.currency = 'usd';
      component.apply(); TestBed.tick();
      const filtered = http.expectOne('/api/ui/pipeline?currency=USD');
      filtered.flush({ pageSize: 2, stages: [{ ...board().stages[0], count: 3, totals: [{ currency: 'USD', count: 3, expectedFee: '3.00' }],
        items: [card(6, 'USD', '1.00'), card(7, 'USD', '1.00')] }, ...board().stages.slice(1).map(s => empty(s.stage))] });
      fixture.detectChanges();
      const stale = component.loadMore(component.columns()![0]);
      http.expectOne(request => request.url.includes('currency=USD') && request.url.includes('stage=DISCOVERY'))
        .flush({ code: 'request.invalid', message: 'The pipeline continuation no longer matches. Reload the board.' }, { status: 400, statusText: 'Bad Request' });
      await stale;
      TestBed.tick();
      expect(component.moreError()).toContain('The board was reloaded.');
      http.expectOne('/api/ui/pipeline?currency=USD');
      http.verify();
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
