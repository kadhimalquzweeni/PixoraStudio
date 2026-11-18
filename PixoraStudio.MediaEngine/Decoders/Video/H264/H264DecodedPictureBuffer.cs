using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Frame-coded decoded picture buffer for 8-bit 4:2:0/4:2:2 pictures.
/// It applies sliding-window reference marking and MMCO operations 1–6 and can
/// retain collocated motion metadata for temporal direct prediction. Output
/// reordering and field-picture DPB semantics are separate stages.
/// </summary>
public sealed class H264DecodedPictureBuffer
{
    private readonly List<H264DecodedPictureBufferEntry> _entries = [];
    private int _maxLongTermFrameIndex = -1;
    private readonly H264ReferencePictureListBuilder _listBuilder = new();
    private readonly H264BReferencePictureListBuilder _bListBuilder = new();

    public int Count => _entries.Count;
    public int? MaxLongTermFrameIndex =>
        _maxLongTermFrameIndex < 0 ? null : _maxLongTermFrameIndex;
    public IReadOnlyList<H264DecodedPictureBufferEntry> Entries =>
        _entries.Select(entry => entry with
        {
            MotionVectorStateL0 = entry.MotionVectorStateL0?.Clone(),
            MotionVectorStateL1 = entry.MotionVectorStateL1?.Clone(),
            ReferenceList0 = entry.ReferenceList0 is null
                ? null : Array.AsReadOnly(entry.ReferenceList0.ToArray()),
            ReferenceList1 = entry.ReferenceList1 is null
                ? null : Array.AsReadOnly(entry.ReferenceList1.ToArray())
        }).ToArray();

    public void Reset()
    {
        _entries.Clear();
        _maxLongTermFrameIndex = -1;
    }

    /// <summary>
    /// Applies reference marking and stores a completed picture when it is
    /// a reference picture. For IDR pictures, the IDR marking flags are used;
    /// for other reference pictures, adaptive MMCO or sliding-window marking
    /// is applied before the current picture is inserted.
    /// </summary>
    public H264DecodedPictureBufferEntry? StoreDecodedPicture(
        H264DecodedPictureSurface surface,
        H264ReferencePicture picture,
        bool isReference,
        uint maxNumRefFrames,
        int currentFrameNum,
        int maxFrameNum,
        H264DecodedReferenceMarking? marking,
        bool idrPicture = false,
        H264MotionVectorPictureState? motionVectorStateL0 = null,
        H264MotionVectorPictureState? motionVectorStateL1 = null,
        IReadOnlyList<H264ReferencePicture>? referenceList0 = null,
        IReadOnlyList<H264ReferencePicture>? referenceList1 = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(picture);
        if (!isReference)
            return null;
        if (!surface.IsComplete)
            throw new InvalidOperationException("A reference picture must be fully reconstructed before DPB insertion.");
        if (surface.HasCompleteDeblockingMetadata && !surface.IsDeblockingFilterApplied)
            H264DeblockingPictureFilter.Apply(surface);
        if (maxNumRefFrames is 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maxNumRefFrames),
                "A reference picture requires max_num_ref_frames in the range 1..16.");
        ValidateFrameNumbers(currentFrameNum, maxFrameNum);
        if (picture.FrameNum != currentFrameNum)
            throw new ArgumentException("The current picture frame_num does not match currentFrameNum.", nameof(picture));
        if (picture.IsLongTerm)
            throw new ArgumentException("The current picture must not be pre-marked as long-term.", nameof(picture));
        bool hasMotionMetadata = motionVectorStateL0 is not null ||
            motionVectorStateL1 is not null || referenceList0 is not null || referenceList1 is not null;
        if (hasMotionMetadata &&
            (motionVectorStateL0 is null || motionVectorStateL1 is null ||
             referenceList0 is null || referenceList1 is null))
            throw new ArgumentException("Temporal-direct metadata must include both motion grids and both reference lists.");
        if (hasMotionMetadata)
        {
            if (motionVectorStateL0!.MacroblockWidth != surface.MacroblockWidth ||
                motionVectorStateL0.MacroblockHeight != surface.MacroblockHeight ||
                motionVectorStateL1!.MacroblockWidth != surface.MacroblockWidth ||
                motionVectorStateL1.MacroblockHeight != surface.MacroblockHeight)
                throw new ArgumentException("Stored motion grids must match the decoded surface geometry.");
            if (referenceList0!.Any(item => item is null) ||
                referenceList1!.Any(item => item is null))
                throw new ArgumentException("Stored collocated reference lists cannot contain null entries.");
            referenceList0 = Array.AsReadOnly(referenceList0.ToArray());
            referenceList1 = Array.AsReadOnly(referenceList1.ToArray());
        }
        if (marking is null)
            throw new ArgumentNullException(nameof(marking),
                "Reference pictures require decoded reference marking syntax.");
        if (!idrPicture)
            ValidateSurfaceCompatibility(surface);

