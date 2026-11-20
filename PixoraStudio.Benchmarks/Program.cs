using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

BenchmarkRunner.Run<H264HotPathBenchmarks>();

[MemoryDiagnoser]
public class H264HotPathBenchmarks
{
    private const int Iterations = 4096;
    private byte[] _bitstream = [];
    private H264SequenceParameterSet _sps = null!;
    private H264PictureParameterSetHeader _pps = null!;
    private H264IntraChromaResidualComponent _chromaComponent = null!;
    private byte[] _chromaPrediction = [];
    private int[] _transform8x8Coefficients = [];
    private H264DecodedPictureSurface _motionReference = null!;
    private H264MotionCompensator _motionCompensator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bitstream = new byte[4096];
        new Random(8675309).NextBytes(_bitstream);

        _sps = new H264SequenceParameterSet(
            0, 100, 40, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);
        _pps = new H264PictureParameterSetHeader(0, 0, false, false, 0);
        H264IntraChromaResidualBlock[] blocks = Enumerable.Range(0, 4)
            .Select(index => new H264IntraChromaResidualBlock(
                index % 2, index / 2, false,
                new H264CavlcResidualBlock(15, 0, 0, 0, new int[15])))
            .ToArray();
        _chromaComponent = new H264IntraChromaResidualComponent(1, null, blocks);
        _chromaPrediction = Enumerable.Repeat((byte)128, 64).ToArray();
        _transform8x8Coefficients = Enumerable.Range(-32, 64).ToArray();
        _motionReference = new H264DecodedPictureSurface(_sps);
        _motionReference.WriteMacroblock(
            0, 0, 0, false,
            new byte[256],
            Enumerable.Repeat((byte)96, 64).ToArray(),
            Enumerable.Repeat((byte)160, 64).ToArray());
        _motionCompensator = new H264MotionCompensator();
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

    [Benchmark(OperationsPerInvoke = Iterations)]
    public int CabacDecisionBinsWithProfiling()
    {
        H264CabacPerformanceProfiler profiler = new();
        H264CabacArithmeticDecoder decoder = new(
            new H264BitReader(new byte[Iterations]), profiler);
        H264CabacContextModel context = new(10, 0);
        int checksum = 0;
        for (int i = 0; i < Iterations; i++)
            checksum += decoder.DecodeDecision(ref context);

        H264CabacPerformanceSnapshot snapshot = profiler.GetSnapshot();
        return checksum ^ decoder.Range ^ context.State ^
            (int)snapshot.DecisionBinCount ^ (int)snapshot.RenormalizationCount;
    }

    [Benchmark]
    public int MotionCompensationIntegerMotion()
    {
        H264MotionCompensatedBlock block = _motionCompensator.Predict(
            _motionReference, 0, 0, 16, 16, new H264MotionVector(8, -8));
        return block.Luma[0] + block.Cb[0] + block.Cr[^1];
    }

    [Benchmark]
    public int MotionCompensationFractionalMotion()
    {
        H264MotionCompensatedBlock block = _motionCompensator.Predict(
            _motionReference, 0, 0, 16, 16, new H264MotionVector(3, -5));
        return block.Luma[0] + block.Cb[0] + block.Cr[^1];
    }

    [Benchmark(OperationsPerInvoke = 1)]
    public int Inverse8x8Transform()
    {
        int[] output = H264InverseTransform.Inverse8x8(_transform8x8Coefficients);
        return output[0] ^ output[31] ^ output[63];
    }

    [Benchmark(OperationsPerInvoke = 1)]
    public int ChromaResidualReconstruction()
    {
        byte[] output = H264IntraChromaResidualReconstructor.Reconstruct(
            _chromaPrediction, _chromaComponent, 24, _sps, _pps);
        return output[0] + output[63];
    }

}
