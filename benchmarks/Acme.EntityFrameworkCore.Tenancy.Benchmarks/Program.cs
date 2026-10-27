using BenchmarkDotNet.Running;

// Selects the benchmarks by the command line, e.g. --filter '*' for all of them or
// --filter '*Materialization*' for one class; without arguments it asks interactively
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Entry point; the benchmarks are the classes in the other files.</summary>
public partial class Program
{
}
