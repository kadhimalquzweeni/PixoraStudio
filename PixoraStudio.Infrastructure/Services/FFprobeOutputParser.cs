using System.Globalization;
using System.Text.Json;
using PixoraStudio.Core.Models;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>Converts FFprobe JSON output into validated video metadata.</summary>
public static class FFprobeOutputParser
{
    public static VideoInfo Parse(string json, string filePath)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("FFprobe returned an empty response.");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("streams", out JsonElement streams) ||
            streams.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("FFprobe did not return any media streams.");

        JsonElement? videoStream = null;
        foreach (JsonElement candidate in streams.EnumerateArray())
        {
            if (TryGetString(candidate, "codec_type") == "video" ||
                (TryGetPositiveInt(candidate, "width") > 0 &&
                 TryGetPositiveInt(candidate, "height") > 0))
            {
                videoStream = candidate;
                break;
            }
        }

        if (videoStream is null)
            throw new InvalidDataException("The file does not contain a readable video stream.");

        JsonElement stream = videoStream.Value;
        int width = TryGetPositiveInt(stream, "width");
        int height = TryGetPositiveInt(stream, "height");
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("FFprobe could not determine the video's resolution.");

        double durationSeconds = TryGetPositiveDouble(root, "format", "duration");
        if (durationSeconds <= 0)
            durationSeconds = TryGetPositiveDouble(stream, "duration");

        if (durationSeconds <= 0)
        {
            double durationTicks = TryGetPositiveDouble(stream, "duration_ts");
            double timeBase = ParseRational(TryGetString(stream, "time_base"));
            if (durationTicks > 0 && timeBase > 0)
                durationSeconds = durationTicks * timeBase;
        }

        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new InvalidDataException(
                "FFprobe could not determine a valid video duration. The file may be incomplete or have a damaged container index.");

        double frameRate = ParseRational(TryGetString(stream, "avg_frame_rate"));
        if (frameRate <= 0)
            frameRate = ParseRational(TryGetString(stream, "r_frame_rate"));

        return new VideoInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Duration = TimeSpan.FromSeconds(durationSeconds),
            Width = width,
            Height = height,
            FramesPerSecond = double.IsFinite(frameRate) && frameRate > 0 ? frameRate : 0
        };
    }

    private static int TryGetPositiveInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return Math.Max(0, number);

        return int.TryParse(GetScalarText(value), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int parsed) ? Math.Max(0, parsed) : 0;
    }

    private static double TryGetPositiveDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return 0;

        return double.TryParse(GetScalarText(value), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double parsed) &&
            double.IsFinite(parsed) && parsed > 0 ? parsed : 0;
    }

    private static double TryGetPositiveDouble(
        JsonElement root, string objectName, string propertyName)
    {
        return root.TryGetProperty(objectName, out JsonElement nested)
            ? TryGetPositiveDouble(nested, propertyName)
            : 0;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            ? GetScalarText(value)
            : null;
    }

    private static string? GetScalarText(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

    private static double ParseRational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "N/A")
            return 0;

        string[] parts = value.Split('/');
        if (parts.Length == 1)
        {
            return double.TryParse(parts[0], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double decimalValue) &&
                double.IsFinite(decimalValue) && decimalValue > 0
                    ? decimalValue : 0;
        }

        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double denominator) ||
            denominator <= 0)
            return 0;

        double result = numerator / denominator;
        return double.IsFinite(result) && result > 0 ? result : 0;
    }
}
