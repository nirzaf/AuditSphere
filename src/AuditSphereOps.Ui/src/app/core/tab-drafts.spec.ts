import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { TabDrafts } from './tab-drafts';
import { SessionService } from './session';
import {
  emptyCreate,
  emptyBatch,
  emptyAction,
  intent,
  createDraft,
  batchDraft,
  actionDraft,
} from '../features/audit/confirmation-drafts';

const id = '11111111-1111-4111-8111-111111111111';
const scope = { entity: 'confirmations/' + id + '/create', baseRevision: 'a'.repeat(64) };
describe('Versioned tab drafts', () => {
  let drafts: TabDrafts, session: SessionService;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    session = TestBed.inject(SessionService);
    session.current.set({ userId: id, firmId: id, generation: '0', staff: true });
    drafts = TestBed.inject(TabDrafts);
    TestBed.tick();
  });
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function stored() {
    const key = sessionStorage.key(0)!;
    return { key, value: JSON.parse(sessionStorage.getItem(key)!) };
  }
  it('stores only allowlisted intent, scope and revision metadata, never review or credentials', () => {
    const value = intent({ ...emptyCreate(), respondent: 'Synthetic bank', reviewed: true });
    expect(drafts.save(scope, value, createDraft)).toBe(true);
    expect(drafts.read(scope, createDraft)).toEqual({
      state: 'ready',
      draft: { value, submissionPending: false },
    });
    const raw = stored().value;
    expect(raw.schemaVersion).toBe(1);
    expect(raw.generation).toBe('0');
    expect(raw.value.reviewed).toBeUndefined();
    for (const field of [
      'password',
      'accessToken',
      'refreshToken',
      'reviewToken',
      'privateKey',
      'files',
    ])
      expect(drafts.save(scope, { ...value, [field]: 'excluded' }, createDraft)).toBe(false);
  });
  it('requires the current base and refuses newer, unsupported or malformed envelopes', () => {
    drafts.save(scope, intent(emptyCreate()), createDraft);
    expect(drafts.read({ ...scope, baseRevision: 'b'.repeat(64) }, createDraft).state).toBe(
      'stale',
    );
    const { key, value } = stored();
    sessionStorage.setItem(key, JSON.stringify({ ...value, schemaVersion: 2 }));
    expect(drafts.read(scope, createDraft).state).toBe('stale');
    expect(sessionStorage.length).toBe(0);
    sessionStorage.setItem(key, '{invalid');
    expect(drafts.read(scope, createDraft).state).toBe('stale');
    expect(sessionStorage.length).toBe(0);
  });
  it('expires after four hours and refuses changed identity, firm, epoch and entity', () => {
    drafts.save(scope, intent(emptyCreate()), createDraft);
    const { key, value } = stored();
    vi.spyOn(Date, 'now').mockReturnValue(value.expiresAt);
    expect(drafts.read(scope, createDraft).state).toBe('stale');
    vi.restoreAllMocks();
    drafts.save(scope, intent(emptyCreate()), createDraft);
    for (const field of ['userId', 'firmId', 'generation', 'entity']) {
      const e = stored();
      sessionStorage.setItem(e.key, JSON.stringify({ ...e.value, [field]: 'wrong' }));
      expect(drafts.read(scope, createDraft).state).toBe('stale');
      drafts.save(scope, intent(emptyCreate()), createDraft);
    }
    session.current.update((s) => ({ ...s!, userId: '22222222-2222-4222-8222-222222222222' }));
    expect(drafts.read(scope, createDraft).state).toBe('absent');
    expect(drafts.clear(scope.entity)).toBe(true);
    expect(sessionStorage.getItem(key)).not.toBeNull();
  });
  it('reports unavailable storage and leaves memory values usable without claiming persistence', () => {
    const value = intent(emptyCreate());
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota', 'QuotaExceededError');
    });
    expect(drafts.save(scope, value, createDraft)).toBe(false);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Denied', 'SecurityError');
    });
    expect(drafts.read(scope, createDraft).state).toBe('unavailable');
    expect(value).toEqual(intent(emptyCreate()));
  });
  it('clears every scoped tab draft on logout or session invalidation', () => {
    drafts.save(scope, intent(emptyCreate()), createDraft);
    drafts.save({ ...scope, entity: scope.entity + '/other' }, intent(emptyCreate()), createDraft);
    session.clear();
    TestBed.tick();
    expect(sessionStorage.length).toBe(0);
    expect(drafts.read(scope, createDraft).state).toBe('absent');
  });
  it('keeps pending submission evidence as a fence, not an automatically executable command', () => {
    drafts.save(scope, intent(emptyCreate()), createDraft, true);
    const read = drafts.read(scope, createDraft);
    expect(read.state).toBe('ready');
    if (read.state === 'ready') expect(read.draft.submissionPending).toBe(true);
  });
  it('bounds batch rows and string lengths while preserving exact decimal text', () => {
    const batch = intent(emptyBatch());
    batch.cases[0].bookedAmount = '987654321.123456';
    expect(batchDraft(batch)?.cases[0].bookedAmount).toBe('987654321.123456');
    expect(batchDraft({ ...batch, cases: Array(101).fill(batch.cases[0]) })).toBeNull();
    expect(createDraft({ ...intent(emptyCreate()), respondent: 'x'.repeat(501) })).toBeNull();
    expect(actionDraft({ ...intent(emptyAction()), evidence: 'x'.repeat(20001) })).toBeNull();
    expect(batchDraft({ ...batch, clientSecret: 'excluded' })).toBeNull();
    expect(actionDraft({ ...intent(emptyAction()), reviewed: true })).toBeNull();
  });
  it('bounds stored drafts but allows updating a pending fence at the limit', () => {
    for (let i = 0; i < 50; i++) {
      expect(
        drafts.save(
          { ...scope, entity: scope.entity + '/' + i },
          intent(emptyCreate()),
          createDraft,
        ),
      ).toBe(true);
    }
    expect(drafts.save(scope, intent(emptyCreate()), createDraft)).toBe(false);
    const existing = { ...scope, entity: scope.entity + '/0' };
    expect(drafts.save(existing, intent(emptyCreate()), createDraft, true)).toBe(true);
    const read = drafts.read(existing, createDraft);
    expect(read.state === 'ready' && read.draft.submissionPending).toBe(true);
  });
});
