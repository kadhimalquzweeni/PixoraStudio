namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Owns the CABAC context models for one slice and exposes context-coded,
/// bypass and termination bins. Syntax-element binarisation is implemented
/// by the slice and macroblock syntax readers.
/// </summary>
public sealed class H264CabacSliceDataReader : IH264CabacSliceDataReader, IH264CabacDiagnosticStateProvider, IH264CabacDiagnosticTraceSink
{
    private readonly H264BitReader _reader;
    private H264CabacArithmeticDecoder _arithmeticDecoder;
    private readonly H264CabacContextModel[] _contexts = new H264CabacContextModel[460];
    private readonly bool[] _initialized = new bool[460];
    private bool _terminated;
    private const int MaximumDiagnosticMarkers = 512;
    private const int PreservedInitialDiagnosticMarkers = 256;
    private readonly List<string>? _diagnosticTrace;

    public H264CabacSliceDataReader(
        H264BitReader reader,
        int sliceQpY,
        bool isIntraSlice,
        int cabacInitIdc,
        bool enableDiagnosticTrace = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (sliceQpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(sliceQpY));
        if (cabacInitIdc is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(cabacInitIdc));

        _reader = reader;
        _diagnosticTrace = enableDiagnosticTrace ? [] : null;

        // cabac_alignment_one_bit consists of one bits up to the next byte boundary.
        while (!reader.IsByteAligned)
        {
            if (!reader.ReadFlag())
                throw new FormatException("CABAC alignment bits must all be one.");
        }

        for (int index = 0; index < _contexts.Length; index++)
        {
            _initialized[index] = H264CabacContextInitializer.TryInitialize(
                index, sliceQpY, isIntraSlice, cabacInitIdc, out _contexts[index]);
        }

        _arithmeticDecoder = new H264CabacArithmeticDecoder(reader);
    }

    public bool IsTerminated => _terminated;

    public bool IsDiagnosticTraceEnabled => _diagnosticTrace is not null;

    /// <summary>Most recent syntax annotations, retained for failure diagnostics.</summary>
    public IReadOnlyList<string> DiagnosticTrace =>
        _diagnosticTrace?.ToArray() ?? Array.Empty<string>();

    public void TraceDiagnostic(string marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (_diagnosticTrace is not { } trace)
            return;

        if (trace.Count == MaximumDiagnosticMarkers)
            trace.RemoveAt(PreservedInitialDiagnosticMarkers);
        trace.Add(marker);
    }

    public int ArithmeticRange => _arithmeticDecoder.Range;

    public int ArithmeticOffset => _arithmeticDecoder.Offset;

    public int BitPosition => _reader.PositionBits;

    public H264CabacContextModel GetContextModel(int contextIndex)
    {
        if (contextIndex is < 0 or >= 460)
            throw new ArgumentOutOfRangeException(nameof(contextIndex));
        if (!_initialized[contextIndex])
            throw new ArgumentException(
                $"CABAC context index {contextIndex} is not defined for this slice type.",
                nameof(contextIndex));

        return _contexts[contextIndex];
    }

    /// <summary>
    /// Restarts the arithmetic engine after PCM samples. Probability contexts
    /// remain unchanged. The caller must consume all PCM samples and byte-align.
    /// </summary>
    public void RestartAfterPcm()
    {
        EnsureNotTerminated();
        if (!_reader.IsByteAligned)
            throw new InvalidOperationException("The bit reader must be byte-aligned after PCM samples.");
        _arithmeticDecoder = new H264CabacArithmeticDecoder(_reader);
    }

    public int DecodeBin(int contextIndex)
    {
        EnsureNotTerminated();
        if (contextIndex is < 0 or >= 460)
            throw new ArgumentOutOfRangeException(nameof(contextIndex));
        if (!_initialized[contextIndex])
            throw new ArgumentException(
                $"CABAC context index {contextIndex} is not defined for this slice type.",
                nameof(contextIndex));

        // Context models live in this slice-owned array. Decode in place to
        // avoid copying the two-int struct out and back for every CABAC bin.
        ref H264CabacContextModel context = ref _contexts[contextIndex];
        return _arithmeticDecoder.DecodeDecisionUnchecked(ref context);
    }

    public int DecodeBypass()
    {
        EnsureNotTerminated();
        return _arithmeticDecoder.DecodeBypass();
    }

    public int DecodeTerminate()
    {
        EnsureNotTerminated();
        return _arithmeticDecoder.DecodeTerminate();
    }

    public void MarkTerminated() => _terminated = true;

    private void EnsureNotTerminated()
    {
        if (_terminated)
            throw new InvalidOperationException("No CABAC bins can be decoded after the terminating bin.");
    }
}
