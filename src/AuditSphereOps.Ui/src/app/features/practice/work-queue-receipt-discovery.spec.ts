import { WorkQueueReceiptDiscovery, WORK_QUEUE_RECEIPT_SCAN_MAX_CHARS,
  WORK_QUEUE_RECEIPT_SCAN_MAX_ENTRIES } from './work-queue-receipt-discovery';
import type { WorkQueueRegistryOwner } from './work-queue-action-registry';

const firm = '11111111-1111-4111-8111-111111111111';
const user = '22222222-2222-4222-8222-222222222222';
const other = '33333333-3333-4333-8333-333333333333';
const engagement = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const task = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const request = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const key = (kind: string, firmId = firm, userId = user, engagementId = engagement, taskId = task) =>
  `auditsphere.workflow-${kind}:${firmId}:${userId}:${engagementId}:${taskId}`;

class MemoryStorage {
  readonly values = new Map<string, string>();
  writes = 0;
  get length(): number { return this.values.size; }
  clear(): void { this.writes++; this.values.clear(); }
  getItem(name: string): string | null { return this.values.get(name) ?? null; }
  key(index: number): string | null { return [...this.values.keys()][index] ?? null; }
  removeItem(name: string): void { this.writes++; this.values.delete(name); }
  setItem(name: string, value: string): void { this.writes++; this.values.set(name, value); }
}

describe('work queue receipt discovery', () => {
  let owner: WorkQueueRegistryOwner | null;
  let storage: MemoryStorage;
  let discovery: WorkQueueReceiptDiscovery;

  beforeEach(() => {
    owner = { firmId: firm, userId: user, generation: '3', invalidation: 0, staff: true };
    storage = new MemoryStorage();
    discovery = new WorkQueueReceiptDiscovery(() => owner, () => storage as unknown as Storage);
  });

  it('returns only exact supported, valid keys for the current firm and user without mutating storage', () => {
    const original = key('evidence', firm, user, engagement.toUpperCase(), task.toUpperCase());
    storage.setItem(original, request.toUpperCase());
    storage.setItem(key('ordering'), request);
    storage.setItem(key('applicability'), request);
    storage.setItem(key('evidence', other), request);
    storage.setItem(key('evidence', firm, other), request);
    storage.setItem(key('unrecognized'), request);
    const before = [...storage.values.entries()]; const writes = storage.writes;
    const result = discovery.snapshot(owner!);
    expect(result.complete).toBe(true);
    if (!result.complete) throw new Error(result.reason);
    expect(result.candidates.map(candidate => candidate.kind)).toEqual(['evidence', 'ordering', 'applicability']);
    expect(result.candidates[0]).toEqual({ kind: 'evidence', engagementId: engagement.toUpperCase(), taskId: task.toUpperCase(),
      requestKey: request.toUpperCase(), storageKey: original });
    expect(discovery.verify(owner!, result.candidates[0])).toEqual({ valid: true });
    expect(storage.writes).toBe(writes); expect([...storage.values.entries()]).toEqual(before);
  });

  it('reports an incomplete scan for malformed entries belonging to the current owner without changing them', () => {
    for (const [name, value] of [
      [key('evidence', firm, user, engagement, 'bad-task'), request],
      [`${key('evidence')}:extra`, request],
      [key('ordering', firm, user, engagement, other), 'not-a-guid'],
      [key('applicability', firm, user, engagement, other), '00000000-0000-0000-0000-000000000000'],
    ]) {
      storage.clear(); storage.setItem(name, value);
      const before = [...storage.values.entries()]; const writes = storage.writes;
      expect(discovery.snapshot(owner!)).toEqual({ complete: false, reason: 'invalid_candidate' });
      expect(storage.writes).toBe(writes); expect([...storage.values.entries()]).toEqual(before);
    }
  });

  it('reports incomplete when the bounded entry or character limit is exceeded', () => {
    for (let i = 0; i <= WORK_QUEUE_RECEIPT_SCAN_MAX_ENTRIES; i++) storage.setItem(`other-${i}`, 'x');
    expect(discovery.snapshot(owner!)).toEqual({ complete: false, reason: 'limit_exceeded' });
    storage.clear(); storage.setItem('other', 'x'.repeat(WORK_QUEUE_RECEIPT_SCAN_MAX_CHARS));
    expect(discovery.snapshot(owner!)).toEqual({ complete: false, reason: 'limit_exceeded' });
  });

  it('does not return partial results when keys or values change during the scan', () => {
    storage.setItem(key('evidence'), request); storage.setItem('unrelated', 'first');
    const get = storage.getItem.bind(storage); let reads = 0;
    storage.getItem = name => {
      const value = get(name);
      if (++reads === 2) storage.setItem('unrelated', 'second');
      return value;
    };
    expect(discovery.snapshot(owner!)).toEqual({ complete: false, reason: 'storage_changed' });
    expect(storage.values.get(key('evidence'))).toBe(request);
  });

  it('fails closed on unavailable storage and session changes while scanning', () => {
    const unavailable = new WorkQueueReceiptDiscovery(() => owner, () => { throw new Error('unavailable'); });
    expect(unavailable.snapshot(owner!)).toEqual({ complete: false, reason: 'storage_unavailable' });
    const captured = { ...owner! };
    storage.setItem(key('evidence'), request);
    const get = storage.getItem.bind(storage);
    storage.getItem = name => { owner = { ...captured, generation: '4', invalidation: 1 }; return get(name); };
    expect(discovery.snapshot(captured)).toEqual({ complete: false, reason: 'session_changed' });
    expect(discovery.snapshot(captured)).toEqual({ complete: false, reason: 'session_changed' });
  });

  it('rechecks the exact candidate and session before use, without accepting a forged candidate', () => {
    storage.setItem(key('evidence'), request);
    const result = discovery.snapshot(owner!);
    if (!result.complete) throw new Error(result.reason);
    const candidate = result.candidates[0]; const captured = { ...owner! };
    expect(discovery.verify(captured, { ...candidate })).toEqual({ valid: false, reason: 'invalid_candidate' });
    storage.setItem(candidate.storageKey, other);
    expect(discovery.verify(captured, candidate)).toEqual({ valid: false, reason: 'storage_changed' });
    storage.setItem(candidate.storageKey, request);
    owner = { ...captured, invalidation: 1 };
    expect(discovery.verify(captured, candidate)).toEqual({ valid: false, reason: 'session_changed' });
    expect(discovery.verify(owner, candidate)).toEqual({ valid: false, reason: 'invalid_candidate' });
  });

  it('detects changes between the two exact-key checks and refuses unavailable reads', () => {
    storage.setItem(key('ordering'), request);
    const result = discovery.snapshot(owner!);
    if (!result.complete) throw new Error(result.reason);
    const get = storage.getItem.bind(storage); let reads = 0;
    storage.getItem = name => { const value = get(name); if (++reads === 1) storage.setItem(name, other); return value; };
    expect(discovery.verify(owner!, result.candidates[0])).toEqual({ valid: false, reason: 'storage_changed' });
    storage.getItem = () => { throw new Error('denied'); };
    expect(discovery.verify(owner!, result.candidates[0])).toEqual({ valid: false, reason: 'storage_unavailable' });
  });
});
