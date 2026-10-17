import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Api } from '../../core/api';
import { decode } from '../../core/decode';
import { CertificationReview, certificationReview } from './certification-review';

const id = '11111111-1111-4111-8111-111111111111';
const source = {
  certificationId: id, subjectUserId: id, name: 'Synthetic qualification', issuer: null, expiresOn: null,
  recordedByUserId: id, recordedAt: '2026-10-18T00:00:00Z', sourceDigest: 'a'.repeat(64), revision: 0,
  latestReview: null, verifiedForCurrentSource: false, expired: false, canReview: true,
};

describe('Certification review', () => {
  it('retains the exact request after an unknown outcome and clears it only after confirmed recovery', async () => {
    const bodies: unknown[] = [];
    const dialog = { disableClose: false, close: () => {} };
    const api = {
      resource: () => ({ data: () => decode(certificationReview, source), loading: () => false, error: () => '', reload: () => {} }),
      command: async (_url: string, body: unknown) => {
        bodies.push(structuredClone(body));
        return bodies.length === 1
          ? { ok: false, unknown: true, code: 'outcome.unknown', message: 'Unknown', status: 0 }
          : { ok: true, value: id };
      },
    };
    TestBed.configureTestingModule({ imports: [CertificationReview], providers: [
      { provide: Api, useValue: api }, { provide: MAT_DIALOG_DATA, useValue: id }, { provide: MatDialogRef, useValue: dialog },
    ] });
    const fixture = TestBed.createComponent(CertificationReview);
    const component = fixture.componentInstance;
    component.model.set({ decision: 'VERIFIED', reference: '', reason: 'Reviewed', reviewed: true });
    expect(component.valid()).toBe(false);
    component.model.set({ decision: 'VERIFIED', reference: 'Retained synthetic evidence', reason: 'Reviewed', reviewed: true });
    await component.publish();
    expect(component.cmd.uncertain()).toBe(true); expect(dialog.disableClose).toBe(true);
    await component.publish(); expect(bodies.length).toBe(1);
    await component.recover();
    expect(bodies.length).toBe(2); expect(bodies[1]).toEqual(bodies[0]);
    expect(component.pending()).toBeNull(); expect(component.cmd.uncertain()).toBe(false); expect(dialog.disableClose).toBe(false);
    fixture.destroy();
  });
});
