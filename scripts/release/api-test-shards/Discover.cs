// Uses the existing pinned xUnit assemblies; no additional test package or runner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using Xunit.Abstractions;
using Xunit.Sdk;

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("assembly output.json");
        var assembly = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(Path.GetDirectoryName(assembly)!, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Discover(assembly, args[1]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Discover(string assembly, string output)
    {
        var sink = new Sink();
        var info = new ReflectionAssemblyInfo(assembly);
        if (info.Assembly.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "Xunit.TestFrameworkAttribute"))
            throw new InvalidOperationException("Custom test frameworks require explicit discovery support");
        using var framework = new XunitTestFramework(sink);
        using var discoverer = framework.GetDiscoverer(info);
        discoverer.Find(false, sink, new Options());
        if (!sink.Done.Wait(TimeSpan.FromMinutes(3))) throw new TimeoutException("xUnit discovery deadline");
        if (sink.Errors.Count != 0) throw new InvalidOperationException(string.Join("\n", sink.Errors));
        if (sink.Cases.Count == 0) throw new InvalidOperationException("Empty discovery");
        var rows = sink.Cases.Select(c => new {
            id = c.UniqueID,
            collection = c.TestMethod.TestClass.TestCollection.UniqueID.ToString(),
            className = c.TestMethod.TestClass.Class.Name,
            method = c.TestMethod.Method.Name,
            display = c.DisplayName,
            kind = c.GetType().FullName,
            skip = c.SkipReason,
        }).OrderBy(c => c.id).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    sealed class Options : ITestFrameworkDiscoveryOptions
    {
        readonly Dictionary<string, object> values = new() {
            ["xunit.discovery.PreEnumerateTheories"] = true,
            ["xunit.discovery.SynchronousMessageReporting"] = true,
        };
        public TValue GetValue<TValue>(string name) => values.TryGetValue(name, out var value) ? (TValue)value : default!;
        public void SetValue<TValue>(string name, TValue value) => values[name] = value!;
    }

    sealed class Sink : IMessageSink
    {
        public ManualResetEventSlim Done { get; } = new();
        public List<ITestCase> Cases { get; } = new();
        public List<string> Errors { get; } = new();
        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestCaseDiscoveryMessage discovered) Cases.Add(discovered.TestCase);
            if (message is IErrorMessage error) Errors.Add(string.Join("\n", error.Messages));
            if (message is IDiscoveryCompleteMessage) Done.Set();
            return true;
        }
    }
}
