import { guidPattern } from '../../core/contracts';

/** The caller must supply the current trusted staff session and its local invalidation counter. */
export interface WorkQueueRegistryOwner {
  firmId: string;
  userId: string;
  generation: string;
  invalidation: number;
  staff: boolean;
}

export interface WorkQueuePendingAction {
  firmId: string;
  userId: string;
  dispatchGeneration: string;
  dispatchInvalidation: number;
  engagementId: string;
  taskId: string;
  expectedRevision: number;
  action: 'complete' | 'changeDueDate' | 'cancel' | 'reopen' | 'suspend' | 'resume' | 'assign' | 'reassign';
  requestedDueDate: string | null;
  /** Required for assignment actions; otherwise absent or null. */
  requestedAssigneeId?: string | null;
  /** Local correlation only. Never send this as a server idempotency key. */
  requestId: string;
}

export type WorkQueueActionInput = Pick<WorkQueuePendingAction,
  'engagementId' | 'taskId' | 'expectedRevision' | 'action' | 'requestedDueDate' | 'requestedAssigneeId' | 'requestId'>;

/** The parent must obtain this from an exact, currently authorized task read. */
export interface WorkQueueTaskObservation {
  owner: WorkQueueRegistryOwner;
  engagementId: string;
  taskId: string;
  revision: number;
  availabilityRevision: number;
}

export type WorkQueueRegistryError = 'session_changed' | 'storage_unavailable' | 'invalid_storage' |
  'invalid_input' | 'capacity' | 'task_pending' | 'request_pending' | 'not_found' | 'observation_stale' | 'dispatch_started' | 'cleanup_started' | 'reconciliation_pending';
export type WorkQueueRegistryResult<T> = { ok: true; value: T } | { ok: false; reason: WorkQueueRegistryError };

/** Opaque, in-memory capability. It cannot be reconstructed from a persisted record. */
export interface WorkQueueDispatchTicket { readonly item: Readonly<WorkQueuePendingAction> }
/** Cleanup only: never grants permission to dispatch. Exists only in the issuing runtime. */
export interface WorkQueueReservationCleanupTicket extends WorkQueueDispatchTicket { readonly cleanupOnly: true }
export type WorkQueueRecoverableReservation = WorkQueueRegistryResult<WorkQueueDispatchTicket> |
  { ok: false; reason: WorkQueueRegistryError; cleanup: WorkQueueReservationCleanupTicket };
export interface WorkQueueReconciliation {
  readonly item: Readonly<WorkQueuePendingAction>;
  readonly kind: 'cleanup' | 'definitive' | 'acknowledgement';
  readonly sameSession: boolean;
  /** Available only to the issuing session. Never use as a dispatch capability. */
  readonly ticket?: WorkQueueDispatchTicket;
}

interface TicketState {
  owner: WorkQueueRegistryOwner; item: WorkQueuePendingAction; invoked: boolean; inFlight: boolean;
  cleanupStarted: boolean; definitiveStarted?: boolean; definitiveVerified?: boolean; acknowledgement?: boolean; reconciling?: boolean;
  revoked?: boolean; sessionInvalidated?: boolean; cleanupExpected?: string; cleanupBefore?: string | null; reservationExpected?: string;
  baseline?: string | null; original?: () => Promise<unknown>;
}

export const WORK_QUEUE_ACTION_CAPACITY = 100;
const prefix = 'auditsphere.work-queue-actions.v1:';
const generationPattern = /^(?:0|[1-9][0-9]{0,18})$/;
const maxGeneration = 9223372036854775807n;
const maxDocumentLength = 100_000;

