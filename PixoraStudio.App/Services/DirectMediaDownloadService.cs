using System.Net;
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
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".mkv"
    };

    public async Task<string> DownloadAsync(
        Uri source,
        string outputDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(source));

        var extension = Path.GetExtension(source.AbsolutePath);
        if (!SupportedExtensions.Contains(extension))
            throw new NotSupportedException("This is not a supported direct video file. HLS and DASH streams are not supported yet.");

        Directory.CreateDirectory(outputDirectory);

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            (mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
             mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
             mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The URL returned a web page or text response, not a video file.");
        }

        var fileName = GetSafeFileName(response.Content.Headers.ContentDisposition, source, extension);
        var destinationPath = GetUniquePath(outputDirectory, fileName);
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".part";

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[81920];
            long totalRead = 0;
            var totalLength = response.Content.Headers.ContentLength;
            int bytesRead;

            while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalLength is > 0)
                    progress?.Report(Math.Clamp((double)totalRead / totalLength.Value * 100d, 0d, 100d));
            }

            await output.FlushAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (totalRead == 0)
                throw new InvalidDataException("The server returned an empty file.");

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

        if (!Path.HasExtension(safe))
            safe += extension;

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
