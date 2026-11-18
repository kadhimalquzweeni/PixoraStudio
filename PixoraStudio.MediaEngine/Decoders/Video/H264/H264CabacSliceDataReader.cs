namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Owns the CABAC context models for one slice and exposes context-coded,
/// bypass and termination bins. Syntax-element binarisation is implemented
/// by the slice and macroblock syntax readers.
/// </summary>
public sealed class H264CabacSliceDataReader
{
    private readonly H264CabacArithmeticDecoder _arithmeticDecoder;
    private readonly H264CabacContextModel[] _contexts = new H264CabacContextModel[460];
    private readonly bool[] _initialized = new bool[460];
    private bool _terminated;

    public H264CabacSliceDataReader(
        H264BitReader reader,
        int sliceQpY,
        bool isIntraSlice,
        int cabacInitIdc)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (sliceQpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(sliceQpY));
        if (cabacInitIdc is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(cabacInitIdc));

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

    public int DecodeBin(int contextIndex)
    {
        EnsureNotTerminated();
        if (contextIndex is < 0 or >= 460)
            throw new ArgumentOutOfRangeException(nameof(contextIndex));
        if (!_initialized[contextIndex])
            throw new ArgumentException(
                $"CABAC context index {contextIndex} is not defined for this slice type.",
                nameof(contextIndex));

        H264CabacContextModel context = _contexts[contextIndex];
        int bin = _arithmeticDecoder.DecodeDecision(ref context);
        _contexts[contextIndex] = context;
        return bin;
    }

    public int DecodeBypass()
    {
        EnsureNotTerminated();
        return _arithmeticDecoder.DecodeBypass();
    }

    public int DecodeTerminate()
    {
        EnsureNotTerminated();
        int bin = _arithmeticDecoder.DecodeTerminate();
        if (bin == 1)
            _terminated = true;
        return bin;
    }

    private void EnsureNotTerminated()
    {
        if (_terminated)
            throw new InvalidOperationException("No CABAC bins can be decoded after the terminating bin.");
    }
}
