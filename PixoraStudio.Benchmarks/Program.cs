using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PixoraStudio.MediaEngine.Decoders.Video.H264;

BenchmarkRunner.Run<H264HotPathBenchmarks>();

[MemoryDiagnoser]
public class H264HotPathBenchmarks
{
    private const int Iterations = 4096;
    private byte[] _bitstream = [];
    private int _checksum;

    [GlobalSetup]
    public void Setup()
    {
        _bitstream = new byte[4096];
        new Random(8675309).NextBytes(_bitstream);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Iterations)]
    public int ReadBits()
    {
        H264BitReader reader = new(_bitstream);
        int checksum = 0;
        for (int i = 0; i < Iterations; i++)
            checksum ^= (int)reader.ReadBits(8);
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = Iterations)]
    public int ReadUnsignedExpGolomb()
    {
        // 0xFF is a sequence of one-bit ue(v)=0 codewords.
        byte[] input = new byte[Iterations / 8];
        Array.Fill(input, (byte)0xFF);
        H264BitReader reader = new(input);
        int checksum = 0;
        for (int i = 0; i < Iterations; i++)
            checksum += (int)reader.ReadUnsignedExpGolomb();
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = Iterations)]
    public int CabacDecisionBins()
    {
        H264CabacArithmeticDecoder decoder = new(new H264BitReader(new byte[Iterations]));
        H264CabacContextModel context = new(10, 0);
        int checksum = 0;
        for (int i = 0; i < Iterations; i++)
            checksum += decoder.DecodeDecision(ref context);
        return checksum ^ decoder.Range ^ context.State;
    }
}
