import { afterEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { ReleaseCandidate } from './records';

const id = '11111111-1111-4111-8111-111111111111';
const otherId = '22222222-2222-4222-8222-222222222222';
const candidate = {
  id, status: 'DRAFT', targetKind: 'WORKPAPER', targetRevision: 7, revision: 2,
  manifestDigest: 'a'.repeat(64), createdAt: '2026-10-16T10:00:00Z',
  checkpoint: { state: 'ABSENT', detail: 'Not observed' },
  attestation: { state: 'ABSENT', detail: 'Not observed' },
  lineage: { state: 'ABSENT', detail: 'Not observed' }, preflightReady: false,
};

describe('Release candidate read identity', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('refuses a different returned candidate instead of exposing its gates or issue action', () => {
    let decode!: (raw: unknown, path: string) => unknown;
    const api = {
      resource: (_url: unknown, decoder: typeof decode) => {
        decode = decoder;
        return { data: () => null, loading: () => false, error: () => '', reload: vi.fn() };
      },
    };
    TestBed.configureTestingModule({ providers: [
      { provide: Api, useValue: api },
      { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } },
    ] });
    const component = TestBed.runInInjectionContext(() => new ReleaseCandidate());
    expect(component.id()).toBe(id);
    expect(decode(candidate, '')).toEqual(candidate);
    expect(() => decode({ ...candidate, id: otherId }, '')).toThrow('Release candidate route identity mismatch');
  });
});
