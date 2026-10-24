using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using AuditSphereOps.Application.Microsoft365;

// Preparation only: no network, signing credential, trust configuration, enrollment or runtime mutation.
if (args is ["--help"])
{
  Console.WriteLine("prepare --readback <file> --binding <independently-reviewed-json> --repository <checkout> --expires-utc <timestamp> --output <new-file>");
  Console.WriteLine("Writes UNSIGNED_CANONICAL_PREPARED only. Approved detached signing and authenticated enrollment remain separate.");
  return 0;
}

string? temporary = null;
try
{
  if (args.Length != 11 || args[0] != "prepare") throw new ArgumentException();
  var options = new Dictionary<string, string>(StringComparer.Ordinal);
  string[] allowed = ["--readback", "--binding", "--repository", "--expires-utc", "--output"];
  for (var i = 1; i < args.Length; i += 2)
    if (!allowed.Contains(args[i], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(args[i + 1]) ||
        !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException();
  var root = Path.GetFullPath(options["--repository"]);
  if (Path.TrimEndingDirectorySeparator(root) != Path.TrimEndingDirectorySeparator(Git(root, "rev-parse", "--show-toplevel")))
    throw new ArgumentException();
  var source = ReadSource();
  if (!(options["--expires-utc"].EndsWith('Z') || options["--expires-utc"].EndsWith("+00:00", StringComparison.Ordinal)) ||
      !DateTimeOffset.TryParse(options["--expires-utc"], CultureInfo.InvariantCulture,
      DateTimeStyles.None, out var expires) || expires.Offset != TimeSpan.Zero) throw new ArgumentException();
  var attestation = MailAuthorizationAttestationCanonicalizer.PrepareReadOnlyAttestation(
    ReadBounded(options["--readback"]), ReadBounded(options["--binding"]), source, DateTimeOffset.UtcNow, expires);
  var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
  if (ReadSource() != source) throw new ArgumentException();
  var output = Path.GetFullPath(options["--output"]);
  var directory = Path.GetDirectoryName(output)!;
  // Require an existing approved operator directory; never replace a retained payload.
  if (!Directory.Exists(directory) || File.Exists(output)) throw new ArgumentException();
  temporary = Path.Combine(directory, $".mail-canonical-{Guid.NewGuid():N}.tmp");
  var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
  if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
  using (var stream = new FileStream(temporary, fileOptions))
  {
    stream.Write(payload);
    stream.Flush(flushToDisk: true);
  }
  File.Move(temporary, output, overwrite: false);
  temporary = null;
  Console.WriteLine($"UNSIGNED_CANONICAL_PREPARED sha256={Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}");
  return 0;

  MailAuthorizationSourceIdentity ReadSource()
  {
    var commit = Git(root, "rev-parse", "HEAD");
    if (Git(root, "status", "--porcelain", "--", "scripts/m365").Length != 0) throw new ArgumentException();
    return new(commit, Hash("Configure-MailAccess.ps1"), Hash("MailAccess.psm1"), Hash("modules.lock.json"),
      Hash("AuditSphere.M365.Common.psm1"), Hash("AuditSphere.M365.Entra.psm1"), Hash("m365-plan.schema.json"));
  }

  string Hash(string relative) => Convert.ToHexString(SHA256.HashData(ReadBounded(
    Path.Combine(root, "scripts", "m365", relative)))).ToLowerInvariant();
}
catch (Exception ex) when (ex is MailAuthorizationAttestationException or ArgumentException or
    IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or KeyNotFoundException or
    System.ComponentModel.Win32Exception)
{
  // Do not echo untrusted provider text, identities, paths or process output.
  Console.Error.WriteLine("MAIL_EVIDENCE_PREPARATION_REFUSED");
  return 2;
}
finally
{
  if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
}

static byte[] ReadBounded(string path)
{
  using var stream = File.OpenRead(path);
  var buffer = new byte[MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes + 1];
  var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
  if (count > MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes) throw new ArgumentException();
  return buffer[..count];
}

static string Git(string root, params string[] arguments)
{
  var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
  start.ArgumentList.Add("-C"); start.ArgumentList.Add(root);
  foreach (var argument in arguments) start.ArgumentList.Add(argument);
  using var process = Process.Start(start) ?? throw new InvalidOperationException();
  var output = process.StandardOutput.ReadToEnd();
  var error = process.StandardError.ReadToEnd();
  process.WaitForExit();
  if (process.ExitCode != 0 || error.Length != 0) throw new InvalidOperationException();
  return output.Trim();
}
