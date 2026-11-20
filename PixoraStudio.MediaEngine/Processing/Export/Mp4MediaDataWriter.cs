using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Copies selected encoded samples into a managed MP4 mdat box without decoding or re-encoding.
/// Uses a 64-bit extended-size box header and records the new absolute chunk offsets.
/// </summary>
public sealed class Mp4MediaDataWriter
{
    private const int CopyBufferSize = 64 * 1024;

    public Mp4MediaDataWriteResult Write(
        Stream source,
        Stream destination,
        IEnumerable<Mp4TrimTrackPlan> trackPlans,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(trackPlans);

        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The source must be readable and seekable.", nameof(source));

        if (!destination.CanWrite || !destination.CanSeek)
            throw new ArgumentException("The destination must be writable and seekable.", nameof(destination));

        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Source and destination streams must be different.", nameof(destination));

        Mp4TrimTrackPlan[] plans = trackPlans.ToArray();
        if (plans.Length == 0)
            throw new ArgumentException("At least one track plan is required.", nameof(trackPlans));

        if (plans.Select(plan => plan.TrackId).Distinct().Count() != plans.Length)
            throw new ArgumentException("Track plans must have unique track IDs.", nameof(trackPlans));

        cancellationToken.ThrowIfCancellationRequested();

        long boxStart = destination.Position;
        long dataStart = checked(boxStart + 16);

        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], 1);
        Encoding.ASCII.GetBytes("mdat", header[4..8]);
        BinaryPrimitives.WriteUInt64BigEndian(header[8..], 0);
        destination.Write(header);

        byte[] buffer = new byte[CopyBufferSize];
        Dictionary<uint, IReadOnlyList<ulong>> offsetsByTrack = [];

        foreach (Mp4TrimTrackPlan plan in plans)
        {
            List<ulong> offsets = new(plan.Samples.Count);

            foreach (Mp4SampleLocation sample in plan.Samples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sample.Offset > long.MaxValue ||
                    sample.Offset > (ulong)source.Length ||
                    sample.Size > (ulong)source.Length - sample.Offset)
                {
                    throw new Mp4ParsingException(
                        $"Sample {sample.SampleNumber} in track {plan.TrackId} extends beyond the source file.");
                }

                offsets.Add(checked((ulong)destination.Position));
                source.Position = checked((long)sample.Offset);

                uint remaining = sample.Size;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = (int)Math.Min((uint)buffer.Length, remaining);
                    source.ReadExactly(buffer.AsSpan(0, count));
                    destination.Write(buffer, 0, count);
                    remaining -= (uint)count;
                }
            }

            offsetsByTrack.Add(plan.TrackId, Array.AsReadOnly(offsets.ToArray()));
        }

        long end = destination.Position;
        ulong boxSize = checked((ulong)(end - boxStart));
        destination.Position = checked(boxStart + 8);
        Span<byte> sizeBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(sizeBytes, boxSize);
        destination.Write(sizeBytes);
        destination.Position = end;

        return new Mp4MediaDataWriteResult(
            checked((ulong)boxStart),
            checked((ulong)dataStart),
            checked((ulong)(end - dataStart)),
            offsetsByTrack);
    }
}
