using BenchmarkDotNet.Attributes;

namespace CyphalSharp.Benchmark
{
    [MemoryDiagnoser]
    public class InitializationBenchmark
    {
        private string? _dsdlPath;

        [GlobalSetup]
        public void Setup()
        {
            // Find the DSDL directory path
            var currentDirectory = Directory.GetCurrentDirectory();
            
            _dsdlPath = Path.Combine(currentDirectory, "DSDL");
        }

        [Benchmark]
        public void InitializeCyphal()
        {
            Cyphal.Initialize(_dsdlPath);
        }
    }
}
