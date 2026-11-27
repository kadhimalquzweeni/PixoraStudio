using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PixoraStudio.App.Services;

public interface IDirectMediaDownloadService
{
    Task<string> DownloadAsync(
        Uri source,
        string outputDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

public sealed class DirectMediaDownloadService(HttpClient httpClient) : IDirectMediaDownloadService
{
    private const int BufferSize = 81920;

    public async Task<string> DownloadAsync(
        Uri source,
        string outputDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (source.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(source));

        var extension = Path.GetExtension(source.AbsolutePath);
        if (!DirectMediaFormats.IsSupportedExtension(extension))
            throw new NotSupportedException("This is not a supported direct video file. HLS and DASH streams are not supported yet.");

        Directory.CreateDirectory(outputDirectory);

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        ValidateContentType(response.Content.Headers.ContentType?.MediaType);

        var fileName = GetSafeFileName(response.Content.Headers.ContentDisposition, source, extension);
        var destinationPath = GetUniquePath(outputDirectory, fileName);
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".part";

        try
        {
            var totalLength = response.Content.Headers.ContentLength;
            long totalRead;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                var headerLength = await ReadHeaderAsync(input, buffer, cancellationToken);
                if (!DirectMediaFormats.HasValidContainerHeader(buffer.AsSpan(0, headerLength), extension))
                    throw new InvalidDataException("The response does not contain a valid MP4, WebM, MOV or MKV file.");

                await output.WriteAsync(buffer.AsMemory(0, headerLength), cancellationToken);
                totalRead = headerLength;
                ReportProgress(progress, totalRead, totalLength);

                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;
                    ReportProgress(progress, totalRead, totalLength);
                }

                await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (totalLength is > 0 && totalRead != totalLength.Value)
                throw new InvalidDataException("The download ended before the complete file was received.");

            // Both streams are disposed before the atomic rename, which is required on Windows.
            File.Move(temporaryPath, destinationPath);
            progress?.Report(100d);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task<int> ReadHeaderAsync(
        Stream input,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < 12)
        {
            var read = await input.ReadAsync(buffer.AsMemory(totalRead, 12 - totalRead), cancellationToken);
            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }

    private static void ValidateContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType) ||
            mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/mp4", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/webm", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/x-matroska", StringComparison.OrdinalIgnoreCase) ||
            mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidDataException("The URL returned a web page or unsupported response type, not a video file.");
    }

    private static void ReportProgress(IProgress<double>? progress, long totalRead, long? totalLength)
    {
        if (totalLength is > 0)
            progress?.Report(Math.Clamp((double)totalRead / totalLength.Value * 100d, 0d, 100d));
        else
            progress?.Report(double.NaN);
    }

    private static string GetSafeFileName(
        ContentDispositionHeaderValue? disposition,
        Uri source,
        string extension)
    {
        var candidate = disposition?.FileNameStar ?? disposition?.FileName;
        if (!string.IsNullOrWhiteSpace(candidate))
            candidate = candidate.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = Path.GetFileName(source.LocalPath);

        candidate = Path.GetFileName(candidate);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(candidate.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
            safe = "download" + extension;

        if (!Path.GetExtension(safe).Equals(extension, StringComparison.OrdinalIgnoreCase))
            safe = Path.ChangeExtension(safe, extension);

        return safe;
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return path;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            path = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(path))
                return path;
        }
    }
}

public static class DirectMediaFormats
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".mkv"
    };

    public static bool IsSupportedExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);

    public static bool HasValidContainerHeader(ReadOnlySpan<byte> header, string extension)
    {
        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mov", StringComparison.OrdinalIgnoreCase))
        {
            return header.Length >= 8 && header.Slice(4, 4).SequenceEqual("ftyp"u8);
        }

        if (extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 });
        }

        return false;
    }
}
