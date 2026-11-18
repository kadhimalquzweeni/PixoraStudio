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
        if (configuration.SequenceParameterSetExtensions.Count != 0)
            throw new NotSupportedException(
                "AVC sequence-parameter-set extension NAL units are not supported by this decoder.");

        Dictionary<uint, H264SequenceParameterSet> sequenceParameterSets = [];
        foreach (H264NalUnit nalUnit in configuration.SequenceParameterSets)
        {
            H264SequenceParameterSet sps = _spsReader.Read(nalUnit);
            if (configuration.Profile != sps.ProfileIdc ||
                configuration.Compatibility != sps.ProfileCompatibility ||
                configuration.Level != sps.LevelIdc)
                throw new FormatException(
                    "The avcC profile, compatibility or level fields do not match its SPS.");
            if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
                throw new NotSupportedException(
                    "The managed AVC decoder supports non-separate 4:2:0 and 4:2:2 only.");
            if (sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0)
                throw new NotSupportedException(
                    "The managed AVC decoder supports 8-bit samples only.");
            if (configuration.ChromaFormat is byte chromaFormat &&
                chromaFormat != sps.ChromaFormatIdc)
                throw new FormatException("The avcC chroma format does not match its SPS.");
            if (configuration.BitDepthLumaMinus8 is byte lumaDepth &&
                lumaDepth != sps.BitDepthLumaMinus8)
                throw new FormatException("The avcC luma bit depth does not match its SPS.");
            if (configuration.BitDepthChromaMinus8 is byte chromaDepth &&
                chromaDepth != sps.BitDepthChromaMinus8)
                throw new FormatException("The avcC chroma bit depth does not match its SPS.");
            if (!sequenceParameterSets.TryAdd(sps.Id, sps))
                throw new FormatException($"The avcC record contains duplicate SPS id {sps.Id}.");
        }

        Dictionary<uint, H264PictureParameterSetHeader> pictureParameterSets = [];
        foreach (H264NalUnit nalUnit in configuration.PictureParameterSets)
        {
            (uint ppsId, uint spsId) =
                _ppsReader.ReadParameterSetIdentifiers(nalUnit);
            if (!sequenceParameterSets.TryGetValue(spsId, out H264SequenceParameterSet? sps))
                throw new FormatException(
                    $"PPS {ppsId} references missing SPS {spsId}.");

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
