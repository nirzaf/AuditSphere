using System.Text.RegularExpressions;
using System.Text.Json;
using Npgsql;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// WU-004: after applying every migration to an owned PostgreSQL database, each trigger the migrations leave in
/// place exists and is enabled, and no seeded single-column key between firm-scoped tables accepts another firm's row.
/// </summary>
[Trait("Profile", "Database")]
[Trait("CompletionUnit", "WU-004")]
public sealed partial class CompletionSchemaInvariantTests(Xunit.Abstractions.ITestOutputHelper output)
{
  [Fact]
  public async Task MigratedSchema_KeepsEveryMigrationTriggerEnabled()
  {
    var expected = ExpectedTriggers();
    Assert.True(expected.Count > 50, $"Only {expected.Count} triggers parsed from migrations; the probe would be vacuous.");
    await using var pg = await PgTestSchema.CreateAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    var actual = new Dictionary<string, char>(StringComparer.Ordinal);
    await using (var command = new NpgsqlCommand("SELECT tgname, tgenabled FROM pg_trigger WHERE NOT tgisinternal", connection))
    await using (var reader = await command.ExecuteReaderAsync())
      while (await reader.ReadAsync()) actual[reader.GetString(0)] = reader.GetChar(1);

    var missing = expected.Where(name => !actual.ContainsKey(name)).ToList();
    var disabled = expected.Where(name => actual.TryGetValue(name, out var state) && state is not ('O' or 'A')).ToList();
    output.WriteLine($"Triggers expected from migrations: {expected.Count}; present: {expected.Count - missing.Count}; disabled: {disabled.Count}");
    Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    Assert.True(disabled.Count == 0, "Disabled: " + string.Join(", ", disabled));
  }

  [Fact]
  public async Task ClientConversionPublicationGuardRejectsForeignFirmParents()
  {
    static async Task ExerciseAsync(PgTestSchema pg, bool disablePublicationGuard, bool foreignProposalReference)
    {
      var own = await PbcSeed.SeedAsync(pg);
      var foreign = await PbcSeed.SeedAsync(pg);
      await using var db = new AuditSphereDbContext(pg.Options);
      var proposalId = await ClientConversionReviewSeed.PopulateAsync(db, own);
      var foreignProposalId = await ClientConversionReviewSeed.PopulateAsync(db, foreign);
      var actor = PbcSeed.Actor(own.Admin, "Administrator");
      var request = new ClientConversionRequest(Guid.CreateVersion7(),
        new ClientConversionFields("PBC TEST CLIENT"));
      var preview = await ClientConversionWorkspace.PreviewAsync(db, actor, proposalId, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal(own.ClientId, preview.Value!.ExistingClientId);

      await using var transaction = await db.Database.BeginTransactionAsync();
      var linked = await PracticeCrmService.ConvertToClientDraftAsync(db, actor,
        new ConvertToClientDraftRequest(proposalId, request.Fields.LegalName,
          request.Fields.CommercialName, request.Fields.RegistrationNumber,
          request.Fields.Jurisdiction, request.Fields.RestrictedProfile));
      Assert.True(linked.Succeeded, linked.Message);
      Assert.Equal(own.ClientId, linked.Value);

      var receipt = new ClientConversion
      {
        Id = Guid.CreateVersion7(),
        FirmId = own.FirmId,
        // Change only one parent at a time. The existing global FK accepts either reference;
        // the publication guard must bind each one to the reviewed firm's commercial handover.
        ProposalId = foreignProposalReference ? foreignProposalId : proposalId,
        ClientId = foreignProposalReference ? own.ClientId : foreign.ClientId,
        ActorId = actor.UserId,
        ActorEpoch = actor.SessionEpoch,
        RequestId = request.RequestId,
        RequestHash = preview.Value.RequestHash,
        ReviewBasis = preview.Value.ReviewBasis,
        PreviewJson = JsonSerializer.Serialize(preview.Value),
        CreatedAt = DateTimeOffset.UtcNow
      };

      if (disablePublicationGuard)
        await db.Database.ExecuteSqlRawAsync(
          "ALTER TABLE client_conversions DISABLE TRIGGER client_conversion_publication_guard");

      db.ClientConversions.Add(receipt);
      await db.SaveChangesAsync();
      if (disablePublicationGuard)
      {
        // The counterfactual lives only in this disposable, owner-created test database.
        await transaction.CommitAsync();
        await using var accepted = new AuditSphereDbContext(pg.Options);
        Assert.True(await accepted.ClientConversions.AnyAsync(x => x.Id == receipt.Id));
        var parentFirm = foreignProposalReference
          ? await accepted.Proposals.Where(x => x.Id == receipt.ProposalId).Select(x => x.FirmId).SingleAsync()
          : await accepted.PracticeClients.Where(x => x.Id == receipt.ClientId).Select(x => x.FirmId).SingleAsync();
        Assert.Equal(foreign.FirmId, parentFirm);
        Assert.NotEqual(receipt.FirmId, parentFirm);
        return;
      }

      var refusal = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
      Assert.Equal("23514", refusal.SqlState);
      Assert.Contains("Conversion requires exact authorized committed commercial handover", refusal.MessageText);

      await using var persisted = new AuditSphereDbContext(pg.Options);
      Assert.False(await persisted.ClientConversions.AnyAsync(x => x.Id == receipt.Id));
      Assert.Null((await persisted.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).PracticeClientId);
      Assert.Empty(await persisted.AcceptanceDecisions.Where(x => x.PracticeClientId == own.ClientId).ToListAsync());
      Assert.Empty(await persisted.ClientPortalIntents.Where(x => x.PracticeClientId == own.ClientId).ToListAsync());
    }

    foreach (var foreignProposalReference in new[] { false, true })
    {
      await using (var counterfactual = await PgTestSchema.CreateAsync())
        await ExerciseAsync(counterfactual, disablePublicationGuard: true, foreignProposalReference);

      await using (var guarded = await PgTestSchema.CreateAsync())
        await ExerciseAsync(guarded, disablePublicationGuard: false, foreignProposalReference);
    }
  }

  [Fact]
  public async Task NativeEngagementActivationGuardRejectsForeignFirmEngagementParent()
  {
    static async Task<EngagementActivationReceipt> ActivateAsync(AuditSphereDbContext db, PbcSeed.Fixture fixture)
    {
      await EngagementActivationReviewSeed.PopulateAsync(db, fixture);
      var actor = PbcSeed.Actor(fixture.Staff, "Partner");
      var state = await EngagementActivationWorkspace.StateAsync(db, actor, fixture.EngagementId);
      Assert.True(state.Succeeded, state.Message);
      Assert.True(state.Value!.Eligible);
      var request = new EngagementActivationRequest(Guid.CreateVersion7(), state.Value.ReviewBasis);
      var preview = await EngagementActivationWorkspace.PreviewAsync(db, actor, fixture.EngagementId, request);
      Assert.True(preview.Succeeded, preview.Message);
      request = request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash };
      var activated = await EngagementActivationWorkspace.ExecuteAsync(db, actor, fixture.EngagementId, request);
      Assert.True(activated.Succeeded, activated.Message);
      return activated.Value!;
    }

    static async Task ExerciseAsync(PgTestSchema pg, bool disableScopeGuard)
    {
      var own = await PbcSeed.SeedAsync(pg);
      var foreign = await PbcSeed.SeedAsync(pg);
      Guid sourceId;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var ownReceipt = await ActivateAsync(db, own);
        var foreignReceipt = await ActivateAsync(db, foreign);
        Assert.Equal(own.EngagementId, ownReceipt.EngagementId);
        Assert.Equal(foreign.EngagementId, foreignReceipt.EngagementId);
        sourceId = ownReceipt.Id;
      }

      if (disableScopeGuard)
        await using (var db = new AuditSphereDbContext(pg.Options))
          await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE engagement_activations DISABLE TRIGGER native_engagement_activation_scope_guard");

      var forgedId = Guid.CreateVersion7();
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using (var transaction = await connection.BeginTransactionAsync())
      {
        await using var insert = new NpgsqlCommand("""
          INSERT INTO engagement_activations
          SELECT (jsonb_populate_record(NULL::engagement_activations, to_jsonb(a) || jsonb_build_object(
            'id', @id, 'request_id', @request_id, 'engagement_id', @engagement_id))).*
          FROM engagement_activations a WHERE a.id = @source_id
          """, connection, transaction);
        insert.Parameters.AddWithValue("id", forgedId);
        insert.Parameters.AddWithValue("request_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("engagement_id", foreign.EngagementId);
        insert.Parameters.AddWithValue("source_id", sourceId);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());

        if (disableScopeGuard)
        {
          await transaction.CommitAsync();
          await using var accepted = new AuditSphereDbContext(pg.Options);
          var receipt = await accepted.EngagementActivations.AsNoTracking().SingleAsync(x => x.Id == forgedId);
          Assert.Equal(own.FirmId, receipt.FirmId);
          Assert.Equal(foreign.EngagementId, receipt.EngagementId);
          Assert.Equal(foreign.FirmId, await accepted.Engagements.Where(x => x.Id == receipt.EngagementId)
            .Select(x => x.FirmId).SingleAsync());
          Assert.NotEqual(receipt.FirmId, foreign.FirmId);
          return;
        }

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        // RAISE EXCEPTION without an explicit ERRCODE uses PostgreSQL's default P0001.
        Assert.Equal("P0001", refusal.SqlState);
        Assert.Equal("Native activation requires the exact current scoped activation and accepted decision", refusal.MessageText);
      }

      await using var persisted = new AuditSphereDbContext(pg.Options);
      Assert.False(await persisted.EngagementActivations.AnyAsync(x => x.Id == forgedId));
      Assert.Single(await persisted.EngagementActivations.Where(x => x.EngagementId == own.EngagementId).ToListAsync());
      Assert.Single(await persisted.EngagementActivations.Where(x => x.EngagementId == foreign.EngagementId).ToListAsync());
    }

    await using (var counterfactual = await PgTestSchema.CreateAsync())
      await ExerciseAsync(counterfactual, disableScopeGuard: true);

