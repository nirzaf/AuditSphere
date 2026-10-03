import { describe, expect, it } from 'vitest';
import { decodeChecklist } from './checklist';
const context = {
  clientId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  generation: '9007199254740993',
  path: 'NEW_CLIENT',
  currentDecision: null,
  priorDecision: null,
  ready: false,
  canEdit: true,
  canReview: false,
  canDecide: false,
  canStartContinuance: false,
  questions: [],
  clearances: [],
  blockers: [{ kind: 'no-questions', message: 'No bank loaded', questionCode: null }],
};
describe('Acceptance workspace contract', () => {
  it('preserves exact generations and explicit blockers', () => {
    expect(decodeChecklist(context).generation).toBe('9007199254740993');
    expect(decodeChecklist(context).ready).toBe(false);
  });
  it('rejects numeric generations and malformed evidence', () => {
    expect(() => decodeChecklist({ ...context, generation: 1 })).toThrow();
    expect(() =>
      decodeChecklist({
        ...context,
        questions: [
          {
            code: 'CE-001',
            section: 'A',
            prompt: 'Question',
            category: 'Identity',
            answerType: 'BOOLEAN',
            requiresEvidence: true,
            adverse: false,
            revision: '0',
            answer: 'Yes',
            evidence: {},
            priorAnswer: null,
            answeredBy: null,
          },
        ],
      }),
    ).toThrow();
  });
});

describe('Assessment context bounds', () => {
  it.each(['0', '01', '9223372036854775808', '9999999999999999999'])(
    'refuses unsafe generation %s',
    (generation) => {
      expect(() => decodeChecklist({ ...context, generation })).toThrow();
    },
  );
  it('refuses unsupported professional decisions', () => {
    expect(() =>
      decodeChecklist({ ...context, currentDecision: 'AutomaticallyAccepted' }),
    ).toThrow();
  });
});
