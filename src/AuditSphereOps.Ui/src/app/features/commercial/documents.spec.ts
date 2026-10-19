import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { CommercialDocuments, decodeDocuments } from './documents';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const artifact = {
  id,
  quotationId: id,
  kind: 'QUOTATION',
  templateVersion: 'v1',
  profileVersion: '1',
  fileName: 'quotation.docx',
  contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  sha256: 'a'.repeat(64),
  createdAt: '2026-10-02T00:00:00Z',
};
const workspace = {
  proposalId: id,
  quotationId: id,
  profileVersion: '2',
  firmName: 'Firm',
  history: 'Reviewed history',
  credentials: 'Reviewed credentials',
  methodology: 'Reviewed methodology',
  commonBlockers: [],
  letterBlockers: ['Record client acceptance.'],
  tenderBlockers: [],
  documents: [artifact],
};
describe('Commercial document contract', () => {
  it('keeps historical profile and immutable artifact identity', () => {
    const result = decodeDocuments(workspace);
    expect(result.profileVersion).toBe('2');
    expect(result.documents[0].profileVersion).toBe('1');
    expect(result.letterBlockers).toHaveLength(1);
  });
  it('rejects forged IDs, incomplete hashes and numeric revisions', () => {
    for (const change of [{ id: 'guessed' }, { sha256: 'a'.repeat(12) }, { profileVersion: 1 }])
      expect(() =>
        decodeDocuments({ ...workspace, documents: [{ ...artifact, ...change }] }),
      ).toThrow();
    expect(() => decodeDocuments({ ...workspace, profileVersion: 2 })).toThrow();
  });
  it('bounds artifact history and blocker messages', () => {
    expect(() => decodeDocuments({ ...workspace, documents: Array(101).fill(artifact) })).toThrow();
    expect(() =>
      decodeDocuments({ ...workspace, commonBlockers: Array(21).fill('Blocked') }),
    ).toThrow();
  });
});

describe('Commercial document command focus recovery', () => {
  let http: HttpTestingController;
  const workspaceUrl = '/api/ui/proposals/' + id + '/documents';
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CommercialDocuments],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  it('focuses tender refusal status for retry and preserves later body interaction', () => {
    const fixture = TestBed.createComponent(CommercialDocuments);
    fixture.componentRef.setInput('proposalId', id);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(workspaceUrl).flush(workspace);
    fixture.detectChanges();
    TestBed.tick();

    const startTender = () => {
      const textareas = fixture.nativeElement.querySelectorAll('textarea') as NodeListOf<HTMLTextAreaElement>;
      textareas[0].value = 'Reviewed synthetic team';
      textareas[0].dispatchEvent(new Event('input', { bubbles: true }));
      textareas[1].value = 'Reviewed synthetic timeline';
      textareas[1].dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
      const assent = fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
      assent.click();
      fixture.detectChanges();
      TestBed.tick();
      const button = [...fixture.nativeElement.querySelectorAll('button')].find(
        (element) => element.textContent.trim() === 'Generate comprehensive proposal',
      ) as HTMLButtonElement;
      expect(fixture.componentInstance.data()).not.toBeNull();
      expect(button.disabled).toBe(false);
      button.focus();
      button.click();
      return { button, request: http.expectOne(workspaceUrl + '/tender') };
    };

    const first = startTender();
    expect(document.activeElement).toBe(first.button);
    first.request.flush(
      { code: 'revision.stale' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    TestBed.tick();

    const status = fixture.nativeElement.querySelector('[role="status"]') as HTMLElement;
    expect(first.button.isConnected).toBe(false);
    expect(status.textContent).toContain(
      'Changed or unverifiable tender inputs require a newly approved quotation revision.',
    );
    expect(document.activeElement).toBe(status);

    const refresh = [...fixture.nativeElement.querySelectorAll('button')].find(
      (element) => element.textContent.trim() === 'Refresh document state',
    ) as HTMLButtonElement;
    refresh.focus();
    refresh.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    refresh.click();
    http.expectOne(workspaceUrl).flush(workspace);
    fixture.detectChanges();
    TestBed.tick();

    const second = startTender();
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    second.request.flush(
      { code: 'revision.stale' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    TestBed.tick();

    expect(second.button.isConnected).toBe(false);
    expect(document.activeElement).toBe(document.body);
    fixture.destroy();
  });
});
