using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Security;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

internal static class Program
{
    static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            var path = Path.Combine(args[0], name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Discover(args);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Discover(string[] args)
    {
        var wrapper = new VsTestConsoleWrapper(Path.Combine(args[0], "vstest.console.dll"));
        wrapper.StartSession();
        try {
            wrapper.InitializeExtensions(new[] { Path.Combine(Path.GetDirectoryName(args[1])!, "xunit.runner.visualstudio.testadapter.dll") });
            var sink = new Sink();
            wrapper.DiscoverTests(new[] { args[1] }, Settings(null), sink);
            if (!sink.Complete || sink.Errors.Count != 0 || sink.Total != sink.Cases.Count || sink.Total == 0)
                throw new InvalidOperationException("Incomplete/error discovery: " + string.Join("\n", sink.Errors));
            var inventory = sink.Cases.Select(c => new {
                id = c.Id, fqn = c.FullyQualifiedName, display = c.DisplayName,
                xunitId = Property(c, "XunitTestCaseUniqueID"),
                className = Property(c, "TestCase.ManagedType"),
                method = Property(c, "TestCase.ManagedMethod"),
            }).OrderBy(c => c.id).ToArray();
            var json = JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true });
            if (args.Length == 3) File.WriteAllText(args[2], json);
            else {
                // Rediscover actual adapter objects in this fresh process, never reconstruct them.
                if (File.ReadAllText(args[2]) != json) throw new InvalidOperationException("Discovery changed after inventory freeze");
                var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[3]))!;
                var selected = sink.Cases.Where(c => ids.Contains(c.Id.ToString())).ToArray();
                if (ids.Length == 0 || ids.Distinct().Count() != ids.Length || selected.Length != ids.Length)
                    throw new InvalidOperationException("Invalid shard assignment");
                var run = new RunSink();
                try { wrapper.RunTests(selected, Settings(args[4]), run); }
                finally {
                    File.WriteAllText(Path.Combine(args[4], "events.json"), JsonSerializer.Serialize(new {
                        complete = run.Complete, total = run.Total, errors = run.Errors,
                        results = run.Results.Select(r => new { id = r.TestCase.Id, outcome = r.Outcome.ToString() })
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                if (!run.Complete || run.Errors.Count != 0 || run.Total != ids.Length ||
                    run.Results.Count != ids.Length || run.Results.Any(r => r.Outcome != TestOutcome.Passed) ||
                    !run.Results.Select(r => r.TestCase.Id.ToString()).OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)))
                    throw new InvalidOperationException("Shard did not pass its complete exact assignment");
            }
        } finally { wrapper.EndSession(); }
        return 0;
    }
    static string Property(TestCase c, string name) => Convert.ToString(c.GetPropertyValue(c.Properties.Single(p => p.Id == name)))!;
    static string Settings(string? results) => "<RunSettings><RunConfiguration><MaxCpuCount>1</MaxCpuCount>" +
        (results == null ? "" : "<ResultsDirectory>" + SecurityElement.Escape(results) + "</ResultsDirectory><TestSessionTimeout>1200000</TestSessionTimeout>") +
        "</RunConfiguration><xUnit><PreEnumerateTheories>true</PreEnumerateTheories></xUnit>" +
        (results == null ? "" : "<LoggerRunSettings><Loggers><Logger friendlyName=\"trx\" enabled=\"true\"><Configuration><LogFileName>results.trx</LogFileName></Configuration></Logger></Loggers></LoggerRunSettings>" +
            "<DataCollectionRunSettings><DataCollectors><DataCollector friendlyName=\"blame\" enabled=\"true\"><Configuration><CollectDumpOnTestSessionHang TestTimeout=\"15m\" HangDumpType=\"None\" /></Configuration></DataCollector></DataCollectors></DataCollectionRunSettings>") + "</RunSettings>";

    sealed class RunSink : ITestRunEventsHandler
    {
        public List<TestResult> Results = new();
        public List<string> Errors = new();
        public bool Complete;
        public long Total;
        public void HandleTestRunStatsChange(TestRunChangedEventArgs? args) { if (args?.NewTestResults != null) Results.AddRange(args.NewTestResults); }
        public void HandleTestRunComplete(TestRunCompleteEventArgs args, TestRunChangedEventArgs? last,
            ICollection<AttachmentSet>? attachments, ICollection<string>? executors) {
            HandleTestRunStatsChange(last); Total = args.TestRunStatistics?.ExecutedTests ?? -1;
            Complete = !args.IsAborted && !args.IsCanceled && args.Error == null;
            if (args.Error != null) Errors.Add(args.Error.ToString());
        }
        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo info) => throw new NotSupportedException();
        public void HandleLogMessage(TestMessageLevel level, string? message) {
            Console.WriteLine(level + ": " + message);
            if (level == TestMessageLevel.Error) Errors.Add(message ?? "Unknown runner error");
        }
        public void HandleRawMessage(string raw) { }
    }
    sealed class Sink : ITestDiscoveryEventsHandler
    {
        public List<TestCase> Cases = new();
        public List<string> Errors = new();
        public bool Complete;
        public long Total;
        public void HandleDiscoveredTests(IEnumerable<TestCase>? tests) { if (tests != null) Cases.AddRange(tests); }
        public void HandleDiscoveryComplete(long total, IEnumerable<TestCase>? last, bool aborted) {
            HandleDiscoveredTests(last); Total = total; Complete = !aborted;
        }
        public void HandleLogMessage(TestMessageLevel level, string? message) {
            Console.Error.WriteLine(level + ": " + message);
            if (level == TestMessageLevel.Error) Errors.Add(message ?? "Unknown discovery error");
        }
        public void HandleRawMessage(string raw) { }
    }
}
