using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixoraStudio.App.Services;

public interface IDirectMediaDownloadService
{
    Task<string> DownloadAsync(
        Uri source,
        string outputDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    void DiscardPartial(Uri source, string outputDirectory) { }

    async Task<DownloadResult> DownloadResumableAsync(
        Uri source,
        string outputDirectory,
        IProgress<double>? progress,
        DownloadControl control,
        CancellationToken cancellationToken)
    {
        var path = await DownloadAsync(source, outputDirectory, progress, cancellationToken);
        return DownloadResult.Completed(path);
    }
}

public sealed class DownloadControl : IDisposable
{
    private readonly CancellationTokenSource _pauseCancellation = new();
    private int _pauseRequested;

    public bool IsPauseRequested => Volatile.Read(ref _pauseRequested) == 1;
    internal CancellationToken PauseToken => _pauseCancellation.Token;

    public void RequestPause()
    {
        if (Interlocked.Exchange(ref _pauseRequested, 1) == 0)
            _pauseCancellation.Cancel();
    }

    public void Dispose() => _pauseCancellation.Dispose();
}

public sealed record DownloadResult(bool IsPaused, string? OutputPath)
{
    public static DownloadResult Paused() => new(true, null);
    public static DownloadResult Completed(string path) => new(false, path);
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
        var result = await DownloadResumableAsync(
            source,
            outputDirectory,
            progress,
            new DownloadControl(),
            cancellationToken);
        return result.OutputPath ?? throw new OperationCanceledException(cancellationToken);
    }

