namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Implements the H.264 CABAC arithmetic engine. Syntax-element binarisation and
/// normative context initialisation are deliberately kept outside this class.
/// </summary>
public sealed class H264CabacArithmeticDecoder
{
    private readonly H264BitReader _reader;
    private readonly byte[,] _rangeLps;
    private readonly byte[] _transMps;
    private readonly byte[] _transLps;
    private int _range = 510;
    private int _offset;

    public H264CabacArithmeticDecoder(H264BitReader reader)
        : this(reader, H264CabacTables.RangeLps, H264CabacTables.TransitionMps, H264CabacTables.TransitionLps)
    {
    }

    public H264CabacArithmeticDecoder(
        H264BitReader reader,
        byte[,] rangeLps,
        byte[] transMps,
        byte[] transLps)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(rangeLps);
        ArgumentNullException.ThrowIfNull(transMps);
        ArgumentNullException.ThrowIfNull(transLps);
        if (rangeLps.GetLength(0) != 64 || rangeLps.GetLength(1) != 4)
            throw new ArgumentException("The CABAC range-LPS table must have dimensions 64 by 4.", nameof(rangeLps));
        if (transMps.Length != 64 || transLps.Length != 64)
            throw new ArgumentException("Each CABAC state-transition table must contain 64 entries.");
        for (int state = 0; state < 64; state++)
        {
            if (transMps[state] >= 64 || transLps[state] >= 64)
                throw new ArgumentException("CABAC state transitions must remain within states 0 through 63.");
            for (int q = 0; q < 4; q++)
                if (rangeLps[state, q] is < 2 or > 240)
                    throw new ArgumentException("CABAC range-LPS values must be between 2 and 510.", nameof(rangeLps));
        }

        _reader = reader;
        _rangeLps = (byte[,])rangeLps.Clone();
        _transMps = (byte[])transMps.Clone();
        _transLps = (byte[])transLps.Clone();
        _offset = checked((int)reader.ReadBits(9));
        if (_offset >= _range)
            throw new FormatException("The initial CABAC offset must be less than 510.");
    }

    public int Range => _range;
    public int Offset => _offset;

    /// <summary>Decodes one context-coded decision bin and updates its context model.</summary>
    public int DecodeDecision(ref H264CabacContextModel context)
    {
        ValidateContext(context);
        int qRangeIndex = (_range >> 6) & 3;
        int rangeLps = _rangeLps[context.State, qRangeIndex];
        _range -= rangeLps;

        int bin;
        if (_offset < _range)
        {
            bin = context.MostProbableSymbol;
            context = context with { State = _transMps[context.State] };
        }
        else
        {
            bin = 1 - context.MostProbableSymbol;
            _offset -= _range;
            _range = rangeLps;
            if (context.State == 0)
                context = context with { MostProbableSymbol = 1 - context.MostProbableSymbol };
            context = context with { State = _transLps[context.State] };
        }

        Renormalize();
        return bin;
    }

    /// <summary>Decodes a bypass bin without adapting a probability context.</summary>
    public int DecodeBypass()
    {
        _offset = checked((_offset << 1) | (int)_reader.ReadBits(1));
        if (_offset >= _range)
        {
            _offset -= _range;
            return 1;
        }
        return 0;
    }

    /// <summary>Decodes the terminating bin. A one indicates the end of the slice.</summary>
    public int DecodeTerminate()
    {
        _range -= 2;
        if (_offset >= _range)
            return 1;
        Renormalize();
        return 0;
    }

    private void Renormalize()
    {
        while (_range < 256)
        {
            _range <<= 1;
            _offset = checked((_offset << 1) | (int)_reader.ReadBits(1));
        }
    }

    private static void ValidateContext(H264CabacContextModel context)
    {
        if (context.State is < 0 or > 63)
            throw new ArgumentOutOfRangeException(nameof(context), "CABAC context state must be between 0 and 63.");
        if (context.MostProbableSymbol is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(context), "CABAC MPS must be zero or one.");
    }
}

/// <summary>A CABAC probability state: pStateIdx and valMPS.</summary>
public record struct H264CabacContextModel(int State, int MostProbableSymbol);
