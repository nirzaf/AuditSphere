import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { forkJoin, Subscription, timeout } from 'rxjs';
import { Api, CommandState, UNKNOWN_OUTCOME } from '../../core/api';
import { arr, bool, decode, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { guidPattern } from '../../core/contracts';
import { SHARED } from '../../core/ui';
import { Client, decodeClient } from './client-contracts';
import {
  ClientContactRouting,
  ClientOrganizationHierarchy,
  ClientRelationship,
  decodeClientHierarchy,
  decodeClientRoutings,
} from './client-relationships-contracts';

const candidatesDecoder = obj({
  items: arr(obj({ id: guid, name: str(500) }), 50),
  total: nat,
  page: nat,
  pageSize: nat,
});
const resolutionDecoder = obj({
  status: oneOf('Resolved', 'NoRecipient', 'Ambiguous', 'Overridden', 'Denied'),
  clientContactId: nullable(guid),
  fullName: nullable(str(500)),
  email: nullable(str(254)),
  purpose: nullable(str(50)),
  message: nullable(str(1000)),
});
const maintenanceProfileDecoder = obj({
  legalName: str(), commercialName: nullable(str()), registrationNumber: nullable(str()),
  taxRegistrationNumber: nullable(str(64)), entityType: nullable(str(64)), jurisdiction: nullable(str()),
  restrictedProfile: nullable(str(10000000)),
});
const maintenanceContactDecoder = obj({
  fullName: str(), email: str(), role: str(), phone: nullable(str(32)), title: nullable(str(100)),
  signatoryAuthority: nullable(str(64)), approvedScope: nullable(str(10000000)),
  validFrom: nullable(instant), validTo: nullable(instant), primary: bool, isActive: bool,
});
const maintenancePrimaryDecoder = obj({ id: guid, name: str() });
const maintenanceGeneration = (value: unknown): string => {
  if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error('Invalid exact generation');
  return value;
};
const maintenanceStateDecoder = obj({
  clientId: guid, operationKind: oneOf('PROFILE', 'CONTACT'), clientName: str(), safetyGeneration: maintenanceGeneration,
  reviewBasis: sha256, profile: nullable(maintenanceProfileDecoder), contactId: nullable(guid),
  contact: nullable(maintenanceContactDecoder), currentPrimary: arr(maintenancePrimaryDecoder, 50),
});
const maintenancePreviewDecoder = obj({
  clientId: guid, operationKind: oneOf('PROFILE', 'CONTACT'), contactId: nullable(guid), requestId: guid,
  reviewBasis: sha256, requestHash: sha256, beforeJson: str(10000000), afterJson: str(10000000),
  replacedPrimary: arr(maintenancePrimaryDecoder, 50), resultPrimary: arr(maintenancePrimaryDecoder, 50),
});
const maintenanceReceiptDecoder = obj({
  id: guid, clientId: guid, contactId: nullable(guid), operationKind: oneOf('PROFILE', 'CONTACT'), actorId: guid,
  actorEpoch: nat, requestId: guid, requestHash: sha256, reviewBasis: sha256,
  previousGeneration: maintenanceGeneration, resultGeneration: maintenanceGeneration, beforeJson: str(10000000),
  afterJson: str(10000000), createdAt: instant,
});
const maintenanceLookupDecoder = obj({ found: bool, receipt: nullable(maintenanceReceiptDecoder) });
const profileIntentDecoder = obj({ requestId: guid, reviewBasis: sha256, requestHash: sha256, profile: maintenanceProfileDecoder });
const contactIntentDecoder = obj({ requestId: guid, reviewBasis: sha256, requestHash: sha256, contact: maintenanceContactDecoder });

type MaintenanceState = ReturnType<typeof decodeMaintenanceState>;
type MaintenancePreview = ReturnType<typeof decodeMaintenancePreview>;
type MaintenanceReceipt = ReturnType<typeof decodeMaintenanceReceipt>;
type MaintenanceProfile = ReturnType<typeof maintenanceProfileDecoder>;
type MaintenanceContact = ReturnType<typeof maintenanceContactDecoder>;
function decodeMaintenanceState(value: unknown) { return decode(maintenanceStateDecoder, value); }
function decodeMaintenancePreview(value: unknown) { return decode(maintenancePreviewDecoder, value); }
function decodeMaintenanceReceipt(value: unknown) { return decode(maintenanceReceiptDecoder, value); }
function decodeMaintenanceLookup(value: unknown) { return decode(maintenanceLookupDecoder, value); }
function decodeProfileIntent(value: unknown) { return decode(profileIntentDecoder, value); }
function decodeContactIntent(value: unknown) { return decode(contactIntentDecoder, value); }

type RoutingReview = {
  clientContactId: string; contactName: string; contactEmail: string; purpose: string;
  effectiveFrom: string; effectiveTo: string; isPrimaryForPurpose: boolean;
};
type RelationshipReview = {
  relatedClientId: string; relatedClientName: string; relationshipKind: string;
  ownershipPercentage: string | null; effectiveFrom: string; effectiveTo: string; notes: string;
};
type RoutingRevokeReview = {
  routingId: string; contactName: string; contactEmail: string; purpose: string;
  effectiveFrom: string | null; effectiveTo: string | null; reason: string;
};
type RelationshipRevokeReview = ClientRelationship & { reason: string };

@Component({
  selector: 'audit-client-relationships',
  imports: [FormsModule, RouterLink, MatButtonModule, MatProgressBarModule, ...SHARED],
  templateUrl: './client-relationships.html',
  styleUrl: './client-relationships.scss',
})
export class ClientRelationships {
  readonly clientId = input.required<string>();

  readonly hierarchy = signal<ClientOrganizationHierarchy | null>(null);
  readonly routings = signal<readonly ClientContactRouting[] | null>(null);
  readonly clientWorkspace = signal<Client | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly contactPage = signal(0);
  readonly relationshipCandidates = signal<readonly { id: string; name: string }[]>([]);
  readonly relationshipCandidateTotal = signal(0);
  readonly relationshipCandidatePage = signal(0);
  readonly relationshipCandidateLoading = signal(false);
  readonly relationshipCandidateError = signal('');
  readonly relationshipSearch = signal('');
  readonly relationshipFormError = signal('');
  readonly routingFormError = signal('');
  readonly routingReview = signal<RoutingReview | null>(null);
  readonly relationshipReview = signal<RelationshipReview | null>(null);
  readonly routingRevokeReview = signal<RoutingRevokeReview | null>(null);
  readonly relationshipRevokeReview = signal<RelationshipRevokeReview | null>(null);
  readonly routingResolution = signal<ReturnType<typeof decodeResolution> | null>(null);
  readonly routingResolutionError = signal('');
  readonly resolutionPurpose = signal('COMMERCIAL');
  readonly routingCommand: CommandState;
  readonly relationshipCommand: CommandState;
  readonly profileMaintenanceState = signal<MaintenanceState | null>(null);
  readonly contactMaintenanceState = signal<MaintenanceState | null>(null);
  readonly profileDraft = signal<MaintenanceProfile | null>(null);
  readonly contactDraft = signal<MaintenanceContact | null>(null);
  readonly profilePreview = signal<MaintenancePreview | null>(null);
  readonly contactPreview = signal<MaintenancePreview | null>(null);
  readonly profileReceipt = signal<MaintenanceReceipt | null>(null);
  readonly contactReceipt = signal<MaintenanceReceipt | null>(null);
  readonly profilePending = signal<PendingRequestReference | null>(null);
  readonly contactPending = signal<PendingRequestReference | null>(null);
  readonly profileReviewed = signal(false);
  readonly contactReviewed = signal(false);
  readonly profileBusy = signal(false);
  readonly contactBusy = signal(false);
  readonly profileUncertain = signal(false);
  readonly contactUncertain = signal(false);
  readonly profileReceiptAbsent = signal(false);
  readonly contactReceiptAbsent = signal(false);
  readonly profileMaintenanceMessage = signal('');
  readonly contactMaintenanceMessage = signal('');
  readonly profileMaintenanceError = signal('');
  readonly contactMaintenanceError = signal('');
  private profileIntent: ReturnType<typeof decodeProfileIntent> | null = null;
  private contactIntent: ReturnType<typeof decodeContactIntent> | null = null;
  private profileReviewFields: MaintenanceProfile | null = null;
  private contactReviewFields: MaintenanceContact | null = null;

  readonly routingDraft = {
    clientContactId: '', purpose: 'COMMERCIAL', effectiveFrom: '', effectiveTo: '', isPrimaryForPurpose: true,
  };
  readonly relationshipDraft = {
    relatedClientId: '', relationshipKind: 'PARENT', ownershipPercentage: '', effectiveFrom: '', effectiveTo: '', notes: '',
  };
  readonly routingReasons: Record<string, string> = {};
  readonly relationshipReasons: Record<string, string> = {};
  routingConfirmation = false;
  relationshipConfirmation = false;
  readonly purposes = [
    { value: 'COMMERCIAL', label: 'Commercial' },
    { value: 'FINANCE', label: 'Finance' },
    { value: 'AUDIT_FIELDWORK', label: 'Audit fieldwork' },
    { value: 'COMPLETION', label: 'Completion' },
  ];
  readonly relationshipKinds = [
    { value: 'PARENT', label: 'Current client is parent of selected client' },
    { value: 'SUBSIDIARY', label: 'Current client is subsidiary of selected client' },
    { value: 'AFFILIATE', label: 'Affiliate' },
  ];

  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private request?: Subscription;
  private candidateRequest?: Subscription;
  private destroyed = false;
  private maintenanceGeneration = 0;

  constructor() {
    this.routingCommand = new CommandState(this.api);
    this.relationshipCommand = new CommandState(this.api);
    effect(() => {
      const id = this.clientId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.candidateRequest?.unsubscribe();
        this.hierarchy.set(null);
        this.routings.set(null);
        this.clientWorkspace.set(null);
        this.relationshipCandidates.set([]);
        this.relationshipCandidateTotal.set(0);
        this.relationshipCandidatePage.set(0);
        this.error.set('');
        this.relationshipCandidateError.set('');
        this.routingResolution.set(null);
        this.routingReview.set(null);
        this.relationshipReview.set(null);
        this.routingRevokeReview.set(null);
        this.relationshipRevokeReview.set(null);
        this.resetClientMaintenance();
        this.resetDrafts();
        this.loading.set(false);
        if (staff && id) this.load();
      });
    });

    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request?.unsubscribe();
      this.candidateRequest?.unsubscribe();
    });
  }

  load(): void {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id)) {
      this.error.set('Invalid client identifier.');
      return;
    }

    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    const clientUrl = `/api/ui/clients/${id}?contactPage=${this.contactPage()}&contactPageSize=50`;
    const hierarchy$ = this.http.get<unknown>(`/api/ui/clients/${id}/hierarchy`).pipe(timeout(10000));
    const routings$ = this.http.get<unknown>(`/api/ui/clients/${id}/routings`).pipe(timeout(10000));
    const client$ = this.http.get<unknown>(clientUrl).pipe(timeout(10000));

    this.request = forkJoin({ hierarchy: hierarchy$, routings: routings$, client: client$ }).subscribe({
      next: (res) => {
        if (this.destroyed) return;
        try {
          const h = decodeClientHierarchy(res.hierarchy);
          const r = decodeClientRoutings(res.routings);
          const client = decodeClient(res.client);
          if (h.clientId.toLowerCase() !== id || client.id.toLowerCase() !== id) throw new Error('Client context changed.');
          this.hierarchy.set(h);
          this.routings.set(r);
          this.clientWorkspace.set(client);
          this.loading.set(false);
          if (client.canManageContacts) void this.loadProfileMaintenanceState(false);
          this.clearUnknownAfterRefresh();
          this.loadRelationshipCandidates();
          void this.resolveRoutingPurpose(this.resolutionPurpose());
        } catch {
          this.error.set('Unsupported client relationship or routing payload.');
          this.loading.set(false);
        }
      },
      error: (err) => {
        if (this.destroyed) return;
        const msg = err?.error?.message ?? err?.message ?? 'Failed to load client relationships.';
        this.error.set(typeof msg === 'string' ? msg : 'Failed to load client relationships.');
        this.loading.set(false);
      },
    });
  }

  private maintenanceUrl(kind: 'PROFILE' | 'CONTACT', contactId?: string): string {
    const id = this.clientId().toLowerCase();
    return kind === 'PROFILE'
      ? `/api/ui/clients/${id}/profile-maintenance`
      : `/api/ui/clients/${id}/contacts/${contactId}/maintenance`;
  }

  private pendingStorageKey(kind: 'PROFILE' | 'CONTACT', contactId?: string): string | null {
    const s = this.session.current();
    const clientId = this.clientId()?.toLowerCase();
    if (!s?.staff || !clientId || !guidPattern.test(clientId)) return null;
    return `auditsphere-client-maintenance-v1:${s.firmId}:${s.userId}:${s.generation}:${clientId}:${kind}:${contactId ?? 'profile'}`;
  }

  private readPending(kind: 'PROFILE' | 'CONTACT', contactId?: string):
    { reference: PendingRequestReference; intent: ReturnType<typeof decodeProfileIntent> | ReturnType<typeof decodeContactIntent> } | null {
    const key = this.pendingStorageKey(kind, contactId);
    if (!key) return null;
    try {
      const raw = sessionStorage.getItem(key);
      if (!raw || raw.length > 100000) return null;
      const value: unknown = JSON.parse(raw);
      if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
      const v = value as Record<string, unknown>;
      const reference = pendingRequestReference(v['reference']);
      if (!reference) return null;
      const intent = kind === 'PROFILE' ? decodeProfileIntent(v['intent']) : decodeContactIntent(v['intent']);
      if (intent.requestId !== reference.requestId || intent.requestHash !== reference.requestHash) return null;
      return { reference, intent };
    } catch { return null; }
  }

  private savePending(kind: 'PROFILE' | 'CONTACT', intent: ReturnType<typeof decodeProfileIntent> | ReturnType<typeof decodeContactIntent>, contactId?: string): boolean {
    const key = this.pendingStorageKey(kind, contactId);
    if (!key) return false;
    try {
      const safe = JSON.stringify({ reference: { requestId: intent.requestId, requestHash: intent.requestHash }, intent });
      if (safe.length > 100000) return false;
      sessionStorage.setItem(key, safe);
      return true;
    } catch { return false; }
  }

  private clearPending(kind: 'PROFILE' | 'CONTACT', contactId?: string): void {
    const key = this.pendingStorageKey(kind, contactId);
    if (key) { try { sessionStorage.removeItem(key); } catch { /* keep UI recovery state */ } }
  }

  private async loadProfileMaintenanceState(openEditor: boolean): Promise<void> {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id) || !this.clientWorkspace()?.canManageContacts) return;
    const generation = this.maintenanceGeneration;
    this.profileMaintenanceError.set('');
    try {
      const state = await this.api.get(this.maintenanceUrl('PROFILE'), decodeMaintenanceState);
      if (generation !== this.maintenanceGeneration || this.clientId().toLowerCase() !== id || state.clientId !== id || state.operationKind !== 'PROFILE' || !state.profile)
        throw new Error('The client profile context changed. Refresh before editing.');
      this.profileMaintenanceState.set(state);
      if (openEditor) this.profileDraft.set(structuredClone(state.profile));
      const pending = this.readPending('PROFILE');
      if (pending) {
        this.profilePending.set(pending.reference);
        this.profileIntent = pending.intent as ReturnType<typeof decodeProfileIntent>;
        this.profileUncertain.set(true);
        this.profileReceiptAbsent.set(false);
        this.profileMaintenanceMessage.set(UNKNOWN_OUTCOME);
      }
    } catch (error) {
      if (generation === this.maintenanceGeneration) this.profileMaintenanceError.set(error instanceof Error ? error.message : 'Client profile details are unavailable.');
    }
  }

  async editClientProfile(): Promise<void> {
    if (this.profileBusy() || this.profileUncertain()) return;
    this.profileBusy.set(true);
    await this.loadProfileMaintenanceState(true);
    this.profileBusy.set(false);
  }

  setProfileField(field: keyof MaintenanceProfile, value: string): void {
    const current = this.profileDraft();
    if (!current || this.profileBusy() || this.profileUncertain()) return;
    this.profileDraft.set({ ...current, [field]: value });
    this.profilePreview.set(null); this.profileReviewed.set(false); this.profileIntent = null;
  }

  setContactField(field: keyof MaintenanceContact, value: string | boolean): void {
    const current = this.contactDraft();
    if (!current || this.contactBusy() || this.contactUncertain()) return;
    const normalized = field === 'validFrom' || field === 'validTo'
      ? (typeof value === 'string' && value.trim() ? value.trim() : null) : value;
    this.contactDraft.set({ ...current, [field]: normalized } as MaintenanceContact);
    this.contactPreview.set(null); this.contactReviewed.set(false); this.contactIntent = null;
  }

  cancelProfileMaintenance(): void {
    if (this.profileBusy() || this.profileUncertain()) return;
    this.profileDraft.set(null); this.profilePreview.set(null); this.profileReviewed.set(false); this.profileIntent = null;
  }

  cancelContactMaintenance(): void {
    if (this.contactBusy() || this.contactUncertain()) return;
    this.contactDraft.set(null); this.contactMaintenanceState.set(null); this.contactPreview.set(null);
    this.contactReviewed.set(false); this.contactIntent = null;
  }

  cancelProfileReview(): void {
    this.profilePreview.set(null); this.profileReviewed.set(false); this.profileIntent = null;
  }

  cancelContactReview(): void {
    this.contactPreview.set(null); this.contactReviewed.set(false); this.contactIntent = null;
  }

  profileReviewValue(field: keyof MaintenanceProfile): string | null {
    return this.profileReviewFields?.[field] ?? null;
  }

  contactReviewValue(field: keyof MaintenanceContact): string | boolean | null {
    return this.contactReviewFields?.[field] ?? null;
  }

  profileReviewIsCurrent(): boolean {
    return !!this.profileIntent && !!this.profileReviewFields &&
      this.profileMaintenanceState()?.reviewBasis === this.profileIntent.reviewBasis &&
      JSON.stringify(this.profileDraft()) === JSON.stringify(this.profileReviewFields);
  }

  contactReviewIsCurrent(): boolean {
    return !!this.contactIntent && !!this.contactReviewFields &&
      this.contactMaintenanceState()?.reviewBasis === this.contactIntent.reviewBasis &&
      JSON.stringify(this.contactDraft()) === JSON.stringify(this.contactReviewFields);
  }

  async editClientContact(contactId: string): Promise<void> {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id) || !this.clientWorkspace()?.contacts.some(x => x.id === contactId) || this.contactBusy() || this.contactUncertain()) return;
    const generation = this.maintenanceGeneration;
    this.contactMaintenanceError.set('');
    this.contactBusy.set(true);
    try {
      const state = await this.api.get(this.maintenanceUrl('CONTACT', contactId), decodeMaintenanceState);
      if (generation !== this.maintenanceGeneration || this.clientId().toLowerCase() !== id || state.clientId !== id ||
          state.operationKind !== 'CONTACT' || state.contactId !== contactId || !state.contact)
        throw new Error('The selected contact context changed. Refresh before editing.');
      this.contactMaintenanceState.set(state);
      this.contactDraft.set(structuredClone(state.contact));
      const pending = this.readPending('CONTACT', contactId);
      if (pending) {
        this.contactPending.set(pending.reference);
        this.contactIntent = pending.intent as ReturnType<typeof decodeContactIntent>;
        this.contactUncertain.set(true);
        this.contactReceiptAbsent.set(false);
        this.contactMaintenanceMessage.set(UNKNOWN_OUTCOME);
      }
    } catch (error) {
      if (generation === this.maintenanceGeneration) this.contactMaintenanceError.set(error instanceof Error ? error.message : 'Client contact details are unavailable.');
    } finally { if (generation === this.maintenanceGeneration) this.contactBusy.set(false); }
  }

  async reviewClientProfile(): Promise<void> {
    const state = this.profileMaintenanceState(), profile = this.profileDraft();
    if (!state || !profile || this.profileBusy() || this.profileUncertain()) return;
    this.profileBusy.set(true); this.profileMaintenanceError.set(''); this.profilePreview.set(null); this.profileReviewed.set(false);
    const canonical = { ...profile, legalName: profile.legalName.trim(),
      commercialName: profile.commercialName?.trim() || null, registrationNumber: profile.registrationNumber?.trim() || null,
      taxRegistrationNumber: profile.taxRegistrationNumber?.trim() || null, entityType: profile.entityType?.trim() || null,
      jurisdiction: profile.jurisdiction?.trim() || null, restrictedProfile: profile.restrictedProfile?.trim() || null };
    this.profileDraft.set(canonical);
    const intent = { requestId: crypto.randomUUID(), reviewBasis: state.reviewBasis, profile: canonical };
    try {
      const result = await this.api.command(this.maintenanceUrl('PROFILE') + '/preview', intent);
      if (!result.ok) { this.profileMaintenanceError.set(result.message); return; }
      const preview = decodeMaintenancePreview(result.value);
      if (preview.clientId !== state.clientId || preview.operationKind !== 'PROFILE' || preview.requestId !== intent.requestId ||
          preview.reviewBasis !== state.reviewBasis || this.profileMaintenanceState()?.reviewBasis !== state.reviewBasis ||
          JSON.stringify(this.profileDraft()) !== JSON.stringify(intent.profile))
        throw new Error('The profile intent changed. Refresh and review it again.');
      this.profileIntent = { ...intent, requestHash: preview.requestHash };
      this.profileReviewFields = structuredClone(intent.profile);
      this.profilePreview.set(preview);
      this.profileMaintenanceMessage.set('Review the exact profile changes before saving.');
    } catch (error) { this.profileMaintenanceError.set(error instanceof Error ? error.message : 'Profile review is unavailable.'); }
    finally { this.profileBusy.set(false); }
  }

  async saveClientProfile(): Promise<void> {
    const state = this.profileMaintenanceState(), preview = this.profilePreview(), intent = this.profileIntent;
    if (!state || !preview || !intent || !this.profileReviewed() || this.profileBusy() || this.profileUncertain() ||
        state.reviewBasis !== intent.reviewBasis || JSON.stringify(this.profileDraft()) !== JSON.stringify(this.profileReviewFields)) return;
    if (!this.savePending('PROFILE', intent)) { this.profileMaintenanceError.set('Recovery storage is unavailable. No profile update was sent.'); return; }
    this.profilePending.set({ requestId: intent.requestId, requestHash: intent.requestHash });
    this.profileBusy.set(true); this.profileReviewed.set(false);
    const result = await this.api.command(this.maintenanceUrl('PROFILE'), { requestId: intent.requestId, reviewBasis: intent.reviewBasis,
      profile: intent.profile, reviewed: true, expectedRequestHash: intent.requestHash });
    if (!result.ok) {
      this.profileMaintenanceMessage.set(result.message);
      if (result.unknown) { this.profileUncertain.set(true); this.profileMaintenanceMessage.set(UNKNOWN_OUTCOME); }
      else { this.clearPending('PROFILE'); this.profilePending.set(null); this.profileIntent = null; this.profilePreview.set(null); }
      this.profileBusy.set(false); return;
    }
    try {
      const receipt = decodeMaintenanceReceipt(result.value);
      if (receipt.clientId !== state.clientId || receipt.operationKind !== 'PROFILE' || receipt.actorId !== this.session.current()?.userId ||
          receipt.requestId !== intent.requestId || receipt.requestHash !== intent.requestHash) throw new Error('Receipt identity mismatch.');
      this.profileReceipt.set(receipt); this.clearPending('PROFILE'); this.profilePending.set(null); this.profileUncertain.set(false);
      this.profilePreview.set(null); this.profileIntent = null; this.profileDraft.set(null);
      this.profileMaintenanceMessage.set('The reviewed profile update and immutable receipt were saved.'); this.load();
    } catch { this.profileUncertain.set(true); this.profileMaintenanceMessage.set(UNKNOWN_OUTCOME); }
    finally { this.profileBusy.set(false); }
  }

  async reviewClientContact(): Promise<void> {
    const state = this.contactMaintenanceState(), contact = this.contactDraft();
    if (!state || !contact || !state.contactId || this.contactBusy() || this.contactUncertain()) return;
    this.contactBusy.set(true); this.contactMaintenanceError.set(''); this.contactPreview.set(null); this.contactReviewed.set(false);
    const canonical = { ...contact, fullName: contact.fullName.trim(), email: contact.email.trim().toLowerCase(), role: contact.role.trim(),
      phone: contact.phone?.trim() || null, title: contact.title?.trim() || null,
      signatoryAuthority: contact.signatoryAuthority?.trim() || null, approvedScope: contact.approvedScope?.trim() || null,
      validFrom: contact.validFrom?.trim() || null, validTo: contact.validTo?.trim() || null };
    const intent = { requestId: crypto.randomUUID(), reviewBasis: state.reviewBasis, contact: canonical };
    this.contactDraft.set(canonical);
    try {
      const result = await this.api.command(this.maintenanceUrl('CONTACT', state.contactId) + '/preview', intent);
      if (!result.ok) { this.contactMaintenanceError.set(result.message); return; }
      const preview = decodeMaintenancePreview(result.value);
      if (preview.clientId !== state.clientId || preview.operationKind !== 'CONTACT' || preview.contactId !== state.contactId ||
          preview.requestId !== intent.requestId || preview.reviewBasis !== state.reviewBasis ||
          this.contactMaintenanceState()?.reviewBasis !== state.reviewBasis || JSON.stringify(this.contactDraft()) !== JSON.stringify(intent.contact))
        throw new Error('The contact intent changed. Refresh and review it again.');
      this.contactIntent = { ...intent, requestHash: preview.requestHash };
      this.contactReviewFields = structuredClone(intent.contact);
      this.contactPreview.set(preview);
      this.contactMaintenanceMessage.set('Review the exact contact changes and primary-contact effects before saving.');
    } catch (error) { this.contactMaintenanceError.set(error instanceof Error ? error.message : 'Contact review is unavailable.'); }
    finally { this.contactBusy.set(false); }
  }

  async saveClientContact(): Promise<void> {
    const state = this.contactMaintenanceState(), preview = this.contactPreview(), intent = this.contactIntent;
    if (!state || !preview || !intent || !state.contactId || !this.contactReviewed() || this.contactBusy() || this.contactUncertain() ||
        state.reviewBasis !== intent.reviewBasis || JSON.stringify(this.contactDraft()) !== JSON.stringify(this.contactReviewFields)) return;
    if (!this.savePending('CONTACT', intent, state.contactId)) { this.contactMaintenanceError.set('Recovery storage is unavailable. No contact update was sent.'); return; }
    this.contactPending.set({ requestId: intent.requestId, requestHash: intent.requestHash });
    this.contactBusy.set(true); this.contactReviewed.set(false);
    const result = await this.api.command(this.maintenanceUrl('CONTACT', state.contactId), { requestId: intent.requestId,
      reviewBasis: intent.reviewBasis, contact: intent.contact, reviewed: true, expectedRequestHash: intent.requestHash });
    if (!result.ok) {
      this.contactMaintenanceMessage.set(result.message);
      if (result.unknown) { this.contactUncertain.set(true); this.contactMaintenanceMessage.set(UNKNOWN_OUTCOME); }
      else { this.clearPending('CONTACT', state.contactId); this.contactPending.set(null); this.contactIntent = null; this.contactPreview.set(null); }
      this.contactBusy.set(false); return;
    }
    try {
      const receipt = decodeMaintenanceReceipt(result.value);
      if (receipt.clientId !== state.clientId || receipt.contactId !== state.contactId || receipt.operationKind !== 'CONTACT' ||
          receipt.actorId !== this.session.current()?.userId || receipt.requestId !== intent.requestId || receipt.requestHash !== intent.requestHash)
        throw new Error('Receipt identity mismatch.');
      this.contactReceipt.set(receipt); this.clearPending('CONTACT', state.contactId); this.contactPending.set(null); this.contactUncertain.set(false);
      this.contactPreview.set(null); this.contactIntent = null; this.contactDraft.set(null);
      this.contactMaintenanceMessage.set('The reviewed contact update and immutable receipt were saved.'); this.load();
    } catch { this.contactUncertain.set(true); this.contactMaintenanceMessage.set(UNKNOWN_OUTCOME); }
    finally { this.contactBusy.set(false); }
  }

  async reconcileClientMaintenance(kind: 'PROFILE' | 'CONTACT'): Promise<void> {
    const contactId = kind === 'CONTACT' ? this.contactMaintenanceState()?.contactId ?? undefined : undefined;
    const pending = kind === 'PROFILE' ? this.profilePending() : this.contactPending();
    const generation = this.maintenanceGeneration;
    if (!pending || kind === 'CONTACT' && !contactId) return;
    if (kind === 'PROFILE') this.profileBusy.set(true); else this.contactBusy.set(true);
    try {
      const lookup = await this.api.get(this.maintenanceUrl(kind, contactId) + `/receipts/${pending.requestId}?requestHash=${pending.requestHash}`, decodeMaintenanceLookup);
      if (generation !== this.maintenanceGeneration) return;
      if (lookup.found && lookup.receipt && lookup.receipt.clientId === this.clientId().toLowerCase() &&
          lookup.receipt.actorId === this.session.current()?.userId && lookup.receipt.operationKind === kind &&
          lookup.receipt.contactId === (contactId ?? null) && lookup.receipt.requestId === pending.requestId &&
          lookup.receipt.requestHash === pending.requestHash) {
        if (kind === 'PROFILE') this.profileReceipt.set(lookup.receipt); else this.contactReceipt.set(lookup.receipt);
        if (kind === 'PROFILE') this.profileReceiptAbsent.set(false); else this.contactReceiptAbsent.set(false);
        if (kind === 'PROFILE') this.profileMaintenanceMessage.set('The exact retained receipt confirms the profile update. Acknowledge it before continuing.');
        else this.contactMaintenanceMessage.set('The exact retained receipt confirms the contact update. Acknowledge it before continuing.');
      } else if (!lookup.found) {
        if (kind === 'PROFILE') { this.profileReceiptAbsent.set(true); this.profileMaintenanceMessage.set('No receipt is retained yet. Keep this request reference and check again; the original command will not be resent automatically.'); }
        else { this.contactReceiptAbsent.set(true); this.contactMaintenanceMessage.set('No receipt is retained yet. Keep this request reference and check again; the original command will not be resent automatically.'); }
      } else throw new Error('The retained result does not match this exact request.');
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Receipt verification is unavailable. Keep the request reference and retry the lookup.';
      if (kind === 'PROFILE') this.profileMaintenanceError.set(message); else this.contactMaintenanceError.set(message);
    } finally { if (kind === 'PROFILE') this.profileBusy.set(false); else this.contactBusy.set(false); }
  }

  acknowledgeClientMaintenance(kind: 'PROFILE' | 'CONTACT'): void {
    if (kind === 'PROFILE' && this.profileReceipt()) {
      this.clearPending('PROFILE'); this.profilePending.set(null); this.profileUncertain.set(false); this.profileReceiptAbsent.set(false);
      this.profileReceipt.set(null); this.profileMaintenanceState.set(null); this.profileMaintenanceMessage.set(''); void this.loadProfileMaintenanceState(false);
    } else if (kind === 'CONTACT' && this.contactReceipt()) {
      const contactId = this.contactMaintenanceState()?.contactId ?? undefined;
      this.clearPending('CONTACT', contactId); this.contactPending.set(null); this.contactUncertain.set(false); this.contactReceiptAbsent.set(false);
      this.contactReceipt.set(null); this.contactMaintenanceState.set(null); this.contactMaintenanceMessage.set(''); this.load();
    }
  }

  private resetClientMaintenance(): void {
    this.maintenanceGeneration++;
    this.profileMaintenanceState.set(null); this.contactMaintenanceState.set(null); this.profileDraft.set(null); this.contactDraft.set(null);
    this.profilePreview.set(null); this.contactPreview.set(null); this.profileReceipt.set(null); this.contactReceipt.set(null);
    this.profilePending.set(null); this.contactPending.set(null); this.profileReviewed.set(false); this.contactReviewed.set(false);
    this.profileBusy.set(false); this.contactBusy.set(false); this.profileUncertain.set(false); this.contactUncertain.set(false);
    this.profileReceiptAbsent.set(false); this.contactReceiptAbsent.set(false); this.profileMaintenanceMessage.set('');
    this.contactMaintenanceMessage.set(''); this.profileMaintenanceError.set(''); this.contactMaintenanceError.set('');
    this.profileIntent = null; this.contactIntent = null; this.profileReviewFields = null; this.contactReviewFields = null;
  }

  loadRelationshipCandidates(page = this.relationshipCandidatePage()): void {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id) || !this.clientWorkspace()?.canManageContacts) return;
    this.candidateRequest?.unsubscribe();
    this.relationshipCandidateLoading.set(true);
    this.relationshipCandidateError.set('');
    const query = new URLSearchParams({ page: String(page), pageSize: '50' });
    const search = this.relationshipSearch().trim();
    if (search) query.set('search', search);
    this.candidateRequest = this.http.get<unknown>(`/api/ui/clients/${id}/relationship-candidates?${query}`)
      .pipe(timeout(10000)).subscribe({
        next: (value) => {
          if (this.destroyed) return;
          try {
            const result = decode(candidatesDecoder, value);
            if (result.page !== page || result.pageSize !== 50) throw new Error('Wrong candidate page.');
            this.relationshipCandidates.set(result.items.filter(x => x.id.toLowerCase() !== id));
            this.relationshipCandidateTotal.set(result.total);
            this.relationshipCandidatePage.set(page);
            this.relationshipCandidateLoading.set(false);
          } catch {
            this.relationshipCandidateError.set('The related-client choices could not be verified. Refresh and try again.');
            this.relationshipCandidateLoading.set(false);
          }
        },
        error: (err) => {
          if (this.destroyed) return;
          this.relationshipCandidateError.set(err?.error?.message ?? 'Authorized related-client choices are unavailable.');
          this.relationshipCandidateLoading.set(false);
          this.relationshipCandidates.set([]);
          this.relationshipCandidateTotal.set(0);
        },
      });
  }

  searchRelationshipCandidates(): void {
    this.relationshipCandidatePage.set(0);
    this.loadRelationshipCandidates(0);
  }

  changeRelationshipCandidatePage(page: number): void {
    if (!Number.isSafeInteger(page) || page < 0 || page > 10000 || page * 50 >= this.relationshipCandidateTotal()) return;
    this.loadRelationshipCandidates(page);
  }

  relationshipCandidatePageCount(): number {
    return Math.max(1, Math.ceil(this.relationshipCandidateTotal() / 50));
  }

  reviewRoutingAssignment(): void {
    this.routingReview.set(null);
    this.routingConfirmation = false;
    this.routingFormError.set('');
    const contact = this.clientWorkspace()?.contacts.find(x => x.id === this.routingDraft.clientContactId);
    if (!contact || !this.purposes.some(x => x.value === this.routingDraft.purpose)) {
      this.routingFormError.set('Select a contact from this client and a supported correspondence purpose.');
      return;
    }
    if (!this.validDateRange(this.routingDraft.effectiveFrom, this.routingDraft.effectiveTo)) {
      this.routingFormError.set('Enter valid effective dates; the end date must be on or after the start date.');
      return;
    }
    this.routingReview.set({
      clientContactId: contact.id,
      contactName: contact.name,
      contactEmail: contact.email,
      purpose: this.routingDraft.purpose,
      effectiveFrom: this.routingDraft.effectiveFrom,
      effectiveTo: this.routingDraft.effectiveTo,
      isPrimaryForPurpose: this.routingDraft.isPrimaryForPurpose,
    });
  }

  async confirmRoutingAssignment(): Promise<void> {
    const review = this.routingReview();
    const id = this.clientId()?.toLowerCase();
    if (!this.routingConfirmation || !review || !id || !guidPattern.test(id)) return;
    const accepted = await this.routingCommand.run(`/api/ui/clients/${id}/routings`, {
      clientContactId: review.clientContactId,
      purpose: review.purpose,
      effectiveFrom: review.effectiveFrom || null,
      effectiveTo: review.effectiveTo || null,
      isPrimaryForPurpose: review.isPrimaryForPurpose,
    }, 'Routing was assigned. The retained routing and current recipient resolution are refreshed.', () => {
      this.routingReview.set(null);
      this.routingConfirmation = false;
      Object.assign(this.routingDraft, { clientContactId: '', purpose: review.purpose, effectiveFrom: '', effectiveTo: '', isPrimaryForPurpose: true });
      this.resolutionPurpose.set(review.purpose);
      this.load();
    });
    if (!accepted && this.routingCommand.uncertain())
      this.routingCommand.message.set('The request outcome is unknown. Refresh persisted routing state before repeating it.');
  }

  reviewRoutingRevocation(routing: ClientContactRouting): void {
    this.routingRevokeReview.set(null);
    this.routingConfirmation = false;
    const reason = (this.routingReasons[routing.id] ?? '').trim();
    if (!reason) {
      this.routingCommand.message.set('A reason is required before revoking a contact route.');
      return;
    }
    this.routingRevokeReview.set({
      routingId: routing.id, contactName: routing.contactName, contactEmail: routing.contactEmail,
      purpose: routing.purpose, effectiveFrom: routing.effectiveFrom, effectiveTo: routing.effectiveTo, reason,
    });
  }

  async confirmRoutingRevocation(): Promise<void> {
    const review = this.routingRevokeReview();
    const id = this.clientId()?.toLowerCase();
    if (!this.routingConfirmation || !review || !id || !guidPattern.test(id)) return;
    await this.routingCommand.run(`/api/ui/clients/${id}/routings/${review.routingId}`, { reason: review.reason },
      'Routing was revoked and its retained state was refreshed.', () => {
        this.routingRevokeReview.set(null);
        this.routingConfirmation = false;
        this.routingReasons[review.routingId] = '';
        this.resolutionPurpose.set(review.purpose);
        this.load();
      }, 'DELETE');
  }

  reviewRelationshipCreation(): void {
    this.relationshipReview.set(null);
    this.relationshipConfirmation = false;
    this.relationshipFormError.set('');
    const id = this.clientId()?.toLowerCase();
    const related = this.relationshipCandidates().find(x => x.id === this.relationshipDraft.relatedClientId);
    if (!id || !guidPattern.test(id) || !related || related.id.toLowerCase() === id) {
      this.relationshipFormError.set('Select a different client from the authorized choices.');
      return;
    }
    if (!this.relationshipKinds.some(x => x.value === this.relationshipDraft.relationshipKind)) {
      this.relationshipFormError.set('Select a supported relationship kind.');
      return;
    }
    if (!this.validDateRange(this.relationshipDraft.effectiveFrom, this.relationshipDraft.effectiveTo)) {
      this.relationshipFormError.set('Enter valid effective dates; the end date must be on or after the start date.');
      return;
    }
    const percentageText = this.relationshipDraft.ownershipPercentage.trim();
    let ownershipPercentage: string | null = null;
    if (percentageText) {
      if (!/^(0|[1-9]\d*)(\.\d+)?$/.test(percentageText)) {
        this.relationshipFormError.set('Ownership must be a non-negative percentage from 0 to 100.');
        return;
      }
      const numeric = Number(percentageText);
      const significantDigits = percentageText.replace('.', '').replace(/^0+/, '').replace(/0+$/, '').length;
      if (!Number.isFinite(numeric) || numeric > 100 || significantDigits > 15) {
        this.relationshipFormError.set('Ownership must be 0–100 and retain its entered decimal precision.');
        return;
      }
      ownershipPercentage = percentageText;
    }
    if (this.relationshipDraft.notes.length > 2000) {
      this.relationshipFormError.set('Notes must be 2,000 characters or fewer.');
      return;
    }
    this.relationshipReview.set({
      relatedClientId: related.id,
      relatedClientName: related.name,
      relationshipKind: this.relationshipDraft.relationshipKind,
      ownershipPercentage,
      effectiveFrom: this.relationshipDraft.effectiveFrom,
      effectiveTo: this.relationshipDraft.effectiveTo,
      notes: this.relationshipDraft.notes.trim(),
    });
  }

  async confirmRelationshipCreation(): Promise<void> {
    const review = this.relationshipReview();
    const id = this.clientId()?.toLowerCase();
    if (!this.relationshipConfirmation || !review || !id || !guidPattern.test(id)) return;
    await this.relationshipCommand.run(`/api/ui/clients/${id}/relationships`, {
      relatedClientId: review.relatedClientId,
      relationshipKind: review.relationshipKind,
      ownershipPercentage: review.ownershipPercentage === null ? null : Number(review.ownershipPercentage),
      effectiveFrom: review.effectiveFrom || null,
      effectiveTo: review.effectiveTo || null,
      notes: review.notes || null,
    }, 'The client relationship was created. The current hierarchy is refreshed.', () => {
      this.relationshipReview.set(null);
      this.relationshipConfirmation = false;
      Object.assign(this.relationshipDraft, { relatedClientId: '', ownershipPercentage: '', effectiveFrom: '', effectiveTo: '', notes: '' });
      this.load();
    });
  }

  reviewRelationshipRevocation(relationship: ClientRelationship): void {
    this.relationshipRevokeReview.set(null);
    this.relationshipConfirmation = false;
    const reason = (this.relationshipReasons[relationship.id] ?? '').trim();
    if (!reason) {
      this.relationshipCommand.message.set('A reason is required before revoking a client relationship.');
      return;
    }
    this.relationshipRevokeReview.set({ ...relationship, reason });
  }

  async confirmRelationshipRevocation(): Promise<void> {
    const review = this.relationshipRevokeReview();
    const id = this.clientId()?.toLowerCase();
    if (!this.relationshipConfirmation || !review || !id || !guidPattern.test(id)) return;
    await this.relationshipCommand.run(`/api/ui/clients/${id}/relationships/${review.id}`, { reason: review.reason },
      'The relationship was revoked and its retained state was refreshed.', () => {
        this.relationshipRevokeReview.set(null);
        this.relationshipConfirmation = false;
        this.relationshipReasons[review.id] = '';
        this.load();
      }, 'DELETE');
  }

  async resolveRoutingPurpose(purpose: string): Promise<void> {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id) || !this.purposes.some(x => x.value === purpose)) return;
    this.resolutionPurpose.set(purpose);
    this.routingResolution.set(null);
    this.routingResolutionError.set('');
    const query = new URLSearchParams({ purpose, asOfDate: new Date().toISOString().slice(0, 10) });
    try {
      const result = await this.api.get(`/api/ui/clients/${id}/routings/resolve?${query}`, resolutionDecoder);
      if (!this.destroyed && this.clientId().toLowerCase() === id) this.routingResolution.set(result);
    } catch (error) {
      if (!this.destroyed) this.routingResolutionError.set(error instanceof Error ? error.message : 'Recipient resolution is unavailable.');
    }
  }

  relationshipKindLabel(kind: string): string {
    return this.relationshipKinds.find(x => x.value === kind)?.label ?? kind;
  }

  dateRange(from: string | null, to: string | null): string {
    return `${from ?? 'Inception'} to ${to ?? 'Present'}`;
  }

  private validDateRange(from: string, to: string): boolean {
    const valid = (value: string) => !value || /^\d{4}-\d{2}-\d{2}$/.test(value) &&
      Number.isFinite(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
    return valid(from) && valid(to) && (!from || !to || from <= to);
  }

  private clearUnknownAfterRefresh(): void {
    for (const command of [this.routingCommand, this.relationshipCommand]) {
      if (command.uncertain()) {
        command.uncertain.set(false);
        command.message.set('Persisted state refreshed. Review the list before repeating the previous action.');
      }
    }
  }

  private resetDrafts(): void {
    Object.assign(this.routingDraft, { clientContactId: '', purpose: 'COMMERCIAL', effectiveFrom: '', effectiveTo: '', isPrimaryForPurpose: true });
    Object.assign(this.relationshipDraft, { relatedClientId: '', relationshipKind: 'PARENT', ownershipPercentage: '', effectiveFrom: '', effectiveTo: '', notes: '' });
    this.routingFormError.set('');
    this.relationshipFormError.set('');
    this.routingConfirmation = false;
    this.relationshipConfirmation = false;
  }

  cancelRoutingReview(): void {
    this.routingReview.set(null);
    this.routingRevokeReview.set(null);
    this.routingConfirmation = false;
  }

  cancelRelationshipReview(): void {
    this.relationshipReview.set(null);
    this.relationshipRevokeReview.set(null);
    this.relationshipConfirmation = false;
  }
}

function decodeResolution(value: unknown) {
  return decode(resolutionDecoder, value);
}
