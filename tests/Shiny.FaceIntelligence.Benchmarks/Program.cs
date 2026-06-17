using BenchmarkDotNet.Running;

// Run all: dotnet run -c Release
// Quick validation: dotnet run -c Release -- --job dry
// Filter: dotnet run -c Release -- --filter *Recognize*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

public partial class Program;
