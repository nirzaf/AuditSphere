import {
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import {
  FormField,
  FormRoot,
  disabled,
  form,
  maxLength,
  required,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import {
  AssessmentCommandFields,
  assessmentFields,
  decodeAssessmentFields,
} from './assessment-command-contracts';

type Model = {
  answer: string;
  evidence: string;
  area: string;
  specialist: string;
  status: string;
  conditions: string;
  serviceRoute: string;
  decision: string;
  rationale: string;
};
const empty = (): Model => ({
  answer: '',
  evidence: '',
  area: '',
  specialist: '',
  status: 'HOLD',
  conditions: '',
  serviceRoute: 'FinancialStatementAudit',
  decision: 'Accepted',
  rationale: '',
});
const labels: Record<keyof Model, string> = {
  answer: 'Answer',
  evidence: 'Evidence reference',
  area: 'Review area',
  specialist: 'Specialist',
  status: 'Review result',
  conditions: 'Conditions',
  serviceRoute: 'Service route',
  decision: 'Decision',
  rationale: 'Rationale',
};
@Component({
  selector: 'audit-assessment-command-editor',
  imports: [FormField, FormRoot, MatButtonModule],
  templateUrl: './assessment-command-editor.html',
  styleUrl: './assessment-command-editor.scss',
})
export class AssessmentCommandEditor {
  readonly seed = input.required<AssessmentCommandFields>();
  readonly label = input.required<string>();
  readonly editorKey = input.required<string>();
  readonly locked = input(false);
  readonly booleanAnswer = input(false);
  readonly requiresEvidence = input(false);
  readonly requested = output<AssessmentCommandFields>();
  readonly dirtyChange = output<boolean>();
  readonly model = signal<Model>(empty());
  readonly baseline = signal(JSON.stringify(empty()));
  readonly attempted = signal(false);
  readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly evidenceRequired = computed(() =>
    this.seed().kind === 'ANSWER' ? this.requiresEvidence() : this.model().status === 'CLEARED',
  );
  readonly visibleKeys = computed<(keyof Model)[]>(() => {
    switch (this.seed().kind) {
      case 'ANSWER':
        return ['answer', 'evidence'];
      case 'REQUEST_REVIEW':
        return ['area', 'specialist'];
      case 'RECORD_REVIEW':
        return ['status', 'evidence', 'conditions'];
      case 'DECISION':
        return ['serviceRoute', 'decision', 'rationale', 'conditions'];
      default:
        return [];
    }
  });
  readonly fields = form(
    this.model,
    (p) => {
      disabled(p, () => this.locked());
      for (const [key, max] of Object.entries({
        answer: 2000,
        evidence: 500,
        area: 100,
        specialist: 200,
        status: 20,
        conditions: 2000,
        serviceRoute: 100,
        decision: 40,
        rationale: 2000,
      }) as [keyof Model, number][]) {
        maxLength(p[key], max);
        validate(p[key], ({ value }) =>
          /[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/.test(value())
            ? { kind: 'controlCharacters' }
            : undefined,
        );
      }
      required(p.answer, { when: () => this.seed().kind === 'ANSWER' });
      validate(p.answer, ({ value }) =>
        this.seed().kind === 'ANSWER' &&
        (!value().trim() || (this.booleanAnswer() && !['Yes', 'No'].includes(value())))
          ? { kind: 'answerRequired' }
          : undefined,
      );
      required(p.evidence, {
        when: () =>
          ['ANSWER', 'RECORD_REVIEW'].includes(this.seed().kind) && this.evidenceRequired(),
      });
      validate(p.evidence, ({ value }) =>
        ['ANSWER', 'RECORD_REVIEW'].includes(this.seed().kind) &&
        this.evidenceRequired() &&
        !value().trim()
          ? { kind: 'evidenceRequired' }
          : undefined,
      );
      for (const key of ['area', 'specialist'] as const)
        validate(p[key], ({ value }) =>
          this.seed().kind === 'REQUEST_REVIEW' && value().trim().length < 2
            ? { kind: 'reviewRequired' }
            : undefined,
        );
      validate(p.status, ({ value }) =>
        this.seed().kind === 'RECORD_REVIEW' && !['CLEARED', 'HOLD', 'CONDITIONS'].includes(value())
          ? { kind: 'resultRequired' }
          : undefined,
      );
      for (const key of ['serviceRoute', 'rationale'] as const)
        validate(p[key], ({ value }) =>
          this.seed().kind === 'DECISION' && !value().trim()
            ? { kind: 'decisionRequired' }
            : undefined,
        );
      validate(p.decision, ({ value }) =>
        this.seed().kind === 'DECISION' &&
        !['Accepted', 'AcceptedWithConditions', 'Declined', 'Deferred'].includes(value())
          ? { kind: 'decisionRequired' }
          : undefined,
      );
      validate(p.conditions, ({ value }) =>
        this.seed().kind === 'DECISION' && this.model().decision === 'Accepted' && !!value().trim()
          ? { kind: 'unconditional' }
          : ((this.seed().kind === 'DECISION' &&
                this.model().decision === 'AcceptedWithConditions') ||
                (this.seed().kind === 'RECORD_REVIEW' && this.model().status === 'CONDITIONS')) &&
              !value().trim()
            ? { kind: 'conditionsRequired' }
            : undefined,
      );
    },
    {
      submission: {
        ignoreValidators: 'none',
        action: async () => {
          if (this.locked() || !this.valid()) return;
          this.requested.emit(this.intent());
        },
        onInvalid: () => {
          this.attempted.set(true);
          setTimeout(() => this.summary()?.nativeElement.focus());
        },
      },
    },
  );
  readonly valid = computed(() => {
    if (!this.fields().valid() || this.fields().pending()) return false;
    try {
      this.intent();
      return true;
    } catch {
      return false;
    }
  });
  constructor() {
    effect(() => {
      const s = this.seed();
      untracked(() => {
        const model = {
          ...empty(),
          answer: s.answer ?? '',
          evidence: s.evidence ?? '',
          area: s.area ?? '',
          specialist: s.specialist ?? '',
          conditions: s.conditions ?? '',
        };
        this.model.set(model);
        this.baseline.set(JSON.stringify(model));
        this.attempted.set(false);
        this.fields().reset();
      });
    });
    effect(() => {
      const dirty = JSON.stringify(this.model()) !== this.baseline();
      untracked(() => this.dirtyChange.emit(dirty));
    });
  }
  intent(): AssessmentCommandFields {
    const s = this.seed(),
      m = this.model(),
      f = assessmentFields(s.kind, s.generation),
      opt = (s: string) => s.trim() || null;
    switch (s.kind) {
      case 'ANSWER':
        Object.assign(f, {
          questionCode: s.questionCode,
          revision: s.revision,
          answer: opt(m.answer),
          evidence: opt(m.evidence),
        });
        break;
      case 'REQUEST_REVIEW':
        Object.assign(f, { area: opt(m.area), specialist: opt(m.specialist) });
        break;
      case 'RECORD_REVIEW':
        Object.assign(f, {
          reviewId: s.reviewId,
          expectedStatus: s.expectedStatus,
          status: m.status,
          evidence: opt(m.evidence),
          conditions: opt(m.conditions),
        });
        break;
      case 'DECISION':
        Object.assign(f, {
          serviceRoute: opt(m.serviceRoute),
          decision: m.decision,
          rationale: opt(m.rationale),
          conditions: opt(m.conditions),
        });
        break;
    }
    return decodeAssessmentFields(f);
  }
  fieldId(key: string) {
    return 'assessment-' + this.editorKey() + '-' + key;
  }
  focusField(e: Event, key: keyof Model) {
    e.preventDefault();
    document.getElementById(this.fieldId(key))?.focus();
  }
  errorFor(key: keyof Model) {
    const limits: Partial<Record<keyof Model, number>> = {
      answer: 2000,
      evidence: 500,
      area: 100,
      specialist: 200,
      conditions: 2000,
      serviceRoute: 100,
      rationale: 2000,
    };
    if (limits[key] && this.model()[key].length > limits[key]!)
      return `${labels[key]} must contain no more than ${limits[key]} characters.`;
    if (
      key === 'conditions' &&
      this.seed().kind === 'DECISION' &&
      this.model().decision === 'Accepted'
    )
      return 'Unconditional acceptance cannot include blocking conditions.';
    return `Enter a valid ${labels[key].toLowerCase()}${limits[key] ? ' within ' + limits[key] + ' characters' : ''}.`;
  }
}
