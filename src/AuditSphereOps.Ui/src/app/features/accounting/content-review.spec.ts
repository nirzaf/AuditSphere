import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api';
import { contentDecisionIntent, contentDecisionRequest, decodeStatementContentReview, StatementContentReview } from './content-review';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const snapshot = 'a'.repeat(64);

function state(latestReviewId: string | null = null) {
  return decodeStatementContentReview({
    financialPackageId: id1, presentationRevisionId: id2, snapshotSha256Hex: snapshot, approved: false, canReview: true, sourceIsCurrent: true,
    latestReviewId, reviewedChecks: ['NOTE_CONTENT', 'EQUITY_SOURCE_LINEAGE'], reviews: [],
  }, 'review');
}

describe('statement content review contract (F-03)', () => {
  it('sends the exact snapshot and latest decision the reviewer saw', () => {
    expect(contentDecisionRequest(state(), 'APPROVED', null, 'request-1')).toEqual({
      decision: 'APPROVED', reason: null, requestId: 'request-1', expectedSnapshotSha256Hex: snapshot, expectedPreviousReviewId: null,
    });
  });

  it('keeps the request identity for a retry and renews it when the decision or the chain changes', () => {
    const first = contentDecisionIntent(null, state(), 'APPROVED', null, 'request-1');
    expect(contentDecisionIntent(first, state(), 'APPROVED', null, 'request-2')).toBe(first);
    expect(contentDecisionIntent(first, state(), 'RETURNED', 'Missing lease note', 'request-2').requestId).toBe('request-2');
    expect(contentDecisionIntent(first, state(id1), 'APPROVED', null, 'request-3').requestId).toBe('request-3');
  });

  it('renders the exact revision, snapshot and retained reviewer identities without inventing approval', () => {
    const reviewer = '33333333-3333-4333-8333-333333333333';
    const review = '44444444-4444-4444-8444-444444444444';
    const data = signal(decodeStatementContentReview({ ...state(review), canReview: false,
      reviews: [{ id: review, decision: 'RETURNED', reason: 'Source note needs correction', reviewedByUserId: reviewer,
        reviewedAt: '2026-10-15T00:00:00Z', snapshotSha256Hex: snapshot }] }, 'review'));
    TestBed.configureTestingModule({ imports: [StatementContentReview], providers: [{ provide: Api, useValue: {
      resource: () => ({ data, loading: signal(false), error: signal(''), reload: vi.fn() }),
    } }] });
    const fixture = TestBed.createComponent(StatementContentReview);
    fixture.componentRef.setInput('packageId', id1);
    fixture.componentRef.setInput('revisionId', id2);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.review-identity')?.textContent).toContain(id2);
    expect(root.querySelector('.review-identity')?.textContent).toContain(snapshot);
    const cells = [...root.querySelectorAll('tbody td')].map(cell => cell.textContent?.trim());
    expect(cells).toContain(reviewer);
    expect(cells).toContain(review);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Not approved');
    expect(root.querySelector('form')).toBeNull();
  });

  it('keeps historical approval distinct from stale source and refuses another decision', async () => {
    const reviewId = '44444444-4444-4444-8444-444444444444';
    const data = signal(decodeStatementContentReview({ ...state(reviewId), approved: true, canReview: false,
      sourceIsCurrent: false, reviews: [{ id: reviewId, decision: 'APPROVED', reason: '',
        reviewedByUserId: '33333333-3333-4333-8333-333333333333',
        reviewedAt: '2026-10-15T00:00:00Z', snapshotSha256Hex: snapshot }] }, 'review'));
    const command = vi.fn();
    TestBed.configureTestingModule({ imports: [StatementContentReview], providers: [{ provide: Api, useValue: {
      resource: () => ({ data, loading: signal(false), error: signal(''), reload: vi.fn() }), command,
    } }] });
    const fixture = TestBed.createComponent(StatementContentReview);
    fixture.componentRef.setInput('packageId', id1);
    fixture.componentRef.setInput('revisionId', id2);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Historical approval:');
    expect(root.querySelector('.source-currentness')?.textContent).toContain('Source stale or unavailable');
    expect(root.querySelector('form')).toBeNull();
    await fixture.componentInstance.record(data());
    expect(command).not.toHaveBeenCalled();
    expect(() => decodeStatementContentReview({ ...state(), sourceIsCurrent: undefined }, 'review')).toThrow();
  });

  it('refuses an unknown decision or a malformed snapshot digest', () => {
    expect(() => decodeStatementContentReview({ ...state(), snapshotSha256Hex: 'bad' }, 'review')).toThrow();
    expect(() => decodeStatementContentReview({ ...state(), reviews: [{ id: id1, decision: 'PENDING', reason: '', reviewedByUserId: id2,
      reviewedAt: '2026-10-15T00:00:00Z', snapshotSha256Hex: snapshot }] }, 'review')).toThrow();
  });
});
