import { Component, input, output } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EngagementReminders } from './engagement-reminders';
import { EngagementWorkflow } from './engagement-workflow';
import { WORKFLOW_PHASES } from './engagement-workflow-contracts';
import { WorkflowTaskCreate } from './workflow-task-create';
import { SessionService } from '../../core/session';

const engagementId = '11111111-1111-4111-8111-111111111111';

@Component({ selector: 'audit-engagement-reminders', template: '' })
class ReminderPanelStub {
  readonly engagementId = input.required<string>();
}

@Component({ selector: 'audit-workflow-task-create', template: '' })
class TaskCreateStub {
  readonly engagementId = input.required<string>();
  readonly phases = input.required<readonly unknown[]>();
  readonly created = output<void>();
}

function workflow() {
  const phases = WORKFLOW_PHASES.map((phase) => ({
    ...phase,
    status: phase.index === 1 ? 'BLOCKED' : 'NOT_STARTED',
    operationalDueDate: phase.index === 3 ? '2026-12-15' : null,
  }));
  return {
    engagementId,
    clientId: '22222222-2222-4222-8222-222222222222',
    serviceRoute: 'Audit',
    canonicalStage: 'FIELDWORK_ACTIVE',
    canonicalIndex: 6,
    canonicalLabel: 'Fieldwork active',
    currentPhase: phases[0],
    phases,
    responsibleRole: 'AuditManager',
    responsibleUsers: ['Synthetic Manager'],
    blockers: ['Synthetic review gate remains open.'],
    nextActions: [],
    archive: null,
    evaluatedAtUtc: '2026-10-18T08:00:00Z',
  };
}

const savedPlan = {
  id: '33333333-3333-4333-8333-333333333333',
  engagementId,
  periodEnd: '2026-12-31',
  fieldworkStartDate: '2027-01-07',
  draftReportDate: '2027-03-13',
  finalReportDate: '2027-04-15',
  statutoryFilingCutoff: '2027-04-30',
  archiveDeadlineDate: '2027-06-14',
  adjustmentReason: null,
  warningOverrideReason: null,
  warnings: [],
  revision: 1,
  scheduledByUserId: '44444444-4444-4444-8444-444444444444',
  scheduledAt: '2026-10-18T08:00:00Z',
  canConfigure: false,
};

describe('Engagement workflow saved milestone display', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EngagementWorkflow],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.overrideComponent(EngagementWorkflow, {
      remove: { imports: [EngagementReminders, WorkflowTaskCreate] },
      add: { imports: [ReminderPanelStub, TaskCreateStub] },
    });
    TestBed.inject(SessionService).current.set({
      userId: engagementId,
      firmId: engagementId,
      generation: '1',
      staff: true,
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  function open(milestoneResponse: unknown, requestError?: { status: number; statusText: string }) {
    const fixture = TestBed.createComponent(EngagementWorkflow);
    fixture.componentRef.setInput('engagementId', engagementId);
    fixture.detectChanges();
    TestBed.tick();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`/api/ui/engagements/${engagementId}/workflow`).flush(workflow());
    http.expectOne(`/api/ui/engagements/${engagementId}/workflow/tasks`).flush({
      engagementId, tasks: [], truncated: false,
    });
    const milestoneRequest = http.expectOne(`/api/ui/engagements/${engagementId}/milestones`);
    if (requestError) {
      milestoneRequest.flush({ message: 'Access denied.' }, {
        status: requestError.status,
        statusText: requestError.statusText,
      });
    } else {
      milestoneRequest.flush(milestoneResponse as object | null);
    }
    http.expectOne(`/api/ui/engagements/${engagementId}/workflow/tasks/assignees`).flush([]);
    TestBed.tick();
    fixture.detectChanges();
    return fixture;
  }

  it('maps saved dates to their workflow phases and keeps task deadlines distinct', () => {
    const fixture = open(savedPlan);
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Phase 3 · Audit Fieldwork · Fieldwork starts');
    expect(text).toContain('Phase 6 · Report and Delivery · Draft report');
    expect(text).toContain('Phase 6 · Report and Delivery · Final signed report');
    expect(text).toContain('Phase 6 · Report and Delivery · Statutory filing cutoff');
    expect(text).toContain('Phase 7 · Archive and Close · Archive deadline');
    expect(text).toContain('2027-01-07');
    expect(text).toContain('2027-03-13');
    expect(text).toContain('2027-04-15');
    expect(text).toContain('2027-04-30');
    expect(text).toContain('2027-06-14');
    expect(text).toContain('Next task due: 2026-12-15');
    expect(text).toContain('not independently validated against jurisdictional law');
    expect(text).toContain('Blocked');

    TestBed.inject(SessionService).clear();
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('2027-01-07');
  });

  it('shows an explicit empty schedule only when the authorized endpoint returns null', () => {
    const fixture = open(null);
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('No saved milestone schedule is recorded.');
    expect(text).toContain('Next task due: 2026-12-15');
    expect(text).not.toContain('2027-04-30');
  });

  it('does not misreport a role-denied schedule read as an absent schedule', () => {
    const fixture = open(null, { status: 403, statusText: 'Forbidden' });
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('The saved milestone schedule is unavailable to your role');
    expect(text).not.toContain('No saved milestone schedule is recorded.');
  });

  it('announces workflow loading through a persistent status region and clears it after the response', () => {
    const fixture = TestBed.createComponent(EngagementWorkflow);
    fixture.componentRef.setInput('engagementId', engagementId);
    fixture.detectChanges();
    TestBed.tick();

    const http = TestBed.inject(HttpTestingController);
    const workflowRequest = http.expectOne(`/api/ui/engagements/${engagementId}/workflow`);
    const status = fixture.nativeElement.querySelector('[role="status"].workflow-loading-status') as HTMLElement;
    expect(status).toBeTruthy();
    expect(status.getAttribute('aria-live')).toBe('polite');
    expect(status.getAttribute('aria-atomic')).toBe('true');
    expect(status.textContent).toContain('Evaluating engagement progression...');

    workflowRequest.flush(workflow());
    fixture.detectChanges();
    expect(status.textContent?.trim()).toBe('');

    http.expectOne(`/api/ui/engagements/${engagementId}/workflow/tasks`).flush({
      engagementId, tasks: [], truncated: false,
    });
    http.expectOne(`/api/ui/engagements/${engagementId}/milestones`).flush(null);
    http.expectOne(`/api/ui/engagements/${engagementId}/workflow/tasks/assignees`).flush([]);
    TestBed.tick();
    fixture.detectChanges();
    fixture.destroy();
  });
});