        H264ReferencePicture current = picture;
        if (idrPicture)
        {
            _entries.Clear();
            _maxLongTermFrameIndex = marking.LongTermReferenceFlag ? 0 : -1;
            if (marking.LongTermReferenceFlag)
                current = current with { IsLongTerm = true, LongTermFrameIndex = 0 };
        }
        else
        {
            if (marking.AdaptiveRefPicMarkingModeFlag)
            {
                bool mmco5 = ApplyMemoryManagementOperations(
                    marking.Operations, currentFrameNum, maxFrameNum,
                    maxNumRefFrames, ref current);
                if (mmco5)
                    current = current with { FrameNum = 0 };
            }
            else
            {
                ApplySlidingWindowMarking(
                    currentFrameNum, maxFrameNum, checked((int)maxNumRefFrames));
            }
        }

        if (_entries.Count >= maxNumRefFrames)
            throw new FormatException("Reference marking leaves no decoded-picture-buffer slot for the current picture.");

        ValidateNoDuplicatePictureNumber(current);
        H264DecodedPictureBufferEntry entry = new(
            current, surface, motionVectorStateL0?.Clone(), motionVectorStateL1?.Clone(),
            referenceList0, referenceList1);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Creates a temporal-direct motion-field adapter for a reference picture
    /// already stored in the DPB. Motion grids and the reference lists used
    /// when that picture was decoded must have been retained at insertion time.
    /// </summary>
    public H264TemporalDirectMotionField CreateTemporalDirectMotionField(
        H264ReferencePicture collocatedPicture)
    {
        ArgumentNullException.ThrowIfNull(collocatedPicture);
        H264DecodedPictureBufferEntry entry = _entries.SingleOrDefault(
            candidate => candidate.Picture == collocatedPicture)
            ?? throw new ArgumentException("The collocated picture is not present in the DPB.", nameof(collocatedPicture));
        if (entry.MotionVectorStateL0 is null || entry.MotionVectorStateL1 is null ||
            entry.ReferenceList0 is null || entry.ReferenceList1 is null)
            throw new NotSupportedException("The collocated DPB entry was stored without temporal-direct motion metadata.");

        return new H264TemporalDirectMotionField(
            entry.MotionVectorStateL0, entry.MotionVectorStateL1,
            entry.ReferenceList0, entry.ReferenceList1,
            entry.Picture.PictureOrderCount,
            collocatedPictureIsShortTerm: !entry.Picture.IsLongTerm);
    }

    /// <summary>Builds the active frame-coded P-slice RefPicList0 and returns its surfaces.</summary>
    public IReadOnlyList<H264DecodedPictureSurface> BuildList0(
        int currentFrameNum,
        int maxFrameNum,
        int activeReferenceCount,
        IReadOnlyList<H264ReferenceListModification>? modifications = null)
    {
        IReadOnlyList<H264ReferencePicture> ordered = _listBuilder.BuildList0(
            _entries.Select(entry => entry.Picture),
            currentFrameNum, maxFrameNum, activeReferenceCount, modifications);

        H264DecodedPictureSurface[] surfaces = new H264DecodedPictureSurface[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            H264ReferencePicture picture = ordered[i];
            H264DecodedPictureBufferEntry entry = _entries.SingleOrDefault(
                candidate => candidate.Picture == picture)
                ?? throw new InvalidOperationException("A reference-list picture is not present in the decoded picture buffer.");
            surfaces[i] = entry.Surface;
        }
        return surfaces;
    }

    /// <summary>
    /// Builds both frame-coded B-slice reference lists and maps the ordered
    /// reference metadata back to their decoded picture surfaces.
    /// </summary>
    public (IReadOnlyList<H264DecodedPictureSurface> List0,
        IReadOnlyList<H264DecodedPictureSurface> List1) BuildBLists(
        int currentFrameNum,
        int maxFrameNum,
        int currentPictureOrderCount,
        int activeReferenceCountL0,
        int activeReferenceCountL1,
        IReadOnlyList<H264ReferenceListModification>? modificationsL0 = null,
        IReadOnlyList<H264ReferenceListModification>? modificationsL1 = null)
    {
        H264BReferencePictureLists lists = _bListBuilder.BuildLists(
            _entries.Select(entry => entry.Picture),
            currentFrameNum, maxFrameNum, currentPictureOrderCount,
            activeReferenceCountL0, activeReferenceCountL1,
            modificationsL0, modificationsL1);

        return (
            MapPicturesToSurfaces(lists.List0),
            MapPicturesToSurfaces(lists.List1));
    }

