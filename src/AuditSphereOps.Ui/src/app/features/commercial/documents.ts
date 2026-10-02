import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
interface Artifact {
  id: string;
  quotationId: string | null;
  kind: string;
  templateVersion: string;
  profileVersion: string;
  fileName: string;
  contentType: string;
  sha256: string;
  createdAt: string;
}
interface Workspace {
  proposalId: string;
  quotationId: string | null;
  profileVersion: string | null;
  firmName: string | null;
  history: string | null;
  credentials: string | null;
  methodology: string | null;
  commonBlockers: string[];
  letterBlockers: string[];
  tenderBlockers: string[];
  documents: Artifact[];
}
export function decodeDocuments(value: unknown): Workspace {
  if (!value || typeof value !== 'object') throw new Error('Unsupported document workspace');
  const v = value as Record<string, unknown>;
  if (
    typeof v['proposalId'] !== 'string' ||
    !guidPattern.test(v['proposalId']) ||
    !(
      v['quotationId'] === null ||
      (typeof v['quotationId'] === 'string' && guidPattern.test(v['quotationId']))
    ) ||
    !(
      v['profileVersion'] === null ||
      (typeof v['profileVersion'] === 'string' && /^\d{1,19}$/.test(v['profileVersion']))
    ) ||
    !['firmName', 'history', 'credentials', 'methodology'].every(
      (k) => v[k] === null || typeof v[k] === 'string',
    ) ||
    !['commonBlockers', 'letterBlockers', 'tenderBlockers'].every(
      (k) =>
        Array.isArray(v[k]) &&
        (v[k] as unknown[]).length <= 20 &&
        (v[k] as unknown[]).every((x) => typeof x === 'string'),
    ) ||
    !Array.isArray(v['documents']) ||
    v['documents'].length > 100
  )
    throw new Error('Unsupported document workspace');
  for (const d of v['documents']) {
    if (
      !d ||
      typeof d.id !== 'string' ||
      !guidPattern.test(d.id) ||
      !(
        d.quotationId === null ||
        (typeof d.quotationId === 'string' && guidPattern.test(d.quotationId))
      ) ||
      !['kind', 'templateVersion', 'fileName', 'contentType', 'createdAt', 'profileVersion'].every(
        (k) => typeof d[k] === 'string',
      ) ||
      !/^\d{1,19}$/.test(d.profileVersion) ||
      typeof d.sha256 !== 'string' ||
      !/^[a-f0-9]{64}$/i.test(d.sha256)
    )
      throw new Error('Unsupported artifact');
  }
  return v as unknown as Workspace;
}
@Component({
  selector: 'audit-commercial-documents',
  imports: [FormsModule, MatButtonModule, MatProgressBarModule],
  template: `
    <section aria-labelledby="commercial-documents-heading">
      <h2 id="commercial-documents-heading">Commercial documents</h2>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Loading commercial documents" />
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      <button matButton [disabled]="busy()" (click)="load()">Refresh document state</button>
      @if (data(); as w) {
        <p>
          Reviewed quotation: <code>{{ w.quotationId ?? 'Not configured' }}</code
          >. Firm profile {{ w.profileVersion ?? 'Not configured' }} · {{ w.firmName }}
        </p>
        <p>
          One immutable artifact is retained for each document kind and quotation version. An
          existing artifact keeps its original profile and template identity; revise and approve a
          new quotation to reissue. Visual signatures and seals do not constitute electronic
          signature provider acceptance.
        </p>
        <label
          ><input type="checkbox" [(ngModel)]="reviewed" [disabled]="busy()" />I reviewed this
          quotation, firm profile and document action.</label
        >
        <h3>Brief quotation</h3>
        <ul>
          @for (b of w.commonBlockers; track b) {
            <li>{{ b }}</li>
          }
        </ul>
        <button
          matButton
          [disabled]="busy() || uncertain() || !reviewed || w.commonBlockers.length > 0"
          (click)="generate('quotation')"
        >
          Generate brief quotation
        </button>
        <h3>Engagement letter</h3>
        <ul>
          @for (b of w.letterBlockers; track b) {
            <li>{{ b }}</li>
          }
        </ul>
        <button
          matButton
          [disabled]="busy() || uncertain() || !reviewed || w.letterBlockers.length > 0"
          (click)="generate('letter')"
        >
          Generate engagement letter
        </button>
        <h3>Comprehensive five-chapter proposal</h3>
        <p>
          Review the actual firm chapters, assigned team and delivery timeline. No credentials or
          team CVs are invented.
        </p>
        <details>
          <summary>Firm profile chapters</summary>
          <h4>History and registrations</h4>
          <p>{{ w.history }}</p>
          <h4>Industry credentials</h4>
          <p>{{ w.credentials }}</p>
          <h4>Methodology</h4>
          <p>{{ w.methodology }}</p>
        </details>
        <ul>
          @for (b of w.tenderBlockers; track b) {
            <li>{{ b }}</li>
          }
        </ul>
        <label
          >Assigned Partner and team CVs<textarea
            [(ngModel)]="teamCvs"
            maxlength="16000"
            [disabled]="busy()"
          ></textarea>
        </label>
        <label
          >Deliverables timeline<textarea
            [(ngModel)]="timeline"
            maxlength="8000"
            [disabled]="busy()"
          ></textarea>
        </label>
        <button
          matButton
          [disabled]="
            busy() ||
            uncertain() ||
            !reviewed ||
            !teamCvs.trim() ||
            !timeline.trim() ||
            w.tenderBlockers.length > 0
          "
          (click)="generate('tender')"
        >
          Generate comprehensive proposal
        </button>
        <h3>Latest 100 immutable artifacts</h3>
        <ul>
          @for (d of w.documents; track d.id) {
            <li>
              <a [href]="'/api/commercial/documents/' + d.id + '/download'" download>{{
                d.fileName
              }}</a>
              <p>
                {{ d.kind }} · template {{ d.templateVersion }} · profile {{ d.profileVersion }} ·
                {{ d.createdAt }}
              </p>
              <p>
                Document <code>{{ d.id }}</code> · quotation <code>{{ d.quotationId }}</code> ·
                SHA-256 <code>{{ d.sha256 }}</code>
              </p>
            </li>
          } @empty {
            <li>No generated artifacts yet.</li>
          }
        </ul>
        @if (uncertain()) {
          <p>Compare the persisted artifacts before another command.</p>
          <button matButton [disabled]="busy()" (click)="acknowledge()">
            I reviewed the persisted document outcome
          </button>
        }
      }
      <p role="status">{{ message() }}</p>
    </section>
  `,
})
export class CommercialDocuments {
  readonly proposalId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private read?: Subscription;
  private write?: Subscription;
  private fence = 0;
  readonly data = signal<Workspace | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  reviewed = false;
  teamCvs = '';
  timeline = '';
  constructor() {
    effect(() => {
      this.proposalId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.fence++;
        this.read?.unsubscribe();
        this.write?.unsubscribe();
        this.data.set(null);
        this.busy.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.reviewed = false;
        this.teamCvs = this.timeline = '';
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.fence++;
      this.read?.unsubscribe();
      this.write?.unsubscribe();
    });
  }
  acknowledge(): void {
    if (this.data() && !this.busy()) {
      this.uncertain.set(false);
      this.reviewed = false;
      this.message.set(
        'Persisted outcome reviewed. Review the current inputs before another command.',
      );
    }
  }
  generate(kind: 'quotation' | 'letter' | 'tender'): void {
    const w = this.data();
    if (
      !w ||
      !w.quotationId ||
      !w.profileVersion ||
      !this.reviewed ||
      this.busy() ||
      this.uncertain()
    )
      return;
    const fence = this.fence;
    this.busy.set(true);
    this.message.set('Generating reviewed document…');
    this.write = this.http
      .post<unknown>('/api/ui/proposals/' + this.proposalId() + '/documents/' + kind, {
        quotationId: w.quotationId,
        profileVersion: w.profileVersion,
        reviewed: this.reviewed,
        teamCvs: this.teamCvs,
        timeline: this.timeline,
      })
      .pipe(timeout(30000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.reviewed = false;
          const result = value as { id?: unknown };
          if (typeof result?.id !== 'string' || !guidPattern.test(result.id)) {
            this.uncertain.set(true);
            this.data.set(null);
            this.message.set('Outcome unconfirmed. Refresh persisted artifacts.');
            return;
          }
          this.message.set(
            'Immutable document available. An existing document is reused for the same quotation.',
          );
          this.load();
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.reviewed = false;
          this.uncertain.set(!(failure.status >= 400 && failure.status < 500));
          this.data.set(null);
          this.message.set(
            this.uncertain()
              ? 'Outcome unconfirmed. Refresh and review persisted artifacts before another command.'
              : 'Generation refused. Refresh and resolve current quotation, profile, acceptance, Partner authority or signature/seal prerequisites.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.read?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.reviewed = false;
    if (!guidPattern.test(this.proposalId()) || !this.session.current()?.staff) return;
    const fence = this.fence;
    this.loading.set(true);
    this.read = this.http
      .get<unknown>('/api/ui/proposals/' + this.proposalId() + '/documents')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            const w = decodeDocuments(value);
            if (w.proposalId !== this.proposalId()) throw new Error('Wrong proposal');
            this.data.set(w);
          } catch {
            this.error.set('Unsupported document response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.loading.set(false);
          this.error.set(
            'Commercial documents unavailable. Current firm-wide commercial access is required.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
