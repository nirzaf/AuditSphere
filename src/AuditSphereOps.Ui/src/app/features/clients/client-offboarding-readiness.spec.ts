import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { ClientOffboardingReadiness } from './client-offboarding-readiness';
import { decodeOffboardingReadiness } from './client-contracts';

const clientId = '11111111-1111-4111-8111-111111111111';
const otherId = '22222222-2222-4222-8222-222222222222';
const body = (id = clientId) => ({
  clientId: id, isArchived: false, archiveRevision: '0', assessedAt: '2026-10-31T10:00:00Z', overallState: 'ACTION_REQUIRED',
  sections: [
    { key: 'WORK', title: 'Open work', state: 'ACTION_REQUIRED', ownerRoute: '/app/work',
      items: [{ key: 'OPEN_TASKS', label: 'Open or in-progress tasks', state: 'ACTION_REQUIRED', count: 2, detail: null }] },
    { key: 'FINANCE', title: 'Money', state: 'UNKNOWN', ownerRoute: null,
      items: [{ key: 'OUTSTANDING_INVOICES', label: 'Posted invoices with an outstanding balance', state: 'UNKNOWN', count: null, detail: 'Your access does not include firm finance for this client.' }] },
  ],
  coverageNotes: ['NO_FINDINGS is not evidence that offboarding is complete.'],
});

describe('client offboarding readiness', () => {
  it('rejects an UNKNOWN item that carries a count, and a count that contradicts its state', () => {
    const unknownWithCount = body(); unknownWithCount.sections[1].items[0].count = 0 as never;
    expect(() => decodeOffboardingReadiness(unknownWithCount)).toThrow();
    const clearWithFindings = body(); clearWithFindings.sections[0].items[0].state = 'NO_FINDINGS';
    expect(() => decodeOffboardingReadiness(clearWithFindings)).toThrow();
    const hiddenAsClear = body(); hiddenAsClear.overallState = 'NO_FINDINGS';
    expect(() => decodeOffboardingReadiness(hiddenAsClear)).toThrow();
    expect(decodeOffboardingReadiness(body()).sections).toHaveLength(2);
  });

  describe('component', () => {
    let http: HttpTestingController;
    beforeEach(() => {
      TestBed.configureTestingModule({ imports: [ClientOffboardingReadiness], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
      http = TestBed.inject(HttpTestingController);
      TestBed.inject(SessionService).current.set({ userId: clientId, firmId: clientId, generation: '1', staff: true });
    });
    afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });

    it('loads only on request, shows unreadable evidence as Unknown, and clears when the client changes', () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      http.expectNone(`/api/ui/clients/${clientId}/offboarding-readiness`);
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      fixture.detectChanges();
      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Open or in-progress tasks: 2');
      expect(text).toContain('Posted invoices with an outstanding balance: Unknown');
      fixture.componentRef.setInput('clientId', otherId);
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Open or in-progress tasks');
    });

    it('discards a response for a client that is no longer shown', () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      const pending = http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`);
      fixture.componentRef.setInput('clientId', otherId);
      fixture.detectChanges();
      if (!pending.cancelled) pending.flush(body());
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Open or in-progress tasks');
    });
  });
});
