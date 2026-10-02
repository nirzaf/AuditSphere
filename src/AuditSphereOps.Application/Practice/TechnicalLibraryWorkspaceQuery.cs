using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Practice;

public sealed record LibraryCatalogueItem(Guid Id, string Code, string Title, string Category, string Audience, int? PublishedVersion, bool HasDraft);
public sealed record LibraryVersionView(Guid Id, int Version, string Status, DateOnly EffectiveFrom, string SourceReference, string ContentSha256, string? Body);
public sealed record LibraryEntryView(Guid Id, string Code, string Title, string Category, LibraryVersionView Version, IReadOnlyList<LibraryVersionView> History);

/// <summary>Browser projection of the technical library; reuses the service's audience and curator checks.</summary>
public static class TechnicalLibraryWorkspaceQuery
{
  public static async Task<CommandResult<IReadOnlyList<LibraryCatalogueItem>>> CatalogueAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default) =>
    CommandResult<IReadOnlyList<LibraryCatalogueItem>>.Ok((await TechnicalLibraryService.ListAsync(db, actor, ct))
      .Select(x => new LibraryCatalogueItem(x.Document.Id, x.Document.Code, x.Document.Title, x.Document.Category, x.Document.Audience, x.PublishedVersion, x.HasDraft)).ToList());

  public static async Task<CommandResult<LibraryEntryView>> OpenAsync(IAuditSphereDbContext db, ActorContext actor, Guid documentId, int? version, CancellationToken ct = default)
  {
    var entry = await TechnicalLibraryService.OpenAsync(db, actor, documentId, version, ct);
    if (!entry.Succeeded) return CommandResult<LibraryEntryView>.Fail(entry.ErrorCode!, entry.Message!);
    var e = entry.Value!;
    static LibraryVersionView View(Domain.Practice.TechnicalLibraryVersion v, bool body) =>
      new(v.Id, v.Version, v.Status, v.EffectiveFrom, v.SourceReference, v.ContentSha256, body ? v.Body : null);
    return CommandResult<LibraryEntryView>.Ok(new(e.Document.Id, e.Document.Code, e.Document.Title, e.Document.Category, View(e.Version, true),
      e.History.Select(v => View(v, false)).ToList()));
  }

  public static async Task<CommandResult<IReadOnlyList<LibraryHit>>> SearchAsync(IAuditSphereDbContext db, ActorContext actor, string term, CancellationToken ct = default) =>
    term.Length > 100 ? CommandResult<IReadOnlyList<LibraryHit>>.Fail("request.invalid", "Search terms are limited to 100 characters.")
      : CommandResult<IReadOnlyList<LibraryHit>>.Ok(await TechnicalLibraryService.SearchAsync(db, actor, term, ct));
}
