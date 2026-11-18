using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// H.264 8-bit sample-level deblocking kernels for frame-coded 4:2:0/4:2:2.
/// The picture-level boundary-strength and slice-boundary traversal stages
/// supply edge parameters separately.
/// </summary>
public static class H264DeblockingFilter
{
    private static readonly byte[] AlphaTable =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        4, 4, 5, 6, 7, 8, 9, 10, 12, 13, 15, 17, 20, 22, 25, 28,
        32, 36, 40, 45, 50, 56, 63, 71, 80, 90, 101, 113, 127, 144, 162, 182,
        203, 226, 255, 255
    ];

    private static readonly byte[] BetaTable =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 6, 6, 7, 7, 8, 8,
        9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16,
        17, 17, 18, 18
    ];

    // Columns are bS=0,1,2,3. bS=0 is always zero.
    private static readonly byte[,] Tc0Table =
    {
        {0,0,0,0}, {0,0,0,0}, {0,0,0,0}, {0,0,0,0},
        {0,0,0,0}, {0,0,0,0}, {0,0,0,0}, {0,0,0,0},
        {0,0,0,0}, {0,0,0,0}, {0,0,0,0}, {0,0,0,0},
        {0,0,0,0}, {0,0,0,0}, {0,0,0,0}, {0,0,0,0},
        {0,0,0,0}, {0,0,0,1}, {0,0,0,1}, {0,0,0,1},
        {0,0,0,1}, {0,0,1,1}, {0,0,1,1}, {0,1,1,1},
        {0,1,1,1}, {0,1,1,1}, {0,1,1,1}, {0,1,1,2},
        {0,1,1,2}, {0,1,1,2}, {0,1,1,2}, {0,1,2,3},
        {0,1,2,3}, {0,2,2,3}, {0,2,2,4}, {0,2,3,4},
        {0,2,3,4}, {0,3,3,5}, {0,3,4,6}, {0,3,4,6},
        {0,4,5,7}, {0,4,5,8}, {0,4,6,9}, {0,5,7,10},
        {0,6,8,11}, {0,6,8,13}, {0,7,10,14}, {0,8,11,16},
        {0,9,12,18}, {0,10,13,20}, {0,11,15,23}, {0,13,17,25}
    };

    public static H264DeblockingThresholds DeriveThresholds(
        int qpP,
        int qpQ,
        int alphaOffsetDiv2,
        int betaOffsetDiv2,
        int boundaryStrength)
    {
        ValidateParameters(qpP, qpQ, alphaOffsetDiv2, betaOffsetDiv2, boundaryStrength);
        int qpAverage = (qpP + qpQ + 1) >> 1;
        int indexA = Clip3(0, 51, qpAverage + (alphaOffsetDiv2 << 1));
        int indexB = Clip3(0, 51, qpAverage + (betaOffsetDiv2 << 1));
        return new H264DeblockingThresholds(
            indexA, indexB, AlphaTable[indexA], BetaTable[indexB],
            boundaryStrength is >= 1 and <= 3 ? Tc0Table[indexA, boundaryStrength] : 0);
    }

    /// <summary>
    /// Filters one edge segment in-place. q0Offset points to the first q0
    /// sample; perpendicularStride steps across the edge and sampleStride
    /// steps along it. Four sample sets are normally filtered per 4x4 edge.
    /// </summary>
    public static bool FilterEdge(
        Span<byte> plane,
        int q0Offset,
        int perpendicularStride,
        int sampleStride,
        int sampleCount,
        H264DeblockingEdgeParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (perpendicularStride <= 0)
            throw new ArgumentOutOfRangeException(nameof(perpendicularStride));
        if (sampleStride <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleStride));
        if (sampleCount is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        H264DeblockingThresholds thresholds = DeriveThresholds(
            parameters.QpP, parameters.QpQ,
            parameters.AlphaOffsetDiv2, parameters.BetaOffsetDiv2,
            parameters.BoundaryStrength);
        if (parameters.BoundaryStrength == 0 ||
            thresholds.Alpha == 0 || thresholds.Beta == 0)
            return false;

        int first = q0Offset - (4 * perpendicularStride);
        int last = q0Offset + (3 * perpendicularStride) +
            ((sampleCount - 1) * sampleStride);
        if (first < 0 || last >= plane.Length)
            throw new ArgumentOutOfRangeException(nameof(q0Offset),
                "The complete p3..q3 edge neighbourhood must lie within the plane.");

        bool modified = false;
        for (int i = 0; i < sampleCount; i++)
        {
            int q0Index = q0Offset + i * sampleStride;
            modified |= FilterSampleSet(plane, q0Index, perpendicularStride,
                parameters.IsChroma, parameters.BoundaryStrength, thresholds);
        }

        return modified;
    }

    private static bool FilterSampleSet(
        Span<byte> plane,
        int q0Index,
        int stride,
        bool chroma,
        int boundaryStrength,
        H264DeblockingThresholds thresholds)
    {
        int p3Index = q0Index - 4 * stride;
        int p2Index = q0Index - 3 * stride;
        int p1Index = q0Index - 2 * stride;
        int p0Index = q0Index - stride;
        int q1Index = q0Index + stride;
        int q2Index = q0Index + 2 * stride;
        int q3Index = q0Index + 3 * stride;

        int p3 = plane[p3Index], p2 = plane[p2Index], p1 = plane[p1Index], p0 = plane[p0Index];
        int q0 = plane[q0Index], q1 = plane[q1Index], q2 = plane[q2Index], q3 = plane[q3Index];
        if (Abs(p0 - q0) >= thresholds.Alpha ||
            Abs(p1 - p0) >= thresholds.Beta ||
            Abs(q1 - q0) >= thresholds.Beta)
            return false;

        if (boundaryStrength == 4)
        {
            if (chroma)
            {
                int newP0 = (2 * p1 + p0 + q1 + 2) >> 2;
                int newQ0 = (2 * q1 + q0 + p1 + 2) >> 2;
                plane[p0Index] = Clip1(newP0);
                plane[q0Index] = Clip1(newQ0);
                return newP0 != p0 || newQ0 != q0;
            }

            bool modifyP = Abs(p2 - p0) < thresholds.Beta &&
                Abs(p0 - q0) < ((thresholds.Alpha >> 2) + 2);
            bool modifyQ = Abs(q2 - q0) < thresholds.Beta &&
                Abs(p0 - q0) < ((thresholds.Alpha >> 2) + 2);

            int newP0 = modifyP
                ? (p2 + 2 * p1 + 2 * p0 + 2 * q0 + q1 + 4) >> 3
                : (2 * p1 + p0 + q1 + 2) >> 2;
            int newQ0 = modifyQ
                ? (p1 + 2 * p0 + 2 * q0 + 2 * q1 + q2 + 4) >> 3
                : (2 * q1 + q0 + p1 + 2) >> 2;

            int newP1 = modifyP ? (p2 + p1 + p0 + q0 + 2) >> 2 : p1;
            int newQ1 = modifyQ ? (p0 + q0 + q1 + q2 + 2) >> 2 : q1;
            int newP2 = modifyP ? (2 * p3 + 3 * p2 + p1 + p0 + q0 + 4) >> 3 : p2;
            int newQ2 = modifyQ ? (2 * q3 + 3 * q2 + q1 + q0 + p0 + 4) >> 3 : q2;

            plane[p0Index] = Clip1(newP0);
            plane[q0Index] = Clip1(newQ0);
            plane[p1Index] = Clip1(newP1);
            plane[q1Index] = Clip1(newQ1);
            plane[p2Index] = Clip1(newP2);
            plane[q2Index] = Clip1(newQ2);
            return newP0 != p0 || newQ0 != q0 || newP1 != p1 ||
                newQ1 != q1 || newP2 != p2 || newQ2 != q2;
        }

        int ap = Abs(p2 - p0);
        int aq = Abs(q2 - q0);
        int tc0 = thresholds.Tc0;
        int tc = chroma ? tc0 + 1 :
            tc0 + (ap < thresholds.Beta ? 1 : 0) +
            (aq < thresholds.Beta ? 1 : 0);
        int delta = Clip3(-tc, tc,
            (((q0 - p0) << 2) + (p1 - q1) + 4) >> 3);
        int filteredP0 = Clip1(p0 + delta);
        int filteredQ0 = Clip1(q0 - delta);
        plane[p0Index] = (byte)filteredP0;
        plane[q0Index] = (byte)filteredQ0;

        int filteredP1 = p1;
        int filteredQ1 = q1;
        if (!chroma && ap < thresholds.Beta)
            filteredP1 = Clip1(p1 + Clip3(-tc0, tc0,
                (p2 + ((p0 + q0 + 1) >> 1) - (p1 << 1)) >> 1));
        if (!chroma && aq < thresholds.Beta)
            filteredQ1 = Clip1(q1 + Clip3(-tc0, tc0,
                (q2 + ((p0 + q0 + 1) >> 1) - (q1 << 1)) >> 1));
        plane[p1Index] = (byte)filteredP1;
        plane[q1Index] = (byte)filteredQ1;

        return filteredP0 != p0 || filteredQ0 != q0 ||
            filteredP1 != p1 || filteredQ1 != q1;
    }

    private static void ValidateParameters(
        int qpP, int qpQ, int alphaOffsetDiv2, int betaOffsetDiv2, int boundaryStrength)
    {
        if (qpP is < 0 or > 51) throw new ArgumentOutOfRangeException(nameof(qpP));
        if (qpQ is < 0 or > 51) throw new ArgumentOutOfRangeException(nameof(qpQ));
        if (alphaOffsetDiv2 is < -6 or > 6)
            throw new ArgumentOutOfRangeException(nameof(alphaOffsetDiv2));
        if (betaOffsetDiv2 is < -6 or > 6)
            throw new ArgumentOutOfRangeException(nameof(betaOffsetDiv2));
        if (boundaryStrength is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(boundaryStrength));
    }

    private static int Clip1(int value) => Clip3(0, 255, value);
    private static int Clip3(int minimum, int maximum, int value) =>
        Math.Min(maximum, Math.Max(minimum, value));
    private static int Abs(int value) => Math.Abs(value);
}
