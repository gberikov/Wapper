using BenchmarkDotNet.Running;

// `dotnet run -c Release --project benchmarks/Wapper.Benchmarks -- --filter '*'`
// Results and the environment they were taken in are recorded in docs/performance.md.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
