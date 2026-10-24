import { Injectable, inject } from '@angular/core';
import { Api } from '../../core/api';
import { NativePeriod, NativeSource, NativeSourceIntent, decodeNativeArtifact, decodeNativeEngagement,
  decodeNativePreview, decodeNativeSource } from './native-source-contracts';

export const nativeSourceBase = (clientId: string) => `/api/ui/accounting/clients/${clientId}/native-tb-sources`;
@Injectable({ providedIn: 'root' })
export class NativeSourceApi {
  private readonly api = inject(Api);
  async preview(intent: NativeSourceIntent, period: NativePeriod) {
    const scope = await this.api.get(`/api/ui/engagements/${intent.engagementId}/tb-intake`, decodeNativeEngagement);
    if (scope.clientId !== intent.clientId || scope.engagementId !== intent.engagementId)
      throw new Error('The engagement does not belong to this client.');
    const result = await this.api.command(nativeSourceBase(intent.clientId) + '/preview',
      { ...intent, reviewBasis: null, periodLedgerReviewed: true, reviewed: false });
    if (!result.ok) throw new Error(result.message);
    return decodeNativePreview(result.value, period, intent);
  }
  seal(intent: NativeSourceIntent) {
    return this.api.command(nativeSourceBase(intent.clientId) + '/seal',
      { ...intent, periodLedgerReviewed: true, reviewed: true });
  }
  view(clientId: string, datasetId: string) {
    return this.api.get(nativeSourceBase(clientId) + '/' + datasetId,
      raw => decodeNativeSource(raw, clientId, datasetId));
  }
  async artifact(source: NativeSource) {
    const value = await this.api.get(nativeSourceBase(source.clientId) + '/' + source.datasetId + '/artifact',
      raw => decodeNativeArtifact(raw, source));
    const hash = async (text: string) => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
      new TextEncoder().encode(text)))).map(b => b.toString(16).padStart(2, '0')).join('');
    if (await hash(value.generatedRowsJson) !== value.generatedRowsHash || await hash(value.manifestJson) !== value.manifestHash)
      throw new Error('Generated artifact bytes do not match their retained hashes. No file was saved.');
    return value;
  }
}
