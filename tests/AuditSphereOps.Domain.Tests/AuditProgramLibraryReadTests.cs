using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditProgramLibraryReadTests
{
  [Theory]
  [InlineData("Auditor", "firm")]
  [InlineData("Reviewer", "firm")]
  [InlineData("Manager", "firm")]
  [InlineData("Partner", "firm")]
  [InlineData("Administrator", "firm")]
  [InlineData("AccountingPreparer", "firm")]
  [InlineData("AccountingReviewer", "firm")]
  [InlineData("Auditor", "client")]
  [InlineData("Reviewer", "engagement")]
  [InlineData("AccountingPreparer", "client")]
  public async Task CurrentInternalLibraryReadersCanBrowseAndSearchFirmMethodologyWithoutPublishing(string role, string scope)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var publication = await AuditProgramTestHelpers.PublishAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), new("SYNTHETIC-READ", AuditProgramCatalog.SourceHash));
    Assert.True(publication.Succeeded, publication.Message);
    var reader = PbcSeed.User(f.FirmId, "Staff");
    db.Users.Add(reader);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, reader, role, scope == "firm" ? null : f.ClientId, scope == "engagement" ? f.EngagementId : null));
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(reader, role);
    var library = await AuditProgramLibraryQuery.GetLibraryAsync(db, actor);
    Assert.True(library.Succeeded, library.Message);
    Assert.Equal(publication.Value!.ProgramVersionId, library.Value!.SelectedVersion!.ProgramVersionId);
    var sectionNumber = library.Value.Sections.First().SectionNumber;
    var section = await AuditProgramLibraryQuery.GetSectionAsync(db, actor, publication.Value.ProgramVersionId, sectionNumber);
    Assert.True(section.Succeeded, section.Message);
    Assert.NotEmpty(section.Value!.Items);
    var first = section.Value.Items[0];
    var search = await AuditProgramLibraryQuery.GetSectionAsync(db, actor, publication.Value.ProgramVersionId, sectionNumber, first.SourceProcedureId);
    Assert.True(search.Succeeded, search.Message);
    Assert.Contains(search.Value!.Items, x => x.ProcedureId == first.ProcedureId);
    var empty = await AuditProgramLibraryQuery.GetSectionAsync(db, actor, publication.Value.ProgramVersionId, sectionNumber, "SYNTHETIC-UNMATCHED-PROCEDURE");
    Assert.True(empty.Succeeded, empty.Message);
    Assert.Empty(empty.Value!.Items);
    Assert.Equal(0, empty.Value.TotalCount);
    Assert.Single(await db.AuditProgramVersions.ToListAsync());
  }

  [Theory]
  [InlineData("expired")]
  [InlineData("revoked")]
  [InlineData("disabled")]
  [InlineData("client")]
  [InlineData("unsupported-role")]
  public async Task IneligibleIdentitiesCannotReadVersionsOrSections(string mode)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var publication = await AuditProgramTestHelpers.PublishAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), new("SYNTHETIC-READ", AuditProgramCatalog.SourceHash));
    Assert.True(publication.Succeeded, publication.Message);
    var user = PbcSeed.User(f.FirmId, mode == "client" ? "Client" : "Staff");
    user.Disabled = mode == "disabled";
    var role = mode == "unsupported-role" ? "Staff" : "Auditor";
    var grant = PbcSeed.Grant(f.FirmId, user, role);
    if (mode == "expired") { grant.GrantedAt = DateTimeOffset.UtcNow.AddHours(-2); grant.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1); }
    if (mode == "revoked") grant.RevokedAt = DateTimeOffset.UtcNow;
    db.Users.Add(user); db.RoleGrants.Add(grant); await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(user, role);
    var library = await AuditProgramLibraryQuery.GetLibraryAsync(db, actor);
    var section = await AuditProgramLibraryQuery.GetSectionAsync(db, actor, publication.Value!.ProgramVersionId, 1);
    Assert.False(library.Succeeded); Assert.Null(library.Value);
    Assert.False(section.Succeeded); Assert.Null(section.Value);
  }

  [Fact]
  public async Task DisabledStaleAndForeignReadersCannotRecoverLibraryContent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var publication = await AuditProgramTestHelpers.PublishAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), new("SYNTHETIC-READ", AuditProgramCatalog.SourceHash));
    Assert.True(publication.Succeeded, publication.Message);
    var foreign = await PbcSeed.SeedAsync(pg);
    var foreignListing = await AuditProgramLibraryQuery.GetLibraryAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"));
    Assert.True(foreignListing.Succeeded, foreignListing.Message);
    Assert.Empty(foreignListing.Value!.Versions);
    Assert.Null(foreignListing.Value.SelectedVersion);
    Assert.Empty(foreignListing.Value.Sections);
    var denied = await AuditProgramLibraryQuery.GetSectionAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), publication.Value!.ProgramVersionId, 1);
    var guessed = await AuditProgramLibraryQuery.GetSectionAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), Guid.NewGuid(), 1);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    Assert.Equal(denied.ErrorCode, guessed.ErrorCode);
    Assert.Equal(denied.Message, guessed.Message);
    await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.False((await AuditProgramLibraryQuery.GetLibraryAsync(db, PbcSeed.Actor(f.Admin, "Administrator"))).Succeeded);
    await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, false).SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditProgramLibraryQuery.GetLibraryAsync(db, PbcSeed.Actor(f.Admin, "Administrator"))).ErrorCode);
  }
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RevocationDuringLibraryOrSectionCompositionRefusesAllContent(bool section)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    Guid version;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var publication = await AuditProgramTestHelpers.PublishAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), new("SYNTHETIC-READ", AuditProgramCatalog.SourceHash));
      Assert.True(publication.Succeeded, publication.Message); version = publication.Value!.ProgramVersionId;
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Auditor")); await db.SaveChangesAsync();
    }
    var interceptor = new RevokeAfterProcedureRead(async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      await revoke.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "Auditor").ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Staff, "Auditor");
    if (section)
    {
      var result = await AuditProgramLibraryQuery.GetSectionAsync(reader, actor, version, 1);
      Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode); Assert.Null(result.Value);
    }
    else
    {
      var result = await AuditProgramLibraryQuery.GetLibraryAsync(reader, actor);
      Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode); Assert.Null(result.Value);
    }
    Assert.Equal(1, interceptor.Calls);
  }
  private sealed class RevokeAfterProcedureRead(Func<Task> revoke) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => calls;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("FROM audit_program_procedures", StringComparison.Ordinal) && Interlocked.CompareExchange(ref calls, 1, 0) == 0) await revoke();
      return result;
    }
  }

}
