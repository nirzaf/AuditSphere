import { Injectable, inject } from '@angular/core';
import { Api } from '../../core/api';
import { NativeSource } from './native-source-contracts';
import {
  NativeLedgerIntent,
  NativeLedgerSource,
  decodeNativeLedgerArtifact,
  decodeNativeLedgerPreview,
} from './native-ledger-source-contracts';

export const nativeLedgerBase = (clientId: string) =>
  `/api/ui/accounting/clients/${clientId}/native-gl-sources`;
@Injectable({ providedIn: 'root' })
export class NativeLedgerSourceApi {
  private readonly api = inject(Api);
  async preview(intent: NativeLedgerIntent, tb: NativeSource) {
    const result = await this.api.command(nativeLedgerBase(intent.clientId) + '/preview', {
      ...intent,
      reviewBasis: null,
      periodLedgerReviewed: true,
      reviewed: false,
    });
    if (!result.ok) throw new Error(result.message);
    return decodeNativeLedgerPreview(result.value, tb);
  }
  seal(intent: NativeLedgerIntent) {
    return this.api.command(nativeLedgerBase(intent.clientId) + '/seal', {
      ...intent,
      periodLedgerReviewed: true,
      reviewed: true,
    });
  }
  async artifact(source: NativeLedgerSource) {
    const a = await this.api.get(
      nativeLedgerBase(source.clientId) + '/' + source.importBatchId + '/artifact',
      (raw) => decodeNativeLedgerArtifact(raw, source),
    );
    const hash = async (text: string) =>
      Array.from(
        new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))),
      )
        .map((b) => b.toString(16).padStart(2, '0'))
        .join('');
    if (
      (await hash(a.generatedRowsJson)) !== a.generatedRowsHash ||
      (await hash(a.manifestJson)) !== a.manifestHash
    )
      throw new Error('Retained native ledger bytes do not match their hashes. No file was saved.');
    return a;
  }
}
