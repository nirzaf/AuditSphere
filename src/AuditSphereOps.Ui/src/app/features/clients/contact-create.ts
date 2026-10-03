import { Component, DestroyRef, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { disabled, form, FormField, maxLength, readonly, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { ContactFields, contactFields, decodeContactLookup, decodeContactPreview, decodeContactReceipt, decodeContactState, emptyContact, validContact } from './contact-create-contracts';
import { clientLocation, clientUtcTime, defaultClientLocation } from './client-contracts';
import { PortfolioNavigation } from '../portfolio/portfolio-contracts';

type Request = { requestId: string; reviewBasis: string; fields: ContactFields; reviewed: boolean; expectedRequestHash?: string };
@Component({ selector: 'audit-contact-create', imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './contact-create.html', styleUrl: './contact-create.scss' })
export class ContactCreate implements NavigationProtected {
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts); private readonly dialog = inject(MatDialog);
  readonly id = routeGuid(); readonly utcTime = clientUtcTime;
  readonly portfolioNavigation = inject(PortfolioNavigation);
  private readonly route = inject(ActivatedRoute);
  readonly returnParams = clientLocation(k => this.route.snapshot?.queryParamMap?.get(k) ?? null) ?? defaultClientLocation;
  readonly model = signal<ContactFields>(emptyContact());
  readonly fields = form(this.model, p => { required(p.name); maxLength(p.name,200); required(p.email); maxLength(p.email,254); required(p.role); maxLength(p.role,200); readonly(p.name,()=>this.locked()); readonly(p.email,()=>this.locked()); readonly(p.role,()=>this.locked()); disabled(p.primary,()=>this.locked()); });
  readonly state = this.api.resource(() => this.base(), (raw,path) => { const s=decodeContactState(raw,path); if(s.clientId!==this.id())throw new Error('Wrong client');return s; });
  readonly preview = signal<ReturnType<typeof decodeContactPreview>|null>(null);
  readonly receipt = signal<ReturnType<typeof decodeContactReceipt>|null>(null);
  readonly pending = signal<PendingRequestReference|null>(null);
  readonly reviewed = signal(false); readonly busy = signal(false); readonly uncertain = signal(false); readonly absent = signal(false);
  readonly message = signal(''); readonly draftAvailable = signal(false);
  readonly valid = computed(() => this.fields().valid() && validContact(this.model()));
  readonly locked = computed(() => this.busy() || this.uncertain() && !this.absent());
  private readonly baseline = signal(JSON.stringify(emptyContact()));
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  private generation = 0; private request: Request|null = null;
  constructor() {
    effect(() => { this.id(); this.session.invalidation(); const s=this.session.current(); s?.firmId; s?.userId; s?.generation;
      untracked(() => { this.generation++; this.model.set(emptyContact()); this.baseline.set(JSON.stringify(emptyContact()));
        this.preview.set(null);this.receipt.set(null);this.reviewed.set(false);this.pending.set(null);this.busy.set(false);this.uncertain.set(false);
        this.absent.set(false);this.message.set('');this.draftAvailable.set(false);this.request=null; }); });
    effect(() => { const s=this.state.data(); if(!s)return;
      untracked(() => { this.preview.set(null);this.reviewed.set(false);this.request=null;
        this.draftAvailable.set(this.drafts.read(this.scope(),contactFields).state==='ready');
        const p=this.drafts.readPendingRequest(this.scope(true)); if(p.state==='ready') {
          this.pending.set(p.draft.value);this.uncertain.set(true);this.absent.set(false);this.message.set(UNKNOWN_OUTCOME);
        } }); });
    effect(() => { this.model();untracked(() => {this.preview.set(null);this.reviewed.set(false);this.request=null;}); });
    inject(DestroyRef).onDestroy(() => { this.generation++; });
  }
  private base() { return this.id() ? '/api/ui/clients/'+this.id()+'/contact-creation' : null; }
  private scope(pending=false) { return {entity:(pending?'contact-request/':'contact-fields/')+this.id(),baseRevision:this.state.data()?.reviewBasis??''}; }
  private matches(r:ReturnType<typeof decodeContactReceipt>,p:PendingRequestReference) { return r.clientId===this.id() && r.actorId===this.session.current()?.userId && r.requestId===p.requestId && r.requestHash===p.requestHash; }
  async prepare() {
    const s=this.state.data(), g=this.generation, pending=this.pending();if(!s||!this.valid()||this.locked())return;
    const r:Request={requestId:pending?.requestId??crypto.randomUUID(),reviewBasis:s.reviewBasis,fields:structuredClone(this.model()),reviewed:false};
    this.busy.set(true);this.preview.set(null);this.reviewed.set(false);
    try {const result=await this.api.command(this.base()+'/preview',r);if(g!==this.generation)return;
      if(!result.ok){this.message.set(result.message);return;}
      const p=decodeContactPreview(result.value,'preview');
      if(p.clientId!==this.id()||p.requestId!==r.requestId||p.reviewBasis!==r.reviewBasis||this.state.data()?.reviewBasis!==r.reviewBasis||
        JSON.stringify(p.fields)!==JSON.stringify({name:r.fields.name.trim(),email:r.fields.email.trim(),role:r.fields.role.trim(),primary:r.fields.primary})||pending&&p.requestHash!==pending.requestHash)
        throw new Error('The exact contact intent changed. Restore its original fields or verify the retained receipt.');
      this.request=r;this.preview.set(p);this.message.set('Review the contact and any primary-contact replacement.');
    }catch(e){if(g===this.generation)this.message.set(e instanceof Error?e.message:'Review unavailable.');}finally{if(g===this.generation)this.busy.set(false);}
  }
  async execute() {
    const s=this.state.data(),p=this.preview(),r=this.request,g=this.generation;
    if(!s||!p||!r||!this.reviewed()||this.busy()||s.reviewBasis!==r.reviewBasis||JSON.stringify(this.model())!==JSON.stringify(r.fields))return;
    const ref={requestId:r.requestId,requestHash:p.requestHash};
    if(!this.drafts.save(this.scope(),this.model(),contactFields,true)||!this.drafts.save(this.scope(true),ref,pendingRequestReference,true)) {
      this.message.set('Recovery storage is unavailable in this tab. No contact was sent.');return;
    }
    this.pending.set(ref);this.busy.set(true);this.reviewed.set(false);
    const result=await this.api.command(this.base()!,{...r,reviewed:true,expectedRequestHash:p.requestHash});
    if(g!==this.generation)return;this.busy.set(false);this.preview.set(null);this.request=null;
    if(!result.ok){this.message.set(result.message);if(result.unknown){this.uncertain.set(true);this.absent.set(false);}else{this.clearPending();}return;}
    try {const receipt=decodeContactReceipt(result.value,'receipt');if(!this.matches(receipt,ref))throw new Error('Wrong receipt');
      this.receipt.set(receipt);this.clearPending();this.drafts.clear(this.scope().entity);this.model.set(emptyContact());this.baseline.set(JSON.stringify(emptyContact()));
      this.message.set('Contact recorded with an immutable receipt. No portal or Microsoft access was granted.');this.state.reload();
    }catch{this.uncertain.set(true);this.absent.set(false);this.message.set(UNKNOWN_OUTCOME);}
  }
  private clearPending() {this.drafts.clear(this.scope(true).entity);this.pending.set(null);this.uncertain.set(false);this.absent.set(false);this.request=null;}
  async reconcile() {
    const p=this.pending(),g=this.generation;if(!p||!this.state.data()||this.busy())return;
    this.busy.set(true);this.preview.set(null);this.reviewed.set(false);
    try {const lookup=await this.api.get(this.base()+'/receipts/'+p.requestId+'?requestHash='+p.requestHash,decodeContactLookup);if(g!==this.generation)return;
      if(lookup.found&&lookup.receipt&&this.matches(lookup.receipt,p)){this.receipt.set(lookup.receipt);this.absent.set(false);this.message.set('The retained receipt confirms this contact. Acknowledge the result before continuing.');}
      else if(!lookup.found){this.absent.set(true);this.message.set('No receipt is currently retained. Restore or enter the identical original fields, then obtain fresh review to retry the same request. Changed fields cannot reuse it.');}
      else throw new Error('Wrong receipt');
    }catch{if(g===this.generation)this.message.set('Receipt verification is unavailable. Keep the reference and check again.');}finally{if(g===this.generation)this.busy.set(false);}
  }
  acknowledge() {const r=this.receipt(),p=this.pending();if(!r||!p||!this.matches(r,p))return;
    this.clearPending();this.drafts.clear(this.scope().entity);this.model.set(emptyContact());this.baseline.set(JSON.stringify(emptyContact()));this.state.reload();}
  saveDraft() {const ok=!this.busy()&&!this.uncertain()&&this.drafts.save(this.scope(),this.model(),contactFields);this.message.set(ok?'Editable fields saved in this tab for four hours. Review assent is excluded.':'Draft unavailable.');return ok;}
  restoreDraft() {if(this.locked())return;const d=this.drafts.read(this.scope(),contactFields);if(d.state!=='ready'){this.message.set('Saved fields are stale or unavailable.');return;}
    this.model.set(d.draft.value);this.draftAvailable.set(false);this.preview.set(null);this.reviewed.set(false);}
  discardDraft() {if(this.busy()||this.uncertain())return false;this.model.set(JSON.parse(this.baseline()));this.preview.set(null);this.reviewed.set(false);this.drafts.clear(this.scope().entity);return true;}
  async confirmNavigation() {if(this.busy()||this.uncertain()){this.message.set('Verify the retained contact request before leaving.');return false;}if(!this.dirty())return true;
    const d=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());return d==='save'?this.saveDraft():d==='discard'?this.discardDraft():false;}
  @HostListener('window:beforeunload',['$event']) beforeUnload(e:BeforeUnloadEvent){if(this.dirty()||this.busy()||this.uncertain()){e.preventDefault();e.returnValue='';}}
  async refresh(){if(this.busy())return;if(!this.uncertain()&&this.dirty()&&!(await this.confirmNavigation()))return;
    this.preview.set(null);this.reviewed.set(false);this.state.reload();}
}