function failure<T>(reason: WorkQueueRegistryError): WorkQueueRegistryResult<T> { return { ok: false, reason }; }
function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value) && value !== '00000000-0000-0000-0000-000000000000';
}
function validGeneration(value: unknown): value is string {
  return typeof value === 'string' && generationPattern.test(value) && BigInt(value) <= maxGeneration;
}
function validOwner(value: WorkQueueRegistryOwner | null): value is WorkQueueRegistryOwner {
  return !!value && value.staff === true && isGuid(value.firmId) && isGuid(value.userId) &&
    validGeneration(value.generation) && Number.isSafeInteger(value.invalidation) && value.invalidation >= 0;
}
function sameOwner(a: WorkQueueRegistryOwner, b: WorkQueueRegistryOwner): boolean {
  return a.firmId.toLowerCase() === b.firmId.toLowerCase() && a.userId.toLowerCase() === b.userId.toLowerCase() &&
    a.generation === b.generation && a.invalidation === b.invalidation;
}
function validDate(value: unknown): value is string | null {
  if (value === null) return true;
  if (typeof value !== 'string' || !/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value)) return false;
  const year = Number(value.slice(0, 4)), month = Number(value.slice(5, 7)), day = Number(value.slice(8, 10));
  if (year < 1 || month < 1 || month > 12 || day < 1) return false;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  return day <= [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
}
function validRevision(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 1;
}
function validInput(value: WorkQueueActionInput): boolean {
  return !!value && isGuid(value.engagementId) && isGuid(value.taskId) && isGuid(value.requestId) &&
    validRevision(value.expectedRevision) && ['complete', 'changeDueDate', 'cancel', 'reopen', 'suspend', 'resume', 'assign', 'reassign'].includes(value.action) &&
    validDate(value.requestedDueDate) && (value.action === 'changeDueDate' || value.requestedDueDate === null) &&
    (value.action === 'assign' || value.action === 'reassign' ? isGuid(value.requestedAssigneeId) :
      value.requestedAssigneeId === undefined || value.requestedAssigneeId === null);
}
function record(value: unknown, firmId: string, userId: string, version: number): WorkQueuePendingAction | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const x = value as Record<string, unknown>;
  const fields = ['firmId', 'userId', 'dispatchGeneration', 'dispatchInvalidation', 'engagementId', 'taskId',
    'expectedRevision', 'action', 'requestedDueDate', 'requestId'];
  const hasAssignee = Object.prototype.hasOwnProperty.call(x, 'requestedAssigneeId');
  if (fields.some(key => !Object.prototype.hasOwnProperty.call(x, key)) ||
      Object.keys(x).some(key => !fields.includes(key) && key !== 'requestedAssigneeId') ||
      (version === 1 && hasAssignee)) return null;
  if (!isGuid(x['firmId']) || x['firmId'].toLowerCase() !== firmId ||
      !isGuid(x['userId']) || x['userId'].toLowerCase() !== userId || !validGeneration(x['dispatchGeneration']) ||
      typeof x['dispatchInvalidation'] !== 'number' || !Number.isSafeInteger(x['dispatchInvalidation']) || x['dispatchInvalidation'] < 0 ||
      !isGuid(x['engagementId']) || !isGuid(x['taskId']) || !isGuid(x['requestId']) ||
      !validRevision(x['expectedRevision']) ||
      (x['action'] !== 'complete' && x['action'] !== 'changeDueDate' &&
        (version === 1 || (x['action'] !== 'cancel' && x['action'] !== 'reopen' && x['action'] !== 'suspend' && x['action'] !== 'resume' &&
          x['action'] !== 'assign' && x['action'] !== 'reassign'))) ||
      !validDate(x['requestedDueDate']) || (x['action'] !== 'changeDueDate' && x['requestedDueDate'] !== null) ||
      (x['action'] === 'assign' || x['action'] === 'reassign'
        ? !isGuid(x['requestedAssigneeId']) : hasAssignee && x['requestedAssigneeId'] !== null)) return null;
  return { firmId, userId, dispatchGeneration: x['dispatchGeneration'], dispatchInvalidation: x['dispatchInvalidation'],
    engagementId: x['engagementId'].toLowerCase(), taskId: x['taskId'].toLowerCase(),
    expectedRevision: x['expectedRevision'], action: x['action'] as WorkQueuePendingAction['action'], requestedDueDate: x['requestedDueDate'],
    requestId: x['requestId'].toLowerCase(),
    ...(hasAssignee ? { requestedAssigneeId: isGuid(x['requestedAssigneeId']) ? x['requestedAssigneeId'].toLowerCase() : null } : {}) };
}

/**
 * Per-tab recovery fences. All records are unresolved from reservation until explicitly removed;
 * a caller may separately show an in-flight state while its own request is running. This class
 * neither authorizes reads nor proves a command outcome. Callers control when a command is invoked.
 */
export class WorkQueueActionRegistry {
  private static readonly reconciliationFences = new WeakMap<Storage, Map<string, object>>();
  private static readonly fenceStorage = new WeakMap<object, Storage>();
  private static readonly fenceTickets = new WeakMap<object, TicketState>();
  private readonly dispatchTickets = new WeakMap<WorkQueueDispatchTicket, TicketState>();
  constructor(private readonly currentOwner: () => WorkQueueRegistryOwner | null,
    private readonly storage: () => Storage = () => sessionStorage) {}

  private current(owner: WorkQueueRegistryOwner): boolean {
    try {
      const current = this.currentOwner();
      return validOwner(owner) && validOwner(current) && sameOwner(owner, current);
    } catch { return false; }
  }
  private key(owner: WorkQueueRegistryOwner): string {
    return `${prefix}${owner.firmId.toLowerCase()}:${owner.userId.toLowerCase()}`;
  }

  private invalidateTicketSession(ticket: WorkQueueDispatchTicket, state: TicketState): void {
    state.sessionInvalidated = true;
    state.original = undefined;
    this.dispatchTickets.delete(ticket);
  }

  private ticketSession(ticket: WorkQueueDispatchTicket, state: TicketState): WorkQueueRegistryResult<void> {
    if (state.sessionInvalidated) return failure('invalid_input');
    if (!this.current(state.owner)) { this.invalidateTicketSession(ticket, state); return failure('session_changed'); }
    return { ok: true, value: undefined };
  }

