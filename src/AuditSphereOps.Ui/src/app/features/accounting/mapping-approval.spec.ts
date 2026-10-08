import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import {
  MappingApproval,
  decodeMappingApproval,
  mappingReviewCheckpoint,
} from './mapping-approval';

const id = '11111111-1111-4111-8111-111111111111',
  actor = '22222222-2222-4222-8222-222222222222',
  root = `/api/ui/accounting/mappings/${id}/approval`;
const plan = {
  mappingId: id,
  clientId: id,
  engagementId: id,
  datasetId: id,
  version: 1,
  status: 'DRAFT',
  mappingGeneration: 1,
  inputGeneration: 1,
  taxonomyVersion: 'synthetic-tax-v1',
  chartVersionId: null,
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  sourceAccountCount: 2,
  allocationCount: 2,
  preparerId: id,
  reviewerId: null,
  reviewedAt: null,
  revision: 'a'.repeat(64),
  canApprove: true,
  blocker: null,
};
const approved = {
  ...plan,
  status: 'APPROVED',
  mappingGeneration: 2,
  inputGeneration: 2,
  reviewerId: actor,
  reviewedAt: '2026-10-02T20:00:00Z',
  revision: 'b'.repeat(64),
  canApprove: false,
  blocker: 'Approval retained.',
};

describe('Independent exact mapping review', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [MappingApproval],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id })) },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: actor,
      firmId: id,
      generation: '0',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
    vi.restoreAllMocks();
  });
  async function open(response: object = plan) {
    const f = TestBed.createComponent(MappingApproval);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(root).flush(response);
    await f.whenStable();
    TestBed.tick();
    return { f, c: f.componentInstance, http };
  }
  function assent(c: MappingApproval) {
    c.model.set({ reviewed: true });
    c.assent({ target: { checked: true } } as unknown as Event);
  }
  it('requires fresh exact-version assent, dispatches once and excludes assent from checkpoints', async () => {
    const { f, c, http } = await open();
    await c.approve();
    http.expectNone(root);
    assent(c);
    const task = c.approve();
    const write = http.expectOne(root);
    expect(write.request.method).toBe('POST');
    expect(write.request.body).toEqual({ revision: plan.revision, reviewed: true });
    const checkpoint = sessionStorage.getItem(sessionStorage.key(0)!)!;
    expect(checkpoint).not.toContain('reviewed');
    expect(c.model().reviewed).toBe(false);
    write.flush({ value: approved });
    await task;
    http.expectOne(root).flush(approved);
    await f.whenStable();
    TestBed.tick();
    expect(sessionStorage.length).toBe(0);
    expect(c.canApprove()).toBe(false);
    await c.approve();
    http.expectNone(root);
    f.destroy();
  });
  it('fences an unknown approval across reload and requires an explicit persisted read and acknowledgement', async () => {
    const first = await open();
    assent(first.c);
    const task = first.c.approve();
    first.http.expectOne(root).flush({}, { status: 503, statusText: 'Unknown' });
    await task;
    first.http.expectOne(root).flush(approved);
    await first.f.whenStable();
    TestBed.tick();
    expect(first.c.uncertain()).toBe(true);
    expect(first.c.reconciled()).toBe(false);
    expect(first.c.confirmNavigation()).toBe(false);
    first.f.destroy();
    const { f, c, http } = await open(approved);
    expect(c.uncertain()).toBe(true);
    expect(c.model().reviewed).toBe(false);
    c.refresh();
    http.expectOne(root).flush(approved);
    await f.whenStable();
    TestBed.tick();
    expect(c.reconciled()).toBe(true);
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.confirmNavigation()).toBe(true);
    await c.approve();
    http.expectNone(root);
    f.destroy();
  });
  it('requires checkpoint storage, refuses mismatched receipts and clears content after invalidation', async () => {
    const { f, c, http } = await open();
    assent(c);
    const broken = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Full');
    });
    await c.approve();
    http.expectNone(root);
    expect(c.message()).toContain('unavailable');
    broken.mockRestore();
    const task = c.approve();
    http.expectOne(root).flush({ value: { ...approved, clientId: actor } });
    await task;
    expect(c.uncertain()).toBe(true);
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((n) => n + 1);
    TestBed.tick();
    expect(c.review.data()).toBeNull();
    expect(c.model().reviewed).toBe(false);
    expect(c.message()).toBe('');
    f.destroy();
  });
  it('clears visible assent on refresh and blocks stale generation and forged approvable states', async () => {
    const { f, c, http } = await open();
    assent(c);
    c.refresh();
    expect(c.model().reviewed).toBe(false);
    http
      .expectOne(root)
      .flush({
        ...plan,
        inputGeneration: 2,
        canApprove: false,
        blocker: 'Inputs changed',
        revision: 'c'.repeat(64),
      });
    await f.whenStable();
    TestBed.tick();
    expect(c.canApprove()).toBe(false);
    await c.approve();
    http.expectNone(root);
    f.destroy();
    expect(() => decodeMappingApproval({ ...plan, inputGeneration: 2 })).toThrow();
    expect(() => decodeMappingApproval({ ...approved, reviewerId: null })).toThrow();
    expect(() => decodeMappingApproval({ ...plan, allocationCount: 5001 })).toThrow();
    expect(mappingReviewCheckpoint({ mappingId: id })).toEqual({ mappingId: id });
    expect(mappingReviewCheckpoint({ mappingId: id, reviewed: true })).toBeNull();
  });
});
