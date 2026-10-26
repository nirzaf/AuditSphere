using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class UserProfileTests
{
  private static readonly DateOnly Today = new(2026, 10, 26);

  [Theory]
  [InlineData("2026-10-26", null, null, null, "Date of birth must be a past date.")]
  [InlineData(null, null, "12345", null, "Enter a mobile number of 7 to 15 digits, optionally starting with + and the country code.")]
  [InlineData(null, null, "+97455551234x", null, "Enter a mobile number of 7 to 15 digits, optionally starting with + and the country code.")]
  [InlineData(null, "2026-10-28", null, null, "Joined date cannot be in the future.")]
  [InlineData("1990-05-01", "1990-05-01", null, null, "Joined date must be after the date of birth.")]
  [InlineData(null, null, null, "UNSPECIFIED", "Choose a listed gender option or leave it blank.")]
  public void Rules_RefuseImpossibleOrMalformedDetails(string? birth, string? joined, string? mobile, string? gender, string expected)
  {
    var input = new UserProfileDetails(null, gender, birth is null ? null : DateOnly.Parse(birth), mobile,
      joined is null ? null : DateOnly.Parse(joined));
    Assert.Equal(expected, UserProfileRules.Normalize(input, Today).Error);
  }

  [Fact]
  public void Rules_KeepEveryFieldOptionalAndNormalizeText()
  {
    Assert.Null(UserProfileRules.Normalize(new UserProfileDetails(null, null, null, null, null), Today).Error);
    var (details, error) = UserProfileRules.Normalize(new UserProfileDetails("  Audit Senior ", " female ",
      new DateOnly(1990, 5, 1), " +974 5555-1234 ", Today), Today);
    Assert.Null(error);
    Assert.Equal(new UserProfileDetails("Audit Senior", UserProfileGenders.Female, new DateOnly(1990, 5, 1),
      "+974 5555-1234", Today), details);
    Assert.NotNull(UserProfileRules.Normalize(new UserProfileDetails(new string('x', 121), null, null, null, null), Today).Error);
  }

  [Theory]
  [InlineData("  Alex  Reviewer ", "Alex  Reviewer")]
  [InlineData("   ", null)]
  [InlineData(null, null)]
  [InlineData("Name\nInjected", null)]
  public void SignInFullName_UsesOnlyAUsableProviderName(string? claim, string? expected) =>
    Assert.Equal(expected, UserProfileService.SignInFullName(claim));

  [Fact]
  public async Task Profile_IsSelfServiceRevisionFencedAndNeverExposesAnotherUser()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
    await using var db = new AuditSphereDbContext(pg.Options);

    var initial = (await UserProfileService.GetAsync(db, staff)).Value!;
    Assert.Equal((f.Staff.Id, "Staff", f.Staff.DisplayName, f.Staff.Email, 0L), (initial.UserId, initial.UserType,
      initial.FullName, initial.Email, initial.Revision));
    Assert.Null(initial.Position);

    var created = await UserProfileService.UpdateAsync(db, staff,
      new UpdateUserProfileRequest(" Audit Senior ", "MALE", new DateOnly(1990, 5, 1), "+974 5555 1234", new DateOnly(2020, 2, 3), 0));
    Assert.True(created.Succeeded, created.Message);
    Assert.Equal(("Audit Senior", 1L), (created.Value!.Position, created.Value.Revision));
    Assert.Equal(f.Staff.DisplayName, created.Value.FullName);

    // A second window that still holds the empty profile cannot overwrite the saved one.
    var stale = await UserProfileService.UpdateAsync(db, staff, new UpdateUserProfileRequest("Manager", null, null, null, null, 0));
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    var cleared = await UserProfileService.UpdateAsync(db, staff, new UpdateUserProfileRequest(null, null, null, " ", null, 1));
    Assert.True(cleared.Succeeded, cleared.Message);
    Assert.Equal((2L, (string?)null, (string?)null), (cleared.Value!.Revision, cleared.Value.Position, cleared.Value.MobileNumber));

    var invalid = await UserProfileService.UpdateAsync(db, staff,
      new UpdateUserProfileRequest(null, null, null, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), 2));
    Assert.Equal("request.invalid", invalid.ErrorCode);

    // Each user reads only their own record; there is no identifier to address someone else's.
    var other = (await UserProfileService.GetAsync(db, reviewer)).Value!;
    Assert.Equal((f.Reviewer.Id, 0L, (string?)null), (other.UserId, other.Revision, other.Gender));

    await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.Equal(ErrorCodes.ScopeDenied, (await UserProfileService.GetAsync(db, staff)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await UserProfileService.UpdateAsync(db, staff,
      new UpdateUserProfileRequest("Manager", null, null, null, null, 2))).ErrorCode);
    Assert.Equal(2, (await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == f.Staff.Id)).Revision);
  }
}
