using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Stateful implementation of the H.264 picture order count process (specification 8.2.1).
/// Call once for each primary coded picture, not once for every slice in that picture.
/// </summary>
public sealed class H264PictureOrderCountCalculator
{
    private int _previousPicOrderCntMsb;
    private int _previousPicOrderCntLsb;
    private int _previousFrameNum;
    private int _previousFrameNumOffset;

    public void Reset()
    {
        _previousPicOrderCntMsb = 0;
        _previousPicOrderCntLsb = 0;
        _previousFrameNum = 0;
        _previousFrameNumOffset = 0;
    }

    public H264PictureOrderCount Calculate(
        H264SequenceParameterSet sps,
        H264SliceHeader slice,
        byte nalRefIdc)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(slice);

        if (sps.PicOrderCntType > 2)
            throw new NotSupportedException($"pic_order_cnt_type {sps.PicOrderCntType} is not supported.");
        if (nalRefIdc > 3)
            throw new ArgumentOutOfRangeException(nameof(nalRefIdc));
        if (slice.FrameNum >= (1u << checked((int)sps.Log2MaxFrameNumMinus4 + 4)))
            throw new FormatException("frame_num exceeds the range declared by the SPS.");

        bool idr = slice.IdrPicId.HasValue;
        bool reference = nalRefIdc != 0;
        bool mmco5 = slice.DecodedReferenceMarking?.Operations.Any(
            operation => operation.MemoryManagementControlOperation == 5) == true;

        if (idr)
            Reset();

        int frameNumOffset = idr ? 0 : _previousFrameNum > slice.FrameNum
            ? checked(_previousFrameNumOffset + (1 << checked((int)sps.Log2MaxFrameNumMinus4 + 4)))
            : _previousFrameNumOffset;

        int top = int.MaxValue;
        int bottom = int.MaxValue;

        switch (sps.PicOrderCntType)
        {
            case 0:
                CalculateTypeZero(sps, slice, reference, idr, mmco5, out top, out bottom);
                break;
            case 1:
                CalculateTypeOne(sps, slice, reference, frameNumOffset, out top, out bottom);
                break;
            case 2:
                CalculateTypeTwo(slice, reference, idr, frameNumOffset, out top, out bottom);
                break;
        }

        bool hasTop = !slice.FieldPicFlag || !slice.BottomFieldFlag;
        bool hasBottom = !slice.FieldPicFlag || slice.BottomFieldFlag;
        int pictureOrder = hasTop && hasBottom ? Math.Min(top, bottom) : hasTop ? top : bottom;

        if (reference)
        {
            if (mmco5)
            {
                _previousFrameNum = 0;
                _previousFrameNumOffset = 0;
            }
            else
            {
                _previousFrameNum = checked((int)slice.FrameNum);
                _previousFrameNumOffset = frameNumOffset;
            }
        }

        return new H264PictureOrderCount(top, bottom, pictureOrder, frameNumOffset, hasTop, hasBottom);
    }

    private void CalculateTypeZero(
        H264SequenceParameterSet sps,
        H264SliceHeader slice,
        bool reference,
        bool idr,
        bool mmco5,
        out int top,
        out int bottom)
    {
        if (slice.PicOrderCntLsb is null)
            throw new FormatException("POC type 0 requires pic_order_cnt_lsb.");

        int maxLsb = 1 << checked((int)sps.Log2MaxPicOrderCntLsbMinus4 + 4);
        int lsb = checked((int)slice.PicOrderCntLsb.Value);
        int previousMsb = idr ? 0 : _previousPicOrderCntMsb;
        int previousLsb = idr ? 0 : _previousPicOrderCntLsb;
        int msb = previousMsb;

        if (lsb < previousLsb && previousLsb - lsb >= maxLsb / 2)
            msb = checked(previousMsb + maxLsb);
        else if (lsb > previousLsb && lsb - previousLsb > maxLsb / 2)
            msb = checked(previousMsb - maxLsb);

        if (!slice.FieldPicFlag)
        {
            top = checked(msb + lsb);
            bottom = checked(top + (slice.DeltaPicOrderCntBottom ?? 0));
        }
        else if (slice.BottomFieldFlag)
        {
            top = int.MaxValue;
            bottom = checked(msb + lsb);
        }
        else
        {
            top = checked(msb + lsb);
            bottom = int.MaxValue;
        }

        if (reference)
        {
            if (mmco5)
            {
                _previousPicOrderCntMsb = 0;
                _previousPicOrderCntLsb = slice.FieldPicFlag && slice.BottomFieldFlag ? 0 : top;
            }
            else
            {
                _previousPicOrderCntMsb = msb;
                _previousPicOrderCntLsb = lsb;
            }
        }
    }

    private static void CalculateTypeOne(
        H264SequenceParameterSet sps,
        H264SliceHeader slice,
        bool reference,
        int frameNumOffset,
        out int top,
        out int bottom)
    {
        int cycleLength = sps.OffsetForRefFrame.Count;
        long absoluteFrameNum = cycleLength == 0 ? 0 : (long)frameNumOffset + slice.FrameNum;
        if (!reference && absoluteFrameNum > 0)
            absoluteFrameNum--;

        long expected = 0;
        if (absoluteFrameNum > 0 && cycleLength > 0)
        {
            long cycleCount = (absoluteFrameNum - 1) / cycleLength;
            int frameInCycle = (int)((absoluteFrameNum - 1) % cycleLength);
            long expectedPerCycle = sps.OffsetForRefFrame.Sum(value => (long)value);
            expected = cycleCount * expectedPerCycle;
            for (int i = 0; i <= frameInCycle; i++)
                expected += sps.OffsetForRefFrame[i];
        }

        if (!reference)
            expected += sps.OffsetForNonRefPic;

        int delta0 = slice.DeltaPicOrderCnt0 ?? 0;
        int delta1 = slice.DeltaPicOrderCnt1 ?? 0;
        if (slice.FieldPicFlag)
        {
            if (slice.BottomFieldFlag)
            {
                top = int.MaxValue;
                bottom = checked((int)(expected + sps.OffsetForTopToBottomField + delta0));
            }
            else
            {
                top = checked((int)(expected + delta0));
                bottom = int.MaxValue;
            }
        }
        else
        {
            top = checked((int)(expected + delta0));
            bottom = checked((int)(expected + sps.OffsetForTopToBottomField + delta1));
        }
    }

    private static void CalculateTypeTwo(
        H264SliceHeader slice,
        bool reference,
        bool idr,
        int frameNumOffset,
        out int top,
        out int bottom)
    {
        int poc = idr ? 0 : checked(2 * (frameNumOffset + (int)slice.FrameNum) - (reference ? 0 : 1));
        top = poc;
        bottom = poc;
        if (slice.FieldPicFlag)
        {
            if (slice.BottomFieldFlag) top = int.MaxValue;
            else bottom = int.MaxValue;
        }
    }
}
