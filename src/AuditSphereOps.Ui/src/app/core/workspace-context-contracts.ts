import { arr, bool, decode, guid, nat, nullable, obj, str } from './decode';

const choiceDecoder = obj({ engagementId: guid, serviceRoute: str(200), periodStart: str(100), periodEnd: str(100), status: str(200) });
const pageDecoder = obj({ clientId: guid, clientName: str(2000), canOpenClient: bool, pageSize: nat,
  items: arr(choiceDecoder, 100), hasMore: bool, nextCursor: nullable(str(4096)), validForMs: nat });

export type WorkspaceContextChoicesPage = ReturnType<typeof pageDecoder>;

export function decodeWorkspaceContextChoices(value: unknown, clientId: string, pageSize: number): WorkspaceContextChoicesPage {
  if (!value || typeof value !== 'object' || !Object.prototype.hasOwnProperty.call(value, 'nextCursor'))
    throw new Error('Invalid context choices');
  const page = decode(pageDecoder, value);
  const ids = page.items.map(item => item.engagementId.toLowerCase());
  if (page.clientId === '00000000-0000-0000-0000-000000000000' || ids.includes('00000000-0000-0000-0000-000000000000') ||
      page.clientId.toLowerCase() !== clientId.toLowerCase() || page.pageSize !== pageSize ||
      pageSize < 1 || pageSize > 100 || page.items.length > pageSize ||
      page.validForMs < 1 || page.validForMs > 30000 || new Set(ids).size !== ids.length ||
      (page.hasMore ? !page.items.length || !page.nextCursor || !/^pc1\.[A-Za-z0-9_-]+$/.test(page.nextCursor)
        : page.nextCursor !== null)) throw new Error('Invalid context choices');
  return page;
}
