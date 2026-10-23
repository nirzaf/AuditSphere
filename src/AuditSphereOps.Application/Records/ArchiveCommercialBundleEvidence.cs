using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

internal sealed record ArchiveCommercialBundlePart(string Path, byte[] Content, string Sha256);

internal sealed record ArchiveCommercialBundleSource(
  string EntryKind,
  string SourceKind,
  Guid SourceId,
  string RelativeName,
  string ContentHash,
  long ByteCount,
  string MetadataJson,
  string SourceVersion,
  byte[] Content);

internal sealed record ArchiveCommercialBundleEvidence(
  CommercialDeliverableBundle Bundle,
  Release Release,
  ReleaseCandidate Candidate,
  ReleaseCheckpoint Checkpoint,
  FinancialPackageArtifact PdfArtifact,
  FinancialStatementComposition Composition,
  FinancialPackageArtifact WordArtifact,
  FinancialPackageArtifact ExcelArtifact,
  Invoice BalanceInvoice,
  FeeMilestone BalanceMilestone,
  AuditDeliverable SignedReport,
  SignatureApplication ReportSignature,
  AuditDeliverable UnsignedReport,
  AuditDeliverable ManagementLetter,
  AuditDeliverable RepresentationLetter,
  SignedRepresentationLetter SignedRepresentation,
  RepresentationLetterVerification RepresentationVerification,
  IReadOnlyList<ArchiveCommercialBundlePart> Parts)
{
  private const string Classification = "ASSEMBLED_REFERENCE";
  private const string BundleSourceKind = "CommercialDeliverableBundle";
  private const string BundlePartSourceKind = "CommercialBundlePart";
  private const int MaximumBundleBytes = 120 * 1024 * 1024;
  private const int MaximumExpandedBytes = 120 * 1024 * 1024;
  private const int MaximumParts = 64;

  private static readonly string[] RequiredPartPaths =
  [
    "01_Report_and_statements/Independent_auditors_report.pdf",
    "01_Report_and_statements/Selected_financial_statements.pdf",
    "01_Report_and_statements/Selected_financial_statements.docx",
    "01_Report_and_statements/Selected_financial_statements.xlsx",
    "01_Report_and_statements/Certified_financial_statements.pdf",
    "02_Management_letter/Management_letter.docx",
    "03_Representation_letter/Representation_letter.docx",
    "03_Representation_letter/Management_signed_letter.pdf",
    "04_Management_correspondence/Audit_trail.json",
    "05_Final_balance/Balance_fee_note.pdf"
  ];

  public static async Task<ArchiveCommercialBundleEvidence> ResolveAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid bundleId,
    Guid? requiredReleaseId, CancellationToken ct)
  {
    var bundle = await db.CommercialDeliverableBundles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == bundleId, ct);
    if (bundle is null || bundle.Id == Guid.Empty || bundle.AssembledByUserId == Guid.Empty || bundle.AssembledAt == default ||
        !IsSha256(bundle.SourceDigest))
      throw new OperationBlockedException("archive-copy-commercial-bundle-unavailable");

    if (bundle.Content is null || bundle.Content.Length == 0 || bundle.Content.Length > MaximumBundleBytes ||
        !IsSha256(bundle.ContentSha256) || Hashing.Sha256Hex(bundle.Content) != bundle.ContentSha256 ||
        string.IsNullOrWhiteSpace(bundle.ManifestJson) || Encoding.UTF8.GetByteCount(bundle.ManifestJson) > 1_048_576 ||
        !IsSha256(bundle.ManifestSha256) ||
        Hashing.Sha256Hex(Encoding.UTF8.GetBytes(bundle.ManifestJson)) != bundle.ManifestSha256)
      throw new OperationBlockedException("archive-copy-commercial-bundle-integrity-failed");

    var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == bundle.FinancialPackageReleaseId, ct);
    if (release is null || (requiredReleaseId.HasValue && release.Id != requiredReleaseId.Value))
      throw new OperationBlockedException("archive-copy-commercial-release-lineage-mismatch");
    var candidate = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == release.ReleaseCandidateId, ct);
    var checkpoint = await db.ReleaseCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == release.CheckpointId, ct);
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == release.PackageId, ct);
    var composition = release.PinnedCompositionId is Guid compositionId
      ? await db.FinancialStatementCompositions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.Id == compositionId, ct)
      : null;
    var presentation = composition is null ? null : await db.FinancialStatementPresentationRevisions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.Id == composition.PresentationRevisionId, ct);
    List<FinancialPackageArtifact> artifacts = composition is null ? [] : await db.FinancialPackageArtifacts.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.FinancialPackageId == release.PackageId &&
      (x.Id == composition.PdfArtifactId || x.Id == composition.WordArtifactId || x.Id == composition.ExcelArtifactId))
      .ToListAsync(ct);
    var pdf = artifacts.SingleOrDefault(x => x.Id == composition?.PdfArtifactId);
    var word = artifacts.SingleOrDefault(x => x.Id == composition?.WordArtifactId);
    var excel = artifacts.SingleOrDefault(x => x.Id == composition?.ExcelArtifactId);
    var agreements = await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.PracticeClientId == clientId && x.EngagementId == engagementId).Take(2).ToListAsync(ct);
    var balanceMilestones = agreements.Count == 1
      ? await db.FeeMilestones.AsNoTracking().Where(x => x.FirmId == firmId && x.AgreementId == agreements[0].Id &&
        x.Kind == FeeMilestoneKinds.Balance).Take(2).ToListAsync(ct)
      : new List<FeeMilestone>();
    var balanceMilestone = balanceMilestones.Count == 1 ? balanceMilestones[0] : null;
    var balanceInvoice = balanceMilestone?.InvoiceId is Guid invoiceId
      ? await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == invoiceId, ct)
      : null;
    var billingAccount = balanceInvoice is null ? null : await db.BillingAccounts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == balanceInvoice.BillingAccountId &&
        x.PracticeClientId == clientId, ct);

    if (candidate is null || checkpoint is null || package is null || composition is null || presentation is null ||
        pdf is null || word is null || excel is null || candidate.Status != ReleaseStates.Issued ||
        candidate.TargetKind != ReleaseTargetKinds.FinancialPackage || candidate.TargetId != release.PackageId ||
        candidate.TargetRevision != release.PackageRevision || candidate.ManifestDigest != release.ManifestDigest ||
        checkpoint.ReleaseCandidateId != candidate.Id || checkpoint.CandidateRevision != candidate.Revision ||
        checkpoint.AuthorizedReleaseKey != release.AuthorizedReleaseKey || checkpoint.ManifestDigest != release.ManifestDigest ||
        checkpoint.ReadBackDigest != checkpoint.ManifestDigest || checkpoint.VerifiedStatus != "VERIFIED" || checkpoint.VerifiedAt is null ||
        release.PinnedArtifactId != bundle.FinancialPackageArtifactId || release.PinnedArtifactVersion is null ||
        release.PinnedArtifactSha256Hex is null || release.PinnedCompositionId != bundle.FinancialStatementCompositionId ||
        release.PinnedCompositionManifestDigest != bundle.CompositionManifestDigest ||
        composition.ManifestDigest != release.PinnedCompositionManifestDigest ||
        bundle.CompositionManifestDigest != composition.ManifestDigest ||
        composition.FinancialPackageId != package.Id || package.Revision != release.PackageRevision ||
        package.CalculationHash != composition.PackageCalculationHash ||
        presentation.FinancialPackageId != package.Id || presentation.PackageRevision != package.Revision ||
        presentation.PackageGeneration != package.Generation || presentation.PackageCalculationHash != package.CalculationHash ||
        presentation.Status is not (FinancialStatementPresentationRevisionStatuses.Candidate or FinancialStatementPresentationRevisionStatuses.Approved) ||
        composition.PdfArtifactId != release.PinnedArtifactId || composition.PdfSha256Hex != release.PinnedArtifactSha256Hex ||
        pdf.ArtifactVersion != release.PinnedArtifactVersion || pdf.ArtifactSha256Hex != composition.PdfSha256Hex ||
        word.ArtifactSha256Hex != composition.WordSha256Hex || excel.ArtifactSha256Hex != composition.ExcelSha256Hex ||
        new[] { pdf, word, excel }.Any(x => x.FinancialPackageId != package.Id || x.PackageRevision != package.Revision ||
          x.PackageGeneration != package.Generation || x.PackageHash != package.CalculationHash || x.ArtifactBytes is null ||
          x.ArtifactBytes.Length == 0 || Hashing.Sha256Hex(x.ArtifactBytes) != x.ArtifactSha256Hex) ||
        release.PinnedArtifactId != pdf.Id || release.PinnedArtifactSha256Hex != pdf.ArtifactSha256Hex ||
        balanceMilestone is null || balanceMilestone.InvoiceId != bundle.BalanceInvoiceId || balanceInvoice is null ||
        billingAccount is null || balanceInvoice.Status is not (BillingStates.InvoicePosted or BillingStates.InvoiceSent) ||
        balanceInvoice.PostedAt is null || balanceInvoice.Revision < 1 || balanceInvoice.Total != balanceMilestone.Amount ||
        balanceInvoice.Currency != agreements[0].Currency ||
        release.ReleasedAt == default || release.ReleasedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(checkpoint.StoredReference))
      throw new OperationBlockedException("archive-copy-commercial-release-or-composition-stale");

    var signedReport = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == bundle.SignedReportId, ct);
    var unsignedReport = signedReport?.SignedFromDeliverableId is Guid unsignedId
      ? await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId &&
        x.EngagementId == engagementId && x.Id == unsignedId, ct)
      : null;
    var reportSignature = signedReport is null ? null : await db.SignatureApplications.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.EngagementId == engagementId && x.SignedDeliverableId == signedReport.Id, ct);
    var management = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == bundle.ManagementLetterId, ct);
    var signedRepresentation = await db.SignedRepresentationLetters.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.Id == bundle.SignedRepresentationLetterId, ct);
    var representation = signedRepresentation is null ? null : await db.AuditDeliverables.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.Id == signedRepresentation.DeliverableId, ct);
    var verification = signedRepresentation is null ? null : await db.RepresentationLetterVerifications.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.SignedLetterId == signedRepresentation.Id, ct);

    if (signedReport is null || unsignedReport is null || reportSignature is null || management is null ||
        representation is null || signedRepresentation is null || verification is null ||
        signedReport.Kind != DeliverableKinds.IndependentAuditorsReport || signedReport.SignedFromDeliverableId != unsignedReport.Id ||
        unsignedReport.Kind != DeliverableKinds.IndependentAuditorsReport || unsignedReport.SignedFromDeliverableId is not null ||
        reportSignature.SourceDeliverableId != unsignedReport.Id || reportSignature.SignedDeliverableId != signedReport.Id ||
        reportSignature.SignatureKind is not (ReportSignatureKinds.Visual or ReportSignatureKinds.Certificate) ||
        management.Kind != DeliverableKinds.ManagementLetter || representation.Kind != DeliverableKinds.RepresentationLetter ||
        signedRepresentation.DeliverableId != representation.Id || signedRepresentation.DeliverableSha256 != representation.ContentSha256 ||
        verification.SignedLetterId != signedRepresentation.Id || signedReport.Content is null || management.Content is null ||
        representation.Content is null || signedRepresentation.Content is null ||
        Hashing.Sha256Hex(signedReport.Content) != signedReport.ContentSha256 ||
        Hashing.Sha256Hex(management.Content) != management.ContentSha256 ||
        Hashing.Sha256Hex(representation.Content) != representation.ContentSha256 ||
        Hashing.Sha256Hex(signedRepresentation.Content) != signedRepresentation.ContentSha256)
      throw new OperationBlockedException("archive-copy-commercial-signed-part-lineage-mismatch");

    var signatureCheck = AuditDeliverableService.CheckReportSignature(signedReport, reportSignature);
    if (signatureCheck.State == ReportSignatureStates.Altered ||
        (reportSignature.SignatureKind == ReportSignatureKinds.Certificate && signatureCheck.State != ReportSignatureStates.Valid))
      throw new OperationBlockedException("archive-copy-commercial-report-signature-invalid");

    var parts = ReadAndValidateBundle(bundle);
    ValidateManifestLineage(bundle, release, composition, pdf, word, excel, signedReport, reportSignature,
      management, representation, signedRepresentation, balanceInvoice!, balanceMilestone!, parts);
    RequirePartContent(parts, RequiredPartPaths[0], signedReport.Content);
    RequirePartContent(parts, RequiredPartPaths[1], pdf.ArtifactBytes);
    RequirePartContent(parts, RequiredPartPaths[2], word.ArtifactBytes);
    RequirePartContent(parts, RequiredPartPaths[3], excel.ArtifactBytes);
    RequirePartContent(parts, RequiredPartPaths[5], management.Content);
    RequirePartContent(parts, RequiredPartPaths[6], representation.Content);
    RequirePartContent(parts, RequiredPartPaths[7], signedRepresentation.Content);

    return new ArchiveCommercialBundleEvidence(bundle, release, candidate, checkpoint, pdf, composition, word, excel,
      balanceInvoice!, balanceMilestone!, signedReport, reportSignature, unsignedReport, management, representation,
      signedRepresentation, verification, parts);
  }

  public IReadOnlyList<ArchiveCommercialBundleSource> CreateSources()
  {
    var lineage = JsonSerializer.Serialize(new
    {
      schema = "records-commercial-bundle-lineage.v1",
      classification = Classification,
      commercialIssuanceId = (Guid?)null,
      BundleId = Bundle.Id,
      BundleSourceDigest = Bundle.SourceDigest,
      BundleContentSha256 = Bundle.ContentSha256,
      BundleManifestSha256 = Bundle.ManifestSha256,
      BundleAssembledByUserId = Bundle.AssembledByUserId,
      BundleAssembledAt = Bundle.AssembledAt.ToUniversalTime(),
      ReleaseId = Release.Id,
      ReleaseCandidateId = Candidate.Id,
      ReleaseCandidateRevision = Candidate.Revision,
      ReleaseManifestSha256 = Release.ManifestDigest,
      CheckpointId = Checkpoint.Id,
      CheckpointManifestSha256 = Checkpoint.ManifestDigest,
      CheckpointReadBackSha256 = Checkpoint.ReadBackDigest,
      ReleaseReleasedAt = Release.ReleasedAt.ToUniversalTime(),
      ReleaseReleasedByUserId = Release.ReleasedByUserId,
      FinancialPackageArtifactId = PdfArtifact.Id,
      FinancialPackageArtifactVersion = PdfArtifact.ArtifactVersion,
      FinancialPackageArtifactSha256 = PdfArtifact.ArtifactSha256Hex,
      CompositionId = Composition.Id,
      CompositionManifestSha256 = Composition.ManifestDigest,
      BalanceInvoiceId = BalanceInvoice.Id,
      BalanceInvoiceRevision = BalanceInvoice.Revision,
      BalanceInvoiceStatus = BalanceInvoice.Status,
      BalanceInvoicePostedAt = BalanceInvoice.PostedAt?.ToUniversalTime(),
      SignedReportId = SignedReport.Id,
      SignedReportSha256 = SignedReport.ContentSha256,
      UnsignedReportId = UnsignedReport.Id,
      ReportSignatureApplicationId = ReportSignature.Id,
      ReportSignatureKind = ReportSignature.SignatureKind,
      ReportSignedByUserId = ReportSignature.SignedByUserId,
      ReportSignedAt = ReportSignature.SignedAt.ToUniversalTime(),
      ReportCertificateThumbprintSha256 = ReportSignature.CertificateThumbprintSha256,
      SignedRepresentationLetterId = SignedRepresentation.Id,
      SignedRepresentationSha256 = SignedRepresentation.ContentSha256,
      RepresentationVerificationId = RepresentationVerification.Id,
      RepresentationVerifiedByUserId = RepresentationVerification.VerifiedByUserId,
      RepresentationVerifiedAt = RepresentationVerification.VerifiedAt.ToUniversalTime()
    });
    var sources = new List<ArchiveCommercialBundleSource>
    {
      Make("COMMERCIAL_BUNDLE_ASSEMBLY", BundleSourceKind, Bundle.Id, $"commercial/bundles/{Bundle.Id:N}.zip",
        Bundle.ContentSha256, Bundle.Content, lineage, $"assembled:{Bundle.Id:N}:{Bundle.ContentSha256}"),
      Make("COMMERCIAL_BUNDLE_MANIFEST", BundleSourceKind, Bundle.Id, $"commercial/bundles/{Bundle.Id:N}/manifest.json",
        Bundle.ManifestSha256, Encoding.UTF8.GetBytes(Bundle.ManifestJson), lineage,
        $"manifest:{Bundle.Id:N}:{Bundle.ManifestSha256}")
    };
    foreach (var part in Parts.OrderBy(x => x.Path, StringComparer.Ordinal))
    {
      var metadata = JsonSerializer.Serialize(new
      {
        schema = "records-commercial-bundle-part.v1", classification = Classification,
        commercialIssuanceId = (Guid?)null, BundleId = Bundle.Id,
        BundleManifestSha256 = Bundle.ManifestSha256, PartPath = part.Path,
        PartSha256 = part.Sha256, PartByteCount = part.Content.LongLength
      });
      sources.Add(Make("COMMERCIAL_BUNDLE_PART", BundlePartSourceKind, Bundle.Id,
        $"commercial/bundles/{Bundle.Id:N}/parts/{part.Path}", part.Sha256, part.Content, metadata,
        $"part:{Bundle.Id:N}:{part.Path}:{part.Sha256}"));
    }
    var reportMetadata = JsonSerializer.Serialize(new
    {
      schema = "records-signed-report-lineage.v1", classification = Classification,
      commercialIssuanceId = (Guid?)null, BundleId = Bundle.Id, BundleManifestSha256 = Bundle.ManifestSha256,
      SignedReportId = SignedReport.Id, SignedReportSha256 = SignedReport.ContentSha256,
      UnsignedReportId = UnsignedReport.Id, ReportSignatureApplicationId = ReportSignature.Id,
      ReportSignatureKind = ReportSignature.SignatureKind, ReportSignedByUserId = ReportSignature.SignedByUserId,
      ReportSignedAt = ReportSignature.SignedAt.ToUniversalTime(),
      ReportCertificateThumbprintSha256 = ReportSignature.CertificateThumbprintSha256
    });
    sources.Add(Make("SIGNED_AUDIT_REPORT", "AuditDeliverable", SignedReport.Id,
      $"commercial/bundles/{Bundle.Id:N}/signed-parts/independent-auditors-report.pdf",
      SignedReport.ContentSha256, SignedReport.Content, reportMetadata,
      $"deliverable:{SignedReport.Id:N}:{SignedReport.Version}:{SignedReport.ContentSha256}"));

    var representationMetadata = JsonSerializer.Serialize(new
    {
      schema = "records-signed-representation-lineage.v1", classification = Classification,
      commercialIssuanceId = (Guid?)null, BundleId = Bundle.Id, BundleManifestSha256 = Bundle.ManifestSha256,
      SignedRepresentationLetterId = SignedRepresentation.Id,
      SignedRepresentationSha256 = SignedRepresentation.ContentSha256,
      RepresentationDeliverableId = RepresentationLetter.Id,
      RepresentationDeliverableSha256 = RepresentationLetter.ContentSha256,
      RepresentationVerificationId = RepresentationVerification.Id,
      RepresentationVerifiedByUserId = RepresentationVerification.VerifiedByUserId,
      RepresentationVerifiedAt = RepresentationVerification.VerifiedAt.ToUniversalTime(),
      RepresentationVerificationReasonSha256 = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(RepresentationVerification.Reason))
    });
    sources.Add(Make("SIGNED_MANAGEMENT_REPRESENTATION", "SignedRepresentationLetter", SignedRepresentation.Id,
      $"commercial/bundles/{Bundle.Id:N}/signed-parts/management-signed-representation.pdf",
      SignedRepresentation.ContentSha256, SignedRepresentation.Content, representationMetadata,
      $"signed-scan:{SignedRepresentation.Id:N}:{SignedRepresentation.ContentSha256}"));
    return sources;
  }

  private static ArchiveCommercialBundleSource Make(string entryKind, string sourceKind, Guid sourceId,
    string relativeName, string sha256, byte[] content, string metadataJson, string sourceVersion)
  {
    if (!IsSha256(sha256) || Hashing.Sha256Hex(content) != sha256 || content.LongLength < 0 ||
        metadataJson.Length > 16_384 || relativeName.Length > 2_000 || sourceVersion.Length > 500)
      throw new OperationBlockedException("archive-copy-commercial-source-invalid");
    return new ArchiveCommercialBundleSource(entryKind, sourceKind, sourceId, relativeName, sha256,
      content.LongLength, metadataJson, sourceVersion, content);
  }

  private static IReadOnlyList<ArchiveCommercialBundlePart> ReadAndValidateBundle(CommercialDeliverableBundle bundle)
  {
    try
    {
      using var json = JsonDocument.Parse(bundle.ManifestJson, new JsonDocumentOptions { MaxDepth = 32 });
      var declared = json.RootElement.GetProperty("Parts").EnumerateArray().Select(x =>
      {
        var path = x.GetProperty("Path").GetString();
        var sha = x.GetProperty("Sha256").GetString();
        var byteCount = x.GetProperty("Bytes").GetInt64();
        if (!IsSafePath(path) || !IsSha256(sha) || byteCount < 0 || byteCount > MaximumExpandedBytes)
          throw new OperationBlockedException("archive-copy-commercial-bundle-part-invalid");
        return (Path: path!, Sha256: sha!, ByteCount: byteCount);
      }).ToArray();
      if (declared.Length is < 1 or > MaximumParts || declared.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count() != declared.Length ||
          !declared.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(RequiredPartPaths.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
        throw new OperationBlockedException("archive-copy-commercial-bundle-part-set-incomplete");

      using var zipStream = new MemoryStream(bundle.Content, writable: false);
      using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
      if (zip.Entries.Count != declared.Length + 1 || zip.Entries.Any(x => x.FullName.EndsWith("/", StringComparison.Ordinal)) ||
          zip.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
        throw new OperationBlockedException("archive-copy-commercial-bundle-zip-entry-set-mismatch");
      var manifestEntry = zip.GetEntry("manifest.json");
      if (manifestEntry is null || manifestEntry.Length != Encoding.UTF8.GetByteCount(bundle.ManifestJson) ||
          !ReadEntry(manifestEntry, 1_048_576).SequenceEqual(Encoding.UTF8.GetBytes(bundle.ManifestJson)))
        throw new OperationBlockedException("archive-copy-commercial-bundle-inner-manifest-mismatch");

      long expandedTotal = manifestEntry.Length;
      var result = new List<ArchiveCommercialBundlePart>(declared.Length);
      foreach (var part in declared.OrderBy(x => x.Path, StringComparer.Ordinal))
      {
        var entry = zip.GetEntry(part.Path);
        if (entry is null || entry.Length != part.ByteCount || entry.Length > MaximumExpandedBytes ||
            (expandedTotal += entry.Length) > MaximumExpandedBytes)
          throw new OperationBlockedException("archive-copy-commercial-bundle-part-byte-count-mismatch");
        var bytes = ReadEntry(entry, MaximumExpandedBytes);
        if (bytes.LongLength != part.ByteCount || Hashing.Sha256Hex(bytes) != part.Sha256)
          throw new OperationBlockedException("archive-copy-commercial-bundle-part-hash-mismatch");
        result.Add(new(part.Path, bytes, part.Sha256));
      }
      return result;
    }
    catch (OperationBlockedException) { throw; }
    catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or ArgumentException or
      KeyNotFoundException or FormatException or NotSupportedException)
    {
      throw new OperationBlockedException("archive-copy-commercial-bundle-format-invalid");
    }
  }

  private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes)
  {
    if (entry.Length < 0 || entry.Length > maximumBytes || entry.Length > int.MaxValue)
      throw new OperationBlockedException("archive-copy-commercial-bundle-part-too-large");
    using var source = entry.Open();
    using var destination = new MemoryStream((int)entry.Length);
    var buffer = new byte[81920];
    while (true)
    {
      var read = source.Read(buffer, 0, buffer.Length);
      if (read == 0) break;
      if (destination.Length + read > maximumBytes)
        throw new OperationBlockedException("archive-copy-commercial-bundle-part-too-large");
      destination.Write(buffer, 0, read);
    }
    return destination.ToArray();
  }

  private static void ValidateManifestLineage(CommercialDeliverableBundle bundle, Release release,
    FinancialStatementComposition composition, FinancialPackageArtifact pdf, FinancialPackageArtifact word,
    FinancialPackageArtifact excel, AuditDeliverable report, SignatureApplication signature,
    AuditDeliverable management, AuditDeliverable representation,
    SignedRepresentationLetter signedRepresentation, Invoice invoice, FeeMilestone milestone,
    IReadOnlyList<ArchiveCommercialBundlePart> parts)
  {
    try
    {
      using var document = JsonDocument.Parse(bundle.ManifestJson, new JsonDocumentOptions { MaxDepth = 32 });
      var root = document.RootElement;
      var selected = root.GetProperty("SelectedStatementComposition");
      if (root.GetProperty("Format").GetString() != "STE-FIVE-PART-v1" ||
          root.GetProperty("FirmId").GetGuid() != bundle.FirmId || root.GetProperty("ClientId").GetGuid() != bundle.ClientId ||
          root.GetProperty("EngagementId").GetGuid() != bundle.EngagementId ||
          root.GetProperty("SignedReport").GetGuid() != bundle.SignedReportId ||
          root.GetProperty("FinancialPackageRelease").GetGuid() != release.Id ||
          root.GetProperty("FinancialPackageArtifact").GetGuid() != pdf.Id ||
          root.GetProperty("ArtifactSha256Hex").GetString() != pdf.ArtifactSha256Hex ||
          root.GetProperty("ManagementLetter").GetGuid() != management.Id ||
          root.GetProperty("RepresentationLetter").GetGuid() != representation.Id ||
          root.GetProperty("SignedRepresentation").GetGuid() != signedRepresentation.Id ||
          root.GetProperty("ContentSha256").GetString() != signedRepresentation.ContentSha256 ||
          root.GetProperty("BalanceInvoice").GetGuid() != invoice.Id ||
          root.GetProperty("InvoiceRevision").GetInt64() != invoice.Revision ||
          root.GetProperty("InvoiceStatus").GetString() != invoice.Status ||
          invoice.Id != bundle.BalanceInvoiceId || milestone.InvoiceId != invoice.Id ||
          selected.GetProperty("Id").GetGuid() != composition.Id ||
          selected.GetProperty("PresentationRevisionId").GetGuid() != composition.PresentationRevisionId ||
          selected.GetProperty("PackageCalculationHash").GetString() != composition.PackageCalculationHash ||
          selected.GetProperty("ManifestDigest").GetString() != composition.ManifestDigest ||
          selected.GetProperty("Pdf").GetProperty("ArtifactId").GetGuid() != pdf.Id ||
          selected.GetProperty("Pdf").GetProperty("Sha256").GetString() != pdf.ArtifactSha256Hex ||
          selected.GetProperty("Word").GetProperty("ArtifactId").GetGuid() != word.Id ||
          selected.GetProperty("Word").GetProperty("Sha256").GetString() != word.ArtifactSha256Hex ||
          selected.GetProperty("Excel").GetProperty("ArtifactId").GetGuid() != excel.Id ||
          selected.GetProperty("Excel").GetProperty("Sha256").GetString() != excel.ArtifactSha256Hex ||
          root.GetProperty("ReportSignatureKind").GetString() != signature.SignatureKind ||
          root.GetProperty("ReportCertificateThumbprintSha256").GetString() != signature.CertificateThumbprintSha256 ||
          root.GetProperty("Parts").GetArrayLength() != parts.Count)
        throw new OperationBlockedException("archive-copy-commercial-bundle-manifest-lineage-mismatch");
    }
    catch (OperationBlockedException) { throw; }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
    {
      throw new OperationBlockedException("archive-copy-commercial-bundle-manifest-invalid");
    }
  }

  private static void RequirePartContent(IReadOnlyList<ArchiveCommercialBundlePart> parts, string path, byte[] expected)
  {
    var part = parts.SingleOrDefault(x => x.Path == path);
    if (part is null || !part.Content.AsSpan().SequenceEqual(expected))
      throw new OperationBlockedException("archive-copy-commercial-bundle-source-part-mismatch");
  }

  private static bool IsSafePath(string? path) =>
    !string.IsNullOrWhiteSpace(path) && path.Length <= 2_000 && !path.StartsWith("/", StringComparison.Ordinal) &&
    !path.Contains('\\') && !path.Any(char.IsControl) &&
    path.Split('/').All(x => x.Length > 0 && x is not ("." or ".."));

  private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
