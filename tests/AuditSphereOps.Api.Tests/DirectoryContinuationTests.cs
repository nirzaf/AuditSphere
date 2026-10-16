using AuditSphereOps.Api.Ui;
using AuditSphereOps.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AuditSphereOps.Api.Tests;

public sealed class DirectoryContinuationTests
{
  private static DirectoryContinuationContext Context() =>
    new(Guid.NewGuid(), Guid.NewGuid(), 9007199254740993, Guid.NewGuid(), false, "Ada", "example.test");

  [Fact]
  public void ExactContextRoundTripsRawTokenAndNullRemainsFirstPage()
  {
    var helper = new DirectoryContinuation(new EphemeralDataProtectionProvider());
    var context = Context();
    var now = DateTimeOffset.UtcNow;
    Assert.True(helper.TryRead(null, context, now, out var first));
    Assert.Null(first);
    Assert.True(helper.TryWrite(null, context, now, out var last));
    Assert.Null(last);
    Assert.True(helper.TryWrite("opaque-provider-token", context, now, out var token));
    Assert.NotEqual("opaque-provider-token", token);
    Assert.True(helper.TryRead(token, context, now, out var raw));
    Assert.Equal("opaque-provider-token", raw);
  }

  [Theory]
  [InlineData("firm")]
  [InlineData("actor")]
  [InlineData("epoch")]
  [InlineData("tenant")]
  [InlineData("mode")]
  [InlineData("prefix")]
  [InlineData("domain")]
  public void AnyInheritedContextChangeRefusesContinuation(string field)
  {
    var helper = new DirectoryContinuation(new EphemeralDataProtectionProvider());
    var context = Context();
    var now = DateTimeOffset.UtcNow;
    Assert.True(helper.TryWrite("provider-token", context, now, out var token));
    var changed = field switch
    {
      "firm" => context with { FirmId = Guid.NewGuid() },
      "actor" => context with { ActorId = Guid.NewGuid() },
      "epoch" => context with { ActorEpoch = context.ActorEpoch + 1 },
      "tenant" => context with { TenantId = Guid.NewGuid() },
      "mode" => context with { Browse = true, Prefix = null },
      "prefix" => context with { Prefix = "Bob" },
      _ => context with { Domain = "other.test" }
    };
    Assert.False(helper.TryRead(token, changed, now, out var raw));
    Assert.Null(raw);
  }

  [Fact]
  public void ExpiryTamperingRawAndOversizedInputFailClosed()
  {
    var helper = new DirectoryContinuation(new EphemeralDataProtectionProvider());
    var context = Context();
    var now = DateTimeOffset.UtcNow;
    Assert.True(helper.TryWrite("provider-token", context, now, out var token));
    Assert.False(helper.TryRead(token, context, now + DirectoryContinuation.Lifetime, out _));
    Assert.False(helper.TryRead(token, context, now.AddTicks(-1), out _));
    var chars = token!.ToCharArray();
    chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';
    Assert.False(helper.TryRead(new string(chars), context, now, out _));
    Assert.False(helper.TryRead("raw-provider-token", context, now, out _));
    Assert.False(helper.TryRead(string.Empty, context, now, out _));
    Assert.False(helper.TryRead(new string('x', DirectoryContinuation.MaximumEnvelopeLength + 1), context, now, out _));
    Assert.False(helper.TryWrite(new string('x', DirectoryContinuation.MaximumProviderTokenLength + 1), context, now, out _));
  }

  [Fact]
  public void MaximumUnicodeProviderTokenAndPrefixFitExplicitEnvelopeAndUiDecoderBounds()
  {
    var helper = new DirectoryContinuation(new EphemeralDataProtectionProvider());
    var context = Context() with { Prefix = new string('ع', 80), Domain = new string('a', 249) + ".com" };
    var raw = new string('ع', DirectoryContinuation.MaximumProviderTokenLength);
    var now = DateTimeOffset.UtcNow;
    Assert.True(helper.TryWrite(raw, context, now, out var token));
    Assert.NotNull(token);
    Assert.True(token.Length <= DirectoryContinuation.MaximumEnvelopeLength);
    Assert.Equal(20000, DirectoryContinuation.MaximumEnvelopeLength);
    Assert.True(helper.TryRead(token, context, now, out var decoded));
    Assert.Equal(raw, decoded);
  }

  [Fact]
  public void QueryCanonicalizationMatchesInheritedProviderSemantics()
  {
    var actor = new ActorContext(Guid.NewGuid(), Guid.NewGuid(), 17, ["Administrator"]);
    var tenant = Guid.NewGuid().ToString("D");
    Assert.True(DirectoryContinuation.TryContext(actor, tenant, false, " Ada ", " @Example.Test ", out var context));
    Assert.Equal("Ada", context!.Prefix);
    Assert.Equal("example.test", context.Domain);
    Assert.True(DirectoryContinuation.TryContext(actor, tenant, true, "ignored", "", out var browse));
    Assert.Null(browse!.Prefix);
    Assert.Null(browse.Domain);
    Assert.False(DirectoryContinuation.TryContext(actor, tenant, false, "A", null, out _));
    Assert.False(DirectoryContinuation.TryContext(actor, tenant, true, null, "bad'example.test", out _));
  }
}
