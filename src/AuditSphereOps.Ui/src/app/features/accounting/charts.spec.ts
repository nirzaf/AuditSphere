import { describe, expect, it, vi } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import {
  decodeCharts, decodeAccounts, decodePublication, decodeAliases,
  decodeDimensionDraftReceipt, decodeDimensionPublication, decodeResolvedDimensionRevision, AccountingCharts,
} from './charts';
describe('Chart contracts', () => {
  it('requires exact publication snapshot and explicit authority', () => {
    const value = { chartId: '11111111-1111-4111-8111-111111111111', version: '1', digest: 'f'.repeat(64), accountCount: 1, canPublish: false };
    expect(decodePublication(value).canPublish).toBe(false);
    expect(() => decodePublication({ ...value, digest: 'bad' })).toThrow();
    expect(() => decodePublication({ ...value, version: 1 })).toThrow();
  });
  it('keeps empty revisions and account pages explicit', () => {
    const charts = decodeCharts({ clientId: '11111111-1111-4111-8111-111111111111', items: [], hasMore: false,
      canSaveDimensionSchema: true, canPublishDimensionSchema: false });
    expect(charts.items).toEqual([]);
    expect(charts.canSaveDimensionSchema).toBe(true);
    expect(charts.canPublishDimensionSchema).toBe(false);
    expect(decodeAccounts({ items: [], totalCount: 0, page: 1, pageSize: 50 }).totalCount).toBe(0);
    expect(decodeAliases([])).toEqual([]);
  });
  it('decodes valid source account aliases', () => {
    const aliases = decodeAliases([{
      id: '11111111-1111-4111-8111-111111111111',
      clientAccountId: '22222222-2222-4222-8222-222222222222',
      accountCode: '1000',
      sourceSystem: 'XERO',
      aliasCode: '100-BANK',
      aliasName: 'Operating Account',
      createdAt: '2026-10-01T00:00:00Z'
    }]);
    expect(aliases.length).toBe(1);
    expect(aliases[0].aliasCode).toBe('100-BANK');
    expect(aliases[0].sourceSystem).toBe('XERO');
  });
  it('rejects invalid identity and unbounded pages', () => {
    expect(() => decodeCharts({ clientId: 'bad', items: [], hasMore: false })).toThrow();
    expect(() => decodeCharts({ clientId: '11111111-1111-4111-8111-111111111111', items: [], hasMore: false,
      canSaveDimensionSchema: true, canPublishDimensionSchema: 'yes' })).toThrow();
    expect(() => decodeAccounts({ items: [], totalCount: 0, page: 10001, pageSize: 50 })).toThrow();
    expect(() => decodeAccounts({ items: [], totalCount: 0, page: 1, pageSize: 501 })).toThrow();
    expect(() => decodeAliases([{ id: 'bad', clientAccountId: '22222222-2222-4222-8222-222222222222', accountCode: '1000', sourceSystem: 'XERO', aliasCode: '100', aliasName: '', createdAt: '' }])).toThrow();
    const boundedAlias = { id: '11111111-1111-4111-8111-111111111111', clientAccountId: '22222222-2222-4222-8222-222222222222', accountCode: '1000', sourceSystem: 'XERO', aliasCode: '100', aliasName: '', createdAt: '2026-10-01T00:00:00Z' };
    expect(() => decodeAliases(Array.from({ length: 1002 }, () => boundedAlias))).toThrow();
  });

  it('decodes exact dimension draft, publication and resolution identities', () => {
    const schemaId = '11111111-1111-4111-8111-111111111111';
    const revisionId = '22222222-2222-4222-8222-222222222222';
    const digest = 'a'.repeat(64);
    const draft = decodeDimensionDraftReceipt({ draftRevisionRef: revisionId, schemaId, revision: 1,
      canonicalDigestVersion: 1, canonicalDigest: digest,
      definitions: [{ dimensionType: 'DEPARTMENT', code: 'OPS', name: 'Operations', required: false, values: [] }] });
    expect(draft.schemaId).toBe(schemaId);
    expect(() => decodeDimensionDraftReceipt({ ...draft, canonicalDigest: 'invalid' })).toThrow();
    expect(() => decodeDimensionDraftReceipt({ ...draft, definitions: [{ ...draft.definitions[0], values: [{
      code: 'A', name: 'A', effectiveFrom: '2026-99-99', effectiveTo: null, active: true,
    }] }] })).toThrow();

    const publication = decodeDimensionPublication({ schemaRevisionRef: revisionId, schemaId, revision: 1,
      canonicalDigestVersion: 1, canonicalDigest: digest, status: 'PUBLISHED' });
    expect(publication.schemaRevisionRef).toBe(revisionId);
    expect(() => decodeDimensionPublication({ ...publication, schemaRevisionRef: 'bad' })).toThrow();

    const resolved = decodeResolvedDimensionRevision({ firmId: '33333333-3333-4333-8333-333333333333',
      clientId: '44444444-4444-4444-8444-444444444444', reference: { schemaId, revisionId, revision: 1,
        canonicalDigestVersion: 1, canonicalDigest: digest } });
    expect(resolved.reference.canonicalDigest).toBe(digest);
    expect(() => decodeResolvedDimensionRevision({ ...resolved, clientId: 'bad' })).toThrow();
  });

  it('saves an authorized draft once, withholds publication for preparers, then resolves a reviewer publication', async () => {
    const clientId = '11111111-1111-4111-8111-111111111111';
    const schemaId = '22222222-2222-4222-8222-222222222222';
    const revisionId = '33333333-3333-4333-8333-333333333333';
    const digest = 'b'.repeat(64);
    const definitions = [{ dimensionType: 'DEPARTMENT', code: 'OPS', name: 'Operations', required: false, values: [] }];
    const draft = { draftRevisionRef: revisionId, schemaId, revision: 1, canonicalDigestVersion: 1, canonicalDigest: digest, definitions };
    const publication = { schemaRevisionRef: revisionId, schemaId, revision: 1, canonicalDigestVersion: 1, canonicalDigest: digest, status: 'PUBLISHED' };
    const resolved = { firmId: '44444444-4444-4444-8444-444444444444', clientId,
      reference: { schemaId, revisionId, revision: 1, canonicalDigestVersion: 1, canonicalDigest: digest } };
    const command = vi.fn()
      .mockResolvedValueOnce({ ok: true, value: draft })
      .mockResolvedValueOnce({ ok: true, value: publication });
    const get = vi.fn().mockResolvedValue(resolved);
    TestBed.configureTestingModule({
      imports: [AccountingCharts],
      providers: [provideHttpClient(), { provide: SessionService, useValue: {
        current: signal(null), invalidation: signal(0), refresh: vi.fn(),
      } }, { provide: Api, useValue: { command, get } }],
    });
    const fixture = TestBed.createComponent(AccountingCharts);
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.data.set(decodeCharts({ clientId, items: [], hasMore: false,
      canSaveDimensionSchema: true, canPublishDimensionSchema: false }));
    component.dimensionDefinitionsJson = JSON.stringify(definitions);
    await component.saveDimensionDraft();
    expect(command).toHaveBeenCalledTimes(1);
    expect(command).toHaveBeenCalledWith(`/api/ui/clients/${clientId}/dimension-schemas/drafts`, {
      baseRevisionRef: null, definitions,
    });
    component.dimensionReviewed = true;
    component.dimensionImpactAcknowledged = true;
    await component.publishDimensionDraft();
    expect(command).toHaveBeenCalledTimes(1);

    component.data.set(decodeCharts({ clientId, items: [], hasMore: false,
      canSaveDimensionSchema: true, canPublishDimensionSchema: true }));
    component.dimensionReviewed = true;
    component.dimensionImpactAcknowledged = true;
    await component.publishDimensionDraft();
    expect(command).toHaveBeenCalledTimes(2);
    expect(command.mock.calls[1][0]).toBe(`/api/ui/clients/${clientId}/dimension-schemas/${schemaId}/publish`);
    expect(command.mock.calls[1][1]).toMatchObject({ impactAcknowledged: true, expectedCanonicalDigest: digest });
    expect(get).toHaveBeenCalledOnce();
    expect(component.dimensionResolved()?.reference.revisionId).toBe(revisionId);
    fixture.destroy();
    TestBed.resetTestingModule();
  });
});
