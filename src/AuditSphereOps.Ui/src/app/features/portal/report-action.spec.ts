import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { PortalReportAction } from './report-action';

const id = (n: number) => `40000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const digest = 'b'.repeat(64);
const composition = (n: number) => ({
  presentationRevisionId: id(n + 100), compositionId: id(n + 200), compositionManifestDigest: digest,
  pdfSha256Hex: digest, wordSha256Hex: digest, excelSha256Hex: digest, selectedAt: '2026-10-15T10:00:00Z',
});
const packageDetail = (packageId: string, managementDecision = 'PENDING', currentComposition: ReturnType<typeof composition> | null = null) => ({
  packageId, framework: 'Synthetic framework', periodStart: '2026-01-01', periodEnd: '2026-12-31', currency: 'QAR',
  templateVersion: '1', packageHash: digest, hasCashFlow: false, hasDisclosures: false,
  managementDecision, managementDecidedAt: null, statementTotals: [], composition: currentComposition, latestDecisionId: null,
});

describe('Portal report action card', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PortalReportAction],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: false });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); }
  });

  it('shows only the server current report invitation and follows refreshed composition state', () => {
    const fixture = TestBed.createComponent(PortalReportAction);
    fixture.componentRef.setInput('packageId', id(10));
    fixture.detectChanges(); TestBed.tick();
    const path = `/api/ui/portal/accounting/packages/${id(10)}`;
    http.expectOne(path).flush(packageDetail(id(10), 'PENDING', null));
    TestBed.tick(); fixture.detectChanges();
    let root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('no current sealed report invitation');
    expect(root.textContent).toContain('View package status');
    expect(root.textContent).not.toContain('report response requested');
    expect(root.querySelector('a')?.getAttribute('href')).toBe(`/portal/accounting/packages/${id(10)}`);

    fixture.componentInstance.workspace.reload(); TestBed.tick();
    http.expectOne(path).flush(packageDetail(id(10), 'PENDING', composition(10)));
    TestBed.tick(); fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('report response requested');
    expect(root.textContent).toContain('Review exact report');

    fixture.componentInstance.workspace.reload(); TestBed.tick();
    http.expectOne(path).flush(packageDetail(id(10), 'APPROVED', composition(11)));
    TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).toContain('management response recorded');
    expect(root.textContent).toContain('View report response');
    expect(root.textContent).not.toContain('Review exact report');
    http.expectNone((request) => request.method !== 'GET');
    fixture.destroy();
  });

  it('fails closed for a wrong package identity, malformed response, or forbidden read', () => {
    const fixture = TestBed.createComponent(PortalReportAction);
    fixture.componentRef.setInput('packageId', id(20));
    fixture.detectChanges(); TestBed.tick();
    const path = `/api/ui/portal/accounting/packages/${id(20)}`;
    http.expectOne(path).flush(packageDetail(id(21), 'APPROVED', composition(20)));
    TestBed.tick(); fixture.detectChanges();
    let root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Report action unavailable. Refresh your portal.');
    expect(root.querySelector('a')).toBeNull();
    expect(root.textContent).not.toContain('Synthetic framework');

    fixture.componentInstance.workspace.reload(); TestBed.tick();
    http.expectOne(path).flush({ ...packageDetail(id(20)), managementDecision: 7 });
    TestBed.tick(); fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Report action unavailable. Refresh your portal.');
    expect(root.querySelector('a')).toBeNull();

    fixture.componentInstance.workspace.reload(); TestBed.tick();
    http.expectOne(path).flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).toContain('Report action unavailable. Refresh your portal.');
    expect(root.querySelector('a')).toBeNull();
    fixture.destroy();
  });

  it('cancels a pending read and clears the card when the client identity changes', () => {
    const fixture = TestBed.createComponent(PortalReportAction);
    fixture.componentRef.setInput('packageId', id(30));
    fixture.detectChanges(); TestBed.tick();
    const pending = http.expectOne(`/api/ui/portal/accounting/packages/${id(30)}`);
    TestBed.inject(SessionService).current.set(null);
    TestBed.tick(); fixture.detectChanges();
    expect(pending.cancelled).toBe(true);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a')).toBeNull();
    expect(root.textContent).not.toContain('Synthetic framework');
    fixture.destroy();
  });
});
