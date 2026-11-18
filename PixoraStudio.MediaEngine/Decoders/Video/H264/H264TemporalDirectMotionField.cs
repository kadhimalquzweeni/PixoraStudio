using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Adapts a decoded collocated picture's stored motion grids and its reference
/// lists into the region-based temporal-direct callback consumed by B slices.
/// List 0 is preferred; list 1 is used when the collocated block has no L0
/// motion/reference. The callback returns null when the collocated block is
/// intra, unavailable, or has no usable reference.
/// </summary>
public sealed class H264TemporalDirectMotionField
{
    private readonly H264MotionVectorPictureState _motionL0;
    private readonly H264MotionVectorPictureState _motionL1;
    private readonly IReadOnlyList<H264ReferencePicture> _referencesL0;
    private readonly IReadOnlyList<H264ReferencePicture> _referencesL1;
    private readonly int _collocatedPictureOrderCount;
    private readonly bool _collocatedPictureIsShortTerm;

    public H264TemporalDirectMotionField(
        H264MotionVectorPictureState motionL0,
        H264MotionVectorPictureState motionL1,
        IReadOnlyList<H264ReferencePicture> referencesL0,
        IReadOnlyList<H264ReferencePicture> referencesL1,
        int collocatedPictureOrderCount,
        bool collocatedPictureIsShortTerm)
    {
        ArgumentNullException.ThrowIfNull(motionL0);
        ArgumentNullException.ThrowIfNull(motionL1);
        ArgumentNullException.ThrowIfNull(referencesL0);
        ArgumentNullException.ThrowIfNull(referencesL1);
        if (motionL0.MacroblockWidth != motionL1.MacroblockWidth ||
            motionL0.MacroblockHeight != motionL1.MacroblockHeight)
            throw new ArgumentException("Collocated list motion grids must have matching picture geometry.");
        if (referencesL0.Any(reference => reference is null) ||
            referencesL1.Any(reference => reference is null))
            throw new ArgumentException("Collocated reference lists cannot contain null entries.");

        _motionL0 = motionL0;
        _motionL1 = motionL1;
        _referencesL0 = referencesL0.ToArray();
        _referencesL1 = referencesL1.ToArray();
        _collocatedPictureOrderCount = collocatedPictureOrderCount;
        _collocatedPictureIsShortTerm = collocatedPictureIsShortTerm;
    }

    public Func<int, int, int, int, H264TemporalDirectPredictionContext?> AsProvider(
        int currentPictureOrderCount,
        IReadOnlyList<H264ReferencePicture> currentReferenceList0)
    {
        ArgumentNullException.ThrowIfNull(currentReferenceList0);
        H264ReferencePicture[] currentReferences = currentReferenceList0.ToArray();
        return (x, y, width, height) =>
            GetContext(x, y, width, height, currentPictureOrderCount, currentReferences);
    }

    public H264TemporalDirectPredictionContext? GetContext(
        int x, int y, int width, int height, int currentPictureOrderCount,
        IReadOnlyList<H264ReferencePicture> currentReferenceList0)
    {
        ArgumentNullException.ThrowIfNull(currentReferenceList0);
        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x >= _motionL0.BlockWidth || y >= _motionL0.BlockHeight ||
            width > _motionL0.BlockWidth - x || height > _motionL0.BlockHeight - y)
            throw new ArgumentOutOfRangeException(nameof(x),
                "The collocated lookup region must lie inside the stored motion grid.");

        if (_motionL0.TryGetStoredMotionVector(x, y, out H264MotionVectorCandidate list0))
        {
            H264ReferencePicture reference = ResolveReference(_referencesL0, list0.ReferenceIndex, "list 0");
            return new H264TemporalDirectPredictionContext(
                list0.MotionVector, currentPictureOrderCount,
                _collocatedPictureOrderCount, reference.PictureOrderCount,
                reference.IsLongTerm, list0.ReferenceIndex,
                _collocatedPictureIsShortTerm,
                MapReferenceToCurrentList0(reference, currentReferenceList0));
        }

        if (_motionL1.TryGetStoredMotionVector(x, y, out H264MotionVectorCandidate list1))
        {
            H264ReferencePicture reference = ResolveReference(_referencesL1, list1.ReferenceIndex, "list 1");
            return new H264TemporalDirectPredictionContext(
                list1.MotionVector, currentPictureOrderCount,
                _collocatedPictureOrderCount, reference.PictureOrderCount,
                reference.IsLongTerm, list1.ReferenceIndex,
                _collocatedPictureIsShortTerm,
                MapReferenceToCurrentList0(reference, currentReferenceList0));
        }

        return null;
    }

    private static int MapReferenceToCurrentList0(
        H264ReferencePicture collocatedReference,
        IReadOnlyList<H264ReferencePicture> currentReferenceList0)
    {
        for (int i = 0; i < currentReferenceList0.Count; i++)
        {
            // A reference can be re-marked short-term/long-term after the
            // collocated picture was decoded. POC identifies the same frame
            // across that marking transition for the supported frame-only path.
            if (currentReferenceList0[i].PictureOrderCount ==
                collocatedReference.PictureOrderCount)
                return i;
        }

        throw new FormatException(
            "The collocated reference picture is not present in the current RefPicList0.");
    }

    private static H264ReferencePicture ResolveReference(
        IReadOnlyList<H264ReferencePicture> references,
        int index,
        string listName)
    {
        if ((uint)index >= (uint)references.Count)
            throw new FormatException($"The collocated motion field refers to an unavailable {listName} reference index {index}.");
        return references[index];
    }
}
