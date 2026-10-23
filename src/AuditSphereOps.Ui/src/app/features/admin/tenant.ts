import { Component, effect, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { decode, guid } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { consentDestination, tenantWorkspace } from './tenant-contracts';
import { SharePointAdministration } from './sharepoint';
import { TenantSetupEditor } from './tenant-setup-editor';
import { WorkspaceAdministration } from './workspaces';

type MailEvidenceRecovery = { actorKey: string; attestationSha256: string; receiptId: string | null };
type MailEvidenceWorkspaceOverride = { actorKey: string; workspace: ReturnType<typeof tenantWorkspace> | null };

@Component({ selector: 'audit-tenant-connection', imports: [TenantSetupEditor, ReactiveFormsModule, RouterLink, MatButtonModule, SharePointAdministration, WorkspaceAdministration, ...SHARED], template: `
  <audit-page-header title="Microsoft tenant connection" description="Microsoft authenticates the tenant administrator and asks for consent. AuditSphere verifies the exact tenant and each capability separately." />
  <nav class="actions" aria-label="Microsoft administration"><a matButton routerLink="/app/administration">Administration overview</a><a matButton routerLink="/app/administration/users">Users & Access</a></nav>
  <button matButton (click)="refresh()" [disabled]="busy()">Refresh persisted status</button>
  <audit-state [loading]="ws.loading()" [error]="ws.error()" label="Microsoft tenant connection" />
  @if (workspaceSnapshot(); as w) {
    @if(w.simulation) { <p role="status" class="notice">Simulation: these checks exercise the configured test provider. They do not prove live Microsoft consent.</p> }
    <section class="panel"><h2>Configured tenant</h2>
      @if(w.workspace.setupMetadata?.tenantDisplayName; as name) { <p>{{ name }}</p><p>Friendly setup label only; Microsoft tenant ID remains the identity boundary.</p> }
      <p>{{ w.workspace.connection.expectedTenantId ?? 'No tenant setup draft is prepared.' }}</p>
      <dl class="facts"><dt>Connection</dt><dd><audit-status [value]="w.workspace.connection.connectionState ?? 'NOT_PREPARED'" /></dd>
        <dt>Consent</dt><dd><audit-status [value]="w.workspace.connection.consentState ?? 'NOT_VERIFIED'" /></dd>
        <dt>Latest attempt</dt><dd>{{ w.workspace.connection.lastAttemptState ?? 'None' }} · {{ w.workspace.connection.lastAttemptAt ?? 'Not attempted' }}</dd>
        <dt>Directory verification</dt><dd>{{ w.workspace.connection.directoryCapabilityState }} · {{ w.workspace.connection.directoryLastCheckedAt ?? 'Not observed' }}</dd></dl>
      @if(w.workspace.consentingAdministrator; as a) { <h3>Verified consenting identity</h3><p>Tenant {{ a.tenantId }} · object {{ a.objectId }} · {{ a.verifiedAt }}</p> }
      <p>AuditSphere never asks for a Microsoft password. The deployment supplies approved credentials privately; the browser receives none.</p>
      @if (!w.workspace.connection.connectionState && w.preparationConfigured && w.workspace.connection.draftId && w.configuredTenantId) {
        <h3>Prepare the tenant connection</h3><p>Record configured tenant {{ w.configuredTenantId }} in setup revision {{ w.workspace.draftRevision }}. This prepares the consent flow; it does not grant Microsoft permissions or verify SharePoint access.</p>
        <label><input type="checkbox" [formControl]="prepareReviewed" /> I reviewed the configured tenant and want to prepare this connection.</label>
        <button matButton="filled" (click)="prepare()" [disabled]="!prepareReviewed.value || busy() || uncertain()">Prepare tenant connection</button>
      }
      @if (!w.consentConfigured) { <p role="status">BLOCKED_EXTERNAL: configure the separately approved consent identity and fixed callback. A Microsoft tenant administrator must grant its documented permissions.</p> }
      @else if(!w.workspace.connection.draftId || !w.workspace.connection.connectionState) { <p role="status">The installation administrator must prepare a tenant setup revision before consent can begin.</p> }
      @else {
        <form [formGroup]="form" (ngSubmit)="connect()"><label><input type="checkbox" formControlName="reviewed" /> I reviewed the configured tenant and requested capability permissions.</label>
          <button matButton="filled" [disabled]="form.invalid || busy() || uncertain()">Connect Microsoft 365 tenant</button></form>
      }
    </section>
    @if(w.workspace.setupMetadata; as metadata) {
      @if(w.workspace.connection.draftId && w.workspace.draftRevision) {
      <audit-tenant-setup-editor [draftId]="w.workspace.connection.draftId!" [revision]="w.workspace.draftRevision!" [initial]="metadata" [editable]="canEditSetup(w.workspace.connection.draftState, w.workspace.connection.connectionState)" (changed)="refresh()" />
}
<section class="panel" aria-labelledby="setup-capability-heading"><h2 id="setup-capability-heading">Saved setup configuration</h2>
        <dl class="facts"><dt>Mail setup</dt><dd><audit-status [value]="metadata.mailState" /></dd>
          <dt>Records setup</dt><dd><audit-status [value]="metadata.recordsState" /></dd></dl>
        <p>These are saved draft states, not verification results. Mail permission and transport are checked separately. A configured records profile does not prove document protection, release readiness or external provider acceptance.</p>
      </section>
    }
    <section class="panel"><h2>Granted capabilities</h2><p>Selected SharePoint site permission and exact library access require their separate resource verification.</p>
      <button matButton="filled" (click)="verify()" [disabled]="busy() || uncertain()">Verify all enabled capabilities</button>
      <div class="table-scroll"><table><caption>Independent Microsoft capability verification</caption><thead><tr><th>Capability</th><th>Permission</th><th>Configured</th><th>Verified state</th><th>Last check</th><th>Diagnostic</th></tr></thead><tbody>
        @for(c of w.workspace.capabilities; track c.capability) { <tr><td>{{ c.displayName }}</td><td>{{ c.permission }}</td><td>{{ c.enabled ? 'Enabled' : 'Disabled' }}</td><td><audit-status [value]="c.stale ? 'STALE' : c.state" /></td><td>{{ c.lastVerifiedAt ?? 'Not observed' }}</td><td>{{ c.diagnosticCode }}</td></tr> }
      </tbody></table></div><a matButton routerLink="/app/administration">Review permission matrix and external blockers</a>
    </section>
    @if (mailCapability(w); as mail) {
      <section class="panel" aria-labelledby="mail-authorization-status-heading">
        <h2 id="mail-authorization-status-heading">Persisted mail authorization evidence</h2>
        <p>Authorization evidence only; it is not proof of a live send or delivery.</p>
        <dl class="facts"><dt>Current capability evaluation</dt><dd><audit-status [value]="mail.stale ? 'STALE' : mail.state" /></dd>
          <dt>Evaluation diagnostic</dt><dd>{{ mail.diagnosticCode }}</dd></dl>
        @if (mail.authorizationEvidence; as evidence) {
          <dl class="facts"><dt>Receipt ID</dt><dd><code>{{ evidence.receiptId }}</code></dd>
            <dt>Permission model</dt><dd>{{ evidence.model }}</dd>
            <dt>Attestation SHA-256</dt><dd><code>{{ evidence.attestationSha256 }}</code></dd>
            <dt>Expected binding SHA-256</dt><dd><code>{{ evidence.bindingSha256 }}</code></dd>
            <dt>Evidence expires</dt><dd>{{ evidence.expiresAtUtc }}</dd></dl>
        } @else {
          <p role="status">No typed signed receipt is persisted. Historical generic or role-only mail rows remain untrusted.</p>
        }
      </section>
    }
    @if (w.mailAuthorizationEnrollmentAvailable) {
      <section class="panel" aria-labelledby="mail-authorization-evidence-heading">
        <h2 id="mail-authorization-evidence-heading">Enroll signed mail authorization evidence</h2>
        <p>This records deployment-bound authorization evidence only. It does not send mail and is not proof of a live send or delivery.</p>
        <p>Choose the exact canonical JSON payload and its detached CMS signature. The files are uploaded byte-for-byte for server-side verification.</p>
        <label>Canonical payload (up to 1 MiB)
          <input type="file" accept="application/json,.json" (change)="selectMailAuthorizationFile($event, 'payload')" [disabled]="busy() || uncertain() || mailEvidenceReading()" />
        </label>
        @if (mailPayloadFile(); as file) { <p>Selected payload: {{ file.name }} · {{ file.size }} bytes</p> }
        <label>Detached CMS signature (up to 64 KiB)
          <input type="file" accept=".p7m,.p7s,.cms,application/pkcs7-mime,application/pkcs7-signature" (change)="selectMailAuthorizationFile($event, 'signature')" [disabled]="busy() || uncertain() || mailEvidenceReading()" />
        </label>
        @if (mailSignatureFile(); as file) { <p>Selected signature: {{ file.name }} · {{ file.size }} bytes</p> }
        <label><input #mailReview type="checkbox" [checked]="mailAuthorizationReviewed()" (change)="mailAuthorizationReviewed.set(mailReview.checked)" [disabled]="busy() || uncertain() || mailEvidenceReading()" /> I reviewed these exact files and understand this is authorization evidence, not live-send or delivery confirmation.</label>
        <button matButton="filled" (click)="enrollMailAuthorizationEvidence()" [disabled]="busy() || uncertain() || mailEvidenceReading() || !mailPayloadFile() || !mailSignatureFile() || !mailAuthorizationReviewed()">Enroll evidence</button>
        @if (mailEvidenceMessage()) { <p [attr.role]="mailEvidenceFailed() ? 'alert' : 'status'">{{ mailEvidenceMessage() }}</p> }
      </section>
    }
    @if(w.workspace.setupProgress; as p) { <section class="panel"><h2>Selected workspace setup</h2><ul>
      <li>Tenant recorded: {{ p.tenantRecorded ? 'Yes' : 'Pending' }}</li><li>Connection prepared: {{ p.connectionPrepared ? 'Yes' : 'Pending' }}</li>
      <li>Administrator consent verified: {{ p.consentVerified ? 'Yes' : 'Pending' }}</li><li>Selected resource evidence: {{ p.selectedResourcesEvidenceRecorded ? 'Recorded' : 'Pending' }}</li>
      <li>Client template approved: {{ p.clientTemplateApproved ? 'Yes' : 'Pending' }}</li><li>Workspace activated: {{ p.workspaceActivated ? 'Yes' : 'Pending' }}</li></ul></section> }
  }
  <audit-command-message [message]="message()" [failed]="failed()" />
  @if (!workspaceSnapshot() && uncertain() && mailEvidenceMessage()) { <p role="alert">{{ mailEvidenceMessage() }}</p> }
  <audit-sharepoint-administration (changed)="refreshAfterSharePointChange()" />
  <audit-workspace-administration />
`,
  styles: [`p { overflow-wrap: anywhere; } @media (max-width: 48rem) { dl.facts { grid-template-columns: minmax(0, 1fr); } dl.facts dt { margin-block-start: .5rem; } }`],
})
export class TenantConnection {
  readonly setupEditor = viewChild(TenantSetupEditor);
  readonly sharePointEditor = viewChild(SharePointAdministration);
  async confirmNavigation() {
    if (this.setupEditor() && !(await this.setupEditor()!.confirmNavigation())) return false;
    return this.sharePointEditor()?.confirmNavigation() ?? true;
  }
  async refresh() {
    if (this.busy() || !(await this.confirmNavigation())) return;
    const pending = this.pendingMailEvidenceRecovery;
    if (pending) {
      this.busy.set(true);
      try { await this.readBackMailAuthorizationEvidence(pending); }
      finally { this.busy.set(false); }
    } else this.reloadWorkspace();
  }
  refreshAfterSharePointChange() { this.reloadWorkspace(); }
  canEditSetup(draft: string | null, connection: string | null) { return !!draft && ![draft, connection].some(x => x === 'ACTIVE' || x === 'SUSPENDED' || x === 'BLOCKED'); }
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  readonly ws = this.api.resource(() => '/api/ui/administration/microsoft365', tenantWorkspace, 'Current firm-wide Administrator access is required.');
  private readonly mailEvidenceWorkspaceOverride = signal<MailEvidenceWorkspaceOverride | null>(null);
  private pendingMailEvidenceRecovery: MailEvidenceRecovery | null = null;
  workspaceSnapshot() {
    const override = this.mailEvidenceWorkspaceOverride();
    if (override) return override.actorKey === this.currentMailEvidenceActorKey() ? override.workspace : null;
    return this.ws.data();
  }
  readonly form = inject(FormBuilder).nonNullable.group({ reviewed: [false, Validators.requiredTrue] });
  readonly prepareReviewed = inject(FormBuilder).nonNullable.control(false);
  readonly mailPayloadFile = signal<File | null>(null);
  readonly mailSignatureFile = signal<File | null>(null);
  readonly mailAuthorizationReviewed = signal(false);
  readonly mailEvidenceReading = signal(false);
  readonly mailEvidenceMessage = signal('');
  readonly mailEvidenceFailed = signal(false);
  readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  private mailEvidenceReadVersion = 0;
  private mailEvidenceActorKey = '';
  mailCapability(w: ReturnType<typeof tenantWorkspace>) {
    return w.workspace.capabilities.find(capability => capability.capability === 'OUTBOUND_MAIL') ?? null;
  }
  constructor() {
    effect(() => {
      const actorKey = this.currentMailEvidenceActorKey(), workspace = this.workspaceSnapshot();
      if (actorKey !== this.mailEvidenceActorKey) {
        const hadPendingMailRecovery = this.pendingMailEvidenceRecovery !== null;
        this.mailEvidenceActorKey = actorKey;
        this.mailEvidenceWorkspaceOverride.set(null);
        this.resetMailAuthorizationEvidence();
        if (hadPendingMailRecovery) this.uncertain.set(false);
      }
      if (!workspace) { this.form.reset(); this.prepareReviewed.reset(); this.message.set(''); }
    });
  }
  selectMailAuthorizationFile(event: Event, part: 'payload' | 'signature'): void {
    ++this.mailEvidenceReadVersion;
    this.mailEvidenceReading.set(false);
    const input = event.currentTarget as HTMLInputElement;
    const files = input.files;
    this.mailAuthorizationReviewed.set(false);
    this.mailEvidenceFailed.set(false);
    this.mailEvidenceMessage.set('');
    const file = files?.length === 1 ? files[0] ?? null : null;
    const maxBytes = part === 'payload' ? 1_048_576 : 65_536;
    const accepted = file && file.size > 0 && file.size <= maxBytes ? file : null;
    (part === 'payload' ? this.mailPayloadFile : this.mailSignatureFile).set(accepted);
    if (!accepted) this.mailEvidenceMessage.set(`Choose exactly one ${part === 'payload' ? 'canonical payload' : 'detached CMS signature'} from 1 byte to ${maxBytes.toLocaleString()} bytes.`);
    input.value = '';
  }
  async enrollMailAuthorizationEvidence(): Promise<void> {
    const workspace = this.workspaceSnapshot();
    const payload = this.mailPayloadFile(), signature = this.mailSignatureFile();
    if (!workspace?.mailAuthorizationEnrollmentAvailable || !payload || !signature || !this.mailAuthorizationReviewed() ||
        this.busy() || this.uncertain() || this.mailEvidenceReading()) return;
    const generation = this.session.invalidation(), actorKey = this.currentMailEvidenceActorKey();
    const readVersion = ++this.mailEvidenceReadVersion;
    let submitted = false;
    this.mailEvidenceReading.set(true);
    this.busy.set(true);
    this.mailEvidenceFailed.set(false);
    this.mailEvidenceMessage.set('Reading the selected files without changing their bytes…');
    try {
      const [payloadBuffer, signatureBuffer] = await Promise.all([payload.arrayBuffer(), signature.arrayBuffer()]);
      if (generation !== this.session.invalidation() || readVersion !== this.mailEvidenceReadVersion || this.workspaceSnapshot() !== workspace) return;
      const payloadBytes = new Uint8Array(payloadBuffer), signatureBytes = new Uint8Array(signatureBuffer);
      if (payloadBytes.byteLength < 1 || payloadBytes.byteLength > 1_048_576 || signatureBytes.byteLength < 1 || signatureBytes.byteLength > 65_536) {
        this.mailEvidenceFailed.set(true);
        this.mailEvidenceMessage.set('The selected file bytes do not match the allowed size bounds. Select the files again.');
        return;
      }
      const attestationSha256 = await this.mailEvidenceEnvelopeDigest(payloadBytes, signatureBytes);
      if (generation !== this.session.invalidation() || actorKey !== this.currentMailEvidenceActorKey() ||
          readVersion !== this.mailEvidenceReadVersion || this.workspaceSnapshot() !== workspace) return;
      this.mailEvidenceMessage.set('Submitting the exact files for server-side signature and current-binding verification…');
      const request = {
        canonicalPayloadBase64: this.bytesToBase64(payloadBytes),
        detachedCmsBase64: this.bytesToBase64(signatureBytes),
        reviewed: true,
      };
      const recovery: MailEvidenceRecovery = { actorKey, attestationSha256, receiptId: null };
      this.pendingMailEvidenceRecovery = recovery;
      submitted = true;
      const result = await this.api.command('/api/ui/administration/microsoft365/mail-authorization-evidence/enroll', request);
      if (generation !== this.session.invalidation() || actorKey !== this.currentMailEvidenceActorKey() ||
          readVersion !== this.mailEvidenceReadVersion) return;
      if (!result.ok) {
        this.mailEvidenceFailed.set(true);
        if (result.unknown) {
          this.uncertain.set(true);
          this.mailEvidenceMessage.set('The server outcome is unknown. Persisted status is refreshing; do not submit again until that status is reviewed.');
          await this.readBackMailAuthorizationEvidence(recovery);
        } else {
          this.pendingMailEvidenceRecovery = null;
          this.mailEvidenceMessage.set(result.message);
        }
        return;
      }
      recovery.receiptId = decode(guid, result.value);
      this.uncertain.set(true);
      this.mailEvidenceFailed.set(false);
      this.mailEvidenceMessage.set(`Evidence receipt ${recovery.receiptId} acknowledged. Checking the exact persisted receipt…`);
      await this.readBackMailAuthorizationEvidence(recovery);
    } catch {
      if (generation === this.session.invalidation() && actorKey === this.currentMailEvidenceActorKey() &&
          readVersion === this.mailEvidenceReadVersion) {
        this.mailEvidenceFailed.set(true);
        if (submitted) {
          this.uncertain.set(true);
          this.mailEvidenceMessage.set('The server outcome is unknown. Persisted status is refreshing; do not submit again until that status is reviewed.');
          if (this.pendingMailEvidenceRecovery) await this.readBackMailAuthorizationEvidence(this.pendingMailEvidenceRecovery);
        } else this.mailEvidenceMessage.set('The selected evidence could not be prepared safely. Select the files again and retry.');
      }
    } finally {
      if (readVersion === this.mailEvidenceReadVersion) this.mailEvidenceReading.set(false);
      this.busy.set(false);
    }
  }
  private async mailEvidenceEnvelopeDigest(payload: Uint8Array, cms: Uint8Array): Promise<string> {
    // Mirrors MailAuthorizationRuntimeGate.ComputeAttestationDigest: big-endian Int32 lengths frame each byte segment.
    const envelope = new Uint8Array(8 + payload.byteLength + cms.byteLength);
    const view = new DataView(envelope.buffer);
    view.setInt32(0, payload.byteLength, false);
    envelope.set(payload, 4);
    const cmsLengthOffset = 4 + payload.byteLength;
    view.setInt32(cmsLengthOffset, cms.byteLength, false);
    envelope.set(cms, cmsLengthOffset + 4);
    const exactEnvelope = new ArrayBuffer(envelope.byteLength);
    new Uint8Array(exactEnvelope).set(envelope);
    const digest = new Uint8Array(await globalThis.crypto.subtle.digest('SHA-256', exactEnvelope));
    return Array.from(digest, value => value.toString(16).padStart(2, '0')).join('');
  }
  private async readBackMailAuthorizationEvidence(recovery: MailEvidenceRecovery): Promise<void> {
    const generation = this.session.invalidation();
    const isCurrent = () => generation === this.session.invalidation() &&
      recovery.actorKey === this.currentMailEvidenceActorKey() && this.pendingMailEvidenceRecovery === recovery;
    try {
      const refreshed = await this.api.get('/api/ui/administration/microsoft365', tenantWorkspace);
      if (!isCurrent()) return;
      this.mailEvidenceWorkspaceOverride.set({ actorKey: recovery.actorKey, workspace: refreshed });
      const capability = refreshed.workspace.capabilities.find(item => item.capability === 'OUTBOUND_MAIL');
      const evidence = capability?.authorizationEvidence;
      const exactReceipt = refreshed.mailAuthorizationEnrollmentAvailable === true && capability?.enabled === true &&
        capability.state === 'VERIFIED' && capability.stale === false &&
        evidence?.model === 'LEGACY_APPLICATION_ACCESS_POLICY' &&
        evidence.attestationSha256.toLowerCase() === recovery.attestationSha256 &&
        (!recovery.receiptId || evidence.receiptId.toLowerCase() === recovery.receiptId.toLowerCase());
      if (!exactReceipt || !evidence) {
        this.mailEvidenceFailed.set(true);
        this.mailEvidenceMessage.set('No exact current receipt matches the submitted files. The outcome remains unknown; refresh persisted status before taking any further action. No new enrollment was sent.');
        return;
      }
      this.pendingMailEvidenceRecovery = null;
      this.uncertain.set(false);
      this.mailEvidenceFailed.set(false);
      this.mailPayloadFile.set(null);
      this.mailSignatureFile.set(null);
      this.mailAuthorizationReviewed.set(false);
      this.mailEvidenceMessage.set(recovery.receiptId
        ? `Evidence receipt ${evidence.receiptId} is persisted for the exact submitted files. No second enrollment was sent.`
        : `Evidence receipt ${evidence.receiptId} matches the exact submitted files after the acknowledgment was lost. No second enrollment was sent.`);
    } catch {
      if (!isCurrent()) return;
      this.mailEvidenceWorkspaceOverride.set({ actorKey: recovery.actorKey, workspace: null });
      this.ws.reload();
      this.mailEvidenceFailed.set(true);
      this.mailEvidenceMessage.set('Persisted status could not be confirmed. The outcome remains unknown; refresh persisted status before taking any further action. No new enrollment was sent.');
    }
  }
  private bytesToBase64(bytes: Uint8Array): string {
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += 0x8000)
      binary += String.fromCharCode(...bytes.subarray(offset, Math.min(offset + 0x8000, bytes.length)));
    return btoa(binary);
  }
  private resetMailAuthorizationEvidence(): void {
    ++this.mailEvidenceReadVersion;
    this.pendingMailEvidenceRecovery = null;
    this.mailPayloadFile.set(null);
    this.mailSignatureFile.set(null);
    this.mailAuthorizationReviewed.set(false);
    this.mailEvidenceReading.set(false);
    this.mailEvidenceMessage.set('');
    this.mailEvidenceFailed.set(false);
  }
  private currentMailEvidenceActorKey(): string {
    const session = this.session.current(), epoch = this.session.invalidation();
    return session ? `${session.firmId}:${session.userId}:${session.generation}:${session.staff}:${epoch}` : `anonymous:${epoch}`;
  }
  private reloadWorkspace(): void {
    const pending = this.pendingMailEvidenceRecovery;
    this.mailEvidenceWorkspaceOverride.set(pending ? { actorKey: pending.actorKey, workspace: null } : null);
    this.ws.reload();
  }
  async prepare(): Promise<void> {
    const w = this.workspaceSnapshot(); if (!w?.workspace.connection.draftId || !w.workspace.draftRevision || !w.preparationConfigured || !this.prepareReviewed.value || this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try { const result = await this.api.command('/api/ui/administration/microsoft365/prepare', { draftId: w.workspace.connection.draftId, expectedRevision: w.workspace.draftRevision, reviewed: true });
      if (generation !== this.session.invalidation()) return;
      this.failed.set(!result.ok); this.message.set(result.ok ? 'Tenant connection prepared. Microsoft administrator consent and independent capability verification are still required.' : result.message);
      if (result.ok) this.reloadWorkspace(); else if (result.unknown) this.uncertain.set(true);
    } finally { this.busy.set(false); }
  }
  async connect(): Promise<void> {
    const w = this.workspaceSnapshot(), draftId = w?.workspace.connection.draftId, tenant = w?.workspace.connection.expectedTenantId;
    if (!w || !draftId || !tenant || !w.consentConfigured || this.form.invalid || this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try {
      const result = await this.api.command('/api/ui/administration/microsoft365/connect', { draftId, reviewed: true });
      if (generation !== this.session.invalidation()) return;
      if (result.ok) location.assign(consentDestination(result.value, tenant, w.simulation));
      else { this.message.set(result.message); this.failed.set(true); if(result.unknown) this.uncertain.set(true); }
    } catch { this.failed.set(true); this.message.set('The consent redirect could not be verified. Refresh the persisted attempt state before starting again.'); this.uncertain.set(true); }
    finally { this.busy.set(false); }
  }
  async verify(): Promise<void> {
    if (this.busy() || this.uncertain()) return; const generation = this.session.invalidation(); this.busy.set(true);
    try { const result = await this.api.command('/api/ui/administration/microsoft365/verify'); if(generation !== this.session.invalidation()) return;
      this.failed.set(!result.ok); this.message.set(result.ok ? 'Verification results were recorded independently. Review each capability below.' : result.message);
      if(result.ok) this.reloadWorkspace(); else if(result.unknown) this.uncertain.set(true);
    } finally { this.busy.set(false); }
  }
}
