import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { clientSiteIntentReceipt, clientSiteOperatorReview, clientSites, siteLink, workspacePage, workspaceResult, workspaceReview } from './workspace-contracts';

const prefix = '/api/ui/administration/microsoft365/';
@Component({ selector:'audit-workspace-administration', imports:[ReactiveFormsModule, RouterLink, MatButtonModule, ...SHARED], template:`
  <section class="panel" aria-labelledby="workspace-heading"><h2 id="workspace-heading">Client and engagement workspaces</h2>
    <p>Only an accepted client has a workspace intent. Folder provisioning uses the approved selected resource and templates. Client folders being ready does not verify PBC upload or read-back.</p>
    <button matButton (click)="refresh()" [disabled]="busy()">Refresh workspace status</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="Client workspaces" />
    @if(ws.data(); as w) {
      @if(!w.providerConfigured) { <p role="status">BLOCKED_EXTERNAL: the selected-site provider is not configured. A Microsoft SharePoint administrator must grant the exact approved site to the document application; the server needs its approved credential.</p> }
      @if(!w.workspace.clients.length) { <p>No accepted client workspace intents exist on this page. Complete the client acceptance workflow first.</p> }
      @for(c of w.workspace.clients; track c.clientId) { <section class="panel">
        <h3><a [routerLink]="['/app/clients',c.clientId]">{{ c.clientName }}</a></h3>
        <p>Client workspace: <audit-status [value]="c.workspaceState" /> · last verification {{ c.lastVerifiedAt ?? 'Never' }} · {{ c.lastErrorCode ?? 'No recorded failure' }}</p>
        <button matButton="outlined" (click)="select(c.clientId,false)" [disabled]="busy() || uncertain()">Review client workspace</button>
        <div class="table-scroll"><table><caption>Engagement repositories (up to 25 per client)</caption><thead><tr><th>Engagement</th><th>PBC capability</th><th>Last test</th><th>Action</th></tr></thead><tbody>
          @for(e of c.engagements; track e.engagementId) { <tr><td><a [routerLink]="['/app/engagements',e.engagementId]">{{ e.label }}</a></td><td><audit-status [value]="e.bindingState" /></td><td>{{ e.testedAt ?? 'Never' }}</td>
            <td><button matButton (click)="select(e.engagementId,true)" [disabled]="busy() || uncertain()">Review engagement repository</button></td></tr> }
        </tbody></table></div>
      </section> }
      <nav aria-label="Workspace pages"><button matButton (click)="move(-1)" [disabled]="page() === 0 || busy() || uncertain()">Previous workspace page</button>
        <span>Page {{ page()+1 }}</span><button matButton (click)="move(1)" [disabled]="!w.workspace.hasMore || busy() || uncertain()">Next workspace page</button></nav>
    }
    <audit-state [loading]="review.loading()" [error]="review.error()" label="Exact workspace review" />
    @if(review.data(); as r) { <section class="panel" aria-label="Workspace provisioning review"><h3>Review {{ r.kind === 'CLIENT' ? 'client workspace' : 'engagement repository' }}</h3>
      <p>{{ r.name }} · {{ r.targetId }} · current state <audit-status [value]="r.currentState" /></p>
      <p>Actor {{ session.current()?.userId }} · firm {{ session.current()?.firmId }}</p>
      <dl><dt>Tenant</dt><dd>{{ r.tenantId ?? 'Unavailable' }}</dd><dt>Selected site</dt><dd>{{ r.siteId ?? 'Unavailable' }}</dd><dt>Library</dt><dd>{{ r.driveId ?? 'Unavailable' }}</dd>
        <dt>Root folder</dt><dd>{{ r.rootFolderId ?? 'Unavailable' }}</dd><dt>Approved client template</dt><dd>{{ r.clientTemplateId ?? 'Unavailable' }}</dd><dt>Approved engagement template</dt><dd>{{ r.engagementTemplateId ?? 'Not required for client folders' }}</dd></dl>
      <p>{{ r.requiredAction }}</p><p>Proposed change: create or reconcile these approved folders. Engagement provisioning tests a disposable upload, exact read-back and deletion. This action grants no local role or Microsoft staff membership.</p>
      <form [formGroup]="form" (ngSubmit)="provision()"><label>Reason<textarea formControlName="reason" maxlength="1000" rows="3"></textarea></label>
        <label><input type="checkbox" formControlName="reviewed" /> I reviewed the target, resource binding, approved templates and current state.</label>
        <button matButton="filled" [disabled]="!canProvision()">Provision reviewed workspace</button>
        <button matButton type="button" (click)="closeReview()" [disabled]="busy()">Close review</button>
      </form>
    </section> }
    @if(uncertain()) { <p role="status">The outcome is unconfirmed. Refresh persisted workspace state, then select the target and review it again before reconciliation. No action is automatically retried.</p> }
    <audit-command-message [message]="message()" [failed]="failed()" />
  </section>
  <section class="panel" aria-labelledby="client-site-heading"><h2 id="client-site-heading">Dedicated client SharePoint sites</h2>
    <p>Site creation and membership reconciliation run in the isolated client-sites worker. Its privileged certificate is unavailable to this API and browser.</p>
    <p role="note">Assigned staff have Full Control over the entire client site, including sibling engagements, sharing and deletion. AuditSphere roles and client/engagement scopes remain separate. SharePoint permission revocation completes when the worker reconciles it; local session revocation does not prove Microsoft revocation.</p>
    <audit-state [loading]="sites.loading()" [error]="sites.error()" label="Dedicated client sites" />
    @if(sites.data(); as status) {
      <dl><dt>Provisioning policy</dt><dd><audit-status [value]="status.enabled ? 'ENABLED' : 'DISABLED'" /> · {{ status.policyState }}</dd>
        <dt>Configured rollout boundary</dt><dd>{{ status.configuredRolloutBoundary ?? 'Not set' }}</dd>
        <dt>Effective persisted boundary</dt><dd>{{ status.effectiveRolloutBoundary ?? 'Not applied' }}</dd>
        <dt>Eligible clients</dt><dd>{{ status.eligibleClientCount }} · {{ status.existingClientsOutsideRollout }} remain outside the rollout</dd>
        <dt>Tenant consent</dt><dd><audit-status [value]="status.tenantConsentState" /></dd>
        <dt>Worker readiness</dt><dd><audit-status [value]="status.workerReadiness" /> · last heartbeat {{ status.workerLastSeenAt ?? 'Never observed' }} · {{ status.workerDiagnosticCode ?? 'No configuration blocker recorded' }}</dd>
        <dt>Microsoft site permissions</dt><dd><audit-status [value]="status.microsoftPermissionState" /> · last verified {{ status.microsoftPermissionVerifiedAt ?? 'Not yet verified by a live site operation' }}</dd></dl>
      <p>The first 100 eligible or already-provisioned clients are shown. <a routerLink="/app/operations">Review worker operation evidence and recovery</a>.</p>
      @if(!status.clients.length) { <p>No eligible or provisioned client sites are available.</p> }
      <div class="table-scroll"><table><caption>Client-site and membership verification</caption><thead><tr><th>Client</th><th>Site</th><th>Membership</th><th>Last verified members</th><th>Last site verification</th><th>Last membership verification</th><th>Operation</th><th>Error</th><th>Required action</th></tr></thead><tbody>
        @for(s of status.clients; track s.clientId) { <tr><td>{{ s.clientName }}</td><td><audit-status [value]="s.state" />
          @if(siteLink(s.state,s.url); as link) { <a [href]="link" target="_blank" rel="noopener noreferrer">Open verified site</a> }</td>
          <td><audit-status [value]="s.membershipState" /></td><td>{{ s.verifiedMembers }}</td><td>{{ s.verifiedAt ?? 'Never' }}</td><td>{{ s.membershipVerifiedAt ?? 'Never' }}</td><td>{{ s.operationState ?? 'Not queued' }}</td><td>{{ s.errorCode ?? 'None' }}</td><td>{{ s.requiredAction }}
            <div class="actions"><button matButton type="button" (click)="reviewClientSiteIntent(s.clientId, false)" [disabled]="busy() || intentBusy() || intentUnknown()">Review site request</button>
              <button matButton type="button" (click)="reviewClientSiteIntent(s.clientId, true)" [disabled]="busy() || intentBusy() || intentUnknown()">Review staff reconciliation</button></div></td></tr> }
      </tbody></table></div>
    }
    <audit-state [loading]="intentReview.loading()" [error]="intentReview.error()" label="Client-site intent review" />
    @if(intentReview.data(); as intent) {
      <section class="panel" aria-label="Client-site operator intent review">
        <h3>{{ intentMode()?.reconcileOnly ? 'Review staff-only membership reconciliation' : 'Review dedicated-site request' }}</h3>
        <p>Client <code>{{ intent.clientId }}</code> · tenant <code>{{ intent.tenantId }}</code> · site {{ intent.url ?? 'Unavailable' }}</p>
        <p>Current worker eligibility: <audit-status [value]="intent.eligible ? 'READY' : 'BLOCKED'" /> · {{ intent.requiredAction }}</p>
        <p>Membership <audit-status [value]="intent.membershipState" /> · receipt {{ intent.receiptState }} · operation {{ intent.operationState ?? 'Not queued' }}</p>
        @if(intent.publishedReceipt; as receipt) { <p>Published operation <code>{{ receipt.operationId }}</code> · evidence {{ receipt.digest }}</p> }
        <form [formGroup]="intentForm" (ngSubmit)="requestClientSiteIntent()">
          <label>Reason<textarea formControlName="reason" maxlength="1000" rows="3"></textarea></label>
          <label><input type="checkbox" formControlName="reviewed" /> I reviewed this exact client, current worker state and requested action.</label>
          <button matButton="filled" [disabled]="!canRequestClientSiteIntent()">Request reviewed worker action</button>
          <button matButton type="button" (click)="closeClientSiteIntent()" [disabled]="intentBusy()">Close review</button>
        </form>
      </section>
    }
    @if(intentUnknown()) { <p role="alert">The request outcome is unknown. Refresh current client-site evidence and review it again before requesting any action.</p>
      <button matButton type="button" (click)="refreshClientSiteIntent()" [disabled]="intentBusy()">Refresh client-site evidence</button> }
    @if(intentMessage()) { <p [attr.role]="intentFailed() ? 'alert' : 'status'">{{ intentMessage() }}</p> }
  </section>
`,styles:`label { display:block; margin-block:.8rem; } textarea { display:block; width:100%; box-sizing:border-box; } dd { overflow-wrap:anywhere; } nav { display:flex; align-items:center; gap:1rem; }` })
export class WorkspaceAdministration {
  private readonly api = inject(Api); readonly session = inject(SessionService); private readonly fb = inject(FormBuilder).nonNullable;
  readonly page = signal(0); private readonly target = signal<{id:string;engagement:boolean}|null>(null);
  readonly ws = this.api.resource(() => prefix+'workspaces?page='+this.page(), workspacePage, 'Current firm-wide Administrator access is required.');
  readonly sites = this.api.resource(() => prefix+'client-sites',clientSites,'Current firm-wide Administrator access is required.');
  readonly review = this.api.resource(() => { const t=this.target(); return t ? prefix+'workspaces/'+(t.engagement ? 'engagements/' : 'clients/')+t.id+'/review' : null; },workspaceReview);
  readonly intentMode = signal<{clientId:string;reconcileOnly:boolean}|null>(null);
  readonly intentReview = this.api.resource(() => { const t=this.intentMode(); return t ? prefix+'client-sites/'+t.clientId+'/'+(t.reconcileOnly?'staff-review':'review') : null; },raw=>{
    const mode=this.intentMode(), view=decode(clientSiteOperatorReview,raw);
    if(!mode || view.clientId!==mode.clientId) throw new Error('Client-site review identity mismatch');
    return view;
  },'The current client-site intent is unavailable in the authenticated firm scope.');
  readonly form = this.fb.group({ reason:['',[Validators.required,Validators.maxLength(1000),Validators.pattern(/\S/)]], reviewed:[false,Validators.requiredTrue] });
  readonly intentForm = this.fb.group({ reason:['',[Validators.required,Validators.maxLength(1000),Validators.pattern(/\S/)]], reviewed:[false,Validators.requiredTrue] });
  readonly busy=signal(false); readonly uncertain=signal(false); readonly message=signal(''); readonly failed=signal(false); readonly siteLink=siteLink;
  readonly intentBusy=signal(false); readonly intentUnknown=signal(false); readonly intentMessage=signal(''); readonly intentFailed=signal(false);
  private refreshing=false;
  constructor() {
    effect(() => { this.review.data(); this.form.reset(); });
    effect(() => { const w=this.ws.data(); if(w && this.refreshing) { this.refreshing=false; this.uncertain.set(false); this.message.set('Persisted state refreshed. Select the target and review it again.'); } });
    effect(() => { this.session.invalidation(); this.target.set(null); this.message.set(''); this.failed.set(false); this.uncertain.set(false);
      this.intentMode.set(null); this.intentForm.reset(); this.intentMessage.set(''); this.intentFailed.set(false); this.intentUnknown.set(false); });
  }
  select(id:string,engagement:boolean):void { if(this.busy() || this.uncertain()) return; this.form.reset(); this.target.set({id,engagement}); }
  closeReview():void { if(!this.busy()) this.target.set(null); }
  move(change:number):void { if(this.busy() || this.uncertain()) return; this.closeReview(); this.page.update(p=>Math.max(0,p+change)); }
  refresh():void { if(this.busy()) return; this.target.set(null); this.form.reset(); this.refreshing=true; this.ws.reload(); this.sites.reload(); }
  reviewClientSiteIntent(clientId:string,reconcileOnly:boolean):void {
    if(this.intentBusy() || this.intentUnknown() || !this.sites.data()?.clients.some(x=>x.clientId===clientId)) return;
    this.intentForm.reset(); this.intentMessage.set(''); this.intentFailed.set(false); this.intentMode.set({clientId,reconcileOnly});
  }
  closeClientSiteIntent():void { if(!this.intentBusy()) { this.intentMode.set(null); this.intentForm.reset(); } }
  canRequestClientSiteIntent():boolean {
    const mode=this.intentMode(),view=this.intentReview.data();
    return !!mode && !!view && view.clientId===mode.clientId && view.eligible && this.intentForm.valid &&
      !this.intentBusy() && !this.intentUnknown();
  }
  async requestClientSiteIntent():Promise<void> {
    if(!this.canRequestClientSiteIntent()) return;
    const mode=this.intentMode()!,view=this.intentReview.data()!,epoch=this.session.invalidation(); this.intentBusy.set(true);
    try {
      const result=await this.api.command(prefix+'client-sites/'+mode.clientId+'/request',{
        reviewToken:view.reviewToken,reconcileOnly:mode.reconcileOnly,...this.intentForm.getRawValue()});
      if(epoch!==this.session.invalidation() || this.intentMode()!==mode) return;
      this.intentForm.controls.reviewed.setValue(false); this.intentFailed.set(!result.ok);
      if(result.ok) {
        try { const receipt=decode(clientSiteIntentReceipt,result.value); if(receipt.clientId!==mode.clientId) throw new Error('Client-site request scope changed');
          this.intentMessage.set(`${receipt.state}${receipt.operationId ? ': '+receipt.operationId : ''}`); this.intentMode.set(null); this.intentForm.reset(); this.sites.reload(); }
        catch { this.intentUnknown.set(true); this.intentFailed.set(true); this.intentMessage.set('The request response could not be verified. Refresh persisted evidence before reconciliation.'); }
      } else { this.intentMessage.set(result.message); if(result.unknown) this.intentUnknown.set(true); }
    } finally { this.intentBusy.set(false); }
  }
  refreshClientSiteIntent():void { if(this.intentBusy()) return; this.intentUnknown.set(false); this.intentMode.set(null); this.intentForm.reset(); this.sites.reload(); }
  canProvision():boolean { const r=this.review.data(),t=this.target(); return !!r?.eligible && !!t && r.targetId===t.id &&
    r.kind===(t.engagement?'ENGAGEMENT':'CLIENT') && !!this.ws.data()?.providerConfigured && this.form.valid && !this.busy() && !this.uncertain(); }
  async provision():Promise<void> {
    if(!this.canProvision()) return; const r=this.review.data()!,t=this.target()!,epoch=this.session.invalidation(); this.busy.set(true);
    try { const result=await this.api.command(prefix+'workspaces/'+(t.engagement?'engagements':'clients')+'/provision',
      {targetId:r.targetId,reviewToken:r.reviewToken,...this.form.getRawValue()});
      if(epoch!==this.session.invalidation()) return;
      this.form.controls.reviewed.setValue(false); this.failed.set(!result.ok);
      if(result.ok) { try { const value=decode(workspaceResult,result.value); this.message.set(value.state+': '+value.message); this.failed.set(!['READY','VERIFIED'].includes(value.state));
        this.target.set(null); this.ws.reload(); this.sites.reload(); }
        catch { this.uncertain.set(true); this.failed.set(true); this.message.set('Unsupported response. Refresh persisted state before reconciliation.'); }
      } else { this.message.set(result.message); if(result.unknown) this.uncertain.set(true); else this.target.set(null); }
    } finally { this.busy.set(false); }
  }
}