    private IReadOnlyList<H264DecodedPictureSurface> MapPicturesToSurfaces(
        IReadOnlyList<H264ReferencePicture> pictures)
    {
        H264DecodedPictureSurface[] surfaces = new H264DecodedPictureSurface[pictures.Count];
        for (int i = 0; i < pictures.Count; i++)
        {
            H264ReferencePicture picture = pictures[i];
            H264DecodedPictureBufferEntry entry = _entries.SingleOrDefault(
                candidate => candidate.Picture == picture)
                ?? throw new InvalidOperationException("A reference-list picture is not present in the decoded picture buffer.");
            surfaces[i] = entry.Surface;
        }
        return surfaces;
    }

    private bool ApplyMemoryManagementOperations(
        IReadOnlyList<H264MemoryManagementControlOperation> operations,
        int currentFrameNum,
        int maxFrameNum,
        uint maxNumRefFrames,
        ref H264ReferencePicture current)
    {
        bool mmco5 = false;
        foreach (H264MemoryManagementControlOperation operation in operations)
        {
            switch (operation.MemoryManagementControlOperation)
            {
                case 1:
                {
                    int picNum = DeriveShortTermPicNum(
                        currentFrameNum, maxFrameNum,
                        operation.DifferenceOfPicNumsMinus1);
                    int index = FindShortTermByPicNum(picNum, currentFrameNum, maxFrameNum);
                    _entries.RemoveAt(index);
                    break;
                }
                case 2:
                {
                    int longTermPicNum = ReadRequiredInt(
                        operation.LongTermPicNum, nameof(operation.LongTermPicNum));
                    int index = _entries.FindIndex(entry =>
                        entry.Picture.IsLongTerm &&
                        entry.Picture.LongTermFrameIndex == longTermPicNum);
                    if (index < 0)
                        throw new FormatException($"MMCO 2 selects unavailable LongTermPicNum {longTermPicNum}.");
                    _entries.RemoveAt(index);
                    break;
                }
                case 3:
                {
                    int picNum = DeriveShortTermPicNum(
                        currentFrameNum, maxFrameNum,
                        operation.DifferenceOfPicNumsMinus1);
                    int longTermIndex = ReadRequiredInt(
                        operation.LongTermFrameIdx, nameof(operation.LongTermFrameIdx));
                    ValidateLongTermIndex(longTermIndex, maxNumRefFrames);
                    int shortIndex = FindShortTermByPicNum(picNum, currentFrameNum, maxFrameNum);
                    H264DecodedPictureBufferEntry selected = _entries[shortIndex];
                    _entries.RemoveAll(entry => entry.Picture.IsLongTerm &&
                        entry.Picture.LongTermFrameIndex == longTermIndex);
                    H264ReferencePicture converted = selected.Picture with
                    {
                        IsLongTerm = true,
                        LongTermFrameIndex = longTermIndex
                    };
                    _entries.Remove(selected);
                    _entries.Add(selected with { Picture = converted });
                    break;
                }
                case 4:
                {
                    int maxLongTermPlus1 = ReadRequiredInt(
                        operation.MaxLongTermFrameIdxPlus1,
                        nameof(operation.MaxLongTermFrameIdxPlus1));
                    if (maxLongTermPlus1 > maxNumRefFrames)
                        throw new FormatException("MMCO 4 max_long_term_frame_idx_plus1 exceeds max_num_ref_frames.");
                    _maxLongTermFrameIndex = maxLongTermPlus1 - 1;
                    _entries.RemoveAll(entry => entry.Picture.IsLongTerm &&
                        entry.Picture.LongTermFrameIndex > _maxLongTermFrameIndex);
                    break;
                }
                case 5:
                    _entries.Clear();
                    _maxLongTermFrameIndex = -1;
                    mmco5 = true;
                    break;
                case 6:
                {
                    int longTermIndex = ReadRequiredInt(
                        operation.LongTermFrameIdx, nameof(operation.LongTermFrameIdx));
                    ValidateLongTermIndex(longTermIndex, maxNumRefFrames);
                    _entries.RemoveAll(entry => entry.Picture.IsLongTerm &&
                        entry.Picture.LongTermFrameIndex == longTermIndex);
                    current = current with
                    {
                        IsLongTerm = true,
                        LongTermFrameIndex = longTermIndex
                    };
                    break;
                }
                default:
                    throw new FormatException(
                        $"Invalid memory_management_control_operation {operation.MemoryManagementControlOperation}.");
            }
        }

        return mmco5;
    }

