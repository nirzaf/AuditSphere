using AuditSphereOps.Application.Acceptance;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE v2.2 section 4.1.3: onboarding approval cards show one of Pending, Awaiting Evidence, Approved, Rejected or Stale. Only
/// Approved clears a key, and an unrecognised internal status is Pending rather than cleared.
/// </summary>
public sealed class OnboardingDisplayStatusTests
{
  [Theory]
  [InlineData(OnboardingReadinessStatuses.Current, OnboardingDisplayStatus.Approved)]
  [InlineData(OnboardingReadinessStatuses.AwaitingClient, OnboardingDisplayStatus.AwaitingEvidence)]
  [InlineData(OnboardingReadinessStatuses.NotDispatched, OnboardingDisplayStatus.Pending)]
  [InlineData(OnboardingReadinessStatuses.QuotationNotApproved, OnboardingDisplayStatus.Pending)]
  [InlineData(OnboardingReadinessStatuses.Declined, OnboardingDisplayStatus.Rejected)]
  [InlineData(OnboardingReadinessStatuses.Superseded, OnboardingDisplayStatus.Stale)]
  [InlineData(OnboardingReadinessStatuses.Stale, OnboardingDisplayStatus.Stale)]
  [InlineData("ACCEPTED_BY_EMAIL", OnboardingDisplayStatus.Pending)]
  public void ClientCommercialStatusesMapToTheApprovalVocabulary(string status, string expected) =>
    Assert.Equal(expected, OnboardingDisplayStatus.ForClient(status));

  [Theory]
  [InlineData(OnboardingReadinessStatuses.Current, OnboardingDisplayStatus.Approved)]
  [InlineData(OnboardingReadinessStatuses.Missing, OnboardingDisplayStatus.Pending)]
  [InlineData(OnboardingReadinessStatuses.Conditional, OnboardingDisplayStatus.Pending)]
  [InlineData(OnboardingReadinessStatuses.Deferred, OnboardingDisplayStatus.Pending)]
  [InlineData(OnboardingReadinessStatuses.Declined, OnboardingDisplayStatus.Rejected)]
  [InlineData(OnboardingReadinessStatuses.Stale, OnboardingDisplayStatus.Stale)]
  [InlineData(OnboardingReadinessStatuses.Unavailable, OnboardingDisplayStatus.AwaitingEvidence)]
  [InlineData("CLEARED_BY_CHAT", OnboardingDisplayStatus.Pending)]
  public void PartnerRiskStatusesMapToTheApprovalVocabulary(string status, string expected) =>
    Assert.Equal(expected, OnboardingDisplayStatus.ForPartner(status));
}