    public void DiscardPartial(Uri source, string outputDirectory)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)));
        var partialPath = Path.Combine(outputDirectory, $".pixora-{key}.part");
        DeletePartialFiles(partialPath, partialPath + ".json");
    }

    public async Task<DownloadResult> DownloadResumableAsync(
        Uri source,
        string outputDirectory,
        IProgress<double>? progress,
        DownloadControl control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (source.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(source));

        var extension = Path.GetExtension(source.AbsolutePath);
        if (!DirectMediaFormats.IsSupportedExtension(extension))
            throw new NotSupportedException("This is not a supported direct video file. HLS and DASH streams are not supported yet.");

        Directory.CreateDirectory(outputDirectory);
        var partialKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)));
        var partialPath = Path.Combine(outputDirectory, $".pixora-{partialKey}.part");
        var metadataPath = partialPath + ".json";
        var metadata = await ReadResumeMetadataAsync(metadataPath, source, partialPath, outputDirectory, cancellationToken);
        var existingLength = metadata is not null && File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        if (metadata is null || existingLength == 0)
        {
            DeletePartialFiles(partialPath, metadataPath);
            metadata = null;
            existingLength = 0;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            control.PauseToken);
        var token = linkedCancellation.Token;

        try
        {
            var response = await SendDownloadRequestAsync(source, existingLength, metadata, token);
            using (response)
            {
                var isAppend = existingLength > 0 &&
                               response.StatusCode == HttpStatusCode.PartialContent &&
                               response.Content.Headers.ContentRange?.From == existingLength &&
                               HasMatchingValidator(response, metadata!);

                if (existingLength > 0 && !isAppend)
                {
                    response.Dispose();
                    DeletePartialFiles(partialPath, metadataPath);
                    metadata = null;
                    existingLength = 0;

                    using var restartResponse = await SendDownloadRequestAsync(source, 0, null, token);
                    restartResponse.EnsureSuccessStatusCode();
                    return await WriteResponseAsync(
                        restartResponse, source, extension, partialPath, metadataPath,
                        outputDirectory, null, 0, progress, control, cancellationToken, token);
                }

                response.EnsureSuccessStatusCode();
                return await WriteResponseAsync(
                    response, source, extension, partialPath, metadataPath,
                    outputDirectory, metadata, existingLength, progress, control, cancellationToken, token);
            }
        }
        catch (OperationCanceledException) when (
            control.IsPauseRequested && !cancellationToken.IsCancellationRequested)
        {
            return DownloadResult.Paused();
        }
        catch
        {
            DeletePartialFiles(partialPath, metadataPath);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(
        Uri source,
        long offset,
        ResumeMetadata? metadata,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (offset > 0 && metadata is not null)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
            if (EntityTagHeaderValue.TryParse(metadata.ETag, out var etag) && !etag.IsWeak)
                request.Headers.IfRange = new RangeConditionHeaderValue(etag);
            else if (metadata.LastModifiedUtc is { } lastModified)
                request.Headers.IfRange = new RangeConditionHeaderValue(lastModified);
        }

        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<DownloadResult> WriteResponseAsync(
        HttpResponseMessage response,
        Uri source,
        string extension,
        string partialPath,
        string metadataPath,
        string outputDirectory,
        ResumeMetadata? existingMetadata,
        long offset,
        IProgress<double>? progress,
        DownloadControl control,
        CancellationToken cancellationToken,
        CancellationToken transferToken)
    {
        ValidateContentType(response.Content.Headers.ContentType?.MediaType);

        var fileName = existingMetadata?.FileName ??
                       GetSafeFileName(response.Content.Headers.ContentDisposition, source, extension);
        var destinationPath = existingMetadata?.DestinationPath ??
                              GetUniquePath(outputDirectory, fileName);
        var etagValue = response.Headers.ETag?.ToString() ?? existingMetadata?.ETag;
        var lastModified = response.Content.Headers.LastModified?.UtcDateTime ??
                           existingMetadata?.LastModifiedUtc;
        var expectedLength = response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange?.Length
            : response.Content.Headers.ContentLength;

        var metadata = new ResumeMetadata(
            source.AbsoluteUri, destinationPath, fileName, etagValue, lastModified, expectedLength);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata), transferToken);

        var totalRead = offset;
        await using (var input = await response.Content.ReadAsStreamAsync(transferToken))
        await using (var output = new FileStream(
            partialPath,
            offset > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[BufferSize];
            if (offset == 0)
            {
                var headerLength = await ReadHeaderAsync(input, buffer, transferToken);
                if (!DirectMediaFormats.HasValidContainerHeader(buffer.AsSpan(0, headerLength), extension))
                    throw new InvalidDataException("The response does not contain a valid MP4, WebM, MOV or MKV file.");

                await output.WriteAsync(buffer.AsMemory(0, headerLength), transferToken);
                totalRead += headerLength;
            }
            else
            {
                await using var headerStream = new FileStream(
                    partialPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    12, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var headerLength = await ReadHeaderAsync(headerStream, buffer, transferToken);
                if (!DirectMediaFormats.HasValidContainerHeader(buffer.AsSpan(0, headerLength), extension))
                    throw new InvalidDataException("The saved partial file has an invalid media header.");
            }

            ReportProgress(progress, totalRead, expectedLength);
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, transferToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), transferToken);
                totalRead += bytesRead;
                ReportProgress(progress, totalRead, expectedLength);
            }

            await output.FlushAsync(transferToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (expectedLength is > 0 && totalRead != expectedLength.Value)
            throw new InvalidDataException("The download ended before the complete file was received.");

        File.Move(partialPath, destinationPath);
        if (File.Exists(metadataPath))
            File.Delete(metadataPath);
        progress?.Report(100d);
        return DownloadResult.Completed(destinationPath);
    }

    private static async Task<ResumeMetadata?> ReadResumeMetadataAsync(
        string metadataPath,
        Uri source,
        string partialPath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath) || !File.Exists(partialPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<ResumeMetadata>(json);
            if (metadata is null ||
                !string.Equals(metadata.Source, source.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(Path.GetDirectoryName(metadata.DestinationPath), outputDirectory, StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(metadata.ETag) && metadata.LastModifiedUtc is null))
            {
                return null;
            }

            return metadata;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasMatchingValidator(HttpResponseMessage response, ResumeMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ETag))
            return string.Equals(response.Headers.ETag?.ToString(), metadata.ETag, StringComparison.Ordinal);

        return metadata.LastModifiedUtc is { } expected &&
               response.Content.Headers.LastModified?.UtcDateTime == expected;
    }

    private static void DeletePartialFiles(string partialPath, string metadataPath)
    {
        if (File.Exists(partialPath))
            File.Delete(partialPath);
        if (File.Exists(metadataPath))
            File.Delete(metadataPath);
    }

    private sealed record ResumeMetadata(
        string Source,
        string DestinationPath,
        string FileName,
        string? ETag,
        DateTime? LastModifiedUtc,
        long? ExpectedLength);

    private static async Task<int> ReadHeaderAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
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
            return;

        throw new InvalidDataException("The URL returned a web page or unsupported response type, not a video file.");
    }

    private static void ReportProgress(IProgress<double>? progress, long totalRead, long? totalLength)
    {
        if (totalLength is > 0)
            progress?.Report(Math.Clamp((double)totalRead / totalLength.Value * 100d, 0d, 100d));
        else
            progress?.Report(double.NaN);
    }

    private static string GetSafeFileName(ContentDispositionHeaderValue? disposition, Uri source, string extension)
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
            return header.Length >= 8 && header.Slice(4, 4).SequenceEqual("ftyp"u8);

        if (extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            return header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 });

        return false;
    }
}
