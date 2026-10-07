import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { AdvancedConsolidation } from './advanced';

const scopeId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const firmId = '33333333-3333-4333-8333-333333333333';
const workspaceUrl = `/api/ui/consolidation/advanced/${scopeId}`;
const freshSchedule = {
  id: '44444444-4444-4444-8444-444444444444', status: 'SUBMITTED', createdByUserId: actorId,
  approvedByUserId: null, createdAt: '2026-10-07T10:00:00Z', approvedAt: null, inputDigest: 'a'.repeat(64),
};

function workspace(schedules: typeof freshSchedule[] = []) {
  return {
    scope: { id: scopeId, groupName: 'Synthetic group', version: 1, groupRevision: 1, method: 'ACQUISITION_NCI',
      reportingCurrency: 'QAR', status: 'APPROVED', approvedComponentCount: 0, approvedReviewedJournalCount: 0 },
    schedules, executions: [], canPrepare: true, canReview: false, suggestedSourceManifestJson: '{"sources":[]}',
  };
}

describe('advanced consolidation command recovery', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(() => {
    sessionStorage.clear();
    params = new BehaviorSubject(convertToParamMap({ id: scopeId }));
    TestBed.configureTestingModule({
      imports: [AdvancedConsolidation],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } }],
    });
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); sessionStorage.clear(); }
  });

  async function open(initial = workspace()) {
    const fixture = TestBed.createComponent(AdvancedConsolidation);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(workspaceUrl).flush(initial);
    await fixture.whenStable();
    TestBed.tick();
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

  it('blocks duplicate actions after an unknown response until persisted state is read and reviewed', async () => {
    const { fixture, component } = await open();
    const submit = component.submit(scopeId);
    const post = http.expectOne(r => r.method === 'POST' && r.url === `${workspaceUrl}/schedules`);
    post.flush({}, { status: 503, statusText: 'Unavailable' });
    await submit;
    TestBed.tick();

    expect(component.cmd.uncertain()).toBe(true);
    http.expectNone(r => r.method === 'POST' && r.url === `${workspaceUrl}/schedules`);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Command outcome needs review');
    expect(fixture.nativeElement.textContent).not.toContain(freshSchedule.id);
    const submitButton = () => Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent.includes('Submit schedule'));
    expect(submitButton()).toBeUndefined();

    const check = component.checkPersistedWorkspace();
    http.expectOne(r => r.method === 'GET' && r.url === workspaceUrl).flush(workspace([freshSchedule]));
    await check;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(freshSchedule.id);
    expect(fixture.nativeElement.textContent).toContain('Persisted workspace re-read: 1 schedule and 0 executions.');
    expect(submitButton()?.disabled).toBe(true);

    component.acknowledgeOutcome();
    fixture.detectChanges();
    expect(component.cmd.uncertain()).toBe(false);
    expect(submitButton()?.disabled).toBe(false);
    http.expectNone(r => r.method === 'POST' && r.url === `${workspaceUrl}/schedules`);
  });

  it('keeps the command fenced when persisted state cannot be read', async () => {
    const { fixture, component } = await open(workspace([freshSchedule]));
    expect(fixture.nativeElement.textContent).toContain(freshSchedule.id);
    const submit = component.submit(scopeId);
    http.expectOne(r => r.method === 'POST' && r.url === `${workspaceUrl}/schedules`)
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await submit;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain(freshSchedule.id);

    const check = component.checkPersistedWorkspace();
    http.expectOne(r => r.method === 'GET' && r.url === workspaceUrl)
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    await check;
    component.acknowledgeOutcome();
    fixture.detectChanges();

    expect(component.checkedWorkspace()).toBeNull();
    expect(component.cmd.uncertain()).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain(freshSchedule.id);
  });
});
