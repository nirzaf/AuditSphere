import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { COMMUNICATION_STATUS_LABELS, decodeEngagementCommunications, EngagementCommunicationsPanel } from './engagement-communications';

const engagementId = '0d3b6a53-7d0a-4b47-9d53-3a2c5c1f4b11';
const entry = (overrides: Record<string, unknown> = {}) => ({
  id: '5a1c1f6e-2f0b-4a0e-8a53-0a1b2c3d4e5f',
  source: 'PBC',
  kind: 'PBC request email',
  subject: 'Bank confirmations',
  recipient: 'liaison@example.test',
  status: 'PROVIDER_ACCEPTED',
  recordedAtUtc: '2026-10-12T07:00:00Z',
  deliveredAtUtc: null,
  sourceLink: `/app/engagements/${engagementId}/pbc`,
  ...overrides,
});

describe('engagement communications contract', () => {
  it('decodes a recorded timeline', () => {
    const timeline = decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry()] });
    expect(timeline.entries[0].status).toBe('PROVIDER_ACCEPTED');
  });

  it('requires a natural total covering the entries and unique source/id pairs', () => {
    expect(() => decodeEngagementCommunications({ engagementId, totalCount: -1, entries: [] })).toThrow();
    expect(() => decodeEngagementCommunications({ engagementId, totalCount: 0, entries: [entry()] })).toThrow();
    expect(() => decodeEngagementCommunications({
      engagementId, totalCount: 2, entries: [entry(), entry({ id: entry().id.toUpperCase() })],
    })).toThrow();

    const sameIdFromAnotherSource = entry({ source: 'COMMERCIAL', sourceLink: '/app/commercial/opportunities' });
    expect(decodeEngagementCommunications({ engagementId, totalCount: 2, entries: [entry(), sameIdFromAnotherSource] }).entries)
      .toHaveLength(2);
  });

  it('refuses an unknown status and a link that leaves the application', () => {
    expect(() => decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry({ status: 'READ' })] })).toThrow();
    expect(() =>
      decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry({ sourceLink: 'https://example.test/x' })] }),
    ).toThrow();
    expect(() =>
      decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry({ sourceLink: '/app/../admin' })] }),
    ).toThrow();
  });

  it('never words provider acceptance or dispatch as delivery', () => {
    expect(COMMUNICATION_STATUS_LABELS['PROVIDER_ACCEPTED']).not.toMatch(/deliver/i);
    expect(COMMUNICATION_STATUS_LABELS['DISPATCHED']).not.toMatch(/deliver/i);
    expect(COMMUNICATION_STATUS_LABELS['RECORDED']).not.toMatch(/deliver/i);
  });
});

describe('engagement communications panel', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EngagementCommunicationsPanel],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: engagementId, firmId: engagementId, generation: '1', staff: true });
  });

  afterEach(() => {
    try { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); }
  });

  it('renders accessible table relationships and distinguishes same-ID sources', () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EngagementCommunicationsPanel);
    fixture.componentRef.setInput('engagementId', engagementId);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/communications`).flush({
      engagementId,
      totalCount: 2,
      entries: [entry(), entry({ source: 'COMMERCIAL', sourceLink: '/app/commercial/opportunities' })],
    });
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const region = root.querySelector<HTMLElement>('[role="region"]')!;
    expect(region.getAttribute('aria-label')).toBe('Recorded communications table; scroll horizontally if needed');
    expect(region.tabIndex).toBe(0);
    expect(region.getAttribute('aria-describedby')).toBe('communications-table-scroll-help');
    expect(root.querySelector('#communications-table-scroll-help')?.textContent).toContain('left and right arrow keys');
    expect([...root.querySelectorAll('thead th')].map((header) => header.getAttribute('scope')))
      .toEqual(['col', 'col', 'col', 'col', 'col', 'col']);
    expect(root.querySelectorAll('tbody tr')).toHaveLength(2);

    const dateHeader = root.querySelector<HTMLElement>('tbody th[scope="row"]')!;
    expect(dateHeader.querySelector('time')?.getAttribute('datetime')).toBe('2026-10-12T07:00:00Z');
    const sourceLinks = [...root.querySelectorAll<HTMLAnchorElement>('tbody a')];
    expect(sourceLinks[0].getAttribute('aria-label')).toContain('PBC source for PBC request email, Bank confirmations, recorded 2026-10-12 07:00 UTC');
    expect(sourceLinks[1].getAttribute('aria-label')).toContain('COMMERCIAL source for PBC request email, Bank confirmations, recorded 2026-10-12 07:00 UTC');

    fixture.destroy();
  });
});
