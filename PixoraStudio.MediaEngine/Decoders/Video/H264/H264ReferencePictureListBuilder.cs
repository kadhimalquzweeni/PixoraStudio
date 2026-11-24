using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Builds the frame-coded P-slice RefPicList0 from short-term and long-term
/// reference pictures and applies ref_pic_list_modification syntax.
/// Field-picture and B-slice list construction are intentionally not covered.
/// </summary>
public sealed class H264ReferencePictureListBuilder
{
    public IReadOnlyList<H264ReferencePicture> BuildList0(
        IEnumerable<H264ReferencePicture> referencePictures,
        int currentFrameNum,
        int maxFrameNum,
        int activeReferenceCount,
        IReadOnlyList<H264ReferenceListModification>? modifications = null)
    {
        ArgumentNullException.ThrowIfNull(referencePictures);
        if (maxFrameNum <= 0 || maxFrameNum > 65536 ||
            (maxFrameNum & (maxFrameNum - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameNum),
                "MaxFrameNum must be a power of two no greater than 65536.");
        if (currentFrameNum < 0 || currentFrameNum >= maxFrameNum)
            throw new ArgumentOutOfRangeException(nameof(currentFrameNum));
        if (activeReferenceCount is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(activeReferenceCount),
                "The active reference count must be between 1 and 32.");

        H264ReferencePicture[] pictures = referencePictures.ToArray();
        ValidatePictures(pictures, maxFrameNum);

        // RefPicList0 is ordered by descending FrameNumWrap (PicNum), not by
        // the stored frame_num. A frame_num above CurrPicNum belongs to the
        // previous frame_num cycle and therefore wraps below zero.
        List<H264ReferencePicture> shortTerm = pictures
            .Where(picture => !picture.IsLongTerm)
            .ToList();

        shortTerm.Sort((left, right) =>
        {
            int leftPicNum = GetFrameNumWrap(
                left.FrameNum, currentFrameNum, maxFrameNum);
            int rightPicNum = GetFrameNumWrap(
                right.FrameNum, currentFrameNum, maxFrameNum);

            return rightPicNum.CompareTo(leftPicNum);
        });

        List<H264ReferencePicture> longTerm = pictures
            .Where(picture => picture.IsLongTerm)
            .OrderBy(picture => picture.LongTermFrameIndex)
            .ToList();

        List<H264ReferencePicture> result = shortTerm
            .Concat(longTerm)
            .ToList();

        if (modifications is { Count: > 0 })
            ApplyModifications(result, shortTerm, longTerm, modifications,
                currentFrameNum, maxFrameNum, activeReferenceCount);

        if (result.Count < activeReferenceCount)
            throw new FormatException("The decoded reference picture set contains fewer pictures than the active list requires.");

        return result.Take(activeReferenceCount).ToArray();
    }

    private static int GetFrameNumWrap(
        int frameNum,
        int currentFrameNum,
        int maxFrameNum) =>
        frameNum > currentFrameNum
            ? frameNum - maxFrameNum
            : frameNum;

    private static void ApplyModifications(
        List<H264ReferencePicture> result,
        List<H264ReferencePicture> shortTerm,
        List<H264ReferencePicture> longTerm,
        IReadOnlyList<H264ReferenceListModification> modifications,
        int currentPicNum,
        int maxPicNum,
        int activeReferenceCount)
    {
        int picNumPred = currentPicNum;
        int referenceIndex = 0;
        bool terminated = false;

        foreach (H264ReferenceListModification modification in modifications)
        {
            byte idc = modification.ModificationOfPicNumsIdc;
            if (idc == 3)
            {
                terminated = true;
                break;
            }

            H264ReferencePicture selected;
            if (idc is 0 or 1)
            {
                if (modification.AbsDiffPicNumMinus1 is not uint differenceMinus1)
                    throw new FormatException("A short-term reference-list modification is missing abs_diff_pic_num_minus1.");

                long difference = (long)differenceMinus1 + 1;
                if (difference > maxPicNum)
                    throw new FormatException("abs_diff_pic_num_minus1 exceeds the permitted PicNum range.");

                long noWrap = idc == 0
                    ? (long)picNumPred - difference
                    : (long)picNumPred + difference;

                if (noWrap < 0)
                    noWrap += maxPicNum;
                else if (noWrap >= maxPicNum)
                    noWrap -= maxPicNum;

                int picNumNoWrap = checked((int)noWrap);
                int picNum = picNumNoWrap > currentPicNum
                    ? picNumNoWrap - maxPicNum
                    : picNumNoWrap;
                picNumPred = picNumNoWrap;

                selected = shortTerm
                    .FirstOrDefault(picture =>
                        GetFrameNumWrap(picture.FrameNum, currentPicNum, maxPicNum) == picNum)
                    ?? throw new FormatException($"Reference-list modification selects unavailable short-term PicNum {picNum}.");
            }
            else if (idc == 2)
            {
                if (modification.LongTermPicNum is not uint longTermPicNum ||
                    longTermPicNum > int.MaxValue)
                    throw new FormatException("A long-term reference-list modification has an invalid long_term_pic_num.");

                selected = longTerm.FirstOrDefault(
                    picture => picture.LongTermFrameIndex == (int)longTermPicNum)
                    ?? throw new FormatException($"Reference-list modification selects unavailable long-term picture {longTermPicNum}.");
            }
            else
            {
                throw new FormatException($"Invalid modification_of_pic_nums_idc value {idc}.");
            }

            if (referenceIndex >= activeReferenceCount)
                continue;

            result.RemoveAll(picture => ReferenceEquals(picture, selected));
            result.Insert(referenceIndex, selected);
            referenceIndex++;
        }

        if (!terminated && modifications.Count > 0)
            throw new FormatException("The reference-list modification sequence is missing its idc 3 terminator.");
    }

    private static void ValidatePictures(
        IReadOnlyList<H264ReferencePicture> pictures,
        int maxFrameNum)
    {
        HashSet<int> shortFrameNums = [];
        HashSet<int> longTermIndices = [];

        foreach (H264ReferencePicture picture in pictures)
        {
            ArgumentNullException.ThrowIfNull(picture);
            if (picture.FrameNum < 0 || picture.FrameNum >= maxFrameNum)
                throw new ArgumentOutOfRangeException(nameof(pictures),
                    "A reference picture frame_num is outside MaxFrameNum.");
            if (picture.LongTermFrameIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pictures),
                    "A long-term frame index cannot be negative.");

            if (picture.IsLongTerm)
            {
                if (!longTermIndices.Add(picture.LongTermFrameIndex))
                    throw new FormatException("The reference picture set contains duplicate long-term frame indices.");
            }
            else if (!shortFrameNums.Add(picture.FrameNum))
            {
                throw new FormatException("The reference picture set contains duplicate short-term frame_num values.");
            }
        }
    }
}
