using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditReviewNoteIsolationTests
{
  [Fact(DisplayName = "Client-scoped staff cannot read or mutate sibling review-note threads")]
  public async Task SiblingReviewNoteIds_AreDeniedForCreateReadAndThreadEvents()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var now = DateTimeOffset.UtcNow;
    var preparerId = Guid.NewGuid();
    var reviewerId = Guid.NewGuid();
    var procedureId = Guid.NewGuid();
    var resultId = Guid.NewGuid();
    var preparer = new ActorContext(preparerId, clientB.FirmId, 1, ["Staff"]);
    var reviewer = new ActorContext(reviewerId, clientB.FirmId, 1, ["Senior"]);
    Guid noteId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var engagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      engagement.ProfessionalWorkBlocked = false;
      db.Users.AddRange(NewUser(clientB.FirmId, preparerId, "review-note-preparer"), NewUser(clientB.FirmId, reviewerId, "review-note-reviewer"));
      db.RoleGrants.AddRange(Grant(clientB, preparerId, "Staff"), Grant(clientB, reviewerId, "Senior"));
      db.AuditProcedures.Add(new AuditProcedure
      {
        Id = procedureId,
        FirmId = clientB.FirmId,
        ClientId = clientB.ClientId,
        EngagementId = clientB.EngagementId,
        SourceProcedureId = "B-REV-01",
        Title = "Client B bank review",
        ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
        Status = AuditProcedureStatuses.Submitted,
        CurrentResultRevision = 1,
        CreatedAt = now
      });
      db.AuditProcedureResults.Add(new AuditProcedureResult
      {
        Id = resultId,
        FirmId = clientB.FirmId,
        ClientId = clientB.ClientId,
        EngagementId = clientB.EngagementId,
        AuditProcedureId = procedureId,
        Revision = 1,
        InputGeneration = 1,
        WorkPerformed = "Agreed the bank balance to the signed statement.",
        StructuredResultJson = "{}",
        Conclusion = "The balance agrees to source evidence.",
        PreparedByUserId = preparerId,
        SubmittedAt = now
      });
      await db.SaveChangesAsync();

      var created = await ReviewNotesService.AddNoteAsync(db, reviewer,
        new AddReviewNoteRequest(resultId, ReviewNoteFields.WorkPerformed, "signed statement", "Confirm the statement date."));
      Assert.True(created.Succeeded, created.Message);
      noteId = created.Value;
      Assert.True((await ReviewNotesService.RespondAsync(db, preparer, noteId, "The statement is dated at year end.")).Succeeded);
      Assert.True((await ReviewNotesService.ResolveAsync(db, reviewer, noteId, "Date and balance confirmed.")).Succeeded);
      Assert.True((await ReviewNotesService.ReopenAsync(db, reviewer, noteId, "Second review requested.")).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await ReviewNotesService.AddNoteAsync(db, clientA.Actor,
          new AddReviewNoteRequest(resultId, ReviewNoteFields.Conclusion, "balance agrees", "Unauthorized sibling note."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await ReviewNotesService.RespondAsync(db, clientA.Actor, noteId, "Unauthorized sibling response.")).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await ReviewNotesService.ResolveAsync(db, clientA.Actor, noteId, "Unauthorized sibling resolution.")).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await ReviewNotesService.ReopenAsync(db, clientA.Actor, noteId, "Unauthorized sibling reopen.")).ErrorCode);
      Assert.Empty(await ReviewNotesService.ListAsync(db, clientA.Actor, procedureId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var note = await db.ProcedureReviewNotes.AsNoTracking().SingleAsync(x => x.Id == noteId);
      var events = await db.ProcedureReviewNoteEvents.AsNoTracking().Where(x => x.NoteId == noteId).OrderBy(x => x.CreatedAt).ToListAsync();
      Assert.Equal(clientB.ClientId, note.ClientId);
      Assert.Equal(clientB.EngagementId, note.EngagementId);
      Assert.Equal("Confirm the statement date.", note.Body);
      Assert.Equal([ReviewNoteEventKinds.Response, ReviewNoteEventKinds.Resolved, ReviewNoteEventKinds.Reopened], events.Select(x => x.Kind));
      Assert.Equal(3, events.Count);
      Assert.Equal(1, await db.ProcedureReviewNotes.CountAsync(x => x.ProcedureId == procedureId));
    }
  }

  private static AppUser NewUser(Guid firmId, Guid userId, string subject) => new()
  {
    Id = userId,
    FirmId = firmId,
    Subject = subject + "-" + userId.ToString("N"),
    TenantId = "tenant-planning",
    Email = subject + "@example.test",
    DisplayName = subject,
    UserKind = "Staff",
    SessionEpoch = 1,
    CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(PlanningScope scope, Guid userId, string role) => new()
  {
    Id = Guid.NewGuid(),
    FirmId = scope.FirmId,
    UserId = userId,
    Role = role,
    ClientId = scope.ClientId,
    EngagementId = scope.EngagementId,
    GrantedAt = DateTimeOffset.UtcNow,
    GrantedByUserId = userId
  };
}