    private void ApplySlidingWindowMarking(
        int currentFrameNum,
        int maxFrameNum,
        int maxNumRefFrames)
    {
        if (_entries.Count < maxNumRefFrames)
            return;

        int oldestIndex = -1;
        int oldestPicNum = int.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            H264ReferencePicture picture = _entries[i].Picture;
            if (picture.IsLongTerm)
                continue;
            int picNum = picture.FrameNum > currentFrameNum
                ? picture.FrameNum - maxFrameNum
                : picture.FrameNum;
            if (picNum < oldestPicNum)
            {
                oldestPicNum = picNum;
                oldestIndex = i;
            }
        }

        if (oldestIndex < 0)
            throw new FormatException("Sliding-window marking cannot free a slot because the DPB contains only long-term references.");
        _entries.RemoveAt(oldestIndex);
    }

    private int FindShortTermByPicNum(
        int picNum,
        int currentFrameNum,
        int maxFrameNum)
    {
        int index = _entries.FindIndex(entry =>
        {
            if (entry.Picture.IsLongTerm)
                return false;
            int frameNumWrap = entry.Picture.FrameNum > currentFrameNum
                ? entry.Picture.FrameNum - maxFrameNum
                : entry.Picture.FrameNum;
            return frameNumWrap == picNum;
        });
        if (index < 0)
            throw new FormatException($"Decoded reference marking selects unavailable short-term PicNum {picNum}.");
        return index;
    }

    private static int DeriveShortTermPicNum(
        int currentFrameNum,
        int maxFrameNum,
        uint? differenceMinus1)
    {
        int differenceMinus1Value = ReadRequiredInt(
            differenceMinus1, nameof(differenceMinus1));
        if (differenceMinus1Value >= maxFrameNum)
            throw new FormatException("difference_of_pic_nums_minus1 exceeds MaxPicNum.");
        int difference = differenceMinus1Value + 1;
        int noWrap = currentFrameNum - difference;
        if (noWrap < 0)
            noWrap += maxFrameNum;
        return noWrap > currentFrameNum ? noWrap - maxFrameNum : noWrap;
    }

    private void ValidateLongTermIndex(int index, uint maxNumRefFrames)
    {
        if (index < 0 || index >= maxNumRefFrames ||
            _maxLongTermFrameIndex < 0 || index > _maxLongTermFrameIndex)
            throw new FormatException($"LongTermFrameIdx {index} exceeds the current MaxLongTermFrameIdx.");
    }

    private void ValidateNoDuplicatePictureNumber(H264ReferencePicture current)
    {
        if (current.IsLongTerm)
        {
            if (_entries.Any(entry => entry.Picture.IsLongTerm &&
                entry.Picture.LongTermFrameIndex == current.LongTermFrameIndex))
                throw new FormatException("The DPB already contains the current LongTermFrameIdx.");
        }
        else if (_entries.Any(entry => !entry.Picture.IsLongTerm &&
            entry.Picture.FrameNum == current.FrameNum))
        {
            throw new FormatException("The DPB already contains the current short-term frame_num.");
        }
    }

    private void ValidateSurfaceCompatibility(H264DecodedPictureSurface surface)
    {
        H264DecodedPictureBufferEntry? existing = _entries.FirstOrDefault();
        if (existing is null)
            return;
        if (surface.Width != existing.Surface.Width ||
            surface.Height != existing.Surface.Height ||
            surface.ChromaFormatIdc != existing.Surface.ChromaFormatIdc)
            throw new ArgumentException("All DPB reference surfaces must have matching picture geometry and chroma format.", nameof(surface));
    }

    private static int ReadRequiredInt(uint? value, string field)
    {
        if (value is not uint actual || actual > int.MaxValue)
            throw new FormatException($"Decoded reference marking is missing or has an invalid {field}.");
        return (int)actual;
    }

    private static void ValidateFrameNumbers(int currentFrameNum, int maxFrameNum)
    {
        if (maxFrameNum <= 0 || maxFrameNum > 65536 ||
            (maxFrameNum & (maxFrameNum - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameNum));
        if (currentFrameNum < 0 || currentFrameNum >= maxFrameNum)
            throw new ArgumentOutOfRangeException(nameof(currentFrameNum));
    }
}

/// <summary>A DPB entry pairing frame metadata and its decoded surface with optional collocated motion/reference metadata.</summary>
public sealed record H264DecodedPictureBufferEntry(
    H264ReferencePicture Picture,
    H264DecodedPictureSurface Surface,
    H264MotionVectorPictureState? MotionVectorStateL0 = null,
    H264MotionVectorPictureState? MotionVectorStateL1 = null,
    IReadOnlyList<H264ReferencePicture>? ReferenceList0 = null,
    IReadOnlyList<H264ReferencePicture>? ReferenceList1 = null);
