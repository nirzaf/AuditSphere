using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record BundleView(Guid Id, string Sha256, DateTimeOffset AssembledAt);
public sealed record BundleAssemblyState(IReadOnlyList<string> Blockers, IReadOnlyList<BundleView> Bundles);

public static partial class AuditDeliverableService
{
  /// <summary>All byte assembly is local. The financial release/checkpoint and posted invoice must already exist.</summary>
  public static async Task<CommandResult<Guid>> AssembleBundleAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    bool financialStatementsReviewed, CancellationToken ct = default)
  {
    var auth = await AuthorizePartnerAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!financialStatementsReviewed) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Confirm that you reviewed the exact released financial statements for certification with this report.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockDeliverableEngagementAsync(db, actor.FirmId, engagementId, ct);
    auth = await AuthorizePartnerAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var inputs = await BundleInputsAsync(db, actor, engagementId, ct);
    if (inputs.Blockers.Count != 0) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, string.Join(" ", inputs.Blockers));
    var report = inputs.Report!; var management = inputs.Management!; var scan = inputs.Scan!;
    var release = inputs.Release!; var statements = inputs.Statements!; var invoice = inputs.Invoice!;
    var representation = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == scan.DeliverableId && x.FirmId == actor.FirmId, ct);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == engagement.PracticeClientId && x.FirmId == actor.FirmId, ct);
    var firm = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    const int maxCorrespondenceRows = 10000;
    if (await db.PbcCommunications.CountAsync(x => x.FirmId == actor.FirmId && x.ClientId == client.Id && x.EngagementId == engagementId, ct) > maxCorrespondenceRows ||
        await db.ClientDeliverableComments.CountAsync(x => x.FirmId == actor.FirmId && db.ClientDeliverableReviews.Any(r => r.FirmId == actor.FirmId && r.EngagementId == engagementId && r.Id == x.ReviewId), ct) > maxCorrespondenceRows)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The correspondence exceeds the bounded assembly limit; request an approved delivery arrangement.");
    var confirmationRows = await ConfirmationRowsAsync(db, actor.FirmId, engagementId, ct);
    var communications = await db.PbcCommunications.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == client.Id && x.EngagementId == engagementId)
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new { x.Id, x.PbcRequestId, x.Kind, x.Body, x.AuthorUserId, x.CreatedAt }).ToListAsync(ct);
    var reviewIds = db.ClientDeliverableReviews.Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).Select(x => x.Id);
    var comments = await db.ClientDeliverableComments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && reviewIds.Contains(x.ReviewId))
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new { x.Id, x.ReviewId, x.Body, x.CreatedAt, x.Resolution, x.ResolvedAt }).ToListAsync(ct);
    var correspondenceJson = JsonSerializer.Serialize(new { Confirmations = confirmationRows, Communications = communications, ClearedQueries = comments });
    var lines = await db.InvoiceLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoice.Id).OrderBy(x => x.Id).ToListAsync(ct);
    var sourceDigest = Digest(new { Report = report.Id, Management = management.Id, Scan = scan.Id, Release = release.Id, Artifact = statements.Id,
      Invoice = invoice.Id, InvoiceRevision = invoice.Revision, InvoiceStatus = invoice.Status, invoice.Total, invoice.PostedAt,
      Lines = lines.Select(x => new { x.Id, x.Description, x.Quantity, x.UnitPrice, x.LineTotal }), Correspondence = correspondenceJson,
      Profile = firm?.Id, CertifiedBy = actor.UserId });
    var prior = await db.CommercialDeliverableBundles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.SourceDigest == sourceDigest, ct);
    if (prior is not null) return CommandResult<Guid>.Ok(prior.Id);
    if ((long)report.Content.Length + management.Content.Length + scan.Content.Length + statements.ArtifactBytes.Length + Encoding.UTF8.GetByteCount(correspondenceJson) > 100 * 1024 * 1024)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The bundle exceeds the 100 MB assembly limit; request an approved delivery arrangement.");
    if (!ValidPdf(report.Content) || !ValidPdf(statements.ArtifactBytes))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The report or financial statements are not readable PDF artifacts.");
    var date = DateOnly.FromDateTime(release.ReleasedAt.UtcDateTime);
    var feePdf = AuditDeliverableRenderer.RenderPdf(new(firm?.LegalName ?? "Audit firm", "Final balance fee note", invoice.InvoiceNumber, date, client.LegalName,
      [new("Posted invoice", [$"Invoice {invoice.InvoiceNumber}; revision {invoice.Revision}; status {invoice.Status}.",
        $"Total: {invoice.Total:N2} {invoice.Currency}. Posted: {invoice.PostedAt:yyyy-MM-dd}.",
        "This note references the posted balance invoice; payments and finance approvals remain in the finance workflow."],
        new DocumentTable(["Description", "Quantity", "Unit price", "Amount"], lines.Select(x => (IReadOnlyList<string>)[x.Description,
          x.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), x.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture), x.LineTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToList()))]));
    var signatureApplication = await db.SignatureApplications.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.SignedDeliverableId == report.Id, ct);
    var specimen = await db.SignatureSpecimens.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == signatureApplication.SpecimenId, ct);
    using var summary = JsonDocument.Parse(report.InputSummaryJson);
    var sealId = summary.RootElement.GetProperty("Seal").GetGuid();
    var seal = await db.FirmSealSpecimens.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == sealId, ct);
    var signer = await db.Users.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == report.CreatedByUserId, ct);
    var cover = AuditDeliverableRenderer.RenderPdf(new(firm?.LegalName ?? "Audit firm", "Financial statements certification", statements.Id.ToString("D"), date,
      client.LegalName, [new("Exact reviewed financial statements", [$"Package: {statements.FinancialPackageId}; revision: {statements.PackageRevision}.",
        $"Source PDF SHA-256: {statements.ArtifactSha256Hex}", $"Released under manifest: {release.ManifestDigest}.",
        "The Engagement Partner confirms these exact released statements accompany the signed auditor's report. Signature and seal are approved visual images."])],
      "Engagement Partner", new(specimen.PngContent, specimen.WidthPixels, specimen.HeightPixels, signer.DisplayName, "Engagement Partner", date), seal.PngContent));
    var certifiedStatements = JoinPdfs(statements.ArtifactBytes, cover);
    var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
    {
      ["01_Report_and_statements/Independent_auditors_report.pdf"] = report.Content,
      ["01_Report_and_statements/Certified_financial_statements.pdf"] = certifiedStatements,
      ["02_Management_letter/Management_letter.docx"] = management.Content,
      ["03_Representation_letter/Representation_letter.docx"] = representation.Content,
      ["03_Representation_letter/Management_signed_letter.pdf"] = scan.Content,
      ["04_Management_correspondence/Audit_trail.json"] = Encoding.UTF8.GetBytes(correspondenceJson),
      ["05_Final_balance/Balance_fee_note.pdf"] = feePdf
    };
    var manifest = JsonSerializer.Serialize(new
    {
      Format = "STE-FIVE-PART-v1", actor.FirmId, ClientId = client.Id, EngagementId = engagementId,
      SignedReport = report.Id, FinancialPackageRelease = release.Id, FinancialPackageArtifact = statements.Id, statements.ArtifactSha256Hex,
      ManagementLetter = management.Id, RepresentationLetter = representation.Id, SignedRepresentation = scan.Id, scan.ContentSha256,
      BalanceInvoice = invoice.Id, InvoiceRevision = invoice.Revision, InvoiceStatus = invoice.Status,
      CertifiedBy = actor.UserId, SourceSignature = specimen.Sha256, Seal = seal.Sha256,
      Parts = files.Select(x => new { Path = x.Key, Sha256 = Hashing.Sha256Hex(x.Value), Bytes = x.Value.Length })
    });
    var digest = Hashing.Sha256Hex(manifest);
    files["manifest.json"] = Encoding.UTF8.GetBytes(manifest);
    using var output = new MemoryStream();
    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
      foreach (var (name, bytes) in files)
      {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open(); stream.Write(bytes);
      }
    var content = output.ToArray();
    var bundle = new CommercialDeliverableBundle
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = client.Id, EngagementId = engagementId, SignedReportId = report.Id,
      FinancialPackageReleaseId = release.Id, FinancialPackageArtifactId = statements.Id, ManagementLetterId = management.Id,
      SignedRepresentationLetterId = scan.Id, BalanceInvoiceId = invoice.Id, ManifestJson = manifest, ManifestSha256 = digest,
      SourceDigest = sourceDigest, Content = content, ContentSha256 = Hashing.Sha256Hex(content), AssembledByUserId = actor.UserId, AssembledAt = DateTimeOffset.UtcNow
    };
    db.CommercialDeliverableBundles.Add(bundle);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(bundle.Id);
  }

  private sealed record BundleInputs(List<string> Blockers, AuditDeliverable? Report, AuditDeliverable? Management,
    SignedRepresentationLetter? Scan, Release? Release, FinancialPackageArtifact? Statements, Invoice? Invoice);

  private static async Task<BundleInputs> BundleInputsAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var blockers = new List<string>();
    var rows = await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.Version).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
    var report = rows.FirstOrDefault(x => x.Kind == DeliverableKinds.IndependentAuditorsReport && x.SignedFromDeliverableId != null);
    if (report is null || report.CreatedByUserId != actor.UserId || !await IsCurrentAsync(db, actor, report, ct))
      blockers.Add("The deciding Engagement Partner must sign a current auditor's report.");
    if (report is not null)
    {
      using var proof = JsonDocument.Parse(report.InputSummaryJson);
      if (!proof.RootElement.TryGetProperty("Seal", out var sealProof) || !sealProof.TryGetGuid(out var sealId) ||
          !await db.FirmSealSpecimens.AnyAsync(x => x.FirmId == actor.FirmId && x.Id == sealId, ct))
        blockers.Add("The signed report requires a recorded firm-seal version; regenerate and sign through the current workflow.");
    }
    var management = rows.FirstOrDefault(x => x.Kind == DeliverableKinds.ManagementLetter);
    if (management is null || !await IsCurrentAsync(db, actor, management, ct)) blockers.Add("Generate a current management letter.");
    var letter = rows.FirstOrDefault(x => x.Kind == DeliverableKinds.RepresentationLetter);
    var scan = letter is null ? null : await db.SignedRepresentationLetters.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.DeliverableId == letter.Id &&
      x.DeliverableSha256 == letter.ContentSha256 && db.RepresentationLetterVerifications.Any(v => v.FirmId == actor.FirmId && v.SignedLetterId == x.Id))
      .OrderByDescending(x => x.UploadedAt).FirstOrDefaultAsync(ct);
    if (scan is null || letter is null || !await IsCurrentAsync(db, actor, letter, ct)) blockers.Add("A Partner-verified signed scan of the current representation letter is required.");
    var clientGate = await ClientGateAsync(db, actor, engagementId, ct);
    if (clientGate is not null && scan is not null) blockers.Add(clientGate);
    var release = await db.Releases.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      db.ReleaseCandidates.Any(c => c.Id == x.ReleaseCandidateId && c.FirmId == actor.FirmId && c.TargetKind == ReleaseTargetKinds.FinancialPackage))
      .OrderByDescending(x => x.ReleasedAt).FirstOrDefaultAsync(ct);
    FinancialPackageArtifact? artifact = null;
    if (release is null) blockers.Add("Release the reviewed financial package through its existing approval and checkpoint workflow first.");
    else
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == release.PackageId && x.EngagementId == engagementId && x.ClientId == release.ClientId, ct);
      if (package is not null && db is IClientAccountingDbContext accounting)
      {
        var reviewed = await FinancialPackageReviewService.RequireCurrentAsync(accounting, actor, package.Id, true, ct);
        if (!reviewed.Succeeded) blockers.Add("The released financial package's management, accounting and Partner reviews are no longer current.");
      }
      if (package is not null && package.Revision == release.PackageRevision)
        artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
          x.EngagementId == engagementId && x.ClientId == package.ClientId && x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
          x.PackageHash == package.CalculationHash && x.ArtifactVersion == FinancialPackageArtifactVersions.Pdf, ct);
      if (artifact is null) blockers.Add("Render the exact released financial package as PDF; mismatched revisions cannot be assembled.");
    }
    var agreements = await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).Take(2).ToListAsync(ct);
    Invoice? invoice = null;
    if (agreements.Count == 1)
    {
      var agreementId = agreements[0].Id;
      var advancePaid = await db.FeeMilestones.AnyAsync(x => x.FirmId == actor.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance && x.State == FeeMilestoneStates.Paid, ct);
      var milestone = await db.FeeMilestones.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Balance, ct);
      if (advancePaid && milestone?.InvoiceId is { } invoiceId)
      {
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.PracticeClientId == agreements[0].PracticeClientId && x.Currency == agreements[0].Currency, ct);
        invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == invoiceId &&
          (x.Status == BillingStates.InvoicePosted || x.Status == BillingStates.InvoiceSent) && x.PostedAt != null && x.Total == milestone.Amount &&
          account != null && x.BillingAccountId == account.Id && x.Currency == agreements[0].Currency, ct);
      }
    }
    if (invoice is null) blockers.Add("Finance must issue, review and post the final balance invoice for the linked 50/50 agreement; the advance must be paid.");
    if (await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == "FROZEN", ct)) blockers.Add("The engagement is frozen; an approved amendment is required.");
    if (report is not null && Hashing.Sha256Hex(report.Content) != report.ContentSha256 || management is not null && Hashing.Sha256Hex(management.Content) != management.ContentSha256 ||
        scan is not null && Hashing.Sha256Hex(scan.Content) != scan.ContentSha256 || artifact is not null && Hashing.Sha256Hex(artifact.ArtifactBytes) != artifact.ArtifactSha256Hex)
      blockers.Add("Stored artifact integrity verification failed.");
    return new(blockers, report, management, scan, release, artifact, invoice);
  }

  public static async Task<CommandResult<BundleAssemblyState>> BundleStateAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, engagementId, ReaderRoles, ct);
    if (!auth.Succeeded) return CommandResult<BundleAssemblyState>.Fail(auth.ErrorCode!, auth.Message!);
    var inputs = await BundleInputsAsync(db, actor, engagementId, ct);
    var bundles = await db.CommercialDeliverableBundles.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.AssembledAt).Take(10).Select(x => new BundleView(x.Id, x.ContentSha256, x.AssembledAt)).ToListAsync(ct);
    return CommandResult<BundleAssemblyState>.Ok(new(inputs.Blockers, bundles));
  }

  public static async Task<CommandResult<CommercialDeliverableBundle>> GetBundleAsync(IAuditSphereDbContext db, ActorContext actor, Guid bundleId, CancellationToken ct = default)
  {
    var bundle = await db.CommercialDeliverableBundles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == bundleId, ct);
    if (bundle is null) return CommandResult<CommercialDeliverableBundle>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = actor.Roles.Contains("ClientUser");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, bundle.ClientId, bundle.EngagementId, client ? ["ClientUser"] : ReaderRoles, InternalOnly: !client), ct);
    return auth.Succeeded ? CommandResult<CommercialDeliverableBundle>.Ok(bundle) : CommandResult<CommercialDeliverableBundle>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<IReadOnlyList<BundleView>> ClientBundlesAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null || !(await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, engagement.PracticeClientId, engagementId, ["ClientUser"]), ct)).Succeeded) return [];
    return await db.CommercialDeliverableBundles.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.AssembledAt).Take(10).Select(x => new BundleView(x.Id, x.ContentSha256, x.AssembledAt)).ToListAsync(ct);
  }

  private static byte[] JoinPdfs(byte[] first, byte[] second)
  {
    using var result = new PdfSharp.Pdf.PdfDocument();
    foreach (var bytes in new[] { first, second })
    {
      using var stream = new MemoryStream(bytes);
      using var source = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
      for (var i = 0; i < source.PageCount; i++) result.AddPage(source.Pages[i]);
    }
    using var output = new MemoryStream(); result.Save(output, closeStream: false); return output.ToArray();
  }
}
