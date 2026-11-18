using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Builds frame-coded B-slice RefPicList0 and RefPicList1 using picture
/// order count and applies each list's reference-list modification syntax.
/// Field-picture and MBAFF list construction are not supported.
/// </summary>
public sealed class H264BReferencePictureListBuilder
{
    public H264BReferencePictureLists BuildLists(
        IEnumerable<H264ReferencePicture> referencePictures,
        int currentFrameNum,
        int maxFrameNum,
        int currentPictureOrderCount,
        int activeReferenceCountL0,
        int activeReferenceCountL1,
        IReadOnlyList<H264ReferenceListModification>? modificationsL0 = null,
        IReadOnlyList<H264ReferenceListModification>? modificationsL1 = null)
    {
        ArgumentNullException.ThrowIfNull(referencePictures);
        ValidateArguments(currentFrameNum, maxFrameNum, activeReferenceCountL0, nameof(activeReferenceCountL0));
        ValidateArguments(currentFrameNum, maxFrameNum, activeReferenceCountL1, nameof(activeReferenceCountL1));

        H264ReferencePicture[] pictures = referencePictures.ToArray();
        ValidatePictures(pictures, maxFrameNum);

        List<H264ReferencePicture> past = pictures
            .Where(picture => !picture.IsLongTerm && picture.PictureOrderCount < currentPictureOrderCount)
            .OrderByDescending(picture => picture.PictureOrderCount)
            .ToList();
        List<H264ReferencePicture> future = pictures
            .Where(picture => !picture.IsLongTerm && picture.PictureOrderCount >= currentPictureOrderCount)
            .OrderBy(picture => picture.PictureOrderCount)
            .ToList();
        List<H264ReferencePicture> longTerm = pictures
            .Where(picture => picture.IsLongTerm)
            .OrderBy(picture => picture.LongTermFrameIndex)
            .ToList();

        List<H264ReferencePicture> list0 = past.Concat(future).Concat(longTerm).ToList();
        List<H264ReferencePicture> list1 = future.Concat(past).Concat(longTerm).ToList();

        if (list0.SequenceEqual(list1) && list1.Count > 1)
            (list1[0], list1[1]) = (list1[1], list1[0]);

        ApplyModifications(list0, past.Concat(future).ToList(), longTerm,
            modificationsL0, currentFrameNum, maxFrameNum, activeReferenceCountL0);
        ApplyModifications(list1, past.Concat(future).ToList(), longTerm,
            modificationsL1, currentFrameNum, maxFrameNum, activeReferenceCountL1);

        if (list0.Count < activeReferenceCountL0)
            throw new FormatException("The reference picture set contains fewer pictures than B-slice RefPicList0 requires.");
        if (list1.Count < activeReferenceCountL1)
            throw new FormatException("The reference picture set contains fewer pictures than B-slice RefPicList1 requires.");

        return new H264BReferencePictureLists(
            list0.Take(activeReferenceCountL0).ToArray(),
            list1.Take(activeReferenceCountL1).ToArray());
    }

    private static void ApplyModifications(
        List<H264ReferencePicture> result,
        IReadOnlyList<H264ReferencePicture> shortTerm,
        IReadOnlyList<H264ReferencePicture> longTerm,
        IReadOnlyList<H264ReferenceListModification>? modifications,
        int currentFrameNum,
        int maxFrameNum,
        int activeReferenceCount)
    {
        if (modifications is not { Count: > 0 })
            return;

        int picNumPred = currentFrameNum;
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
                if (modification.AbsDiffPicNumMinus1 is not uint differenceMinus1 ||
                    differenceMinus1 >= maxFrameNum)
                    throw new FormatException("A B-slice short-term list modification has an invalid abs_diff_pic_num_minus1.");
                int difference = checked((int)differenceMinus1 + 1);
                int noWrap = idc == 0
                    ? picNumPred - difference
                    : picNumPred + difference;
                if (noWrap < 0)
                    noWrap += maxFrameNum;
                else if (noWrap >= maxFrameNum)
                    noWrap -= maxFrameNum;
                int picNum = noWrap > currentFrameNum ? noWrap - maxFrameNum : noWrap;
                picNumPred = noWrap;

                selected = shortTerm.FirstOrDefault(picture =>
                {
                    int wrap = picture.FrameNum > currentFrameNum
                        ? picture.FrameNum - maxFrameNum
                        : picture.FrameNum;
                    return wrap == picNum;
                }) ?? throw new FormatException($"B-slice list modification selects unavailable short-term PicNum {picNum}.");
            }
            else if (idc == 2)
            {
                if (modification.LongTermPicNum is not uint longTermPicNum ||
                    longTermPicNum > int.MaxValue)
                    throw new FormatException("A B-slice long-term list modification has an invalid long_term_pic_num.");
                selected = longTerm.FirstOrDefault(picture =>
                    picture.LongTermFrameIndex == (int)longTermPicNum)
                    ?? throw new FormatException($"B-slice list modification selects unavailable LongTermPicNum {longTermPicNum}.");
            }
            else
            {
                throw new FormatException($"Invalid B-slice modification_of_pic_nums_idc value {idc}.");
            }

            if (referenceIndex < activeReferenceCount)
            {
                result.RemoveAll(picture => ReferenceEquals(picture, selected));
                result.Insert(referenceIndex, selected);
                referenceIndex++;
            }
        }

        if (!terminated)
            throw new FormatException("The B-slice reference-list modification sequence is missing its idc 3 terminator.");
    }

    private static void ValidatePictures(
        IReadOnlyList<H264ReferencePicture> pictures,
        int maxFrameNum)
    {
        HashSet<int> shortTermFrameNums = [];
        HashSet<int> longTermFrameIndices = [];
        foreach (H264ReferencePicture picture in pictures)
        {
            ArgumentNullException.ThrowIfNull(picture);
            if (picture.FrameNum < 0 || picture.FrameNum >= maxFrameNum)
                throw new ArgumentOutOfRangeException(nameof(pictures));
            if (picture.LongTermFrameIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pictures));
            if (picture.IsLongTerm)
            {
                if (!longTermFrameIndices.Add(picture.LongTermFrameIndex))
                    throw new FormatException("The B-slice reference set contains duplicate long-term indices.");
            }
            else if (!shortTermFrameNums.Add(picture.FrameNum))
            {
                throw new FormatException("The B-slice reference set contains duplicate short-term frame_num values.");
            }
        }
    }

    private static void ValidateArguments(
        int currentFrameNum,
        int maxFrameNum,
        int activeReferenceCount,
        string activeCountParameter)
    {
        if (maxFrameNum <= 0 || maxFrameNum > 65536 ||
            (maxFrameNum & (maxFrameNum - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameNum));
        if (currentFrameNum < 0 || currentFrameNum >= maxFrameNum)
            throw new ArgumentOutOfRangeException(nameof(currentFrameNum));
        if (activeReferenceCount is < 1 or > 32)
            throw new ArgumentOutOfRangeException(activeCountParameter);
    }
}
