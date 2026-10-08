import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { ProjectProgress } from './progress';

const endpoint = '/api/ui/administration/project-progress';
const tasks = ['COMPLETED', 'IN_PROGRESS', 'IN_REVIEW', 'NOT_STARTED', 'REOPENED', 'BLOCKED'].map((status, i) => ({
  id: `T00${i + 1}`, title: `Synthetic task ${i + 1}`, status, workPackage: 'AUD-17', modules: [20],
  blockedReason: status === 'BLOCKED' ? 'Independent approval required' : '',
}));
const group = { number: 20, name: 'Audit foundation', tasks, completed: 1, completionPercent: 16, active: 2, blocked: 1, pending: 2 };
const publication = {
  snapshot: { tasks, modules: [group], auditTasks: tasks, sharedTasks: tasks, auditPhases: [group],
    publishedAtUtc: '2026-10-07T00:00:00Z', publicationAgeDays: 0, isStale: false, freshnessWindowDays: 30,
    auditCompleted: 1, sharedCompleted: 1, completed: 1, completionPercent: 16, active: 2, blocked: 1, pending: 2 },
  untracked: [{ name: 'Documents and client portal', route: null }],
};

describe('Project task progress parity and recovery', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ProjectProgress], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: '11111111-1111-4111-8111-111111111111', firmId: '22222222-2222-4222-8222-222222222222', generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  function loaded() {
    const fixture = TestBed.createComponent(ProjectProgress);
    fixture.detectChanges();
    http.expectOne(endpoint).flush(publication);
    fixture.detectChanges();
    return fixture;
  }

  it('retains blocked explanations in every list and full aggregate counts while filtering', () => {
    const fixture = loaded();
    const body: HTMLElement = fixture.nativeElement;
    expect(body.querySelectorAll('li small').length).toBe(4);
    for (const selector of ['#audit-workflow-heading', '#shared-foundation-heading']) {
      expect(body.querySelector(selector)?.parentElement?.textContent).toContain('1 / 6 completed (16%) · 2 active or in review · 2 pending · 1 blocked');
    }
    expect(body.querySelector('[aria-label="Documents and client portal: implementation progress not measured"]')).not.toBeNull();
    fixture.componentInstance.filter.set('Blocked'); fixture.detectChanges();
    expect(body.querySelectorAll('li').length).toBe(4);
    expect(body.querySelector('[aria-label="Overall task-card progress: 1 completed, 2 active, 2 pending, 1 blocked"]')).not.toBeNull();
    expect(body.textContent).toContain('Showing 1 of 6 distinct task cards');
  });

  it('clears old data on refresh and recovers from a transient error only on explicit retry', () => {
    const fixture = loaded();
    fixture.componentInstance.p.reload(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('audit-task-bar')).toBeNull();
    http.expectOne(endpoint).flush({}, { status: 503, statusText: 'Unavailable' }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Temporarily unavailable. Retry shortly.');
    http.expectNone(endpoint);
    const refresh: HTMLButtonElement = fixture.nativeElement.querySelector('button');
    refresh.click(); fixture.detectChanges();
    http.expectOne(endpoint).flush(publication); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('audit-task-bar')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Temporarily unavailable');
  });

  it('shows the stale publication warning and fails closed on a malformed replacement', () => {
    const fixture = loaded();
    fixture.componentInstance.p.reload();
    http.expectOne(endpoint).flush({ ...publication, snapshot: { ...publication.snapshot, isStale: true, publicationAgeDays: 31 } });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Stale task-card snapshot"]')?.textContent).toContain('Snapshot is stale.');
    fixture.componentInstance.p.reload();
    http.expectOne(endpoint).flush({ snapshot: { tasks: [] } }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unsupported response.');
    expect(fixture.nativeElement.querySelector('audit-task-bar')).toBeNull();
  });

  it('removes protected progress when the session is invalidated and cancels an older read', () => {
    const fixture = loaded();
    fixture.componentInstance.p.reload(); fixture.detectChanges();
    const pending = http.expectOne(endpoint);
    TestBed.inject(SessionService).clear(); fixture.detectChanges();
    expect(pending.cancelled).toBe(true);
    expect(fixture.nativeElement.querySelector('audit-task-bar')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic task');
  });
});
