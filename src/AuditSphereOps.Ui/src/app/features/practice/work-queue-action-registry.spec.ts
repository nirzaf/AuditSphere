import { WorkQueueActionRegistry, WORK_QUEUE_ACTION_CAPACITY, type WorkQueueRegistryOwner,
  type WorkQueueActionInput, type WorkQueueTaskObservation } from './work-queue-action-registry';

const firm = '11111111-1111-4111-8111-111111111111';
const user = '22222222-2222-4222-8222-222222222222';
const otherUser = '33333333-3333-4333-8333-333333333333';
const engagement = '44444444-4444-4444-8444-444444444444';
const id = (n: number) => `55555555-5555-4555-8555-${n.toString(16).padStart(12, '0')}`;
const key = (firmId = firm, userId = user) => `auditsphere.work-queue-actions.v1:${firmId}:${userId}`;
const action = (n: number): WorkQueueActionInput => ({ engagementId: engagement, taskId: id(n),
  expectedRevision: 4, action: 'changeDueDate', requestedDueDate: '2026-10-30', requestId: id(n + 1000) });

describe('work queue action registry', () => {
  let owner: WorkQueueRegistryOwner | null;
  let registry: WorkQueueActionRegistry;
  beforeEach(() => {
    sessionStorage.removeItem(key()); sessionStorage.removeItem(key(firm, otherUser));
    owner = { firmId: firm, userId: user, generation: '4', invalidation: 0, staff: true };
    registry = new WorkQueueActionRegistry(() => owner);
  });
  afterEach(() => { sessionStorage.removeItem(key()); sessionStorage.removeItem(key(firm, otherUser)); });

  it('persists distinct task reservations before dispatch and restores them across registry instances', () => {
    const first = registry.reserve(owner!, action(1));
    const second = registry.reserve(owner!, { ...action(2), action: 'complete', requestedDueDate: null });
    expect(first.ok).toBe(true); expect(second.ok).toBe(true);
    expect(registry.reserve(owner!, { ...action(3), taskId: id(1) })).toEqual({ ok: false, reason: 'task_pending' });
    expect(registry.reserve(owner!, { ...action(3), requestId: id(1001) })).toEqual({ ok: false, reason: 'request_pending' });
    const reloaded = new WorkQueueActionRegistry(() => owner);
    const result = reloaded.read(owner!);
    expect(result.ok).toBe(true);
    if (result.ok) expect(result.value.map(x => x.taskId)).toEqual([id(1), id(2)]);
  });

  it('isolates firms and users while retaining prior generations for the same user', () => {
    const original = { ...owner! };
    expect(registry.reserve(original, action(1)).ok).toBe(true);
    owner = { ...original, userId: otherUser };
    expect(registry.read(owner)).toEqual({ ok: true, value: [] });
    expect(registry.read(original)).toEqual({ ok: false, reason: 'session_changed' });
    owner = { ...original, firmId: otherUser };
    expect(registry.read(owner)).toEqual({ ok: true, value: [] });
    owner = { ...original, generation: '5', invalidation: 1 };
    const retained = registry.read(owner);
    expect(retained.ok).toBe(true);
    if (retained.ok) expect(retained.value[0].dispatchGeneration).toBe('4');
    expect(registry.reserve(owner, action(1))).toEqual({ ok: false, reason: 'task_pending' });
    expect(registry.removeDefinitive(owner, id(1001))).toEqual({ ok: false, reason: 'session_changed' });
    expect(registry.removeDefinitive(original, id(1001))).toEqual({ ok: false, reason: 'session_changed' });
  });

  it('accepts explicit acknowledgement only for a matching current observation with advanced revision', () => {
    const dispatchOwner = { ...owner! };
    expect(registry.reserve(dispatchOwner, action(1)).ok).toBe(true);
    owner = { ...dispatchOwner, generation: '5', invalidation: 1 };
    const observation: WorkQueueTaskObservation = { owner: { ...owner }, engagementId: engagement,
      taskId: id(1), revision: 4, availabilityRevision: 7 };
    expect(registry.acknowledge(owner, id(1001), observation, 7)).toEqual({ ok: false, reason: 'observation_stale' });
    expect(registry.acknowledge(owner, id(1001), { ...observation, revision: 5, taskId: id(2) }, 7))
      .toEqual({ ok: false, reason: 'observation_stale' });
    expect(registry.acknowledge(owner, id(1001), { ...observation, revision: 5, engagementId: id(2) }, 7))
      .toEqual({ ok: false, reason: 'observation_stale' });
    expect(registry.acknowledge(owner, id(1001), { ...observation, revision: 5, owner: dispatchOwner }, 7))
      .toEqual({ ok: false, reason: 'observation_stale' });
    expect(registry.acknowledge(owner, id(1001), { ...observation, revision: 5 }, 8))
      .toEqual({ ok: false, reason: 'observation_stale' });
    expect(registry.acknowledge(owner, id(1001), { ...observation, revision: 5 }, 7)).toEqual({ ok: true, value: undefined });
    expect(registry.read(owner)).toEqual({ ok: true, value: [] });
  });

  it('removes only the exact definitive dispatch, leaving other pending tasks untouched', () => {
    expect(registry.reserve(owner!, action(1)).ok).toBe(true);
    expect(registry.reserve(owner!, action(2)).ok).toBe(true);
    expect(registry.removeDefinitive(owner!, id(1001))).toEqual({ ok: true, value: undefined });
    expect(registry.removeDefinitive(owner!, id(1001))).toEqual({ ok: false, reason: 'not_found' });
    const result = registry.read(owner!);
    if (!result.ok) throw new Error(result.reason);
    expect(result.value.map(x => x.taskId)).toEqual([id(2)]);
  });

  it('fails closed on malformed records without replacing or deleting them', () => {
    sessionStorage.setItem(key(), '{invalid');
    expect(registry.read(owner!)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(registry.reserve(owner!, action(1))).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(sessionStorage.getItem(key())).toBe('{invalid');
    sessionStorage.removeItem(key());
    expect(registry.reserve(owner!, action(1)).ok).toBe(true);
    const doc = JSON.parse(sessionStorage.getItem(key())!);
    doc.entries[0].userId = otherUser;
    sessionStorage.setItem(key(), JSON.stringify(doc));
    expect(registry.read(owner!)).toEqual({ ok: false, reason: 'invalid_storage' });
    doc.entries[0].userId = user;
    doc.entries.push({ ...doc.entries[0] });
    sessionStorage.setItem(key(), JSON.stringify(doc));
    expect(registry.read(owner!)).toEqual({ ok: false, reason: 'invalid_storage' });
  });

  it('refuses invalid inputs and invalid or unavailable storage', () => {
    expect(registry.reserve(owner!, { ...action(1), requestedDueDate: '2026-02-29' }))
      .toEqual({ ok: false, reason: 'invalid_input' });
    expect(registry.reserve(owner!, { ...action(1), expectedRevision: Number.MAX_SAFE_INTEGER + 1 }))
      .toEqual({ ok: false, reason: 'invalid_input' });
    expect(registry.reserve(owner!, { ...action(1), action: 'complete' }))
      .toEqual({ ok: false, reason: 'invalid_input' });
    const unavailable = new WorkQueueActionRegistry(() => owner, () => { throw new Error('storage denied'); });
    expect(unavailable.read(owner!)).toEqual({ ok: false, reason: 'storage_unavailable' });
    expect(unavailable.reserve(owner!, action(1))).toEqual({ ok: false, reason: 'storage_unavailable' });
    const writeDenied = new WorkQueueActionRegistry(() => owner, () => ({ getItem: () => null,
      setItem: () => { throw new Error('quota'); } }) as unknown as Storage);
    expect(writeDenied.reserve(owner!, action(1))).toEqual({ ok: false, reason: 'storage_unavailable' });
    owner = null;
    expect(registry.read({ firmId: firm, userId: user, generation: '4', invalidation: 0, staff: true }))
      .toEqual({ ok: false, reason: 'session_changed' });
  });

  it('never evicts unresolved entries when capacity is reached', () => {
    for (let n = 1; n <= WORK_QUEUE_ACTION_CAPACITY; n++) expect(registry.reserve(owner!, action(n)).ok).toBe(true);
    expect(registry.reserve(owner!, action(WORK_QUEUE_ACTION_CAPACITY + 1))).toEqual({ ok: false, reason: 'capacity' });
    const result = registry.read(owner!);
    if (!result.ok) throw new Error(result.reason);
    expect(result.value).toHaveLength(WORK_QUEUE_ACTION_CAPACITY);
    expect(result.value[0].taskId).toBe(id(1));
  });

  it('refuses a read if the session changes while storage is being read', () => {
    const captured = { ...owner! };
    const switched = new WorkQueueActionRegistry(() => owner, () => ({
      getItem: () => { owner = { ...captured, generation: '5', invalidation: 1 }; return null; },
    }) as unknown as Storage);
    expect(switched.read(captured)).toEqual({ ok: false, reason: 'session_changed' });
  });

  it('reads v1, upgrades on write without losing records, and rejects unsupported documents', () => {
    const original = { firmId: firm, userId: user, dispatchGeneration: '3', dispatchInvalidation: 2,
      engagementId: engagement, taskId: id(1), expectedRevision: 4, action: 'complete', requestedDueDate: null, requestId: id(1001) };
    sessionStorage.setItem(key(), JSON.stringify({ version: 1, firmId: firm, userId: user, entries: [original] }));
    expect(registry.read(owner!)).toEqual({ ok: true, value: [original] });
    expect(registry.reserve(owner!, { ...action(2), action: 'cancel', requestedDueDate: null }).ok).toBe(true);
    const upgraded = JSON.parse(sessionStorage.getItem(key())!);
    expect(upgraded.version).toBe(2); expect(upgraded.entries[0]).toEqual(original);
    expect(upgraded.entries[1].action).toBe('cancel');
    upgraded.version = 3; const unsupported = JSON.stringify(upgraded); sessionStorage.setItem(key(), unsupported);
    expect(registry.reserve(owner!, action(3))).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(sessionStorage.getItem(key())).toBe(unsupported);
  });

  it('issues an uninvoked ticket only after verified reservation and never aborts after invocation', async () => {
    const reserved = registry.reserveForDispatch(owner!, { ...action(1), action: 'suspend', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const ticket = reserved.value;
    expect(registry.abortBeforeDispatch(ticket)).toEqual({ ok: true, value: undefined });
    expect(registry.read(owner!)).toEqual({ ok: true, value: [] });
    expect(registry.abortBeforeDispatch(ticket)).toEqual({ ok: false, reason: 'invalid_input' });
    const next = registry.reserveForDispatch(owner!, { ...action(2), action: 'resume', requestedDueDate: null });
    if (!next.ok) throw new Error(next.reason);
    const invoked = registry.invokeReserved(next.value, async () => true);
    expect(invoked.ok).toBe(true);
    if (invoked.ok) expect(await invoked.value).toBe(true);
    expect(registry.abortBeforeDispatch(next.value)).toEqual({ ok: false, reason: 'dispatch_started' });
    expect(registry.read(owner!)).toMatchObject({ ok: true, value: [{ requestId: id(1002) }] });
  });

  it('does not issue an abort capability when reservation readback is uncertain', () => {
    let raw: string | null = null, reads = 0;
    const storage = { getItem: () => { if (++reads === 2) throw new Error('readback failed'); return raw; },
      setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const uncertain = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(uncertain.reserveForDispatch(owner!, { ...action(1), action: 'reopen', requestedDueDate: null }))
      .toEqual({ ok: false, reason: 'storage_unavailable' });
    expect(raw).not.toBeNull();
    expect(uncertain.read(owner!)).toMatchObject({ ok: true, value: [{ action: 'reopen' }] });
  });

  it('issues cleanup-only capability for an uncertain reservation write and preserves v1 entries', () => {
    const original = { firmId: firm, userId: user, dispatchGeneration: '3', dispatchInvalidation: 2,
      engagementId: engagement, taskId: id(2), expectedRevision: 4, action: 'complete', requestedDueDate: null, requestId: id(1002) };
    let raw: string | null = JSON.stringify({ version: 1, firmId: firm, userId: user, entries: [original] }), failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; failRead = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatchRecoverable(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (reserved.ok || !('cleanup' in reserved)) throw new Error('cleanup capability missing');
    let calls = 0;
    expect(guarded.invokeReserved(reserved.cleanup, async () => { calls++; })).toEqual({ ok: false, reason: 'cleanup_started' });
    const peer = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(peer.read(owner!)).toMatchObject({ ok: true, value: [{ taskId: id(2) }, { taskId: id(1) }] });
    expect(peer.reserve(owner!, action(3))).toEqual({ ok: false, reason: 'reconciliation_pending' });
    // Allow cleanup writes to be read back.
    const cleanStorage = { getItem: () => raw, setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    // The registry keeps the storage provider; toggling its write behavior models recovery.
    storage.setItem = cleanStorage.setItem;
    expect(guarded.abortBeforeDispatch(reserved.cleanup)).toEqual({ ok: true, value: undefined });
    expect(guarded.read(owner!)).toEqual({ ok: true, value: [original] });
    expect(peer.reserve(owner!, action(3)).ok).toBe(true);
    expect(calls).toBe(0);
  });

  it('accepts an absent uncertain reservation only at the exact original document, in the issuing session', () => {
    let raw: string | null = null;
    const storage = { getItem: () => raw, setItem: () => { throw new Error('write denied'); } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatchRecoverable(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (reserved.ok || !('cleanup' in reserved)) throw new Error('cleanup capability missing');
    raw = JSON.stringify({ version: 2, firmId: firm, userId: user, entries: [] });
    expect(guarded.abortBeforeDispatch(reserved.cleanup)).toEqual({ ok: false, reason: 'invalid_storage' });
    raw = null;
    expect(guarded.abortBeforeDispatch(reserved.cleanup)).toEqual({ ok: true, value: undefined });
    const again = guarded.reserveForDispatchRecoverable(owner!, { ...action(2), action: 'cancel', requestedDueDate: null });
    if (again.ok || !('cleanup' in again)) throw new Error('cleanup capability missing');
    owner = { ...owner!, generation: '5', invalidation: 1 };
    expect(guarded.abortBeforeDispatch(again.cleanup)).toEqual({ ok: false, reason: 'session_changed' });
    expect(raw).toBeNull();
  });

  it('reconciles only the exact definitive ticket removal after uncertain readback', async () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatchRecoverable(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const first = guarded.invokeReserved(reserved.value, async () => true);
    if (!first.ok) throw new Error(first.reason);
    await first.value;
    const beforeRemoval = raw;
    raw = null;
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    const modified = JSON.parse(beforeRemoval!); modified.entries[0].expectedRevision++;
    raw = JSON.stringify(modified);
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(JSON.parse(raw).entries[0].expectedRevision).toBe(5);
    raw = beforeRemoval;
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    expect(guarded.invokeExactRetry(reserved.value, () => true)).toEqual({ ok: false, reason: 'cleanup_started' });
    const expected = raw!;
    raw = null;
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    raw = expected;
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: true, value: undefined });
    expect(guarded.read(owner!)).toEqual({ ok: true, value: [] });
    owner = { ...owner!, generation: '5', invalidation: 1 };
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
  });

  it('serializes same-user mutations across registry instances until exact definitive reconciliation finishes', async () => {
    const values = new Map<string, string>();
    let writes = 0, failRead = false;
    const storage = { getItem: (k: string) => { if (failRead) { failRead = false; throw new Error('readback'); } return values.get(k) ?? null; },
      setItem: (k: string, value: string) => { values.set(k, value); if (++writes === 3) failRead = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const peer = new WorkQueueActionRegistry(() => owner, () => storage);
    const first = guarded.reserveForDispatchRecoverable(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    const second = peer.reserveForDispatchRecoverable(owner!, { ...action(2), action: 'reopen', requestedDueDate: null });
    if (!first.ok || !second.ok) throw new Error('reservation failed');
    const invoked = guarded.invokeReserved(first.value, async () => true);
    if (!invoked.ok) throw new Error(invoked.reason);
    await invoked.value;
    expect(guarded.removeDefinitiveTicket(first.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const before = values.get(key());
    expect(peer.read(owner!)).toMatchObject({ ok: true, value: [{ taskId: id(2) }] });
    expect(peer.reserve(owner!, action(3))).toEqual({ ok: false, reason: 'reconciliation_pending' });
    expect(peer.removeDefinitive(owner!, id(1002))).toEqual({ ok: false, reason: 'reconciliation_pending' });
    expect(peer.invokeReserved(second.value, async () => true)).toEqual({ ok: false, reason: 'reconciliation_pending' });
    expect(values.get(key())).toBe(before);
    const otherOwner = { ...owner!, userId: otherUser };
    const other = new WorkQueueActionRegistry(() => otherOwner, () => storage);
    expect(other.reserve(otherOwner, action(4)).ok).toBe(true);
    expect(guarded.removeDefinitiveTicket(first.value)).toEqual({ ok: true, value: undefined });
    expect(peer.reserve(owner!, action(3))).toEqual({ ok: false, reason: 'reconciliation_pending' });
    expect(guarded.finishDefinitiveTicket(first.value)).toEqual({ ok: true, value: undefined });
    expect(peer.reserve(owner!, action(3)).ok).toBe(true);
    const result = peer.read(owner!);
    if (!result.ok) throw new Error(result.reason);
    expect(result.value.map(x => x.taskId)).toEqual([id(2), id(3)]);
  });

  it('keeps a same-user mutation fence after an issuing session changes', () => {
    let raw: string | null = null, failRead = false, writes = 0;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    owner = { ...owner!, generation: '5', invalidation: 1 };
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    expect(guarded.read(owner)).toEqual({ ok: true, value: [] });
    expect(guarded.reserve(owner, action(2))).toEqual({ ok: false, reason: 'reconciliation_pending' });
  });

  it('hands a cleanup fence to a replacement registry only in its issuing session', () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    const pending = replacement.pendingReconciliation(owner!);
    if (!pending.ok || !pending.value?.ticket) throw new Error('same-session handoff missing');
    expect(pending.value).toMatchObject({ kind: 'cleanup', sameSession: true, item: { taskId: id(1) } });
    expect(replacement.abortBeforeDispatch(pending.value.ticket)).toEqual({ ok: true, value: undefined });
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    expect(replacement.pendingReconciliation(owner!)).toEqual({ ok: true, value: null });
    expect(replacement.reserve(owner!, action(2)).ok).toBe(true);
  });

  it('hands definitive removal reconciliation to a replacement registry without enabling a resend', async () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const invoked = first.invokeReserved(reserved.value, async () => true);
    if (!invoked.ok) throw new Error(invoked.reason);
    await invoked.value;
    expect(first.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    const pending = replacement.pendingReconciliation(owner!);
    if (!pending.ok || !pending.value?.ticket) throw new Error('definitive handoff missing');
    expect(pending.value.kind).toBe('definitive');
    expect(replacement.invokeExactRetry(pending.value.ticket, () => true)).toEqual({ ok: false, reason: 'invalid_input' });
    expect(replacement.removeDefinitiveTicket(pending.value.ticket)).toEqual({ ok: true, value: undefined });
    expect(replacement.finishDefinitiveTicket(pending.value.ticket)).toEqual({ ok: true, value: undefined });
    expect(first.invokeExactRetry(reserved.value, () => true)).toEqual({ ok: false, reason: 'invalid_input' });
  });

  it('releases a prior-session fence only after exact expected bytes, without modifying records', () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const expected = raw!;
    const priorOwner = { ...owner! }; owner = { ...priorOwner, generation: '5', invalidation: 1 };
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    const pending = replacement.pendingReconciliation(owner);
    expect(pending).toMatchObject({ ok: true, value: { kind: 'cleanup', sameSession: false, item: { taskId: id(1) } } });
    if (pending.ok && pending.value) expect(pending.value.ticket).toBeUndefined();
    raw = null;
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(replacement.reserve(owner, action(2))).toEqual({ ok: false, reason: 'reconciliation_pending' });
    raw = expected;
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(expected);
    expect(replacement.reserve(owner, action(2)).ok).toBe(true);
    owner = priorOwner;
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
  });

  it('releases a prior-session removal at the exact pre-write bytes and preserves the record', () => {
    let raw: string | null = null, failWrite = false;
    const storage = { getItem: () => raw, setItem: (_key: string, value: string) => {
      if (failWrite) throw new Error('write denied'); raw = value;
    } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    failWrite = true;
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const original = raw;
    owner = { ...owner!, generation: '5', invalidation: 1 }; failWrite = false;
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    const changed = JSON.parse(original!);
    changed.entries.push({ ...changed.entries[0], taskId: id(2), requestId: id(1002) });
    raw = JSON.stringify(changed);
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(raw).not.toBe(original);
    raw = original;
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(original);
    expect(replacement.read(owner)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
    expect(replacement.reserve(owner, action(2)).ok).toBe(true);
  });

  it('releases a prior-session unsent reservation only at its exact pre-write document', () => {
    let raw: string | null = null;
    const storage = { getItem: () => raw, setItem: () => { throw new Error('write denied'); } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatchRecoverable(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (reserved.ok || !('cleanup' in reserved)) throw new Error('cleanup capability missing');
    owner = { ...owner!, generation: '5', invalidation: 1 };
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    raw = JSON.stringify({ version: 2, firmId: firm, userId: user, entries: [] });
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: false, reason: 'invalid_storage' });
    raw = null;
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBeNull();
  });

  it('releases an uncertain prior-session acknowledgement at exact pre-write bytes without acknowledging it', () => {
    let raw: string | null = null, failWrite = false;
    const storage = { getItem: () => raw, setItem: (_key: string, value: string) => {
      if (failWrite) throw new Error('write denied'); raw = value;
    } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(first.reserve(owner!, action(1)).ok).toBe(true);
    const observation: WorkQueueTaskObservation = { owner: { ...owner! }, engagementId: engagement,
      taskId: id(1), revision: 5, availabilityRevision: 7 };
    failWrite = true;
    expect(first.acknowledge(owner!, id(1001), observation, 7)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const original = raw;
    owner = { ...owner!, generation: '5', invalidation: 1 }; failWrite = false;
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(replacement.pendingReconciliation(owner)).toMatchObject({ ok: true, value: { kind: 'acknowledgement', sameSession: false } });
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(original);
    expect(replacement.read(owner)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
  });

  it('preserves a definitive-response record if the prior-session removal never changed its pre-write bytes', async () => {
    let raw: string | null = null, failWrite = false;
    const storage = { getItem: () => raw, setItem: (_key: string, value: string) => {
      if (failWrite) throw new Error('write denied'); raw = value;
    } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const invoked = first.invokeReserved(reserved.value, async () => true);
    if (!invoked.ok) throw new Error(invoked.reason);
    await invoked.value; failWrite = true;
    expect(first.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const original = raw;
    owner = { ...owner!, generation: '5', invalidation: 1 }; failWrite = false;
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(original);
    expect(replacement.read(owner)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
  });

  it('retains an exact reservation after a session change during an uncertain reservation write', () => {
    let raw: string | null = null, attemptedRelease: unknown;
    const originalOwner = { ...owner! };
    let storage!: Storage;
    storage = { getItem: () => raw, setItem: (_key: string, value: string) => {
      raw = value; owner = { ...originalOwner, generation: '5', invalidation: 1 };
      attemptedRelease = new WorkQueueActionRegistry(() => owner, () => storage).releasePriorSessionFence(owner);
    } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(first.reserveForDispatchRecoverable(originalOwner, { ...action(1), action: 'cancel', requestedDueDate: null }))
      .toEqual({ ok: false, reason: 'session_changed' });
    expect(attemptedRelease).toEqual({ ok: false, reason: 'dispatch_started' });
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(replacement.pendingReconciliation(owner!)).toMatchObject({ ok: true, value: { sameSession: false, kind: 'cleanup' } });
    const before = raw;
    expect(replacement.releasePriorSessionFence(owner!)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(before);
    expect(replacement.read(owner!)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
  });

  it('reconciles an uncertain acknowledgement from a replacement registry with a fresh exact observation', () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 3) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(first.reserve(owner!, action(1)).ok).toBe(true);
    expect(first.reserve(owner!, action(2)).ok).toBe(true);
    const observation: WorkQueueTaskObservation = { owner: { ...owner! }, engagementId: engagement,
      taskId: id(1), revision: 5, availabilityRevision: 7 };
    expect(first.acknowledge(owner!, id(1001), observation, 7)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const expected = raw!;
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    const pending = replacement.pendingReconciliation(owner!);
    if (!pending.ok || !pending.value?.ticket) throw new Error('acknowledgement handoff missing');
    expect(pending.value).toMatchObject({ kind: 'acknowledgement', sameSession: true, item: { taskId: id(1) } });
    expect(replacement.read(owner!)).toMatchObject({ ok: true, value: [{ taskId: id(2) }] });
    expect(replacement.reserve(owner!, action(3))).toEqual({ ok: false, reason: 'reconciliation_pending' });
    expect(replacement.reconcileAcknowledgement(owner!, pending.value.ticket, { ...observation, revision: 4 }, 7))
      .toEqual({ ok: false, reason: 'observation_stale' });
    expect(replacement.reconcileAcknowledgement(owner!, pending.value.ticket, observation, 8))
      .toEqual({ ok: false, reason: 'observation_stale' });
    raw = null;
    expect(replacement.reconcileAcknowledgement(owner!, pending.value.ticket, observation, 7))
      .toEqual({ ok: false, reason: 'invalid_storage' });
    raw = expected;
    expect(replacement.reconcileAcknowledgement(owner!, pending.value.ticket, observation, 7)).toEqual({ ok: true, value: undefined });
    expect(replacement.read(owner!)).toMatchObject({ ok: true, value: [{ taskId: id(2) }] });
    expect(first.reconcileAcknowledgement(owner!, pending.value.ticket, observation, 7)).toEqual({ ok: false, reason: 'invalid_input' });
    expect(replacement.reserve(owner!, action(3)).ok).toBe(true);
  });

  it('isolates acknowledgement handoff and releases a prior session only for exact expected bytes', () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(first.reserve(owner!, action(1)).ok).toBe(true);
    const observation: WorkQueueTaskObservation = { owner: { ...owner! }, engagementId: engagement,
      taskId: id(1), revision: 5, availabilityRevision: 7 };
    expect(first.acknowledge(owner!, id(1001), observation, 7)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const expected = raw!;
    owner = { ...owner!, userId: otherUser, generation: '5', invalidation: 1 };
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(replacement.pendingReconciliation(owner)).toEqual({ ok: true, value: null });
    owner = { ...owner, userId: user };
    expect(replacement.pendingReconciliation(owner)).toMatchObject({ ok: true, value: { kind: 'acknowledgement', sameSession: false } });
    raw = null;
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: false, reason: 'invalid_storage' });
    raw = expected;
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(expected);
    expect(replacement.pendingReconciliation(owner)).toEqual({ ok: true, value: null });
  });

  it('does not abort an uninvoked reservation after the owner session changes', () => {
    const reserved = registry.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    owner = { ...owner!, generation: '5', invalidation: 1 };
    expect(registry.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    expect(registry.read(owner)).toMatchObject({ ok: true, value: [{ action: 'cancel', taskId: id(1) }] });
  });

  it('retains an uninvoked cleanup ticket through a transient read failure and never dispatches it', () => {
    let raw: string | null = null, unavailable = false, calls = 0;
    const storage = { getItem: () => { if (unavailable) throw new Error('unavailable'); return raw; },
      setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(guarded.reserve(owner!, action(2)).ok).toBe(true);
    const reserved = guarded.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    unavailable = true;
    expect(guarded.invokeReserved(reserved.value, async () => { calls++; })).toEqual({ ok: false, reason: 'storage_unavailable' });
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    unavailable = false;
    expect(guarded.invokeReserved(reserved.value, async () => { calls++; })).toEqual({ ok: false, reason: 'cleanup_started' });
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: true, value: undefined });
    expect(guarded.read(owner!)).toMatchObject({ ok: true, value: [{ taskId: id(2) }] });
    expect(calls).toBe(0);
  });

  it('confirms an uncertain cleanup readback only against its exact resulting document', () => {
    let raw: string | null = null, failReadback = false, failNextRead = false;
    const storage = { getItem: () => { if (failNextRead) { failNextRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (failReadback) { failReadback = false; failNextRead = true; } } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatch(owner!, { ...action(1), action: 'reopen', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    failReadback = true;
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    expect(guarded.invokeReserved(reserved.value, async () => true)).toEqual({ ok: false, reason: 'cleanup_started' });
    const expected = raw!;
    raw = JSON.stringify({ ...JSON.parse(expected), entries: [{ ...reserved.value.item, taskId: id(2), requestId: id(1002) }] });
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(raw).not.toBe(expected);
    raw = expected;
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: true, value: undefined });
    expect(guarded.read(owner!)).toEqual({ ok: true, value: [] });
  });

  it('never clears a mismatched or corrupt reservation and loses cleanup capability after session change', () => {
    let raw: string | null = null;
    const storage = { getItem: () => raw, setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const original = raw!;
    const changed = JSON.parse(original); changed.entries[0].expectedRevision++;
    const mismatched = JSON.stringify(changed); raw = mismatched;
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(raw).toBe(mismatched);
    raw = '{broken';
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(raw).toBe('{broken');
    raw = original;
    const oldOwner = { ...owner! }; owner = { ...oldOwner, generation: '5', invalidation: 1 };
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    expect(raw).toBe(original);
    owner = oldOwner;
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
  });

  it('keeps prior-session fence metadata but never reissues its ticket after observing another session', () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const original = { ...owner! };
    owner = { ...original, generation: '5', invalidation: 1 };
    const replacement = new WorkQueueActionRegistry(() => owner, () => storage);
    const pending = replacement.pendingReconciliation(owner);
    expect(pending).toMatchObject({ ok: true, value: { kind: 'cleanup', sameSession: false } });
    if (pending.ok && pending.value) expect(pending.value.ticket).toBeUndefined();
    owner = original;
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    const reverted = replacement.pendingReconciliation(owner);
    expect(reverted).toMatchObject({ ok: true, value: { kind: 'cleanup', sameSession: false } });
    if (reverted.ok && reverted.value) expect(reverted.value.ticket).toBeUndefined();
    owner = { ...original, generation: '5', invalidation: 1 };
    expect(replacement.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
  });

  it('invalidates a cleanup ticket if the session changes during a failed storage read', () => {
    let raw: string | null = null, switchOnRead = false;
    const original = { ...owner! }, later = { ...original, generation: '5', invalidation: 1 };
    const storage = { getItem: () => {
      if (switchOnRead) { switchOnRead = false; owner = later; throw new Error('read failed'); }
      return raw;
    }, setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(original, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    switchOnRead = true;
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    const unchanged = raw;
    owner = original;
    expect(first.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    owner = later;
    expect(first.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(unchanged);
    expect(first.read(owner)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
  });

  for (const [label, badBytes] of [
    ['malformed JSON', '{broken'],
    ['oversized data', 'x'.repeat(100_001)],
    ['wrong document type', '[]'],
    ['unsupported schema', JSON.stringify({ version: 3, firmId: firm, userId: user, entries: [] })],
    ['invalid record', JSON.stringify({ version: 2, firmId: firm, userId: user, entries: [{}] })],
  ] as const) {
    it(`prioritizes session change over ${label} and never revives the cleanup ticket`, () => {
      let raw: string | null = null, switchOnRead = false;
      const original = { ...owner! }, later = { ...original, generation: '5', invalidation: 1 };
      const storage = { getItem: () => {
        if (switchOnRead) { switchOnRead = false; owner = later; return badBytes; }
        return raw;
      }, setItem: (_key: string, value: string) => { raw = value; } } as unknown as Storage;
      const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
      const reserved = guarded.reserveForDispatch(original, { ...action(1), action: 'cancel', requestedDueDate: null });
      if (!reserved.ok) throw new Error(reserved.reason);
      const unchanged = raw;
      switchOnRead = true;
      expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
      owner = original;
      expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
      owner = later;
      expect(guarded.pendingReconciliation(owner)).toMatchObject({ ok: true, value: { kind: 'cleanup', sameSession: false } });
      expect(guarded.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
      expect(raw).toBe(unchanged);
      expect(guarded.read(owner)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
    });
  }

  it('prioritizes a session change during differing reservation readback and preserves prior-session metadata', () => {
    let raw: string | null = null, switchOnReadback = false;
    const original = { ...owner! }, later = { ...original, generation: '5', invalidation: 1 };
    const storage = { getItem: () => {
      if (switchOnReadback) { switchOnReadback = false; owner = later; return 'different bytes'; }
      return raw;
    }, setItem: (_key: string, value: string) => { raw = value; switchOnReadback = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(guarded.reserveForDispatchRecoverable(original, { ...action(1), action: 'cancel', requestedDueDate: null }))
      .toEqual({ ok: false, reason: 'session_changed' });
    const expected = raw;
    owner = original;
    const oldPending = guarded.pendingReconciliation(owner);
    expect(oldPending).toMatchObject({ ok: true, value: { sameSession: false, kind: 'cleanup' } });
    if (oldPending.ok && oldPending.value) expect(oldPending.value.ticket).toBeUndefined();
    owner = later;
    expect(guarded.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(expected);
    expect(guarded.read(owner)).toMatchObject({ ok: true, value: [{ taskId: id(1) }] });
  });

  it('prioritizes a session change during differing cleanup readback and invalidates the old ticket', () => {
    let raw: string | null = null, writes = 0, switchOnReadback = false;
    const original = { ...owner! }, later = { ...original, generation: '5', invalidation: 1 };
    const storage = { getItem: () => {
      if (switchOnReadback) { switchOnReadback = false; owner = later; return 'different bytes'; }
      return raw;
    }, setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) switchOnReadback = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatch(original, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    const expected = raw;
    owner = original;
    expect(guarded.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    owner = later;
    expect(guarded.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(expected);
    expect(guarded.read(owner)).toEqual({ ok: true, value: [] });
  });

  it('prioritizes a session change during differing acknowledgement readback', () => {
    let raw: string | null = null, writes = 0, switchOnReadback = false;
    const original = { ...owner! }, later = { ...original, generation: '5', invalidation: 1 };
    const storage = { getItem: () => {
      if (switchOnReadback) { switchOnReadback = false; owner = later; return 'different bytes'; }
      return raw;
    }, setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) switchOnReadback = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(guarded.reserve(original, action(1)).ok).toBe(true);
    const observation: WorkQueueTaskObservation = { owner: original, engagementId: engagement,
      taskId: id(1), revision: 5, availabilityRevision: 7 };
    expect(guarded.acknowledge(original, id(1001), observation, 7)).toEqual({ ok: false, reason: 'session_changed' });
    const expected = raw;
    owner = original;
    const oldPending = guarded.pendingReconciliation(owner);
    expect(oldPending).toMatchObject({ ok: true, value: { kind: 'acknowledgement', sameSession: false } });
    if (oldPending.ok && oldPending.value) expect(oldPending.value.ticket).toBeUndefined();
    owner = later;
    expect(guarded.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(expected);
  });

  it('prioritizes a session change during differing definitive-removal readback', async () => {
    let raw: string | null = null, writes = 0, switchOnReadback = false;
    const original = { ...owner! }, later = { ...original, generation: '5', invalidation: 1 };
    const storage = { getItem: () => {
      if (switchOnReadback) { switchOnReadback = false; owner = later; return 'different bytes'; }
      return raw;
    }, setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) switchOnReadback = true; } } as unknown as Storage;
    const guarded = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = guarded.reserveForDispatch(original, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const invoked = guarded.invokeReserved(reserved.value, async () => true);
    if (!invoked.ok) throw new Error(invoked.reason);
    await invoked.value;
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    const expected = raw;
    owner = original;
    expect(guarded.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    owner = later;
    expect(guarded.pendingReconciliation(owner)).toMatchObject({ ok: true, value: { kind: 'definitive', sameSession: false } });
    expect(guarded.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
    expect(raw).toBe(expected);
  });

  it('invalidates an acknowledgement ticket on a session transition without losing the reconciliation fence', () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    expect(first.reserve(owner!, action(1)).ok).toBe(true);
    const original = { ...owner! };
    const observation: WorkQueueTaskObservation = { owner: original, engagementId: engagement,
      taskId: id(1), revision: 5, availabilityRevision: 7 };
    expect(first.acknowledge(original, id(1001), observation, 7)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const pending = first.pendingReconciliation(original);
    if (!pending.ok || !pending.value?.ticket) throw new Error('acknowledgement ticket missing');
    const ticket = pending.value.ticket;
    owner = { ...original, generation: '5', invalidation: 1 };
    expect(first.reconcileAcknowledgement(owner, ticket, { ...observation, owner }, 7)).toEqual({ ok: false, reason: 'session_changed' });
    owner = original;
    expect(first.reconcileAcknowledgement(owner, ticket, observation, 7)).toEqual({ ok: false, reason: 'invalid_input' });
    owner = { ...original, generation: '5', invalidation: 1 };
    expect(first.pendingReconciliation(owner)).toMatchObject({ ok: true, value: { kind: 'acknowledgement', sameSession: false } });
    expect(first.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
  });

  it('invalidates a definitive-removal ticket after a session transition while retaining its exact handoff', async () => {
    let raw: string | null = null, writes = 0, failRead = false;
    const storage = { getItem: () => { if (failRead) { failRead = false; throw new Error('readback'); } return raw; },
      setItem: (_key: string, value: string) => { raw = value; if (++writes === 2) failRead = true; } } as unknown as Storage;
    const first = new WorkQueueActionRegistry(() => owner, () => storage);
    const reserved = first.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const invoked = first.invokeReserved(reserved.value, async () => true);
    if (!invoked.ok) throw new Error(invoked.reason);
    await invoked.value;
    expect(first.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'storage_unavailable' });
    const original = { ...owner! };
    owner = { ...original, generation: '5', invalidation: 1 };
    expect(first.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    owner = original;
    expect(first.removeDefinitiveTicket(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    owner = { ...original, generation: '5', invalidation: 1 };
    expect(first.pendingReconciliation(owner)).toMatchObject({ ok: true, value: { kind: 'definitive', sameSession: false } });
    expect(first.releasePriorSessionFence(owner)).toEqual({ ok: true, value: undefined });
  });

  it('retries only the original in-memory recovery callback, with the same intact fence', async () => {
    const reserved = registry.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    let calls = 0, finish!: (value: string) => void;
    const original = () => { calls++; return calls === 1 ? new Promise<string>(resolve => { finish = resolve; }) : Promise.resolve('same intent'); };
    const first = registry.invokeReserved(reserved.value, original);
    if (!first.ok) throw new Error(first.reason);
    expect(registry.retryReadiness(reserved.value)).toEqual({ ok: true, value: 'in_flight' });
    expect(registry.invokeExactRetry(reserved.value, () => true)).toEqual({ ok: false, reason: 'dispatch_started' });
    finish('unknown'); await first.value;
    expect(registry.retryReadiness(reserved.value)).toEqual({ ok: true, value: 'settled' });
    expect(registry.invokeExactRetry(reserved.value, () => false)).toEqual({ ok: false, reason: 'observation_stale' });
    expect(calls).toBe(1);
    const retry = registry.invokeExactRetry<string>(reserved.value, () => true);
    if (!retry.ok) throw new Error(retry.reason);
    expect(await retry.value).toBe('same intent'); expect(calls).toBe(2);
    expect(registry.read(owner!)).toMatchObject({ ok: true, value: [{ requestId: id(1001), taskId: id(1) }] });
    expect(registry.abortBeforeDispatch(reserved.value)).toEqual({ ok: false, reason: 'dispatch_started' });
    const restored = new WorkQueueActionRegistry(() => owner);
    expect(restored.retryReadiness(reserved.value)).toEqual({ ok: false, reason: 'invalid_input' });
    expect(restored.invokeExactRetry(reserved.value, () => true)).toEqual({ ok: false, reason: 'invalid_input' });
  });

  it('rejects retry after owner change, corrupted storage, or for actions without replay support', async () => {
    const reserved = registry.reserveForDispatch(owner!, { ...action(1), action: 'reopen', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const first = registry.invokeReserved(reserved.value, async () => true);
    if (!first.ok) throw new Error(first.reason);
    await first.value;
    const raw = sessionStorage.getItem(key())!;
    sessionStorage.setItem(key(), '{corrupt');
    expect(registry.retryReadiness(reserved.value)).toEqual({ ok: false, reason: 'invalid_storage' });
    expect(registry.invokeExactRetry(reserved.value, () => true)).toEqual({ ok: false, reason: 'invalid_storage' });
    sessionStorage.setItem(key(), raw);
    const oldOwner = { ...owner! };
    owner = { ...oldOwner, generation: '5', invalidation: 1 };
    expect(registry.retryReadiness(reserved.value)).toEqual({ ok: false, reason: 'session_changed' });
    owner = oldOwner;
    expect(registry.invokeExactRetry(reserved.value, () => true)).toEqual({ ok: false, reason: 'invalid_input' });
    const other = registry.reserveForDispatch(owner, action(2));
    if (!other.ok) throw new Error(other.reason);
    const invoked = registry.invokeReserved(other.value, async () => true);
    if (!invoked.ok) throw new Error(invoked.reason);
    await invoked.value;
    expect(registry.invokeExactRetry(other.value, () => true)).toEqual({ ok: false, reason: 'invalid_input' });
  });

  it('reports a rejected invocation as settled without resolving its stored outcome', async () => {
    const reserved = registry.reserveForDispatch(owner!, { ...action(1), action: 'cancel', requestedDueDate: null });
    if (!reserved.ok) throw new Error(reserved.reason);
    const invoked = registry.invokeReserved(reserved.value, () => Promise.reject(new Error('unknown')));
    if (!invoked.ok) throw new Error(invoked.reason);
    await expect(invoked.value).rejects.toThrow('unknown');
    expect(registry.retryReadiness(reserved.value)).toEqual({ ok: true, value: 'settled' });
    expect(registry.read(owner!)).toMatchObject({ ok: true, value: [{ requestId: id(1001) }] });
  });

  it('retains assign and reassign assignee identities through guarded dispatch and reload', async () => {
    const target = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const assign = registry.reserveForDispatch(owner!, { ...action(1), action: 'assign', requestedDueDate: null,
      requestedAssigneeId: target.toUpperCase() });
    if (!assign.ok) throw new Error(assign.reason);
    expect(assign.value.item.requestedAssigneeId).toBe(target);
    const invoked = registry.invokeReserved(assign.value, async () => true);
    expect(invoked.ok).toBe(true);
    if (invoked.ok) expect(await invoked.value).toBe(true);
    const reassign = registry.reserve(owner!, { ...action(2), action: 'reassign', requestedDueDate: null, requestedAssigneeId: otherUser });
    expect(reassign.ok).toBe(true);
    const reloaded = new WorkQueueActionRegistry(() => owner);
    const result = reloaded.read(owner!);
    if (!result.ok) throw new Error(result.reason);
    expect(result.value.map(x => [x.action, x.requestedAssigneeId])).toEqual([['assign', target], ['reassign', otherUser]]);
    expect(JSON.parse(sessionStorage.getItem(key())!).version).toBe(2);
    expect(registry.abortBeforeDispatch(assign.value)).toEqual({ ok: false, reason: 'dispatch_started' });
  });

  it('rejects missing, invalid, or misplaced requested assignees before writing', () => {
    for (const name of ['assign', 'reassign'] as const) {
      const input = { ...action(1), action: name, requestedDueDate: null };
      expect(registry.reserve(owner!, input)).toEqual({ ok: false, reason: 'invalid_input' });
      expect(registry.reserve(owner!, { ...input, requestedAssigneeId: null })).toEqual({ ok: false, reason: 'invalid_input' });
      expect(registry.reserve(owner!, { ...input, requestedAssigneeId: 'not-a-uuid' })).toEqual({ ok: false, reason: 'invalid_input' });
      expect(registry.reserve(owner!, { ...input, requestedAssigneeId: '00000000-0000-0000-0000-000000000000' }))
        .toEqual({ ok: false, reason: 'invalid_input' });
      expect(registry.reserve(owner!, { ...input, requestedAssigneeId: otherUser, requestedDueDate: '2026-10-30' }))
        .toEqual({ ok: false, reason: 'invalid_input' });
    }
    expect(registry.reserve(owner!, { ...action(2), action: 'cancel', requestedDueDate: null, requestedAssigneeId: otherUser }))
      .toEqual({ ok: false, reason: 'invalid_input' });
    expect(sessionStorage.getItem(key())).toBeNull();
  });

  it('reads legacy v2 records with absent or null assignees and preserves them on a mixed write', () => {
    const original = { firmId: firm, userId: user, dispatchGeneration: '3', dispatchInvalidation: 2,
      engagementId: engagement, taskId: id(1), expectedRevision: 4, action: 'cancel', requestedDueDate: null, requestId: id(1001) };
    const nullable = { ...original, taskId: id(2), requestId: id(1002), action: 'resume', requestedAssigneeId: null };
    sessionStorage.setItem(key(), JSON.stringify({ version: 2, firmId: firm, userId: user, entries: [original, nullable] }));
    expect(registry.read(owner!)).toEqual({ ok: true, value: [original, nullable] });
    expect(registry.reserve(owner!, { ...action(3), action: 'assign', requestedDueDate: null, requestedAssigneeId: otherUser }).ok).toBe(true);
    const document = JSON.parse(sessionStorage.getItem(key())!);
    expect(document.entries.slice(0, 2)).toEqual([original, nullable]);
    expect(document.entries[2].requestedAssigneeId).toBe(otherUser);
  });

  it('fails closed without rewriting malformed v1 or v2 assignee records', () => {
    const original = { firmId: firm, userId: user, dispatchGeneration: '3', dispatchInvalidation: 2,
      engagementId: engagement, taskId: id(1), expectedRevision: 4, action: 'assign', requestedDueDate: null, requestId: id(1001) };
    const documents = [
      { version: 2, entries: [original] },
      { version: 2, entries: [{ ...original, requestedAssigneeId: null }] },
      { version: 2, entries: [{ ...original, requestedAssigneeId: 'invalid' }] },
      { version: 2, entries: [{ ...original, action: 'complete', requestedAssigneeId: otherUser }] },
      { version: 1, entries: [{ ...original, requestedAssigneeId: otherUser }] },
    ];
    for (const doc of documents) {
      const raw = JSON.stringify({ ...doc, firmId: firm, userId: user });
      sessionStorage.setItem(key(), raw);
      expect(registry.read(owner!)).toEqual({ ok: false, reason: 'invalid_storage' });
      expect(registry.reserve(owner!, action(2))).toEqual({ ok: false, reason: 'invalid_storage' });
      expect(sessionStorage.getItem(key())).toBe(raw);
    }
  });

  it('retains assignment fences across generations until an explicit matching advanced observation', () => {
    expect(registry.reserve(owner!, { ...action(1), action: 'assign', requestedDueDate: null, requestedAssigneeId: otherUser }).ok).toBe(true);
    owner = { ...owner!, generation: '5', invalidation: 1 };
    expect(registry.removeDefinitive(owner, id(1001))).toEqual({ ok: false, reason: 'session_changed' });
    const observation: WorkQueueTaskObservation = { owner: { ...owner }, engagementId: engagement, taskId: id(1), revision: 4, availabilityRevision: 1 };
    expect(registry.acknowledge(owner, id(1001), observation, 1)).toEqual({ ok: false, reason: 'observation_stale' });
    expect(registry.read(owner)).toMatchObject({ ok: true, value: [{ action: 'assign', requestedAssigneeId: otherUser }] });
    expect(registry.acknowledge(owner, id(1001), { ...observation, revision: 5 }, 1)).toEqual({ ok: true, value: undefined });
  });
});
