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
    /// Merges only the motion-vector cells owned by one decoded slice.
    /// Slice-local states can therefore be decoded independently and combined
    /// after their workers finish, without exposing cross-slice neighbours.
    /// </summary>
    public void MergeSliceFrom(H264MotionVectorPictureState source, int sliceId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(this, source))
            throw new ArgumentException("A motion-vector state cannot be merged into itself.", nameof(source));
        if (source.MacroblockWidth != MacroblockWidth ||
            source.MacroblockHeight != MacroblockHeight)
            throw new ArgumentException(
                "Source and destination motion-vector states must have matching dimensions.",
                nameof(source));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));

        // Validate the complete merge before writing anything, so a conflict
        // cannot leave the destination with a partially merged slice.
        for (int y = 0; y < BlockHeight; y++)
        for (int x = 0; x < BlockWidth; x++)
        {
            Cell? sourceCell = source._cells[x, y];
            if (sourceCell is { } value &&
                value.SliceId == sliceId &&
                _cells[x, y] is not null)
            {
                throw new InvalidOperationException(
                    "The destination motion-vector state already contains data for a source slice region.");
            }
        }

        for (int y = 0; y < BlockHeight; y++)
        for (int x = 0; x < BlockWidth; x++)
        {
            Cell? sourceCell = source._cells[x, y];
            if (sourceCell is { } value && value.SliceId == sliceId)
                _cells[x, y] = value;
        }
    }

    /// <summary>
    /// Resolves A (left), B (above), C (above-right), and D (above-left)
    /// candidates for a current partition. Neighbours outside the picture,
    /// not yet decoded, or belonging to another slice are unavailable.
    /// Intra-coded neighbours are returned with reference index -1 so they
    /// remain distinct from unavailable neighbours. D is returned separately;
    /// the predictor applies it only when C is unavailable.
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

    /// <summary>
    /// Resolves the A, B, C and D neighbours used by spatial direct prediction
    /// for a macroblock. Unlike ordinary partition prediction, A and B are
    /// adjacent to the macroblock's top-left 4x4 block, while C is the block
    /// immediately above the macroblock to the right.
    /// </summary>
    public H264MotionVectorNeighbourCandidates GetSpatialDirectCandidates(
        int macroblockX, int macroblockY, int sliceId)
    {
        if ((uint)macroblockX >= (uint)MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(macroblockX));
        if ((uint)macroblockY >= (uint)MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(macroblockY));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));

        int x = checked(macroblockX * 4);
        int y = checked(macroblockY * 4);
        return new H264MotionVectorNeighbourCandidates(
            ReadCandidate(x - 1, y, sliceId),
            ReadCandidate(x, y - 1, sliceId),
            ReadCandidate(x + 4, y - 1, sliceId),
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
    /// Records a decoded intra region with the normative unused-reference
    /// sentinel (-1) and a zero motion vector. It remains distinct from an
    /// unavailable neighbour, which has no stored cell.
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
        if (cell is not { } value || value.SliceId != sliceId)
            return null;

        // Intra-coded neighbours have ref_idx == -1 and a zero motion vector.
        // Keep that distinct from an unavailable partition (null): H.264 only
        // substitutes D for C when C is unavailable, not when C is intra.
        return new H264MotionVectorCandidate(
            value.ReferenceIndex ?? -1, value.MotionVector);
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
