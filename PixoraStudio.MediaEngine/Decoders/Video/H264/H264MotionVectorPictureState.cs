using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Stores one reference list's motion vectors on a picture-relative 4x4-luma-block grid.
/// Coordinates and partition dimensions passed to this type are measured in
/// 4x4 luma blocks, not pixels or macroblocks.
/// </summary>
public sealed class H264MotionVectorPictureState
{
    private readonly Cell?[,] _cells;

    public H264MotionVectorPictureState(int macroblockWidth, int macroblockHeight)
    {
        if (macroblockWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockWidth));
        if (macroblockHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(macroblockHeight));

        MacroblockWidth = macroblockWidth;
        MacroblockHeight = macroblockHeight;
        _cells = new Cell?[checked(macroblockWidth * 4), checked(macroblockHeight * 4)];
    }

    public int MacroblockWidth { get; }
    public int MacroblockHeight { get; }
    public int BlockWidth => _cells.GetLength(0);
    public int BlockHeight => _cells.GetLength(1);

    /// <summary>Creates an independent snapshot of all decoded motion cells.</summary>
    public H264MotionVectorPictureState Clone()
    {
        H264MotionVectorPictureState clone = new(MacroblockWidth, MacroblockHeight);
        for (int y = 0; y < BlockHeight; y++)
        for (int x = 0; x < BlockWidth; x++)
            clone._cells[x, y] = _cells[x, y];
        return clone;
    }

    /// <summary>
    /// Resolves A (left), B (above), C (above-right), and D (above-left)
    /// candidates for a current partition. Neighbours outside the picture,
    /// not yet decoded, intra-coded, or belonging to another slice are
    /// unavailable. D is returned separately; the predictor applies it only
    /// when C is unavailable.
    /// </summary>
    public H264MotionVectorNeighbourCandidates GetCandidates(
        int x, int y, int width, int height, int sliceId)
    {
        ValidateRegion(x, y, width, height, sliceId, nameof(x), nameof(y));

        return new H264MotionVectorNeighbourCandidates(
            ReadCandidate(x - 1, y + height - 1, sliceId),
            ReadCandidate(x + width - 1, y - 1, sliceId),
            ReadCandidate(x + width, y - 1, sliceId),
            ReadCandidate(x - 1, y - 1, sliceId));
    }

    /// <summary>Records an already reconstructed inter partition over its 4x4-block region.</summary>
    public void RecordInterPartition(
        int x, int y, int width, int height, int sliceId,
        int referenceIndex, H264MotionVector motionVector)
    {
        if (referenceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex));
        WriteRegion(x, y, width, height, sliceId,
            new Cell(sliceId, referenceIndex, H264MotionVectorRange.Clip(motionVector)));
    }

    /// <summary>
    /// Marks a decoded intra region as unavailable for inter motion-vector
    /// prediction while retaining its occupancy and slice identity.
    /// </summary>
    public void RecordUnavailableRegion(
        int x, int y, int width, int height, int sliceId)
    {
        WriteRegion(x, y, width, height, sliceId,
            new Cell(sliceId, null, new H264MotionVector(0, 0)));
    }

    /// <summary>
    /// Reads stored motion metadata without applying current-slice neighbour
    /// availability rules. This is intended for collocated-picture temporal
    /// direct lookup, where the collocated picture was decoded previously.
    /// </summary>
    public bool TryGetStoredMotionVector(
        int x, int y, out H264MotionVectorCandidate candidate)
    {
        if ((uint)x >= (uint)BlockWidth || (uint)y >= (uint)BlockHeight)
        {
            candidate = default;
            return false;
        }

        Cell? cell = _cells[x, y];
        if (cell is not { } value || value.ReferenceIndex is not int referenceIndex)
        {
            candidate = default;
            return false;
        }

        candidate = new H264MotionVectorCandidate(referenceIndex, value.MotionVector);
        return true;
    }

    /// <summary>
    /// Derives the spatial predictor, adds the parsed MVD, and records the
    /// reconstructed vector immediately. Call in H.264 partition decoding
    /// order so later partitions can use earlier vectors from this macroblock.
    /// The reconstructed components are clipped to the normative signed 16-bit quarter-sample range.
    /// </summary>
    public H264MotionVector ReconstructAndRecord(
        H264MotionVectorPredictor predictor,
        int x, int y, int width, int height, int sliceId,
        int referenceIndex,
        H264MotionVectorDifference motionVectorDifference,
        H264MotionVectorPartitionShape partitionShape = H264MotionVectorPartitionShape.SubMacroblock,
        int partitionIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentNullException.ThrowIfNull(motionVectorDifference);
        if (referenceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(referenceIndex));

        H264MotionVectorNeighbourCandidates candidates =
            GetCandidates(x, y, width, height, sliceId);
        H264MotionVector predicted = predictor.Predict(
            candidates.Left, candidates.Above, candidates.AboveRight,
            candidates.AboveLeft, referenceIndex, partitionShape, partitionIndex);

        H264MotionVector reconstructed = H264MotionVectorRange.Clip(
            (long)predicted.Horizontal + motionVectorDifference.Horizontal,
            (long)predicted.Vertical + motionVectorDifference.Vertical);

        RecordInterPartition(x, y, width, height, sliceId,
            referenceIndex, reconstructed);
        return reconstructed;
    }

    private H264MotionVectorCandidate? ReadCandidate(int x, int y, int sliceId)
    {
        if ((uint)x >= (uint)BlockWidth || (uint)y >= (uint)BlockHeight)
            return null;

        Cell? cell = _cells[x, y];
        if (cell is not { } value || value.SliceId != sliceId ||
            value.ReferenceIndex is not int referenceIndex)
            return null;

        return new H264MotionVectorCandidate(referenceIndex, value.MotionVector);
    }

    private void WriteRegion(
        int x, int y, int width, int height, int sliceId, Cell value)
    {
        ValidateRegion(x, y, width, height, sliceId, nameof(x), nameof(y));

        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
        {
            if (_cells[column, row] is not null)
                throw new InvalidOperationException("A motion-vector partition overlaps an already recorded region.");
        }

        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
            _cells[column, row] = value;
    }

    private void ValidateRegion(
        int x, int y, int width, int height, int sliceId,
        string xParameterName, string yParameterName)
    {
        if (x < 0 || x >= BlockWidth)
            throw new ArgumentOutOfRangeException(xParameterName);
        if (y < 0 || y >= BlockHeight)
            throw new ArgumentOutOfRangeException(yParameterName);
        if (width <= 0 || width > BlockWidth - x)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > BlockHeight - y)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
    }

    private readonly record struct Cell(
        int SliceId,
        int? ReferenceIndex,
        H264MotionVector MotionVector);
}
