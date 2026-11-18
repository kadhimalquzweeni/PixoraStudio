using System.Collections.ObjectModel;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Parses the SPS and PPS NAL units carried by an avcC configuration record
/// and validates the PPS-to-SPS references.
/// </summary>
public sealed class H264AvcParameterSetCollectionReader
{
    private readonly H264AvcDecoderConfigurationReader _configurationReader = new();
    private readonly H264SequenceParameterSetReader _spsReader = new();
    private readonly H264PictureParameterSetReader _ppsReader = new();

    public H264AvcParameterSetCollection Read(Mp4CodecConfiguration configuration) =>
        Read(_configurationReader.Read(configuration));

    public H264AvcParameterSetCollection Read(H264AvcDecoderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Dictionary<uint, H264SequenceParameterSet> sequenceParameterSets = [];
        foreach (H264NalUnit nalUnit in configuration.SequenceParameterSets)
        {
            H264SequenceParameterSet sps = _spsReader.Read(nalUnit);
            if (!sequenceParameterSets.TryAdd(sps.Id, sps))
                throw new FormatException($"The avcC record contains duplicate SPS id {sps.Id}.");
        }

        Dictionary<uint, H264PictureParameterSetHeader> pictureParameterSets = [];
        foreach (H264NalUnit nalUnit in configuration.PictureParameterSets)
        {
            // Parse once without SPS context to discover seq_parameter_set_id.
            // Reparse with the matching SPS so scaling-list count follows its
            // chroma_format_idc when the optional PPS extension is present.
            H264PictureParameterSetHeader preliminary = _ppsReader.Read(nalUnit);
            if (!sequenceParameterSets.TryGetValue(
                preliminary.SequenceParameterSetId, out H264SequenceParameterSet? sps))
                throw new FormatException(
                    $"PPS {preliminary.Id} references missing SPS {preliminary.SequenceParameterSetId}.");

            H264PictureParameterSetHeader pps = _ppsReader.Read(nalUnit, sps);
            if (!pictureParameterSets.TryAdd(pps.Id, pps))
                throw new FormatException($"The avcC record contains duplicate PPS id {pps.Id}.");
        }

        return new H264AvcParameterSetCollection(
            configuration,
            new ReadOnlyDictionary<uint, H264SequenceParameterSet>(sequenceParameterSets),
            new ReadOnlyDictionary<uint, H264PictureParameterSetHeader>(pictureParameterSets));
    }
}
