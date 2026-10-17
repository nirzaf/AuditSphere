import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { forkJoin, Subscription, timeout } from 'rxjs';
import { Api, CommandState } from '../../core/api';
import { arr, decode, guid, nat, nullable, obj, oneOf, str } from '../../core/decode';
import { SessionService } from '../../core/session';
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
  private request?: Subscription;
  private candidateRequest?: Subscription;
  private destroyed = false;

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