  private fence(owner: WorkQueueRegistryOwner, ticket?: object, claim = false): WorkQueueRegistryResult<void> {
    if (!this.current(owner)) return failure('session_changed');
    try {
      const storage = this.storage(), key = this.key(owner);
      if (!this.current(owner)) return failure('session_changed');
      if (ticket && WorkQueueActionRegistry.fenceStorage.has(ticket) && WorkQueueActionRegistry.fenceStorage.get(ticket) !== storage)
        return failure('invalid_storage');
      let fences = WorkQueueActionRegistry.reconciliationFences.get(storage);
      const held = fences?.get(key);
      if (held && held !== ticket) return failure('reconciliation_pending');
      if (claim && ticket && !held) {
        const state = this.dispatchTickets.get(ticket as WorkQueueDispatchTicket);
        if (!state) return failure('invalid_input');
        if (!fences) { fences = new Map(); WorkQueueActionRegistry.reconciliationFences.set(storage, fences); }
        fences.set(key, ticket);
        WorkQueueActionRegistry.fenceStorage.set(ticket, storage);
        WorkQueueActionRegistry.fenceTickets.set(ticket, state);
      }
      return { ok: true, value: undefined };
    } catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
  }

  private releaseFence(owner: WorkQueueRegistryOwner, ticket: object): WorkQueueRegistryResult<void> {
    if (!this.current(owner)) return failure('session_changed');
    try {
      const storage = this.storage(), fences = WorkQueueActionRegistry.reconciliationFences.get(storage), key = this.key(owner);
      if (!this.current(owner)) return failure('session_changed');
      if (WorkQueueActionRegistry.fenceStorage.get(ticket) !== storage || fences?.get(key) !== ticket) return failure('invalid_storage');
      fences.delete(key);
      WorkQueueActionRegistry.fenceStorage.delete(ticket);
      WorkQueueActionRegistry.fenceTickets.delete(ticket);
      return { ok: true, value: undefined };
    } catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
  }

  /** Read-only handoff; callers must reauthorize the exact task before displaying or acting on this item. */
  pendingReconciliation(owner: WorkQueueRegistryOwner): WorkQueueRegistryResult<WorkQueueReconciliation | null> {
    if (!this.current(owner)) return failure('session_changed');
    try {
      const storage = this.storage(), ticket = WorkQueueActionRegistry.reconciliationFences.get(storage)?.get(this.key(owner));
      if (!this.current(owner)) return failure('session_changed');
      if (!ticket) return { ok: true, value: null };
      const state = WorkQueueActionRegistry.fenceTickets.get(ticket);
      if (!state || state.revoked || state.owner.firmId.toLowerCase() !== owner.firmId.toLowerCase() ||
          state.owner.userId.toLowerCase() !== owner.userId.toLowerCase()) return failure('invalid_storage');
      if (!sameOwner(owner, state.owner)) this.invalidateTicketSession(ticket as WorkQueueDispatchTicket, state);
      const sameSession = !state.sessionInvalidated && sameOwner(owner, state.owner);
      return { ok: true, value: { item: { ...state.item }, kind: state.acknowledgement ? 'acknowledgement' :
        state.definitiveStarted ? 'definitive' : 'cleanup', sameSession,
        ...(sameSession ? { ticket: ticket as WorkQueueDispatchTicket } : {}) } };
    } catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
  }

