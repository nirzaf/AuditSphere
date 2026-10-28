using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// One effective-evidence contract for sample readback, completion and dependent deliverables.
internal static class AuditSamplingEvidence
{
  internal static async Task<IReadOnlyList<AuditItemTest>> LatestTestsAsync(IAuditSphereDbContext db,
    Guid firmId, Guid clientId, Guid engagementId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
  {
    var result = new List<AuditItemTest>();
    foreach (var batch in itemIds.Chunk(100))
      result.AddRange(await db.AuditItemTests.AsNoTracking().Where(test =>
        test.FirmId == firmId && test.ClientId == clientId && test.EngagementId == engagementId && batch.Contains(test.SelectionItemId) &&
        db.AuditSelectionItems.Any(item => item.FirmId == firmId && item.ClientId == clientId && item.EngagementId == engagementId &&
          item.Id == test.SelectionItemId && item.SelectionId == test.SelectionId) &&
        !db.AuditItemTests.Any(newer => newer.FirmId == firmId && newer.ClientId == clientId && newer.EngagementId == engagementId &&
          newer.SelectionItemId == test.SelectionItemId && newer.Revision > test.Revision)).ToArrayAsync(ct));
    return result;
  }

  internal static async Task<IReadOnlyDictionary<Guid, AuditItemTestReview>> EffectiveReviewsAsync(IAuditSphereDbContext db,
    Guid firmId, Guid clientId, Guid engagementId, IReadOnlyCollection<Guid> testIds, CancellationToken ct)
  {
    var result = new Dictionary<Guid, AuditItemTestReview>();
    foreach (var batch in testIds.Chunk(100))
    {
      var eligible = from review in db.AuditItemTestReviews.AsNoTracking()
        join test in db.AuditItemTests.AsNoTracking() on review.AuditItemTestId equals test.Id
        where review.FirmId == firmId && review.ClientId == clientId && review.EngagementId == engagementId &&
          test.FirmId == firmId && test.ClientId == clientId && test.EngagementId == engagementId &&
          review.SelectionItemId == test.SelectionItemId && review.TestRevision == test.Revision && batch.Contains(test.Id)
        select review;
      // New decisions have a serialized sequence assigned by the command. Historical
      // timestamp ties cannot establish intent: an adverse decision wins conservatively.
      var current = await eligible.GroupBy(x => x.AuditItemTestId)
        .Select(g => g.OrderByDescending(x => x.ReviewSequence.HasValue).ThenByDescending(x => x.ReviewSequence)
          .ThenByDescending(x => x.CreatedAt)
          .ThenByDescending(x => x.Decision == AuditItemTestReviewDecisions.ChangesRequired)
          .ThenByDescending(x => x.Id).First()).ToArrayAsync(ct);
      foreach (var review in current) result.Add(review.AuditItemTestId, review);
    }
    return result;
  }

  internal static bool IsComplete(AuditItemTest? test, AuditItemTestReview? review, long generation, long selectionGeneration) =>
    test is not null && review is not null && selectionGeneration == generation && test.InputGeneration == generation &&
    test.Result != AuditItemTestResults.Pending && review.AuditItemTestId == test.Id && review.TestRevision == test.Revision &&
    review.Decision == AuditItemTestReviewDecisions.Reviewed;

  internal static async Task<string?> DigestAsync(IAuditSphereDbContext db,
    Guid firmId, Guid clientId, Guid engagementId, CancellationToken ct)
  {
    var selections = await db.AuditSelections.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderBy(x => x.Id).Select(x => new
      {
        x.Id, x.ScheduleId, x.PopulationVersionId, x.ProcedureId, x.InputGeneration,
        x.Status, x.ReviewedByUserId
      }).ToArrayAsync(ct);
    if (selections.Length == 0) return null; // Preserve the existing digest contract when no sampling contributes.
    var items = await db.AuditSelectionItems.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SelectionId, x.StableRowId, x.SignedAmount, x.Currency, x.ScheduleRowId }).ToArrayAsync(ct);
    var tests = await LatestTestsAsync(db, firmId, clientId, engagementId, items.Select(x => x.Id).ToArray(), ct);
    var reviews = await EffectiveReviewsAsync(db, firmId, clientId, engagementId, tests.Select(x => x.Id).ToArray(), ct);
    return Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Version = "sampled-item-evidence.v1", Selections = selections, Items = items,
      Tests = tests.OrderBy(x => x.SelectionItemId).Select(test => new
      {
        test.SelectionItemId, test.SelectionId, test.Id, test.Revision, test.InputGeneration, test.Result,
        test.RiskBasisJson, Review = reviews.TryGetValue(test.Id, out var review)
          ? new { review.Id, review.TestRevision, review.ReviewSequence, review.Decision, review.ReviewerUserId } : null
      })
    }));
  }
}