    await using (var guarded = await PgTestSchema.CreateAsync())
      await ExerciseAsync(guarded, disableScopeGuard: false);
  }

  [Fact]
  public async Task EngagementCreationPublicationGuardRejectsForeignFirmEngagementParent()
  {
    static async Task<EngagementCreationReceipt> CreateAsync(AuditSphereDbContext db, PbcSeed.Fixture fixture)
    {
      await EngagementCreationReviewSeed.PopulateAsync(db, fixture);
      var actor = PbcSeed.Actor(fixture.Staff, "Manager");
      var state = await EngagementCreationWorkspace.StateAsync(db, actor, fixture.ClientId);
      Assert.True(state.Succeeded, state.Message);
      var request = new EngagementCreationRequest(Guid.CreateVersion7(), state.Value!.ReviewBasis,
        new EngagementCreationFields("AccountingOnly", "SYNTHETIC", "2027-01-01", "2027-12-31"));
      var preview = await EngagementCreationWorkspace.PreviewAsync(db, actor, fixture.ClientId, request);
      Assert.True(preview.Succeeded, preview.Message);
      var created = await EngagementCreationWorkspace.ExecuteAsync(db, actor, fixture.ClientId,
        request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
      Assert.True(created.Succeeded, created.Message);
      return created.Value!;
    }

    static async Task ExerciseAsync(PgTestSchema pg, bool disablePublicationGuard)
    {
      var own = await PbcSeed.SeedAsync(pg);
      var foreign = await PbcSeed.SeedAsync(pg);
      EngagementCreationReceipt ownReceipt;
      EngagementCreationReceipt foreignReceipt;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        ownReceipt = await CreateAsync(db, own);
        foreignReceipt = await CreateAsync(db, foreign);
      }
      Assert.NotEqual(own.FirmId, foreign.FirmId);
      var ownNative = await ReadReceiptAsync(pg, ownReceipt.Id);
      var foreignNative = await ReadReceiptAsync(pg, foreignReceipt.Id);
      Assert.Equal(own.FirmId, ownNative.FirmId);
      Assert.Equal(own.ClientId, ownNative.ClientId);
      Assert.Equal(ownReceipt.EngagementId, ownNative.EngagementId);
      Assert.Equal(foreign.FirmId, foreignNative.FirmId);
      Assert.Equal(foreign.ClientId, foreignNative.ClientId);
      Assert.Equal(foreignReceipt.EngagementId, foreignNative.EngagementId);

      var forgedId = Guid.CreateVersion7();
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using var transaction = await connection.BeginTransactionAsync();
      if (disablePublicationGuard)
      {
        await using var disable = new NpgsqlCommand(
          "ALTER TABLE engagement_creations DISABLE TRIGGER engagement_creation_publication_guard", connection, transaction);
        await disable.ExecuteNonQueryAsync();
      }

      await using (var insert = new NpgsqlCommand("""
        INSERT INTO engagement_creations
        SELECT (jsonb_populate_record(NULL::engagement_creations, to_jsonb(c) || jsonb_build_object(
          'id', @id, 'request_id', @request_id, 'engagement_id', @engagement_id))).*
        FROM engagement_creations c WHERE c.id = @source_id
        """, connection, transaction))
      {
        insert.Parameters.AddWithValue("id", forgedId);
        insert.Parameters.AddWithValue("request_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("engagement_id", foreignReceipt.EngagementId);
        insert.Parameters.AddWithValue("source_id", ownReceipt.Id);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
      }

      if (disablePublicationGuard)
      {
        // The counterfactual is confined to this disposable test database. The global FK alone
        // accepts the foreign engagement; the deferred publication guard supplies the scope rule.
        await transaction.CommitAsync();
        await using var accepted = new AuditSphereDbContext(pg.Options);
        var receipt = await accepted.EngagementCreations.AsNoTracking().SingleAsync(x => x.Id == forgedId);
        Assert.Equal(own.FirmId, receipt.FirmId);
        Assert.Equal(foreignReceipt.EngagementId, receipt.EngagementId);
        Assert.Equal(foreign.FirmId, await accepted.Engagements.Where(x => x.Id == receipt.EngagementId)
          .Select(x => x.FirmId).SingleAsync());
        Assert.NotEqual(receipt.FirmId, foreign.FirmId);
        return;
      }

      var refusal = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
      Assert.Equal("23514", refusal.SqlState);
      Assert.Equal("Creation receipt requires exact current scoped blocked publication", refusal.MessageText);

      await using var persisted = new AuditSphereDbContext(pg.Options);
      Assert.False(await persisted.EngagementCreations.AnyAsync(x => x.Id == forgedId));
      Assert.Equal(2, await persisted.EngagementCreations.CountAsync());
      Assert.Equal(ownReceipt.EngagementId, (await ReadReceiptAsync(pg, ownReceipt.Id)).EngagementId);
      Assert.Equal(foreignReceipt.EngagementId, (await ReadReceiptAsync(pg, foreignReceipt.Id)).EngagementId);
    }

    static async Task<EngagementCreation> ReadReceiptAsync(PgTestSchema pg, Guid id)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await db.EngagementCreations.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    await using (var counterfactual = await PgTestSchema.CreateAsync())
      await ExerciseAsync(counterfactual, disablePublicationGuard: true);

    await using (var guarded = await PgTestSchema.CreateAsync())
      await ExerciseAsync(guarded, disablePublicationGuard: false);
  }

  [Theory]
  [InlineData("client")]
  [InlineData("contact")]
  public async Task ClientPortalIntentRequiresItsExactFirmAndClientContact(string foreignParent)
  {
    static async Task<(Guid ClientId, Guid IntentId, Guid ContactId, string RecipientEmail)> ConvertAsync(
      AuditSphereDbContext db, PbcSeed.Fixture fixture, string legalName)
    {
      var proposalId = await ClientConversionReviewSeed.PopulateAsync(db, fixture);
      var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
      var request = new ClientConversionRequest(Guid.CreateVersion7(), new ClientConversionFields(legalName));
      var preview = await ClientConversionWorkspace.PreviewAsync(db, actor, proposalId, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Null(preview.Value!.ExistingClientId);
      request = request with { Reviewed = true, RequestHash = preview.Value.RequestHash, ReviewBasis = preview.Value.ReviewBasis };
      var converted = await ClientConversionWorkspace.ExecuteAsync(db, actor, proposalId, request);
      Assert.True(converted.Succeeded, converted.Message);
      var receipt = converted.Value!;
      var intent = await db.ClientPortalIntents.AsNoTracking().SingleAsync(x =>
        x.FirmId == fixture.FirmId && x.PracticeClientId == receipt.ClientId);
      return (receipt.ClientId, intent.Id, intent.ClientContactId, intent.RecipientEmail);
    }

    Assert.Contains(foreignParent, new[] { "client", "contact" });
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    Guid ownClientId;
    Guid ownIntentId;
    Guid ownContactId;
    string ownRecipientEmail;
    Guid foreignClientId;
    Guid foreignContactId;
    string foreignRecipientEmail;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      (ownClientId, ownIntentId, ownContactId, ownRecipientEmail) = await ConvertAsync(db, own, "PORTAL SCOPE OWN CLIENT");
      (foreignClientId, _, foreignContactId, foreignRecipientEmail) = await ConvertAsync(db, foreign, "PORTAL SCOPE FOREIGN CLIENT");
      Assert.NotEqual(own.FirmId, foreign.FirmId);
      Assert.NotEqual(ownClientId, foreignClientId);
      Assert.Equal(own.FirmId, await db.ClientContacts.Where(x => x.Id == ownContactId).Select(x => x.FirmId).SingleAsync());
      Assert.Equal(ownClientId, await db.ClientContacts.Where(x => x.Id == ownContactId).Select(x => x.PracticeClientId).SingleAsync());
      Assert.Equal(foreign.FirmId, await db.ClientContacts.Where(x => x.Id == foreignContactId).Select(x => x.FirmId).SingleAsync());
      Assert.Equal(foreignClientId, await db.ClientContacts.Where(x => x.Id == foreignContactId).Select(x => x.PracticeClientId).SingleAsync());

      if (foreignParent == "contact")
      {
        foreignRecipientEmail = "firm-b-contact@example.test";
        await db.ClientContacts.Where(x => x.Id == foreignContactId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, foreignRecipientEmail));
      }

      await using var transaction = await db.Database.BeginTransactionAsync();
      var refusal = await Record.ExceptionAsync(async () =>
      {
        if (foreignParent == "client")
          await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE client_portal_intents SET practice_client_id = {foreignClientId} WHERE id = {ownIntentId}");
        else
          await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE client_portal_intents SET client_contact_id = {foreignContactId}, recipient_email = {foreignRecipientEmail} WHERE id = {ownIntentId}");
      });

      if (refusal is not null)
      {
        var postgres = Assert.IsType<PostgresException>(refusal);
        Assert.Equal("23503", postgres.SqlState);
        Assert.Equal("fk_client_portal_intents_contact_scope", postgres.ConstraintName);
        await transaction.RollbackAsync();
      }
      else
      {
        await transaction.CommitAsync();
        await using var accepted = new AuditSphereDbContext(pg.Options);
        var row = await accepted.ClientPortalIntents.AsNoTracking().SingleAsync(x => x.Id == ownIntentId);
        if (foreignParent == "client")
        {
          Assert.Equal(own.FirmId, row.FirmId);
          Assert.Equal(foreignClientId, row.PracticeClientId);
          Assert.Equal(foreign.FirmId, await accepted.PracticeClients.Where(x => x.Id == row.PracticeClientId)
            .Select(x => x.FirmId).SingleAsync());
        }
        else
        {
          Assert.Equal(ownClientId, row.PracticeClientId);
          Assert.Equal(foreignContactId, row.ClientContactId);
          var workspace = await WorkspaceQuery.ClientAsync(accepted, PbcSeed.Actor(own.Admin, "Administrator"), ownClientId);
          Assert.True(workspace.Succeeded, workspace.Message);
          Assert.Equal(foreignRecipientEmail, workspace.Value!.PortalIntent!.RecipientEmail);
          output.WriteLine($"Accepted contact-scope counterfactual exposed recipient {workspace.Value.PortalIntent.RecipientEmail} through the own-client workspace.");
        }
        Assert.Fail("The database accepted a client portal intent parent from the wrong firm or client.");
      }
    }

    await using var persisted = new AuditSphereDbContext(pg.Options);
    var ownIntent = await persisted.ClientPortalIntents.AsNoTracking().SingleAsync(x => x.Id == ownIntentId);
    Assert.Equal(own.FirmId, ownIntent.FirmId);
    Assert.Equal(ownClientId, ownIntent.PracticeClientId);
    Assert.Equal(ownContactId, ownIntent.ClientContactId);
    Assert.Equal(ownRecipientEmail, ownIntent.RecipientEmail);
    Assert.Equal(2, await persisted.ClientPortalIntents.CountAsync());
    var currentWorkspace = await WorkspaceQuery.ClientAsync(persisted, PbcSeed.Actor(own.Admin, "Administrator"), ownClientId);
    Assert.True(currentWorkspace.Succeeded, currentWorkspace.Message);
    Assert.Equal(ownRecipientEmail, currentWorkspace.Value!.PortalIntent!.RecipientEmail);
    Assert.Equal("AWAITING_ACCEPTANCE", currentWorkspace.Value.PortalIntent.State);
    Assert.False(currentWorkspace.Value.PortalIntent.ContactHasClientAccess);
  }

  [Fact]
  public async Task ClientPortalIntentScopeUpgradeAndRollbackPreserveHistoryAndRejectInvalidLink()
  {
    const string previousMigration = "20261031182610_EqrDecisionHistory";
    const string scopeMigration = "20261101024300_ClientPortalIntentContactScope";

    static async Task<(Guid ClientId, Guid IntentId, Guid ContactId, string RecipientEmail)> ConvertAsync(
      AuditSphereDbContext db, PbcSeed.Fixture fixture, string legalName)
    {
      var proposalId = await ClientConversionReviewSeed.PopulateAsync(db, fixture);
      var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
      var request = new ClientConversionRequest(Guid.CreateVersion7(), new ClientConversionFields(legalName));
      var preview = await ClientConversionWorkspace.PreviewAsync(db, actor, proposalId, request);
      Assert.True(preview.Succeeded, preview.Message);
      request = request with { Reviewed = true, RequestHash = preview.Value!.RequestHash, ReviewBasis = preview.Value.ReviewBasis };
      var converted = await ClientConversionWorkspace.ExecuteAsync(db, actor, proposalId, request);
      Assert.True(converted.Succeeded, converted.Message);
      var receipt = converted.Value!;
      var intent = await db.ClientPortalIntents.AsNoTracking().SingleAsync(x =>
        x.FirmId == fixture.FirmId && x.PracticeClientId == receipt.ClientId);
      return (receipt.ClientId, intent.Id, intent.ClientContactId, intent.RecipientEmail);
    }

    static async Task<string[]> ReadColumnAsync(PgTestSchema pg, string sql)
    {
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using var command = new NpgsqlCommand(sql, connection);
      await using var reader = await command.ExecuteReaderAsync();
      var values = new List<string>();
      while (await reader.ReadAsync()) values.Add(reader.GetString(0));
      return values.ToArray();
    }

    static async Task<string> ReadLatestMigrationAsync(PgTestSchema pg)
    {
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using var command = new NpgsqlCommand(
        "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1", connection);
      return (string)(await command.ExecuteScalarAsync())!;
    }

    static async Task MigrateAsync(PgTestSchema pg, string migration)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    await using var pg = await PgTestSchema.CreateAsync(previousMigration);
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    Guid ownIntentId;
    Guid ownContactId;
    Guid foreignContactId;
    string ownRecipientEmail;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var ownPortal = await ConvertAsync(db, own, "PORTAL MIGRATION OWN CLIENT");
      var foreignPortal = await ConvertAsync(db, foreign, "PORTAL MIGRATION FOREIGN CLIENT");
      ownIntentId = ownPortal.IntentId;
      ownContactId = ownPortal.ContactId;
      ownRecipientEmail = ownPortal.RecipientEmail;
      foreignContactId = foreignPortal.ContactId;
    }

    Assert.NotEqual(own.FirmId, foreign.FirmId);
    var retainedRows = await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id");
    Assert.Equal(2, retainedRows.Length);
    var originalForeignKeys = await ReadColumnAsync(pg, """
      SELECT conname || '|' || pg_get_constraintdef(oid)
      FROM pg_constraint
      WHERE conrelid = 'client_portal_intents'::regclass AND contype = 'f'
      ORDER BY conname
      """);
    var originalIndexes = await ReadColumnAsync(pg, """
      SELECT indexname || '|' || indexdef
      FROM pg_indexes
      WHERE schemaname = current_schema() AND tablename = 'client_portal_intents'
      ORDER BY indexname
      """);
    Assert.Contains(originalForeignKeys, x => x.StartsWith("FK_client_portal_intents_client_contacts_client_contact_id|", StringComparison.Ordinal));
    Assert.Contains(originalForeignKeys, x => x.StartsWith("FK_client_portal_intents_practice_clients_practice_client_id|", StringComparison.Ordinal));

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_portal_intents SET client_contact_id = {foreignContactId} WHERE id = {ownIntentId}");
    var invalidRows = await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id");
    Assert.NotEqual(retainedRows, invalidRows);

    var refusal = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(pg, scopeMigration));
    Assert.Equal("23503", refusal.SqlState);
    Assert.Equal("fk_client_portal_intents_contact_scope", refusal.ConstraintName);
    Assert.Equal(previousMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(invalidRows, await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id"));
    Assert.Equal(originalForeignKeys, await ReadColumnAsync(pg, """
      SELECT conname || '|' || pg_get_constraintdef(oid)
      FROM pg_constraint
      WHERE conrelid = 'client_portal_intents'::regclass AND contype = 'f'
      ORDER BY conname
      """));
    Assert.Equal(originalIndexes, await ReadColumnAsync(pg, """
      SELECT indexname || '|' || indexdef
      FROM pg_indexes
      WHERE schemaname = current_schema() AND tablename = 'client_portal_intents'
      ORDER BY indexname
      """));

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_portal_intents SET client_contact_id = {ownContactId} WHERE id = {ownIntentId}");
    Assert.Equal(retainedRows, await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id"));

    await MigrateAsync(pg, scopeMigration);
    Assert.Equal(scopeMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(retainedRows, await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id"));
    var scopedForeignKeys = await ReadColumnAsync(pg, """
      SELECT conname || '|' || pg_get_constraintdef(oid)
      FROM pg_constraint
      WHERE conrelid = 'client_portal_intents'::regclass AND contype = 'f'
      ORDER BY conname
      """);
    var newScopeForeignKey = Assert.Single(scopedForeignKeys,
      x => x.StartsWith("fk_client_portal_intents_contact_scope|", StringComparison.Ordinal));
    Assert.Contains("FOREIGN KEY (firm_id, practice_client_id, client_contact_id) REFERENCES client_contacts(firm_id, practice_client_id, id) ON DELETE RESTRICT", newScopeForeignKey);
    Assert.Equal(originalForeignKeys, scopedForeignKeys
      .Where(x => !x.StartsWith("fk_client_portal_intents_contact_scope|", StringComparison.Ordinal)).ToArray());
    Assert.Equal(originalIndexes, await ReadColumnAsync(pg, """
      SELECT indexname || '|' || indexdef
      FROM pg_indexes
      WHERE schemaname = current_schema() AND tablename = 'client_portal_intents'
      ORDER BY indexname
      """));

    await MigrateAsync(pg, previousMigration);
    Assert.Equal(previousMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(retainedRows, await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id"));
    Assert.Equal(originalForeignKeys, await ReadColumnAsync(pg, """
      SELECT conname || '|' || pg_get_constraintdef(oid)
      FROM pg_constraint
      WHERE conrelid = 'client_portal_intents'::regclass AND contype = 'f'
      ORDER BY conname
      """));
    Assert.Equal(originalIndexes, await ReadColumnAsync(pg, """
      SELECT indexname || '|' || indexdef
      FROM pg_indexes
      WHERE schemaname = current_schema() AND tablename = 'client_portal_intents'
      ORDER BY indexname
      """));

    await MigrateAsync(pg, scopeMigration);
    await using var migrated = new AuditSphereDbContext(pg.Options);
    Assert.Equal(retainedRows, await ReadColumnAsync(pg,
      "SELECT to_jsonb(i)::text FROM client_portal_intents i ORDER BY id"));
    var ownWorkspace = await WorkspaceQuery.ClientAsync(migrated,
      PbcSeed.Actor(own.Admin, "Administrator"), (await migrated.ClientPortalIntents
        .AsNoTracking().SingleAsync(x => x.Id == ownIntentId)).PracticeClientId);
    Assert.True(ownWorkspace.Succeeded, ownWorkspace.Message);
    Assert.Equal(ownRecipientEmail, ownWorkspace.Value!.PortalIntent!.RecipientEmail);
    Assert.NotEqual(foreignContactId, ownContactId);
  }

  [Fact]
  public async Task SharedDeliverables_ExcludeCrossFirmReviewAndCommentRows()
  {
    const string previousMigration = "20261101024300_ClientPortalIntentContactScope";
    await using var pg = await PgTestSchema.CreateAsync(previousMigration);
    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
    {
      var migrations = migrationDb.Database.GetMigrations().ToArray();
      Assert.Contains(previousMigration, migrations);
      if (!string.Equals(migrations[^1], previousMigration, StringComparison.Ordinal))
        await migrationDb.GetService<IMigrator>().MigrateAsync(previousMigration);
    }

    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    static AuditDeliverable SyntheticHeader(PbcSeed.Fixture fixture, string marker, char hash) => new()
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      Kind = DeliverableKinds.ManagementLetter, Version = 1, TemplateVersion = "STRUCTURAL-TEST",
      InputDigest = new string(hash, 64), InputSummaryJson = "{}", FileName = $"{marker}.txt", ContentType = "text/plain",
      Content = [1, 2, 3], ContentSha256 = new string(hash, 64), CreatedByUserId = fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
    };

    // These are structural headers only; this test does not generate, issue, sign, or approve a report.
    var ownDeliverable = SyntheticHeader(own, "own-synthetic-deliverable", 'a');
    var foreignDeliverable = SyntheticHeader(foreign, "foreign-synthetic-deliverable", 'b');
    var now = DateTimeOffset.UtcNow;
    var ownReview = new ClientDeliverableReview
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ClientId = own.ClientId, EngagementId = own.EngagementId,
      DeliverableId = ownDeliverable.Id, SharedByUserId = own.Staff.Id, SharedAt = now
    };
    const string sameFirmComment = "same-firm synthetic review comment";
    var ownComment = new ClientDeliverableComment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ReviewId = ownReview.Id, Body = sameFirmComment,
      AuthorUserId = own.Client.Id, FromClient = true, CreatedAt = now
    };
    await using (var seedDb = new AuditSphereDbContext(pg.Options))
    {
      seedDb.AuditDeliverables.AddRange(ownDeliverable, foreignDeliverable);
      seedDb.ClientDeliverableReviews.Add(ownReview);
      seedDb.ClientDeliverableComments.Add(ownComment);
      await seedDb.SaveChangesAsync();
      await seedDb.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_reviews
          (id, firm_id, client_id, engagement_id, deliverable_id, shared_by_user_id, shared_at,
           acknowledged_by_user_id, acknowledged_at, acknowledged_sha256)
        VALUES ({Guid.NewGuid()}, {own.FirmId}, {own.ClientId}, {own.EngagementId}, {foreignDeliverable.Id},
                {own.Staff.Id}, {now}, NULL, NULL, NULL)
        """);
      await seedDb.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_comments
          (id, firm_id, review_id, body, author_user_id, from_client, created_at,
           resolved_by_user_id, resolved_at, resolution, request_id, request_sha256)
        VALUES ({Guid.NewGuid()}, {foreign.FirmId}, {ownReview.Id}, {"foreign-firm comment marker"},
                {foreign.Client.Id}, TRUE, {now}, NULL, NULL, NULL, NULL, NULL)
        """);
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var shared = await AuditDeliverableService.SharedAsync(db, PbcSeed.Actor(own.Client, "ClientUser"), own.EngagementId);
    Assert.Contains(shared, x => x.DeliverableId == ownDeliverable.Id && x.ContentSha256 == ownDeliverable.ContentSha256);
    var ownView = Assert.Single(shared, x => x.ReviewId == ownReview.Id);
    Assert.Contains(ownView.Comments, x => x.Body == sameFirmComment);

    var foreignDeliverableMetadataLeaked = shared.Any(x => x.DeliverableId == foreignDeliverable.Id && x.ContentSha256 == foreignDeliverable.ContentSha256);
    var foreignCommentLeaked = ownView.Comments.Any(x => x.Body == "foreign-firm comment marker");
    Assert.True(!foreignDeliverableMetadataLeaked && !foreignCommentLeaked,
      $"Cross-firm portal data leaked: deliverableMetadata={foreignDeliverableMetadataLeaked}; comment={foreignCommentLeaked}.");
  }

  [Fact]
  public async Task ClientDeliverableScopeUpgradeAndRollbackPreserveHistoryAndRejectInvalidLinks()
  {
    const string previousMigration = "20261101024300_ClientPortalIntentContactScope";
    const string scopeMigration = "20261101041254_ClientDeliverableReviewScope";

    static async Task<string[]> ReadRowsAsync(PgTestSchema pg, string sql)
    {
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using var command = new NpgsqlCommand(sql, connection);
      await using var reader = await command.ExecuteReaderAsync();
      var values = new List<string>();
      while (await reader.ReadAsync()) values.Add(reader.GetString(0));
      return values.ToArray();
    }

    static Task<string[]> ReadForeignKeysAsync(PgTestSchema pg) => ReadRowsAsync(pg, """
      SELECT conrelid::regclass::text || '|' || conname || '|' || pg_get_constraintdef(oid)
      FROM pg_constraint
      WHERE contype = 'f' AND conrelid IN ('client_deliverable_reviews'::regclass, 'client_deliverable_comments'::regclass)
      ORDER BY conrelid::regclass::text, conname
      """);

    static Task<string[]> ReadIndexesAsync(PgTestSchema pg) => ReadRowsAsync(pg, """
      SELECT tablename || '|' || indexname || '|' || indexdef
      FROM pg_indexes
      WHERE schemaname = current_schema() AND tablename IN ('client_deliverable_reviews', 'client_deliverable_comments')
      ORDER BY tablename, indexname
      """);

    static async Task<string> ReadLatestMigrationAsync(PgTestSchema pg)
    {
      var rows = await ReadRowsAsync(pg,
        "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1");
      return Assert.Single(rows);
    }

    static async Task MigrateAsync(PgTestSchema pg, string migration)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    static AuditDeliverable SyntheticHeader(PbcSeed.Fixture fixture, string marker, char hash,
      int version = 1, Guid? clientId = null, Guid? engagementId = null) => new()
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = clientId ?? fixture.ClientId,
      EngagementId = engagementId ?? fixture.EngagementId,
      Kind = DeliverableKinds.ManagementLetter, Version = version, TemplateVersion = "STRUCTURAL-TEST",
      InputDigest = new string(hash, 64), InputSummaryJson = "{}", FileName = $"{marker}.txt", ContentType = "text/plain",
      Content = [1, 2, 3], ContentSha256 = new string(hash, 64), CreatedByUserId = fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
    };

    await using var pg = await PgTestSchema.CreateAsync(previousMigration);
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    Assert.NotEqual(own.FirmId, foreign.FirmId);
    var ownDeliverable = SyntheticHeader(own, "own-synthetic-deliverable", 'a');
    var foreignDeliverable = SyntheticHeader(foreign, "foreign-synthetic-deliverable", 'b');
    var otherClientDeliverable = SyntheticHeader(own, "same-firm-other-client", 'c', version: 2, clientId: Guid.NewGuid());
    var siblingEngagementDeliverable = SyntheticHeader(own, "same-client-sibling-engagement", 'd', engagementId: Guid.NewGuid());
    var childFirmMismatchDeliverable = SyntheticHeader(own, "child-firm-mismatch", 'e', version: 3);
    var childClientMismatchDeliverable = SyntheticHeader(own, "child-client-mismatch", 'f', version: 4);
    var childEngagementMismatchDeliverable = SyntheticHeader(own, "child-engagement-mismatch", 'g', version: 5);
    var now = DateTimeOffset.UtcNow;
    var ownReview = new ClientDeliverableReview
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ClientId = own.ClientId, EngagementId = own.EngagementId,
      DeliverableId = ownDeliverable.Id, SharedByUserId = own.Staff.Id, SharedAt = now
    };
    var ownComment = new ClientDeliverableComment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ReviewId = ownReview.Id, Body = "same-firm synthetic review comment",
      AuthorUserId = own.Client.Id, FromClient = true, CreatedAt = now
    };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AuditDeliverables.AddRange(ownDeliverable, foreignDeliverable, otherClientDeliverable,
        siblingEngagementDeliverable, childFirmMismatchDeliverable, childClientMismatchDeliverable,
        childEngagementMismatchDeliverable);
      db.ClientDeliverableReviews.Add(ownReview);
      db.ClientDeliverableComments.Add(ownComment);
      await db.SaveChangesAsync();
    }

    var originalReviewRows = await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id");
    var originalCommentRows = await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id");
    Assert.Single(originalReviewRows);
    Assert.Single(originalCommentRows);
    var originalForeignKeys = await ReadForeignKeysAsync(pg);
    var originalIndexes = await ReadIndexesAsync(pg);
    Assert.Contains(originalForeignKeys, x => x.Contains("client_deliverable_reviews|FK_client_deliverable_reviews_audit_deliverables_deliverable_id|", StringComparison.Ordinal));
    Assert.Contains(originalForeignKeys, x => x.Contains("client_deliverable_comments|FK_client_deliverable_comments_client_deliverable_reviews_revi", StringComparison.Ordinal));

    var invalidReviewId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_reviews
          (id, firm_id, client_id, engagement_id, deliverable_id, shared_by_user_id, shared_at,
           acknowledged_by_user_id, acknowledged_at, acknowledged_sha256)
        VALUES ({invalidReviewId}, {own.FirmId}, {own.ClientId}, {own.EngagementId}, {foreignDeliverable.Id},
                {own.Staff.Id}, {now}, NULL, NULL, NULL)
        """);
    var invalidReviewRows = await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id");
    Assert.Equal(2, invalidReviewRows.Length);
    var reviewRefusal = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(pg, scopeMigration));
    Assert.Equal("23503", reviewRefusal.SqlState);
    Assert.Equal("fk_client_deliverable_review_deliverable_scope", reviewRefusal.ConstraintName);
    output.WriteLine($"INVALID_REVIEW: REFUSED SqlState={reviewRefusal.SqlState} Constraint={reviewRefusal.ConstraintName}");
    Assert.Equal(previousMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(invalidReviewRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id"));
    Assert.Equal(originalCommentRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id"));
    Assert.Equal(originalForeignKeys, await ReadForeignKeysAsync(pg));
    Assert.Equal(originalIndexes, await ReadIndexesAsync(pg));

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM client_deliverable_reviews WHERE id = {invalidReviewId}");
    Assert.Equal(originalReviewRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id"));

    var invalidCommentId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_comments
          (id, firm_id, review_id, body, author_user_id, from_client, created_at,
           resolved_by_user_id, resolved_at, resolution, request_id, request_sha256)
        VALUES ({invalidCommentId}, {foreign.FirmId}, {ownReview.Id}, {"foreign-firm comment marker"},
                {foreign.Client.Id}, TRUE, {now}, NULL, NULL, NULL, NULL, NULL)
        """);
    var invalidCommentRows = await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id");
    Assert.Equal(2, invalidCommentRows.Length);
    var commentRefusal = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(pg, scopeMigration));
    Assert.Equal("23503", commentRefusal.SqlState);
    Assert.Equal("fk_client_deliverable_comment_review_scope", commentRefusal.ConstraintName);
    output.WriteLine($"INVALID_COMMENT: REFUSED SqlState={commentRefusal.SqlState} Constraint={commentRefusal.ConstraintName}");
    Assert.Equal(previousMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(originalReviewRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id"));
    Assert.Equal(invalidCommentRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id"));
    Assert.Equal(originalForeignKeys, await ReadForeignKeysAsync(pg));
    Assert.Equal(originalIndexes, await ReadIndexesAsync(pg));

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM client_deliverable_comments WHERE id = {invalidCommentId}");
    Assert.Equal(originalCommentRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id"));

    await MigrateAsync(pg, scopeMigration);
    Assert.Equal(scopeMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(originalReviewRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id"));
    Assert.Equal(originalCommentRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id"));
    var scopedForeignKeys = await ReadForeignKeysAsync(pg);
    Assert.Contains(scopedForeignKeys, x => x.StartsWith("client_deliverable_reviews|fk_client_deliverable_review_deliverable_scope|", StringComparison.Ordinal)
      && x.Contains("FOREIGN KEY (firm_id, client_id, engagement_id, deliverable_id) REFERENCES audit_deliverables(firm_id, client_id, engagement_id, id) ON DELETE RESTRICT", StringComparison.Ordinal));
    Assert.Contains(scopedForeignKeys, x => x.StartsWith("client_deliverable_comments|fk_client_deliverable_comment_review_scope|", StringComparison.Ordinal)
      && x.Contains("FOREIGN KEY (firm_id, review_id) REFERENCES client_deliverable_reviews(firm_id, id) ON DELETE RESTRICT", StringComparison.Ordinal));
    Assert.Equal(originalForeignKeys, scopedForeignKeys.Where(x =>
      !x.Contains("|fk_client_deliverable_review_deliverable_scope|", StringComparison.Ordinal)
      && !x.Contains("|fk_client_deliverable_comment_review_scope|", StringComparison.Ordinal)).ToArray());
    var scopedIndexes = await ReadIndexesAsync(pg);
    var addedIndexes = scopedIndexes.Except(originalIndexes).ToArray();
    Assert.Equal(2, addedIndexes.Length);
    var newSupportingIndex = Assert.Single(addedIndexes, x => x.Contains("firm_id, client_id, engagement_id, deliverable_id", StringComparison.Ordinal));
    Assert.Contains("firm_id, client_id, engagement_id, deliverable_id", newSupportingIndex, StringComparison.Ordinal);
    Assert.Contains(addedIndexes, x => x.Contains("CREATE UNIQUE INDEX", StringComparison.Ordinal)
      && x.Contains("firm_id, id", StringComparison.Ordinal));
    Assert.Equal(originalIndexes, scopedIndexes.Except(addedIndexes).ToArray());

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var postMigrationReviewRefusal = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_reviews
          (id, firm_id, client_id, engagement_id, deliverable_id, shared_by_user_id, shared_at,
           acknowledged_by_user_id, acknowledged_at, acknowledged_sha256)
        VALUES ({invalidReviewId}, {own.FirmId}, {own.ClientId}, {own.EngagementId}, {foreignDeliverable.Id},
                {own.Staff.Id}, {now}, NULL, NULL, NULL)
        """));
      Assert.Equal("23503", postMigrationReviewRefusal.SqlState);
      Assert.Equal("fk_client_deliverable_review_deliverable_scope", postMigrationReviewRefusal.ConstraintName);
      var postMigrationCommentRefusal = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_comments
          (id, firm_id, review_id, body, author_user_id, from_client, created_at,
           resolved_by_user_id, resolved_at, resolution, request_id, request_sha256)
        VALUES ({invalidCommentId}, {foreign.FirmId}, {ownReview.Id}, {"foreign-firm comment marker"},
                {foreign.Client.Id}, TRUE, {now}, NULL, NULL, NULL, NULL, NULL)
        """));
      Assert.Equal("23503", postMigrationCommentRefusal.SqlState);
      Assert.Equal("fk_client_deliverable_comment_review_scope", postMigrationCommentRefusal.ConstraintName);
    }

    var independentScopeMismatches = new[]
    {
      (Name: "parent-other-client", FirmId: own.FirmId, ClientId: own.ClientId, EngagementId: own.EngagementId,
        DeliverableId: otherClientDeliverable.Id),
      (Name: "parent-sibling-engagement", FirmId: own.FirmId, ClientId: own.ClientId, EngagementId: own.EngagementId,
        DeliverableId: siblingEngagementDeliverable.Id),
      (Name: "child-firm-only", FirmId: foreign.FirmId, ClientId: own.ClientId, EngagementId: own.EngagementId,
        DeliverableId: childFirmMismatchDeliverable.Id),
      (Name: "child-client-only", FirmId: own.FirmId, ClientId: foreign.ClientId, EngagementId: own.EngagementId,
        DeliverableId: childClientMismatchDeliverable.Id),
      (Name: "child-engagement-only", FirmId: own.FirmId, ClientId: own.ClientId, EngagementId: foreign.EngagementId,
        DeliverableId: childEngagementMismatchDeliverable.Id)
    };
    foreach (var mismatch in independentScopeMismatches)
    {
      var mismatchedId = Guid.NewGuid();
      await using var db = new AuditSphereDbContext(pg.Options);
      var refusal = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_deliverable_reviews
          (id, firm_id, client_id, engagement_id, deliverable_id, shared_by_user_id, shared_at,
           acknowledged_by_user_id, acknowledged_at, acknowledged_sha256)
        VALUES ({mismatchedId}, {mismatch.FirmId}, {mismatch.ClientId}, {mismatch.EngagementId}, {mismatch.DeliverableId},
                {own.Staff.Id}, {now}, NULL, NULL, NULL)
        """));
      Assert.Equal("23503", refusal.SqlState);
      Assert.Equal("fk_client_deliverable_review_deliverable_scope", refusal.ConstraintName);
      Assert.False(await db.ClientDeliverableReviews.AnyAsync(x => x.Id == mismatchedId));
      output.WriteLine($"{mismatch.Name}: REFUSED SqlState={refusal.SqlState} Constraint={refusal.ConstraintName}");
    }

    await MigrateAsync(pg, previousMigration);
    Assert.Equal(previousMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(originalReviewRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id"));
    Assert.Equal(originalCommentRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id"));
    Assert.Equal(originalForeignKeys, await ReadForeignKeysAsync(pg));
    Assert.Equal(originalIndexes, await ReadIndexesAsync(pg));

    await MigrateAsync(pg, scopeMigration);
    Assert.Equal(scopeMigration, await ReadLatestMigrationAsync(pg));
    Assert.Equal(originalReviewRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(r)::text FROM client_deliverable_reviews r ORDER BY id"));
    Assert.Equal(originalCommentRows, await ReadRowsAsync(pg,
      "SELECT to_jsonb(c)::text FROM client_deliverable_comments c ORDER BY id"));
  }

  [Fact]
  public async Task MigratedSchema_ForeignKeysBetweenFirmScopedTablesCarryTheFirm()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    const string sql = """
      SELECT c.conname, child.relname, parent.relname,
             CASE WHEN cardinality(c.conkey) = 1 THEN (SELECT attname FROM pg_attribute WHERE attrelid = c.conrelid AND attnum = c.conkey[1]) END,
             CASE WHEN cardinality(c.confkey) = 1 THEN (SELECT attname FROM pg_attribute WHERE attrelid = c.confrelid AND attnum = c.confkey[1]) END,
             EXISTS (
               SELECT 1
               FROM unnest(c.conkey, c.confkey) AS key_pair(child_attnum, parent_attnum)
               JOIN pg_attribute child_scope
                 ON child_scope.attrelid = c.conrelid AND child_scope.attnum = key_pair.child_attnum
               JOIN pg_attribute parent_scope
                 ON parent_scope.attrelid = c.confrelid AND parent_scope.attnum = key_pair.parent_attnum
               WHERE child_scope.attname = 'firm_id' AND parent_scope.attname = 'firm_id'
             ) AS carries_firm
      FROM pg_constraint c
      JOIN pg_class child ON child.oid = c.conrelid
      JOIN pg_class parent ON parent.oid = c.confrelid
      JOIN pg_namespace n ON n.oid = child.relnamespace
      WHERE c.contype = 'f' AND n.nspname = current_schema()
        AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attname = 'firm_id' AND NOT a.attisdropped)
        AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.confrelid AND a.attname = 'firm_id' AND NOT a.attisdropped)
        -- Treat a key as scope-bound only when a second FK preserves every child-to-parent column mapping
        -- and explicitly maps firm_id to the parent firm_id. Merely sharing child columns with some other FK
        -- can otherwise hide an unscoped reference to a different unique key on the same parent table.
        AND NOT EXISTS (
          SELECT 1
          FROM pg_constraint o
          WHERE o.contype = 'f' AND o.oid <> c.oid
            AND o.conrelid = c.conrelid AND o.confrelid = c.confrelid
            AND EXISTS (
              SELECT 1
              FROM unnest(o.conkey, o.confkey) AS scoped(child_attnum, parent_attnum)
              JOIN pg_attribute child_scope
                ON child_scope.attrelid = o.conrelid AND child_scope.attnum = scoped.child_attnum
              JOIN pg_attribute parent_scope
                ON parent_scope.attrelid = o.confrelid AND parent_scope.attnum = scoped.parent_attnum
              WHERE child_scope.attname = 'firm_id' AND parent_scope.attname = 'firm_id'
            )
            AND NOT EXISTS (
              SELECT 1
              FROM unnest(c.conkey, c.confkey) AS direct(child_attnum, parent_attnum)
              WHERE NOT EXISTS (
                SELECT 1
                FROM unnest(o.conkey, o.confkey) AS combined(child_attnum, parent_attnum)
                WHERE combined.child_attnum = direct.child_attnum
                  AND combined.parent_attnum = direct.parent_attnum
              )
            )
        )
      ORDER BY child.relname, c.conname
      """;
    var scoped = 0;
    var unscoped = new List<string>();
    var probes = new List<(string Constraint, string Child, string Column, string Parent, string Reference)>();
    await using (var command = new NpgsqlCommand(sql, connection))
    await using (var reader = await command.ExecuteReaderAsync())
      while (await reader.ReadAsync())
      {
        scoped++;
        if (reader.GetBoolean(5)) continue;
        unscoped.Add($"{reader.GetString(1)} -> {reader.GetString(2)} ({reader.GetString(0)})");
        if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
          probes.Add((reader.GetString(0), reader.GetString(1), reader.GetString(3), reader.GetString(2), reader.GetString(4)));
      }
    output.WriteLine($"Foreign keys between firm-scoped tables: {scoped}; without exact firm_id mapping: {unscoped.Count}");
    Assert.True(scoped > 20, $"Only {scoped} firm-scoped foreign keys found; the probe would be vacuous.");

    // Probe each single-column key: copy a seeded child row, repoint it at another firm's parent and observe
    // whether the database refuses it. Every probe is rolled back; nothing is committed.
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var ownSpareBudgetId = await SeedBudgetChildRowsAsync(pg, own);
    var foreignSpareBudgetId = await SeedBudgetChildRowsAsync(pg, foreign);
    var accepted = new List<string>();
    var notProbed = new List<string>();
    var unexpectedRefusals = new List<string>();
    var refusedByTargetForeignKey = 0;
    await using var transaction = await connection.BeginTransactionAsync();
    await new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE", connection, transaction).ExecuteNonQueryAsync();
    foreach (var (constraint, child, column, parent, reference) in probes)
    {
      await new NpgsqlCommand("SAVEPOINT probe", connection, transaction).ExecuteNonQueryAsync();
      var probe = $"""
        INSERT INTO "{child}"
        SELECT (jsonb_populate_record(NULL::"{child}", to_jsonb(c) || jsonb_build_object(
          '{column}', (SELECT p."{reference}" FROM "{parent}" p WHERE p.firm_id <> c.firm_id LIMIT 1), 'id', gen_random_uuid()))).*
        FROM "{child}" c
        WHERE c.firm_id IS NOT NULL AND EXISTS (SELECT 1 FROM "{parent}" p WHERE p.firm_id <> c.firm_id)
        LIMIT 1
        """;
      string outcome;
      try
      {
        var rows = await new NpgsqlCommand(probe, connection, transaction).ExecuteNonQueryAsync();
        outcome = rows == 0 ? "NO_FIXTURE_ROW" : "ACCEPTED_CROSS_FIRM";
        if (rows == 0) notProbed.Add($"{child}.{column} -> {parent}");
        else accepted.Add($"{child}.{column} -> {parent}");
      }
      catch (PostgresException exception)
      {
        if (exception.SqlState == "23503" && exception.ConstraintName == constraint)
        {
          refusedByTargetForeignKey++;
          outcome = $"REFUSED_BY_TARGET_FK {exception.SqlState} {constraint}";
        }
        else
        {
          unexpectedRefusals.Add($"{child}.{column} -> {parent}: {exception.SqlState} {exception.ConstraintName ?? "(no constraint)"}");
          outcome = $"UNEXPECTED_REFUSAL {exception.SqlState} {exception.ConstraintName ?? "(no constraint)"}";
        }
      }
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT probe", connection, transaction).ExecuteNonQueryAsync();
      output.WriteLine($"PROBE {child}.{column} -> {parent}: {outcome}");
    }
    foreach (var child in new[] { "budget_approvals", "budget_preparations" })
    {
      await AssertBudgetReceiptCrossFirmReferenceRefusedAsync(connection, transaction, child, "budget_id", own,
        foreign, ownSpareBudgetId, foreignSpareBudgetId);
      await AssertBudgetReceiptCrossFirmReferenceRefusedAsync(connection, transaction, child, "engagement_id", own,
        foreign, ownSpareBudgetId, foreignSpareBudgetId);
    }
    await transaction.RollbackAsync();
    output.WriteLine($"Single-column scoped-key probes: discovered {probes.Count}; refused by target FK {refusedByTargetForeignKey}; " +
      $"accepted cross-firm {accepted.Count}; no fixture row {notProbed.Count}; unexpected refusal {unexpectedRefusals.Count}");
    Assert.True(accepted.Count == 0, "Cross-firm references accepted: " + string.Join(", ", accepted));
    Assert.True(unexpectedRefusals.Count == 0, "Probe blocked before the target FK: " + string.Join("; ", unexpectedRefusals));
  }

  [Fact]
  public async Task TechnicalLibraryVersions_RejectForeignFirmDocumentReferences()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    async Task<Guid> PublishAsync(PbcSeed.Fixture fixture)
    {
      var publisher = PbcSeed.User(fixture.FirmId, "Staff");
      db.Users.Add(publisher);
      db.RoleGrants.Add(PbcSeed.AdminGrant(fixture.FirmId, publisher));
      await db.SaveChangesAsync();
      var created = await TechnicalLibraryService.CreateAsync(db, PbcSeed.Actor(fixture.Admin, "Administrator"),
        "SCOPE-PROBE", "Synthetic library scope fixture", TechnicalLibraryCategories.FirmGuidance,
        TechnicalLibraryAudiences.AllStaff, "Synthetic library content for a scoped-parent test.",
        "test://library-scoped-parent", new DateOnly(2026, 1, 1));
      Assert.True(created.Succeeded, created.Message);
      var version = await db.TechnicalLibraryVersions.AsNoTracking().SingleAsync(x => x.DocumentId == created.Value);
      var published = await TechnicalLibraryService.PublishAsync(db, PbcSeed.Actor(publisher, "Administrator"), version.Id);
      Assert.True(published.Succeeded, published.Message);
      return created.Value;
    }

    var ownDocument = await PublishAsync(own);
    var foreignDocument = await PublishAsync(foreign);
    var before = JsonSerializer.Serialize(await db.TechnicalLibraryVersions.AsNoTracking().OrderBy(x => x.Id).ToListAsync());
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    async Task<int> InsertAsync(Guid documentId)
    {
      await using var command = new NpgsqlCommand("""
        INSERT INTO technical_library_versions
        SELECT (jsonb_populate_record(NULL::technical_library_versions, to_jsonb(v) || jsonb_build_object(
          'id', @id, 'document_id', @document_id, 'version', 2, 'status', 'SUPERSEDED'))).*
        FROM technical_library_versions v WHERE v.document_id = @source_document_id AND v.version = 1
        """, connection, transaction);
      command.Parameters.AddWithValue("id", Guid.NewGuid());
      command.Parameters.AddWithValue("document_id", documentId);
      command.Parameters.AddWithValue("source_document_id", ownDocument);
      return await command.ExecuteNonQueryAsync();
    }

    await new NpgsqlCommand("SAVEPOINT library_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    Assert.Equal(1, await InsertAsync(ownDocument));
    output.WriteLine("SAME_FIRM_DOCUMENT: ACCEPTED rows=1");
    await new NpgsqlCommand("ROLLBACK TO SAVEPOINT library_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(foreignDocument));
    Assert.Equal("23503", exception.SqlState);
    Assert.Equal("fk_technical_library_version_document_scope", exception.ConstraintName);
    output.WriteLine($"FOREIGN_DOCUMENT: REFUSED SqlState={exception.SqlState} Constraint={exception.ConstraintName}");
    await transaction.RollbackAsync();
    Assert.Equal(before, JsonSerializer.Serialize(await db.TechnicalLibraryVersions.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
  }

  [Fact]
  public async Task TechnicalLibraryScopeUpgrade_RefusesInconsistentHistoryWithoutChangingIt()
  {
    const string baseline = "20261028154245_ItemTestReviewSequence";
    await using var pg = await PgTestSchema.CreateAsync(baseline);
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var created = await TechnicalLibraryService.CreateAsync(db, PbcSeed.Actor(own.Admin, "Administrator"),
      "UPGRADE-PROBE", "Synthetic retained library draft", TechnicalLibraryCategories.FirmGuidance,
      TechnicalLibraryAudiences.AllStaff, "Synthetic retained draft for migration refusal.",
      "test://library-scope-upgrade", new DateOnly(2026, 1, 1));
    Assert.True(created.Succeeded, created.Message);
    var foreignDocument = new TechnicalLibraryDocument
    {
      Id = Guid.NewGuid(), FirmId = foreign.FirmId, Code = "FOREIGN-PARENT", Title = "Synthetic foreign document",
      Category = TechnicalLibraryCategories.FirmGuidance, Audience = TechnicalLibraryAudiences.AllStaff,
      CreatedByUserId = foreign.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    db.TechnicalLibraryDocuments.Add(foreignDocument);
    await db.SaveChangesAsync();
    Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO technical_library_versions
      SELECT (jsonb_populate_record(NULL::technical_library_versions, to_jsonb(v) || jsonb_build_object(
        'id', {Guid.NewGuid()}, 'document_id', {foreignDocument.Id}))).*
      FROM technical_library_versions v WHERE v.document_id = {created.Value}
      """));
    var before = JsonSerializer.Serialize(await db.TechnicalLibraryVersions.AsNoTracking().OrderBy(x => x.Id).ToListAsync());

    var exception = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
      .MigrateAsync("20261029031001_TechnicalLibraryVersionFirmScope"));
    Assert.Equal("23503", exception.SqlState);
    Assert.Equal("fk_technical_library_version_document_scope", exception.ConstraintName);
    Assert.Equal(before, JsonSerializer.Serialize(await db.TechnicalLibraryVersions.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
    Assert.Equal(baseline, (await db.Database.GetAppliedMigrationsAsync()).Last());
    var constraints = await db.Database.SqlQueryRaw<string>("""
      SELECT conname AS "Value" FROM pg_constraint
      WHERE conrelid = 'technical_library_versions'::regclass AND contype = 'f'
      """).ToListAsync();
    Assert.Contains("FK_technical_library_versions_technical_library_documents_docu~", constraints);
    Assert.DoesNotContain("fk_technical_library_version_document_scope", constraints);
    output.WriteLine("INCONSISTENT_HISTORY: upgrade refused; retained bytes, prior migration and original FK unchanged");
  }

  [Fact]
  public async Task TechnicalLibraryMetadataUpgrade_PreservesDraftsAndRefusesDowngradeAfterPublication()
  {
    const string baseline = "20261029031001_TechnicalLibraryVersionFirmScope";
    const string protectedMetadata = "20261029034738_TechnicalLibraryRetainedMetadata";
    await using var pg = await PgTestSchema.CreateAsync(baseline);
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var created = await TechnicalLibraryService.CreateAsync(db, PbcSeed.Actor(fixture.Admin, "Administrator"),
      "METADATA-UPGRADE", "Synthetic retained metadata fixture", TechnicalLibraryCategories.FirmGuidance,
      TechnicalLibraryAudiences.AllStaff, "Synthetic version for metadata upgrade and downgrade safety.",
      "test://library-metadata-upgrade", new DateOnly(2026, 1, 1));
    Assert.True(created.Succeeded, created.Message);
    var draft = await db.TechnicalLibraryVersions.AsNoTracking().SingleAsync(x => x.DocumentId == created.Value);
    async Task<string> ReadVersionAsync() => await db.Database.SqlQuery<string>($"""
      SELECT to_jsonb(v)::text AS "Value" FROM technical_library_versions v WHERE id = {draft.Id}
      """).SingleAsync();
    var before = await ReadVersionAsync();
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync(protectedMetadata);
    Assert.Equal(before, await ReadVersionAsync());
    await migrator.MigrateAsync(baseline);
    Assert.Equal(baseline, (await db.Database.GetAppliedMigrationsAsync()).Last());
    Assert.Equal(before, await ReadVersionAsync());

    var publisher = PbcSeed.User(fixture.FirmId, "Staff");
    db.Users.Add(publisher);
    db.RoleGrants.Add(PbcSeed.AdminGrant(fixture.FirmId, publisher));
    await db.SaveChangesAsync();
    var published = await TechnicalLibraryService.PublishAsync(db, PbcSeed.Actor(publisher, "Administrator"), draft.Id);
    Assert.True(published.Succeeded, published.Message);
    var retained = await ReadVersionAsync();
    await migrator.MigrateAsync(protectedMetadata);
    Assert.Equal(retained, await ReadVersionAsync());
    var functionBefore = await db.Database.SqlQueryRaw<string>("""
      SELECT pg_get_functiondef('protect_published_library_versions()'::regprocedure) AS "Value"
      """).SingleAsync();
    var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(baseline));
    Assert.Equal("P0001", exception.SqlState);
    Assert.Equal("cannot downgrade retained library version protection", exception.MessageText);
    Assert.Equal(protectedMetadata, (await db.Database.GetAppliedMigrationsAsync()).Last());
    Assert.Equal(retained, await ReadVersionAsync());
    Assert.Equal(functionBefore, await db.Database.SqlQueryRaw<string>("""
      SELECT pg_get_functiondef('protect_published_library_versions()'::regprocedure) AS "Value"
      """).SingleAsync());
    output.WriteLine("UPGRADE/DRAFT_DOWN_UP: retained bytes unchanged; PUBLISHED_DOWN: refused with current guard and history intact");
  }

  [Fact]
  public async Task StaffingChangeReceipts_RejectForeignFirmParentsBeforePublication()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    async Task<(StaffingChange Row, StaffingChangePreview Probe)> AssignAsync(PbcSeed.Fixture fixture)
    {
      var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
      var request = new StaffingChangeRequest(Guid.NewGuid(),
        new("ASSIGN", fixture.Reviewer.Id, StaffingLevels.StaffAssociate));
      var preview = await StaffingChangeWorkspace.PreviewAsync(db, actor, fixture.EngagementId, request);
      Assert.True(preview.Succeeded, preview.Message);
      var probe = await StaffingChangeWorkspace.PreviewAsync(db, actor, fixture.EngagementId,
        request with { RequestId = Guid.NewGuid() });
      Assert.True(probe.Succeeded, probe.Message);
      var published = await StaffingChangeWorkspace.ExecuteAsync(db, actor, fixture.EngagementId,
        request with { Reviewed = true, RequestHash = preview.Value!.RequestHash, ReviewBasis = preview.Value.ReviewBasis });
      Assert.True(published.Succeeded, published.Message);
      return (await db.StaffingChanges.AsNoTracking().SingleAsync(x => x.Id == published.Value!.Id), probe.Value!);
    }

    var original = await AssignAsync(own);
    var other = await AssignAsync(foreign);
    var before = JsonSerializer.Serialize(await db.StaffingChanges.AsNoTracking().OrderBy(x => x.Id).ToListAsync());
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE", connection, transaction).ExecuteNonQueryAsync();

    async Task<int> InsertAsync(Guid engagementId, Guid assignmentId)
    {
      await using var command = new NpgsqlCommand("""
        INSERT INTO staffing_changes
        SELECT (jsonb_populate_record(NULL::staffing_changes, to_jsonb(c) || jsonb_build_object(
          'id', @id, 'request_id', @request_id, 'request_hash', @request_hash,
          'review_basis', @review_basis, 'engagement_id', @engagement_id,
          'assignment_id', @assignment_id, 'preview_json', @preview_json))).*
        FROM staffing_changes c WHERE c.id = @source_id
        """, connection, transaction);
      command.Parameters.AddWithValue("id", Guid.NewGuid());
      command.Parameters.AddWithValue("request_id", original.Probe.RequestId);
      command.Parameters.AddWithValue("request_hash", original.Probe.RequestHash);
      command.Parameters.AddWithValue("review_basis", original.Probe.ReviewBasis);
      command.Parameters.AddWithValue("engagement_id", engagementId);
      command.Parameters.AddWithValue("assignment_id", assignmentId);
      command.Parameters.AddWithValue("preview_json", JsonSerializer.Serialize(original.Probe with { EngagementId = engagementId }));
      command.Parameters.AddWithValue("source_id", original.Row.Id);
      return await command.ExecuteNonQueryAsync();
    }

    await new NpgsqlCommand("SAVEPOINT staffing_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    Assert.Equal(1, await InsertAsync(own.EngagementId, original.Row.AssignmentId));
    output.WriteLine("SAME_FIRM_CURRENT_ASSIGNMENT: ACCEPTED rows=1");
    await new NpgsqlCommand("ROLLBACK TO SAVEPOINT staffing_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    foreach (var (label, engagementId, assignmentId) in new[]
    {
      ("FOREIGN_ENGAGEMENT", foreign.EngagementId, original.Row.AssignmentId),
      ("FOREIGN_ASSIGNMENT", own.EngagementId, other.Row.AssignmentId)
    })
    {
      var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(engagementId, assignmentId));
      Assert.Equal("23514", exception.SqlState);
      Assert.Equal("Staffing change requires exact authorized committed assignment", exception.MessageText);
      output.WriteLine($"{label}: REFUSED SqlState={exception.SqlState} publication guard");
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT staffing_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    }
    await transaction.RollbackAsync();
    Assert.Equal(before, JsonSerializer.Serialize(await db.StaffingChanges.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
  }

  private static async Task AssertBudgetReceiptCrossFirmReferenceRefusedAsync(
    NpgsqlConnection connection, NpgsqlTransaction transaction, string child, string column,
    PbcSeed.Fixture own, PbcSeed.Fixture foreign, Guid ownSpareBudgetId, Guid foreignSpareBudgetId)
  {
    var budgetId = column == "budget_id" ? foreignSpareBudgetId : ownSpareBudgetId;
    var engagementId = column == "engagement_id" ? foreign.EngagementId : own.EngagementId;
    var expectedConstraint = $"FK_{child}_{(column == "budget_id" ? "engagement_budgets" : "engagements")}_firm_id_{column}";
    await new NpgsqlCommand("SAVEPOINT budget_receipt_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    var insert = new NpgsqlCommand($"""
      INSERT INTO "{child}"
      SELECT (jsonb_populate_record(NULL::"{child}", to_jsonb(c) || jsonb_build_object(
        'budget_id', @budget_id, 'engagement_id', @engagement_id,
        'request_id', gen_random_uuid(), 'id', gen_random_uuid()))).*
      FROM "{child}" c
      WHERE c.firm_id = @firm_id
      LIMIT 1
      """, connection, transaction);
    insert.Parameters.AddWithValue("budget_id", budgetId);
    insert.Parameters.AddWithValue("engagement_id", engagementId);
    insert.Parameters.AddWithValue("firm_id", own.FirmId);
    PostgresException? observed = null;
    try { await insert.ExecuteNonQueryAsync(); }
    catch (PostgresException exception) { observed = exception; }
    await new NpgsqlCommand("ROLLBACK TO SAVEPOINT budget_receipt_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    await new NpgsqlCommand("RELEASE SAVEPOINT budget_receipt_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    Assert.True(observed is not null, $"{child}.{column} unexpectedly accepted a cross-firm reference.");
    Assert.Equal("23503", observed!.SqlState);
    Assert.Equal(expectedConstraint, observed.ConstraintName);
  }

  private static async Task<Guid> SeedBudgetChildRowsAsync(PgTestSchema pg, PbcSeed.Fixture fixture)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    await BudgetPreparationReviewSeed.PopulateAsync(db, fixture);

    var preparer = PbcSeed.Actor(fixture.Staff, "Manager");
    var preparation = new BudgetPreparationRequest(Guid.NewGuid(), new("QAR", "0",
      [new("Senior", "AUDIT", 480, "PLANNING", "Revenue")]));
    var preparationPreview = await BudgetPreparationWorkspace.PreviewAsync(db, preparer, fixture.EngagementId, preparation);
    Assert.True(preparationPreview.Succeeded, preparationPreview.Message);
    preparation = preparation with
    {
      Fields = preparationPreview.Value!.Fields,
      Reviewed = true,
      ReviewBasis = preparationPreview.Value.ReviewBasis,
      RequestHash = preparationPreview.Value.RequestHash
    };
    var prepared = await BudgetPreparationWorkspace.ExecuteAsync(db, preparer, fixture.EngagementId, preparation);
    Assert.True(prepared.Succeeded, prepared.Message);

    var approver = PbcSeed.Actor(fixture.Admin, "Administrator");
    var approval = new BudgetApprovalRequest(Guid.NewGuid(), new(prepared.Value!.BudgetId, "1"));
    var approvalPreview = await BudgetApprovalWorkspace.PreviewAsync(db, approver, fixture.EngagementId, approval);
    Assert.True(approvalPreview.Succeeded, approvalPreview.Message);
    approval = approval with
    {
      Reviewed = true,
      ReviewBasis = approvalPreview.Value!.ReviewBasis,
      RequestHash = approvalPreview.Value.RequestHash
    };
    var approved = await BudgetApprovalWorkspace.ExecuteAsync(db, approver, fixture.EngagementId, approval);
    Assert.True(approved.Succeeded, approved.Message);

    // A second valid parent keeps the foreign-key probe away from immutable-receipt unique indexes.
    var spare = await PracticeTimeService.ReviseBudgetAsync(db, preparer,
      new(fixture.EngagementId, "QAR", [new("Senior", "AUDIT", 480, "PLANNING", "Revenue")], 1));
    Assert.True(spare.Succeeded, spare.Message);
    return spare.Value;
  }

  [Fact]
  public async Task StaffCostRates_RejectForeignFirmUser()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();

    var sameFirmId = Guid.NewGuid();
    var crossFirmId = Guid.NewGuid();
    string crossFirmOutcome;
    await using (var transaction = await connection.BeginTransactionAsync())
    {
      async Task<int> InsertRateAsync(Guid id, Guid firmId, Guid userId, Guid recordedByUserId)
      {
        await using var command = new NpgsqlCommand("""
          INSERT INTO staff_cost_rates
            (id, firm_id, user_id, hourly_cost, currency, effective_from, recorded_by_user_id, recorded_at)
          VALUES
            (@id, @firm_id, @user_id, @hourly_cost, @currency, @effective_from, @recorded_by_user_id, @recorded_at)
          """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("firm_id", firmId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("hourly_cost", 200m);
        command.Parameters.AddWithValue("currency", "QAR");
        command.Parameters.AddWithValue("effective_from", new DateOnly(2026, 1, 1));
        command.Parameters.AddWithValue("recorded_by_user_id", recordedByUserId);
        command.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow);
        return await command.ExecuteNonQueryAsync();
      }

      var sameFirmRows = await InsertRateAsync(sameFirmId, own.FirmId, own.Staff.Id, own.Admin.Id);
      output.WriteLine($"SAME_FIRM_CONTROL: inserted={sameFirmRows}");
      Assert.Equal(1, sameFirmRows);

      try
      {
        var rows = await InsertRateAsync(crossFirmId, own.FirmId, foreign.Staff.Id, own.Admin.Id);
        crossFirmOutcome = $"ACCEPTED rows={rows}";
      }
      catch (PostgresException exception)
      {
        crossFirmOutcome = $"REFUSED SqlState={exception.SqlState} Constraint={exception.ConstraintName ?? "(none)"}";
      }
      output.WriteLine($"CROSS_FIRM_USER: {crossFirmOutcome}");
      await transaction.RollbackAsync();
    }

    await using var rollbackCheck = new NpgsqlCommand(
      "SELECT count(*) FROM staff_cost_rates WHERE id IN (@same_firm_id, @cross_firm_id)", connection);
    rollbackCheck.Parameters.AddWithValue("same_firm_id", sameFirmId);
    rollbackCheck.Parameters.AddWithValue("cross_firm_id", crossFirmId);
    Assert.Equal(0L, await rollbackCheck.ExecuteScalarAsync());
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_staff_cost_rate_user_scope", crossFirmOutcome);
  }

  [Fact]
  public async Task FirmExpenses_RejectForeignFirmReferences()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var accounts = new[]
    {
      new FirmAccount { Id = Guid.NewGuid(), FirmId = own.FirmId, Code = "6100", Name = "Own expense", AccountType = LedgerStates.AccountExpense, NormalSide = LedgerStates.Debit },
      new FirmAccount { Id = Guid.NewGuid(), FirmId = own.FirmId, Code = "1000", Name = "Own bank", AccountType = LedgerStates.AccountAsset, NormalSide = LedgerStates.Debit },
      new FirmAccount { Id = Guid.NewGuid(), FirmId = foreign.FirmId, Code = "6100", Name = "Foreign expense", AccountType = LedgerStates.AccountExpense, NormalSide = LedgerStates.Debit },
      new FirmAccount { Id = Guid.NewGuid(), FirmId = foreign.FirmId, Code = "1000", Name = "Foreign bank", AccountType = LedgerStates.AccountAsset, NormalSide = LedgerStates.Debit }
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.FirmAccounts.AddRange(accounts);
      foreach (var fixture in new[] { own, foreign })
      {
        db.RoleGrants.AddRange(
          PbcSeed.Grant(fixture.FirmId, fixture.Staff, "FinanceManager"),
          PbcSeed.Grant(fixture.FirmId, fixture.Reviewer, "FinanceReviewer"));
        db.FirmFinanceProfiles.Add(new FirmFinanceProfile
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR",
          ProfileKind = BillingStates.TestProfile, Approved = true,
          ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
          CreatedAt = DateTimeOffset.UtcNow
        });
      }
      await db.SaveChangesAsync();
    }

    async Task<(Guid JournalId, Guid PostingId)> CreatePostedJournalAsync(
      PbcSeed.Fixture fixture, Guid expenseAccountId, Guid bankAccountId, string journalNumber, string sourceKey)
    {
      var finance = PbcSeed.Actor(fixture.Staff, "FinanceManager");
      var reviewer = PbcSeed.Actor(fixture.Reviewer, "FinanceReviewer");
      await using var db = new AuditSphereDbContext(pg.Options);
      var period = await LedgerService.CreateFirmPeriodAsync(db, finance, new CreateFirmPeriodRequest("2026-10"));
      Assert.True(period.Succeeded, period.Message);
      var journal = await LedgerService.CreateFirmJournalDraftAsync(db, finance,
        new CreateFirmJournalDraftRequest(period.Value, journalNumber, "MANUAL", sourceKey, 1, "MANUAL", "QAR",
          [new FirmJournalLineRequest(expenseAccountId, "Synthetic scope debit", 1m, 0m),
           new FirmJournalLineRequest(bankAccountId, "Synthetic scope credit", 0m, 1m)]));
      Assert.True(journal.Succeeded, journal.Message);
      var submitted = await LedgerService.SubmitFirmJournalAsync(db, finance, journal.Value);
      Assert.True(submitted.Succeeded, submitted.Message);
      var approved = await LedgerService.ApproveFirmJournalAsync(db, reviewer, journal.Value);
      Assert.True(approved.Succeeded, approved.Message);
      var posting = await LedgerService.PostFirmJournalAsync(db, finance, journal.Value);
      Assert.True(posting.Succeeded, posting.Message);
      return (journal.Value, posting.Value);
    }

    var ownLedger = await CreatePostedJournalAsync(own, accounts[0].Id, accounts[1].Id, "J-SCOPE", "SCOPE-OWN");
    var foreignLedger = await CreatePostedJournalAsync(foreign, accounts[2].Id, accounts[3].Id, "J-SCOPE", "SCOPE-FOREIGN");

    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var evidence = "synthetic expense receipt"u8.ToArray();
    var evidenceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidence)).ToLowerInvariant();
    var expenseIds = new List<Guid>();

    async Task<string> InsertExpenseAsync(
      Guid expenseAccountId, Guid paymentAccountId, Guid preparerId, Guid reviewerId,
      Guid? journalId = null, Guid? postingId = null, string status = "APPROVED")
    {
      var expenseId = Guid.NewGuid();
      expenseIds.Add(expenseId);
      await new NpgsqlCommand("SAVEPOINT expense_probe", connection, transaction).ExecuteNonQueryAsync();
      string outcome;
      try
      {
        await using var command = new NpgsqlCommand("""
          INSERT INTO firm_expenses
            (id, firm_id, expense_date, category, payee, description, amount, currency,
             expense_account_id, payment_account_id, journal_id, posting_id,
             evidence_file_name, evidence_content_type, evidence_content, evidence_sha256,
             status, prepared_by_user_id, reviewed_by_user_id, reviewed_at, created_at)
          VALUES
            (@id, @firm_id, @expense_date, 'OTHER', 'Synthetic payee', 'Scope probe', 1.00, 'QAR',
             @expense_account_id, @payment_account_id, @journal_id, @posting_id,
             'receipt.pdf', 'application/pdf', @evidence_content, @evidence_sha256, @status, @prepared_by_user_id,
             @reviewed_by_user_id, @reviewed_at, @created_at)
          """, connection, transaction);
        command.Parameters.AddWithValue("id", expenseId);
        command.Parameters.AddWithValue("firm_id", own.FirmId);
        command.Parameters.AddWithValue("expense_date", new DateOnly(2026, 10, 23));
        command.Parameters.AddWithValue("expense_account_id", expenseAccountId);
        command.Parameters.AddWithValue("payment_account_id", paymentAccountId);
        command.Parameters.AddWithValue("journal_id", (object?)journalId ?? DBNull.Value);
        command.Parameters.AddWithValue("posting_id", (object?)postingId ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence_content", evidence);
        command.Parameters.AddWithValue("evidence_sha256", evidenceHash);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("prepared_by_user_id", preparerId);
        command.Parameters.AddWithValue("reviewed_by_user_id", reviewerId);
        command.Parameters.AddWithValue("reviewed_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        var rows = await command.ExecuteNonQueryAsync();
        outcome = $"ACCEPTED rows={rows}";
      }
      catch (PostgresException exception)
      {
        outcome = $"REFUSED SqlState={exception.SqlState} Constraint={exception.ConstraintName ?? "(none)"}";
      }
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT expense_probe", connection, transaction).ExecuteNonQueryAsync();
      await new NpgsqlCommand("RELEASE SAVEPOINT expense_probe", connection, transaction).ExecuteNonQueryAsync();
      return outcome;
    }

    var sameFirmNullableLinksControl = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id);
    var sameFirmJournalControl = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id, ownLedger.JournalId);
    var sameFirmPostingControl = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id,
      ownLedger.JournalId, ownLedger.PostingId, "POSTED");
    var foreignJournal = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id, foreignLedger.JournalId);
    var foreignPosting = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id,
      postingId: foreignLedger.PostingId, status: "POSTED");
    var foreignExpenseAccount = await InsertExpenseAsync(accounts[2].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id);
    var foreignPaymentAccount = await InsertExpenseAsync(accounts[0].Id, accounts[3].Id, own.Admin.Id, own.Staff.Id);
    var foreignPreparer = await InsertExpenseAsync(accounts[0].Id, accounts[1].Id, foreign.Admin.Id, own.Staff.Id);
    var foreignReviewer = await InsertExpenseAsync(accounts[0].Id, accounts[1].Id, own.Admin.Id, foreign.Admin.Id);
    output.WriteLine($"SAME_FIRM_NULLABLE_LINKS: {sameFirmNullableLinksControl}");
    output.WriteLine($"SAME_FIRM_JOURNAL: {sameFirmJournalControl}");
    output.WriteLine($"SAME_FIRM_POSTING: {sameFirmPostingControl}");
    output.WriteLine($"FOREIGN_JOURNAL: {foreignJournal}");
    output.WriteLine($"FOREIGN_POSTING: {foreignPosting}");
    output.WriteLine($"FOREIGN_EXPENSE_ACCOUNT: {foreignExpenseAccount}");
    output.WriteLine($"FOREIGN_PAYMENT_ACCOUNT: {foreignPaymentAccount}");
    output.WriteLine($"FOREIGN_PREPARER: {foreignPreparer}");
    output.WriteLine($"FOREIGN_REVIEWER: {foreignReviewer}");
    await transaction.RollbackAsync();

    await using var rollbackCheck = new NpgsqlCommand(
      "SELECT count(*) FROM firm_expenses WHERE id = ANY(@expense_ids)", connection);
    rollbackCheck.Parameters.AddWithValue("expense_ids", expenseIds.ToArray());
    Assert.Equal(0L, await rollbackCheck.ExecuteScalarAsync());
    Assert.Equal("ACCEPTED rows=1", sameFirmNullableLinksControl);
    Assert.Equal("ACCEPTED rows=1", sameFirmJournalControl);
    Assert.Equal("ACCEPTED rows=1", sameFirmPostingControl);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_journal_scope", foreignJournal);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_posting_scope", foreignPosting);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_expense_account_scope", foreignExpenseAccount);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_payment_account_scope", foreignPaymentAccount);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_prepared_by_user_scope", foreignPreparer);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_reviewed_by_user_scope", foreignReviewer);
  }

  [Theory]
  [InlineData("ENGAGEMENT")]
  [InlineData("USER")]
  [InlineData("ROLE_GRANT")]
  [InlineData("CLIENT")]
  [InlineData("OTHER_USER_GRANT")]
  public async Task StaffingReferences_RefuseForeignFirmParents(string parent)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var staffGrant = await db.RoleGrants.SingleAsync(x => x.FirmId == own.FirmId &&
      x.UserId == own.Staff.Id && x.Role == "Staff");
    var ownAdminGrant = PbcSeed.Grant(own.FirmId, own.Admin, "Staff", own.ClientId, own.EngagementId);
    var foreignAdminGrant = PbcSeed.Grant(foreign.FirmId, foreign.Admin, "Staff", foreign.ClientId, foreign.EngagementId);
    db.RoleGrants.AddRange(ownAdminGrant, foreignAdminGrant);
    db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ClientId = own.ClientId,
      EngagementId = own.EngagementId, UserId = own.Staff.Id, RoleGrantId = staffGrant.Id,
      StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = own.Admin.Id,
      AssignedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(); // All-owned control is valid; candidate uses a different local user.

    var candidate = new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId,
      ClientId = parent == "CLIENT" ? foreign.ClientId : own.ClientId,
      EngagementId = parent == "ENGAGEMENT" ? foreign.EngagementId : own.EngagementId,
      UserId = parent == "USER" ? foreign.Admin.Id : own.Admin.Id,
      RoleGrantId = parent == "ROLE_GRANT" ? foreignAdminGrant.Id :
        parent == "OTHER_USER_GRANT" ? staffGrant.Id : ownAdminGrant.Id,
      StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = own.Admin.Id,
      AssignedAt = DateTimeOffset.UtcNow
    };
    db.EngagementStaffAssignments.Add(candidate);
    var failure = await Record.ExceptionAsync(() => db.SaveChangesAsync());
    if (failure is null) output.WriteLine($"ACCEPTED_FOREIGN_STAFFING_PARENT:{parent}");
    var rejected = Assert.IsType<DbUpdateException>(failure);
    var postgres = Assert.IsType<PostgresException>(rejected.InnerException);
    Assert.Equal("23503", postgres.SqlState);
    var expectedConstraints = parent switch
    {
      "ENGAGEMENT" or "CLIENT" => new[] { "fk_staff_assignment_engagement_scope" },
      "USER" => new[] { "fk_staff_assignment_user_scope", "fk_staff_assignment_grant_scope" },
      _ => new[] { "fk_staff_assignment_grant_scope" }
    };
    Assert.Contains(postgres.ConstraintName, expectedConstraints);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.False(await proof.EngagementStaffAssignments.AnyAsync(x => x.Id == candidate.Id));
    Assert.Equal(1, await proof.EngagementStaffAssignments.CountAsync(x => x.FirmId == own.FirmId));
  }

  [Theory]
  [InlineData("PROFILE", "staff_profiles", "fk_staff_profile_user_scope")]
  [InlineData("CERTIFICATION", "staff_certifications", "fk_staff_certification_user_scope")]
  [InlineData("AVAILABILITY", "staff_availabilities", "fk_staff_availability_user_scope")]
  [InlineData("ALLOCATION_USER", "staff_allocations", "fk_staff_allocation_user_scope")]
  [InlineData("ALLOCATION_ENGAGEMENT", "staff_allocations", "fk_staff_allocation_engagement_scope")]
  [InlineData("ALLOCATION_CLIENT", "staff_allocations", "fk_staff_allocation_engagement_scope")]
  public async Task ResourceRecords_RefuseMismatchedScopedParents(string kind, string table, string constraint)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var sunday = new DateOnly(2026, 10, 25);
    object Row(Guid id, bool invalid)
    {
      var user = invalid && kind is not ("ALLOCATION_ENGAGEMENT" or "ALLOCATION_CLIENT")
        ? foreign.Admin.Id : invalid ? own.Admin.Id : own.Staff.Id;
      return kind switch
      {
        "PROFILE" => new StaffProfile { Id = id, FirmId = own.FirmId, UserId = user,
          Department = "AUDIT", WeeklyCapacityMinutes = 2400, TargetUtilizationPercent = 75,
          UpdatedAt = now, UpdatedByUserId = own.Admin.Id },
        "CERTIFICATION" => new StaffCertification { Id = id, FirmId = own.FirmId, UserId = user,
          Name = "SYNTHETIC CPA", RecordedAt = now, RecordedByUserId = own.Admin.Id },
        "AVAILABILITY" => new StaffAvailability { Id = id, FirmId = own.FirmId, UserId = user,
          StartDate = sunday, EndDate = sunday, MinutesPerDay = 480,
          RecordedAt = now, RecordedByUserId = own.Admin.Id },
        _ => new StaffAllocation { Id = id, FirmId = own.FirmId, UserId = user,
          ClientId = invalid && kind == "ALLOCATION_CLIENT" ? foreign.ClientId : own.ClientId,
          EngagementId = invalid && kind == "ALLOCATION_ENGAGEMENT" ? foreign.EngagementId : own.EngagementId,
          WeekStart = sunday, PlannedMinutes = 480, UpdatedAt = now, UpdatedByUserId = own.Admin.Id }
      };
    }

    var validId = Guid.NewGuid();
    var invalidId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Add(Row(validId, false));
    await db.SaveChangesAsync(); // Distinct valid subject avoids a unique-index false refusal.
    db.Add(Row(invalidId, true));
    var failure = await Record.ExceptionAsync(() => db.SaveChangesAsync());
    if (failure is null) output.WriteLine($"ACCEPTED_FOREIGN_RESOURCE_PARENT:{kind}");
    var rejected = Assert.IsType<DbUpdateException>(failure);
    var postgres = Assert.IsType<PostgresException>(rejected.InnerException);
    Assert.Equal("23503", postgres.SqlState);
    Assert.Equal(constraint, postgres.ConstraintName);

    // Table identifiers are fixed InlineData above, never caller input.
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE id = @id", connection);
    count.Parameters.AddWithValue("id", invalidId);
    Assert.Equal(0L, await count.ExecuteScalarAsync());
    count.Parameters["id"].Value = validId;
    Assert.Equal(1L, await count.ExecuteScalarAsync());
  }

  private static HashSet<string> ExpectedTriggers()
  {
    var root = AppContext.BaseDirectory;
    for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "AuditSphereOps.slnx"))) { root = dir.FullName; break; }
    var migrations = Directory.GetFiles(Path.Combine(root, "src", "AuditSphereOps.Infrastructure", "Persistence", "Migrations"), "*.cs")
      .Where(file => !file.EndsWith(".Designer.cs") && Regex.IsMatch(Path.GetFileName(file), @"^\d{14}_")).Order(StringComparer.Ordinal);
    var live = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in migrations)
    {
      // Only the Up direction defines the schema the application runs on.
      var text = File.ReadAllText(file);
      var down = text.IndexOf("void Down(", StringComparison.Ordinal);
      foreach (Match operation in TriggerOperation().Matches(down < 0 ? text : text[..down]))
        if (operation.Groups[1].Value.StartsWith("DROP", StringComparison.OrdinalIgnoreCase)) live.Remove(operation.Groups[2].Value);
        else live.Add(operation.Groups[2].Value);
    }
    return live;
  }

  [GeneratedRegex(@"\b(CREATE\s+(?:OR\s+REPLACE\s+)?(?:CONSTRAINT\s+)?TRIGGER|DROP\s+TRIGGER(?:\s+IF\s+EXISTS)?)\s+""?(\w+)""?", RegexOptions.IgnoreCase)]
  private static partial Regex TriggerOperation();
}