  /** A new session may release only a stranded serialization fence, never remove a record or claim an outcome. */
  releasePriorSessionFence(owner: WorkQueueRegistryOwner): WorkQueueRegistryResult<void> {
    const pending = this.pendingReconciliation(owner);
    if (!pending.ok) return pending;
    if (!pending.value || pending.value.sameSession) return failure('invalid_input');
    let ticket: object | undefined;
    try { ticket = WorkQueueActionRegistry.reconciliationFences.get(this.storage())?.get(this.key(owner)); }
    catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
    if (!this.current(owner)) return failure('session_changed');
    if (!ticket) return failure('invalid_storage');
    const state = WorkQueueActionRegistry.fenceTickets.get(ticket);
    if (!state || state.revoked || sameOwner(owner, state.owner) || state.owner.firmId.toLowerCase() !== owner.firmId.toLowerCase() ||
        state.owner.userId.toLowerCase() !== owner.userId.toLowerCase()) return failure('session_changed');
    if (state.inFlight || state.reconciling) return failure('dispatch_started');
    const snapshot = this.readSnapshot(owner);
    if (!snapshot.ok) return snapshot;
    const item = snapshot.value.entries.find(x => x.requestId === state.item.requestId);
    if (item && JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
    if (!item && snapshot.value.entries.some(x => x.taskId === state.item.taskId)) return failure('invalid_storage');
    if (state.cleanupExpected !== undefined) {
      if (snapshot.value.raw !== state.cleanupExpected && snapshot.value.raw !== state.cleanupBefore) return failure('invalid_storage');
    } else if (state.reservationExpected !== undefined) {
      if (snapshot.value.raw !== state.reservationExpected && snapshot.value.raw !== state.baseline) return failure('invalid_storage');
    } else if (!item) return failure('invalid_storage');
    let verifiedRaw: string | null;
    try { verifiedRaw = this.storage().getItem(this.key(owner)); }
    catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
    if (!this.current(owner)) return failure('session_changed');
    if (verifiedRaw !== snapshot.value.raw) return failure('invalid_storage');
    const released = this.releaseFence(owner, ticket);
    if (!released.ok) return released;
    state.revoked = true; state.original = undefined;
    return { ok: true, value: undefined };
  }

  /** Includes prior generations for this firm/user; callers must perform fresh authorization before display. */
  read(owner: WorkQueueRegistryOwner): WorkQueueRegistryResult<WorkQueuePendingAction[]> {
    const snapshot = this.readSnapshot(owner);
    return snapshot.ok ? { ok: true, value: snapshot.value.entries } : snapshot;
  }

  private readSnapshot(owner: WorkQueueRegistryOwner): WorkQueueRegistryResult<{ entries: WorkQueuePendingAction[]; raw: string | null }> {
    if (!this.current(owner)) return failure('session_changed');
    let raw: string | null;
    try { raw = this.storage().getItem(this.key(owner)); }
    catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
    if (!this.current(owner)) return failure('session_changed');
    if (raw === null) return { ok: true, value: { entries: [], raw } };
    const invalid = () => this.current(owner) ? failure<{ entries: WorkQueuePendingAction[]; raw: string | null }>('invalid_storage') :
      failure<{ entries: WorkQueuePendingAction[]; raw: string | null }>('session_changed');
    if (raw.length > maxDocumentLength) return invalid();
    try {
      const data: unknown = JSON.parse(raw);
      if (!data || typeof data !== 'object' || Array.isArray(data)) return invalid();
      const doc = data as Record<string, unknown>;
      const firm = owner.firmId.toLowerCase(), user = owner.userId.toLowerCase();
      if (Object.keys(doc).length !== 4 || Object.keys(doc).some(key => !['version', 'firmId', 'userId', 'entries'].includes(key)) ||
          (doc['version'] !== 1 && doc['version'] !== 2) || doc['firmId'] !== firm || doc['userId'] !== user || !Array.isArray(doc['entries']) ||
          doc['entries'].length > WORK_QUEUE_ACTION_CAPACITY) return invalid();
      const entries: WorkQueuePendingAction[] = [];
      const tasks = new Set<string>(), requests = new Set<string>();
      for (const value of doc['entries']) {
        const item = record(value, firm, user, doc['version']);
        if (!item || tasks.has(item.taskId) || requests.has(item.requestId)) return invalid();
        tasks.add(item.taskId); requests.add(item.requestId); entries.push(item);
      }
      return this.current(owner) ? { ok: true, value: { entries, raw } } : failure('session_changed');
    } catch { return invalid(); }
  }

  /** Persist before dispatch. One task may have only one unresolved action across generations. */
  reserve(owner: WorkQueueRegistryOwner, input: WorkQueueActionInput): WorkQueueRegistryResult<WorkQueuePendingAction> {
    if (!this.current(owner)) return failure('session_changed');
    if (!validInput(input)) return failure('invalid_input');
    const available = this.fence(owner);
    if (!available.ok) return available;
    const existing = this.read(owner);
    if (!existing.ok) return existing;
    const engagementId = input.engagementId.toLowerCase(), taskId = input.taskId.toLowerCase(), requestId = input.requestId.toLowerCase();
    if (existing.value.some(x => x.taskId === taskId)) return failure('task_pending');
    if (existing.value.some(x => x.requestId === requestId)) return failure('request_pending');
    if (existing.value.length >= WORK_QUEUE_ACTION_CAPACITY) return failure('capacity');
    const item: WorkQueuePendingAction = { firmId: owner.firmId.toLowerCase(), userId: owner.userId.toLowerCase(),
      dispatchGeneration: owner.generation, dispatchInvalidation: owner.invalidation, engagementId, taskId,
      expectedRevision: input.expectedRevision, action: input.action, requestedDueDate: input.requestedDueDate, requestId,
      ...(input.requestedAssigneeId === undefined ? {} : { requestedAssigneeId: input.requestedAssigneeId?.toLowerCase() ?? null }) };
    const saved = this.write(owner, [...existing.value, item]);
    return saved.ok ? { ok: true, value: { ...item } } : saved;
  }

  /** A ticket is issued only after a reservation has been written and read back successfully. */
  reserveForDispatch(owner: WorkQueueRegistryOwner, input: WorkQueueActionInput): WorkQueueRegistryResult<WorkQueueDispatchTicket> {
    const reserved = this.reserve(owner, input);
    if (!reserved.ok) return reserved;
    const item = { ...reserved.value };
    const ticket: WorkQueueDispatchTicket = Object.freeze({ item: Object.freeze({ ...item }) });
    this.dispatchTickets.set(ticket, { owner: { ...owner }, item, invoked: false, inFlight: false, cleanupStarted: false });
    return { ok: true, value: ticket };
  }

  /** On an uncertain reservation write, return a cleanup-only ticket, never a dispatch ticket. */
  reserveForDispatchRecoverable(owner: WorkQueueRegistryOwner, input: WorkQueueActionInput): WorkQueueRecoverableReservation {
    if (!this.current(owner)) return failure('session_changed');
    if (!validInput(input)) return failure('invalid_input');
    const available = this.fence(owner);
    if (!available.ok) return available;
    const snapshot = this.readSnapshot(owner);
    if (!snapshot.ok) return snapshot;
    const existing = snapshot.value.entries;
    const engagementId = input.engagementId.toLowerCase(), taskId = input.taskId.toLowerCase(), requestId = input.requestId.toLowerCase();
    if (existing.some(x => x.taskId === taskId)) return failure('task_pending');
    if (existing.some(x => x.requestId === requestId)) return failure('request_pending');
    if (existing.length >= WORK_QUEUE_ACTION_CAPACITY) return failure('capacity');
    const item: WorkQueuePendingAction = { firmId: owner.firmId.toLowerCase(), userId: owner.userId.toLowerCase(),
      dispatchGeneration: owner.generation, dispatchInvalidation: owner.invalidation, engagementId, taskId,
      expectedRevision: input.expectedRevision, action: input.action, requestedDueDate: input.requestedDueDate, requestId,
      ...(input.requestedAssigneeId === undefined ? {} : { requestedAssigneeId: input.requestedAssigneeId?.toLowerCase() ?? null }) };
    const cleanup: WorkQueueReservationCleanupTicket = Object.freeze({ item: Object.freeze({ ...item }), cleanupOnly: true });
    const cleanupState: TicketState = { owner: { ...owner }, item, invoked: false, inFlight: true,
      cleanupStarted: true, baseline: snapshot.value.raw, reservationExpected: this.serialize(owner, [...existing, item]) };
    this.dispatchTickets.set(cleanup, cleanupState);
    const claimed = this.fence(owner, cleanup, true);
    if (!claimed.ok) { this.dispatchTickets.delete(cleanup); return claimed; }
    let attempted = false;
    const saved = this.write(owner, [...existing, item], () => { attempted = true; }, cleanup);
    cleanupState.inFlight = false;
    if (!saved.ok) {
      if (saved.reason === 'session_changed') { this.invalidateTicketSession(cleanup, cleanupState); return saved; }
      if (!attempted) {
        const released = this.releaseFence(owner, cleanup);
        if (released.ok) { this.dispatchTickets.delete(cleanup); return saved; }
        if (!this.current(owner)) { this.invalidateTicketSession(cleanup, cleanupState); return saved; }
      }
      if (!this.current(owner)) { this.invalidateTicketSession(cleanup, cleanupState); return saved; }
      return { ok: false, reason: saved.reason, cleanup };
    }
    const released = this.releaseFence(owner, cleanup);
    if (!released.ok) {
      if (released.reason === 'session_changed') { this.invalidateTicketSession(cleanup, cleanupState); return released; }
      if (!this.current(owner)) { this.invalidateTicketSession(cleanup, cleanupState); return released; }
      return { ok: false, reason: released.reason, cleanup };
    }
    this.dispatchTickets.delete(cleanup);
    const ticket: WorkQueueDispatchTicket = Object.freeze({ item: Object.freeze({ ...item }) });
    this.dispatchTickets.set(ticket, { owner: { ...owner }, item, invoked: false, inFlight: false, cleanupStarted: false });
    return { ok: true, value: ticket };
  }

  /** Mark invoked before calling the supplied function. Recovery actions retain this exact in-memory callback. */
  invokeReserved<T>(ticket: WorkQueueDispatchTicket, invoke: () => Promise<T>): WorkQueueRegistryResult<Promise<T>> {
    const state = this.dispatchTickets.get(ticket);
    if (!state || state.revoked) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (state.invoked) return failure('dispatch_started');
    if (state.cleanupStarted) return failure('cleanup_started');
    const available = this.fence(state.owner);
    if (!available.ok) {
      if (available.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return available;
    }
    const existing = this.read(state.owner);
    if (!existing.ok) {
      if (existing.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return existing;
    }
    const item = existing.value.find(x => x.requestId === state.item.requestId);
    if (!item || JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
    const stillAvailable = this.fence(state.owner);
    if (!stillAvailable.ok) {
      if (stillAvailable.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return stillAvailable;
    }
    state.invoked = true;
    if (['cancel', 'reopen', 'suspend', 'resume', 'assign', 'reassign'].includes(state.item.action)) state.original = invoke;
    return { ok: true, value: this.callTicket(state, invoke) };
  }

  /** Reports only whether this ticket's local invocation promise settled; it does not establish the server outcome. */
  retryReadiness(ticket: WorkQueueDispatchTicket): WorkQueueRegistryResult<'in_flight' | 'settled'> {
    const state = this.dispatchTickets.get(ticket);
    if (!state || state.revoked || !state.invoked) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (state.definitiveStarted) return failure('cleanup_started');
    if (!state.original) return failure('invalid_input');
    const available = this.fence(state.owner);
    if (!available.ok) {
      if (available.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return available;
    }
    const existing = this.readSnapshot(state.owner);
    if (!existing.ok) {
      if (existing.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return existing;
    }
    const item = existing.value.entries.find(x => x.requestId === state.item.requestId);
    if (!item || JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
    return { ok: true, value: state.inFlight ? 'in_flight' : 'settled' };
  }

  /** Explicit retry only: the caller must check fresh authorization, exact observation and renewed assent in guard. */
  invokeExactRetry<T>(ticket: WorkQueueDispatchTicket, guard: () => boolean): WorkQueueRegistryResult<Promise<T>> {
    const state = this.dispatchTickets.get(ticket);
    if (!state || state.revoked || !state.invoked) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (state.definitiveStarted) return failure('cleanup_started');
    if (!state.original) return failure('invalid_input');
    if (state.inFlight) return failure('dispatch_started');
    const available = this.fence(state.owner);
    if (!available.ok) {
      if (available.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return available;
    }
    const existing = this.read(state.owner);
    if (!existing.ok) {
      if (existing.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return existing;
    }
    const item = existing.value.find(x => x.requestId === state.item.requestId);
    if (!item || JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
    let allowed = false;
    try { allowed = guard(); } catch { /* Fail closed. */ }
    if (!this.current(state.owner)) { this.invalidateTicketSession(ticket, state); return failure('session_changed'); }
    if (!allowed) return failure('observation_stale');
    const stillAvailable = this.fence(state.owner);
    if (!stillAvailable.ok) {
      if (stillAvailable.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return stillAvailable;
    }
    return { ok: true, value: this.callTicket(state, state.original) as Promise<T> };
  }

  private callTicket<T>(state: { inFlight: boolean }, invoke: () => Promise<T>): Promise<T> {
    state.inFlight = true;
    let result: Promise<T>;
    try { result = Promise.resolve(invoke()); }
    catch (error) { result = Promise.reject(error); }
    return result.then(value => { state.inFlight = false; return value; }, error => { state.inFlight = false; throw error; });
  }

  /** Only an uninvoked ticket issued in this runtime can remove a proven pre-dispatch reservation. */
  abortBeforeDispatch(ticket: WorkQueueDispatchTicket): WorkQueueRegistryResult<void> {
    const state = this.dispatchTickets.get(ticket) ?? WorkQueueActionRegistry.fenceTickets.get(ticket);
    if (!state || state.revoked || state.acknowledgement) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (state.invoked) return failure('dispatch_started');
    if (state.inFlight) return failure('dispatch_started');
    if (state.reconciling) return failure('reconciliation_pending');
    state.reconciling = true;
    try {
      // Once cleanup is requested, this ticket must never be used to dispatch, even if storage fails.
      state.cleanupStarted = true;
      const claimed = this.fence(state.owner, ticket, true);
      if (!claimed.ok) {
        if (claimed.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
        return claimed;
      }
      const existing = this.readSnapshot(state.owner);
      if (!existing.ok) {
        if (existing.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
        return existing;
      }
      const item = existing.value.entries.find(x => x.requestId === state.item.requestId);
      if (!item) {
        // An earlier write may have succeeded despite failed readback. Confirm only its exact resulting document.
        if (state.cleanupExpected === undefined && state.baseline === undefined) return failure('invalid_storage');
        let raw: string | null;
        try { raw = this.storage().getItem(this.key(state.owner)); }
        catch {
          const current = this.ticketSession(ticket, state);
          return current.ok ? failure('storage_unavailable') : current;
        }
        if (!this.current(state.owner)) { this.invalidateTicketSession(ticket, state); return failure('session_changed'); }
        if (state.cleanupExpected !== undefined ? raw !== state.cleanupExpected : raw !== state.baseline) return failure('invalid_storage');
        const released = this.releaseFence(state.owner, ticket);
        if (!released.ok) {
          if (released.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
          return released;
        }
        state.revoked = true;
        this.dispatchTickets.delete(ticket);
        return { ok: true, value: undefined };
      }
      if (JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
      const remaining = existing.value.entries.filter(x => x.requestId !== state.item.requestId);
      state.cleanupBefore = existing.value.raw;
      state.cleanupExpected = this.serialize(state.owner, remaining);
      const result = this.write(state.owner, remaining, undefined, ticket);
      if (result.ok) {
        const released = this.releaseFence(state.owner, ticket);
        if (!released.ok) {
          if (released.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
          return released;
        }
        state.revoked = true;
        this.dispatchTickets.delete(ticket);
      } else if (result.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return result;
    } finally { state.reconciling = false; }
  }

  /** Only after a definitive response to this ticket; retry reconciles its exact removal without dispatching. */
  removeDefinitiveTicket(ticket: WorkQueueDispatchTicket): WorkQueueRegistryResult<void> {
    const state = this.dispatchTickets.get(ticket) ?? WorkQueueActionRegistry.fenceTickets.get(ticket);
    if (!state || state.revoked || state.acknowledgement || !state.invoked) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (state.inFlight) return failure('dispatch_started');
    if (state.reconciling) return failure('reconciliation_pending');
    state.reconciling = true;
    try {
      state.definitiveStarted = true;
      state.original = undefined;
      const claimed = this.fence(state.owner, ticket, true);
      if (!claimed.ok) {
        if (claimed.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
        return claimed;
      }
      const existing = this.readSnapshot(state.owner);
      if (!existing.ok) {
        if (existing.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
        return existing;
      }
      const item = existing.value.entries.find(x => x.requestId === state.item.requestId);
      if (!item) {
        if (state.cleanupExpected === undefined) return failure('invalid_storage');
        let raw: string | null;
        try { raw = this.storage().getItem(this.key(state.owner)); }
        catch {
          const current = this.ticketSession(ticket, state);
          return current.ok ? failure('storage_unavailable') : current;
        }
        if (!this.current(state.owner)) { this.invalidateTicketSession(ticket, state); return failure('session_changed'); }
        if (raw !== state.cleanupExpected) return failure('invalid_storage');
        state.definitiveVerified = true;
        return { ok: true, value: undefined };
      }
      if (JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
      const remaining = existing.value.entries.filter(x => x.requestId !== state.item.requestId);
      state.cleanupBefore = existing.value.raw;
      state.cleanupExpected = this.serialize(state.owner, remaining);
      const result = this.write(state.owner, remaining, undefined, ticket);
      if (result.ok) state.definitiveVerified = true;
      if (!result.ok && result.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      return result;
    } finally { state.reconciling = false; }
  }

  /** Release only after exact removal was verified and the caller has rechecked current authorization. */
  finishDefinitiveTicket(ticket: WorkQueueDispatchTicket): WorkQueueRegistryResult<void> {
    const state = this.dispatchTickets.get(ticket) ?? WorkQueueActionRegistry.fenceTickets.get(ticket);
    if (!state || state.revoked || state.acknowledgement || !state.invoked || !state.definitiveStarted || !state.definitiveVerified) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (state.reconciling) return failure('reconciliation_pending');
    const released = this.releaseFence(state.owner, ticket);
    if (!released.ok && released.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
    if (released.ok) { state.revoked = true; state.original = undefined; this.dispatchTickets.delete(ticket); }
    return released;
  }

  /** Only a definitive response to this exact dispatch may use this method. */
  removeDefinitive(owner: WorkQueueRegistryOwner, requestId: string): WorkQueueRegistryResult<void> {
    if (!this.current(owner)) return failure('session_changed');
    if (!isGuid(requestId)) return failure('invalid_input');
    const available = this.fence(owner);
    if (!available.ok) return available;
    const existing = this.read(owner);
    if (!existing.ok) return existing;
    const item = existing.value.find(x => x.requestId === requestId.toLowerCase());
    if (!item) return failure('not_found');
    if (item.dispatchGeneration !== owner.generation || item.dispatchInvalidation !== owner.invalidation) return failure('session_changed');
    return this.write(owner, existing.value.filter(x => x.requestId !== item.requestId));
  }

  /** Called only after explicit assent. The parent supplies current availability revision and an authorized observation. */
  acknowledge(owner: WorkQueueRegistryOwner, requestId: string, observation: WorkQueueTaskObservation,
    currentAvailabilityRevision: number): WorkQueueRegistryResult<void> {
    if (!this.current(owner)) return failure('session_changed');
    if (!isGuid(requestId) || !observation || !validOwner(observation.owner) || !isGuid(observation.engagementId) ||
        !isGuid(observation.taskId) || !validRevision(observation.revision) ||
        !Number.isSafeInteger(observation.availabilityRevision) || observation.availabilityRevision < 0 ||
        !Number.isSafeInteger(currentAvailabilityRevision) || currentAvailabilityRevision < 0) return failure('invalid_input');
    const available = this.fence(owner);
    if (!available.ok) return available;
    const existing = this.read(owner);
    if (!existing.ok) return existing;
    const item = existing.value.find(x => x.requestId === requestId.toLowerCase());
    if (!item) return failure('not_found');
    if (!sameOwner(owner, observation.owner) || observation.availabilityRevision !== currentAvailabilityRevision ||
        item.engagementId !== observation.engagementId.toLowerCase() || item.taskId !== observation.taskId.toLowerCase() ||
        observation.revision <= item.expectedRevision) return failure('observation_stale');
    const ticket: WorkQueueDispatchTicket = Object.freeze({ item: Object.freeze({ ...item }) });
    this.dispatchTickets.set(ticket, { owner: { ...owner }, item, invoked: true, inFlight: false,
      cleanupStarted: false, acknowledgement: true });
    const claimed = this.fence(owner, ticket, true);
    if (!claimed.ok) { this.dispatchTickets.delete(ticket); return claimed; }
    return this.reconcileAcknowledgement(owner, ticket, observation, currentAvailabilityRevision);
  }

  /** Retry only after a fresh authorized exact observation and explicit acknowledgement in the issuing session. */
  reconcileAcknowledgement(owner: WorkQueueRegistryOwner, ticket: WorkQueueDispatchTicket, observation: WorkQueueTaskObservation,
    currentAvailabilityRevision: number): WorkQueueRegistryResult<void> {
    const state = this.dispatchTickets.get(ticket) ?? WorkQueueActionRegistry.fenceTickets.get(ticket);
    if (!state || state.revoked || !state.acknowledgement) return failure('invalid_input');
    const session = this.ticketSession(ticket, state);
    if (!session.ok) return session;
    if (!this.current(owner) || !sameOwner(owner, state.owner)) return failure('session_changed');
    if (!observation || !validOwner(observation.owner) || !isGuid(observation.engagementId) || !isGuid(observation.taskId) ||
        !validRevision(observation.revision) || !Number.isSafeInteger(observation.availabilityRevision) || observation.availabilityRevision < 0 ||
        !Number.isSafeInteger(currentAvailabilityRevision) || currentAvailabilityRevision < 0) return failure('invalid_input');
    if (!sameOwner(owner, observation.owner) || observation.availabilityRevision !== currentAvailabilityRevision ||
        state.item.engagementId !== observation.engagementId.toLowerCase() || state.item.taskId !== observation.taskId.toLowerCase() ||
        observation.revision <= state.item.expectedRevision) return failure('observation_stale');
    if (state.reconciling) return failure('reconciliation_pending');
    state.reconciling = true;
    try {
      const claimed = this.fence(owner, ticket, true);
      if (!claimed.ok) {
        if (claimed.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
        return claimed;
      }
      const snapshot = this.readSnapshot(owner);
      if (!snapshot.ok) {
        if (snapshot.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
        return snapshot;
      }
      const item = snapshot.value.entries.find(x => x.requestId === state.item.requestId);
      if (!item) {
        if (state.cleanupExpected === undefined || snapshot.value.raw !== state.cleanupExpected) return failure('invalid_storage');
      } else {
        if (JSON.stringify(item) !== JSON.stringify(state.item)) return failure('invalid_storage');
        const remaining = snapshot.value.entries.filter(x => x.requestId !== state.item.requestId);
        state.cleanupBefore = snapshot.value.raw;
        state.cleanupExpected = this.serialize(owner, remaining);
        const result = this.write(owner, remaining, undefined, ticket);
        if (!result.ok) {
          if (result.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
          return result;
        }
      }
      const released = this.releaseFence(owner, ticket);
      if (!released.ok && released.reason === 'session_changed') this.invalidateTicketSession(ticket, state);
      if (released.ok) { state.revoked = true; this.dispatchTickets.delete(ticket); }
      return released;
    } finally { state.reconciling = false; }
  }

  private serialize(owner: WorkQueueRegistryOwner, entries: WorkQueuePendingAction[]): string {
    return JSON.stringify({ version: 2, firmId: owner.firmId.toLowerCase(), userId: owner.userId.toLowerCase(), entries });
  }

  private write(owner: WorkQueueRegistryOwner, entries: WorkQueuePendingAction[], beforeSet?: () => void, ticket?: object): WorkQueueRegistryResult<void> {
    if (!this.current(owner)) return failure('session_changed');
    const available = this.fence(owner, ticket);
    if (!available.ok) return available;
    try {
      const storage = this.storage(), key = this.key(owner);
      const serialized = this.serialize(owner, entries);
      if (!this.current(owner)) return failure('session_changed');
      if (serialized.length > maxDocumentLength) return failure('capacity');
      const stillAvailable = this.fence(owner, ticket);
      if (!stillAvailable.ok) return stillAvailable;
      beforeSet?.();
      storage.setItem(key, serialized);
      if (!this.current(owner)) return failure('session_changed');
      const readback = storage.getItem(key);
      if (!this.current(owner)) return failure('session_changed');
      if (readback !== serialized) return failure('storage_unavailable');
      return { ok: true, value: undefined };
    } catch { return this.current(owner) ? failure('storage_unavailable') : failure('session_changed'); }
  }
}
