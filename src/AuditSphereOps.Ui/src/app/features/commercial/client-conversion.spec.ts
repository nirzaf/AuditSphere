import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { ClientConversion } from './client-conversion';
import {
  ClientConversionState,
  clientConversionFields,
  decodeClientConversionState,
  decodeClientConversionReceipt,
  decodeClientConversionLookup,
} from './client-conversion-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const basis = 'a'.repeat(64),
  hash = 'b'.repeat(64),
  url = '/api/ui/proposals/' + id + '/client-conversion';
const state: ClientConversionState = {
  proposalId: id,
  proposalRevision: '9007199254740993',
  proposalStatus: 'ACCEPTED',
  canConvert: true,
  convertedClientId: null,
  suggestedLegalName: 'Synthetic prospect',
  reviewBasis: basis,
};
const fields = {
  legalName: 'Synthetic client',
  commercialName: null,
  registrationNumber: null,
  jurisdiction: null,
  restrictedProfile: null,
};
const editable = {
  legalName: fields.legalName,
  commercialName: '',
  registrationNumber: '',
  jurisdiction: '',
  restrictedProfile: '',
};
function intent(requestId: string) {
  return {
    proposalId: id,
    requestId,
    requestHash: hash,
    reviewBasis: basis,
    fields,
    proposalRevision: state.proposalRevision,
    serviceRoute: 'AccountingOnly',
    existingClientId: null,
    existingClientName: null,
    existingClientStatus: null,
    contactName: 'Synthetic contact',
    contactEmail: 'contact@example.test',
    portalEffect: 'No invitation or access is granted.',
    professionalEffect: 'Professional acceptance remains separate.',
  };
}
function receipt(requestId: string) {
  return {
    id: other,
    proposalId: id,
    clientId: other,
    actorId: id,
    requestId,
    requestHash: hash,
    reviewBasis: basis,
    preview: intent(requestId),
    createdAt: '2026-10-03T12:30:00Z',
  };
}
describe('Reviewed prospect conversion contracts', () => {
  it('rejects hidden fields and unbound receipts while preserving exact revisions', () => {
    expect(clientConversionFields({ ...fields, reviewed: true })).toBeNull();
    expect(clientConversionFields({ ...fields, legalName: 'x'.repeat(201) })).toBeNull();
    expect(decodeClientConversionState(state).proposalRevision).toBe('9007199254740993');
    expect(() =>
      decodeClientConversionState({
        ...state,
        proposalRevision: 9007199254740993,
      }),
    ).toThrow();
    expect(() => decodeClientConversionState({ ...state, convertedClientId: other })).toThrow();
    expect(() => decodeClientConversionLookup({ found: true, receipt: null })).toThrow();
    expect(() =>
      decodeClientConversionReceipt({
        ...receipt(id),
        requestHash: 'c'.repeat(64),
      }),
    ).toThrow();
  });
});
describe('Native prospect conversion review and recovery', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear();
    ids = new BehaviorSubject(convertToParamMap({ id }));
    TestBed.configureTestingModule({
      imports: [ClientConversion],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: ids, snapshot: { paramMap: ids.value } },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(ClientConversion);
    f.detectChanges();
    TestBed.tick();
    return f;
  }
  function read(value: ClientConversionState = state) {
    const rs = TestBed.inject(HttpTestingController)
      .match((r) => r.method === 'GET' && !r.url.includes('/receipts/'))
      .filter((r) => !r.cancelled);
    expect(rs).toHaveLength(1);
    rs[0].flush(value);
    TestBed.tick();
  }
  async function preview(c: ClientConversion) {
    c.model.set({ ...editable });
    TestBed.tick();
    const pending = c.prepare();
    const r = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    const requestId = r.request.body.requestId;
    r.flush({ value: intent(requestId) });
    await pending;
    TestBed.tick();
    return requestId;
  }
  it('requires exact assent and invalidates it immediately on edited identity', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    c.reviewed = true;
    c.model.update((v) => ({ ...v, legalName: 'Changed' }));
    expect(c.reviewed).toBe(false);
    TestBed.tick();
    expect(c.preview()).toBeNull();
  });
  it('dispatches once and recovers its receipt after conversion changes the state', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      requestId = await preview(c);
    c.reviewed = true;
    const dispatch = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 503, statusText: 'Unknown' });
    await dispatch;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    f.destroy();
    const next = open();
    read({
      ...state,
      canConvert: false,
      convertedClientId: other,
      reviewBasis: 'c'.repeat(64),
    });
    const restored = next.componentInstance;
    expect(restored.pending()?.requestId).toBe(requestId);
    expect(restored.model().legalName).toBe('');
    expect(restored.reviewed).toBe(false);
    const verify = restored.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: receipt(requestId) });
    await verify;
    TestBed.tick();
    expect(restored.receipt()?.clientId).toBe(other);
    restored.acknowledge();
    read({
      ...state,
      canConvert: false,
      convertedClientId: other,
    });
    expect(restored.pending()).toBeNull();
    expect(restored.uncertain()).toBe(false);
  });
  it('excludes restricted-profile content and assent from tab drafts', () => {
    const f = open();
    read();
    const c = f.componentInstance;
    c.model.set({ ...editable, restrictedProfile: 'Memory only' });
    TestBed.tick();
    expect(c.saveDraft()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    c.model.set(editable);
    TestBed.tick();
    expect(c.saveDraft()).toBe(true);
    f.destroy();
    const next = open();
    read();
    expect(next.componentInstance.draftAvailable()).toBe(true);
    next.componentInstance.restoreDraft();
    TestBed.tick();
    expect(next.componentInstance.model()).toEqual(editable);
    expect(next.componentInstance.reviewed).toBe(false);
    expect(
      Array.from({ length: sessionStorage.length }, (_, i) =>
        sessionStorage.getItem(sessionStorage.key(i)!),
      ).join(),
    ).not.toContain('reviewed');
  });
  it('clears protected edits on a current-scope refusal', () => {
    const f = open();
    read();
    const c = f.componentInstance;
    c.model.set(editable);
    TestBed.tick();
    c.state.reload();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(c.model().legalName).toBe('');
    expect(c.preview()).toBeNull();
    expect(c.receipt()).toBeNull();
  });
});
