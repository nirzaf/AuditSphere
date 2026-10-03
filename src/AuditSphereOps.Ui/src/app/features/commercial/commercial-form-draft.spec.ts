import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { SessionService } from '../../core/session';
import { CommercialFormDraft, textFields } from './commercial-form-draft';
import { quotationDraft } from './quotation';
const id = '11111111-1111-4111-8111-111111111111';
describe('commercial form draft lifecycle', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '0',
      staff: true,
    });
    sessionStorage.clear();
  });
  afterEach(() => TestBed.resetTestingModule());
  it('requires the current entity basis and excludes unknown fields', async () => {
    let fields = { note: '' };
    const h = TestBed.runInInjectionContext(
      () =>
        new CommercialFormDraft(
          () => fields,
          (v) => textFields(v, { note: 50 }),
        ),
    );
    await h.bind(`commercial-test:${id}`, { revision: '1' });
    fields = { note: 'Unsaved note' };
    expect(h.dirty()).toBe(true);
    expect(h.save()).toBe(true);
    fields = { note: '' };
    expect(h.recover()).toEqual({ note: 'Unsaved note' });
    await h.bind(`commercial-test:${id}`, { revision: '2' });
    expect(h.recover()).toBeNull();
    expect(textFields({ note: 'n', reviewed: true, requestId: id }, { note: 50 })).toEqual({
      note: 'n',
    });
  });
  it('does not recover fields for another identity or late context', async () => {
    let fields = { note: '' };
    const h = TestBed.runInInjectionContext(
      () =>
        new CommercialFormDraft(
          () => fields,
          (v) => textFields(v, { note: 50 }),
        ),
    );
    await h.bind(`commercial-test:${id}`, { revision: '1' });
    fields = { note: 'Private synthetic note' };
    expect(h.save()).toBe(true);
    TestBed.inject(SessionService).current.set({
      userId: '22222222-2222-4222-8222-222222222222',
      firmId: id,
      generation: '0',
      staff: true,
    });
    expect(h.recover()).toBeNull();
    const binding = h.bind(`commercial-test:${id}`, { revision: '1' });
    h.reset();
    await binding;
    expect(h.scope()).toBeNull();
  });
  it('clears confirmed draft storage and handles unavailable persistence', async () => {
    let fields = { note: '' };
    const h = TestBed.runInInjectionContext(
      () =>
        new CommercialFormDraft(
          () => fields,
          (v) => textFields(v, { note: 50 }),
        ),
    );
    await h.bind(`commercial-test:${id}`, { revision: '1' });
    fields = { note: 'Ready' };
    expect(h.save()).toBe(true);
    h.submitted();
    expect(h.dirty()).toBe(false);
    expect(h.recover()).toBeNull();
    fields = { note: 'x'.repeat(51) };
    expect(h.save()).toBe(false);
  });
  it('bounds quotation input and retains exact decimals without preview or assent', () => {
    const fields = {
      lines: [{ rateCardId: id, hours: '9007199254740993.01' }],
      complexity: '1',
      risk: '0',
      discount: '0',
      nonStandard: false,
      note: '',
      reviewed: true,
      preview: { fee: '1' },
    };
    const d = quotationDraft(fields);
    expect(d?.lines[0].hours).toBe(fields.lines[0].hours);
    expect(d).not.toHaveProperty('reviewed');
    expect(d).not.toHaveProperty('preview');
    expect(quotationDraft({ ...fields, lines: Array(101).fill(fields.lines[0]) })).toBeNull();
    expect(quotationDraft({ ...fields, note: 'x'.repeat(2001) })).toBeNull();
  });
});
