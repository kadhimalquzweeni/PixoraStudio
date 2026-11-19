[CmdletBinding()]
param(
    [string]$FfmpegPath = "ffmpeg",
    [string]$FfprobePath = "ffprobe",
    [string]$OutputDirectory = (Join-Path $env:TEMP "PixoraStudio-H264-CABAC-Reference")
)

$ErrorActionPreference = "Stop"

function Invoke-CheckedFfmpeg {
    param([string[]]$Arguments, [string]$Description)

    & $script:FfmpegPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg failed while $Description (exit code $LASTEXITCODE)."
    }
}

$encoderListing = (& $FfmpegPath -hide_banner -encoders 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to run FFmpeg at '$FfmpegPath'. Install FFmpeg and ensure it is on PATH."
}
if ($encoderListing -notmatch "\blibx264\b") {
    throw "The selected FFmpeg build does not expose libx264. Use a build compiled with libx264."
}

$versionOutput = (& $FfmpegPath -version 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read the FFmpeg version from '$FfmpegPath'."
}
$ffmpegVersion = ($versionOutput -split '\r?\n' | Select-Object -First 1)

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# testsrc2 is deterministic and contains both spatial detail and motion.
# Two slices per picture exercise slice-boundary handling; B frames exercise
# decode/display reordering. Single-threaded encoding improves reproducibility.
$cases = @(
    @{
        Name = "testsrc2-128x96-main-cabac"
        Input = "testsrc2=size=128x96:rate=24:duration=2"
        Width = 128
        Height = 96
        Description = "Moving colour bars and geometric test pattern"
    },
    @{
        Name = "testsrc2-176x144-main-cabac"
        Input = "testsrc2=size=176x144:rate=24:duration=2"
        Width = 176
        Height = 144
        Description = "Larger moving test pattern with additional macroblock rows and columns"
    },
    @{
        Name = "mandelbrot-128x96-main-cabac"
        Input = "mandelbrot=size=128x96:rate=24:duration=2"
        Width = 128
        Height = 96
        Description = "High-detail animated fractal pattern"
    }
)

$manifestCases = @()

foreach ($case in $cases) {
    $mp4Path = Join-Path $OutputDirectory "$($case.Name).mp4"
    $rawPath = Join-Path $OutputDirectory "$($case.Name)-reference.yuv"
    $frameBytes = [int]($case.Width * $case.Height * 3 / 2)

    $encodeArguments = @(
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", $case.Input,
        "-an", "-frames:v", "48",
        "-pix_fmt", "yuv420p",
        "-c:v", "libx264", "-profile:v", "main", "-preset", "medium",
        "-crf", "18", "-threads", "1",
        "-x264-params", "cabac=1:bframes=2:ref=3:keyint=24:min-keyint=24:scenecut=0:slices=2:threads=1",
        "-movflags", "+faststart",
        $mp4Path
    )
    Invoke-CheckedFfmpeg -Arguments $encodeArguments -Description "encoding $($case.Name)"

    $decodeArguments = @(
        "-hide_banner", "-loglevel", "error", "-y",
        "-i", $mp4Path, "-map", "0:v:0",
        "-vsync", "0", "-pix_fmt", "yuv420p",
        "-f", "rawvideo", $rawPath
    )
    Invoke-CheckedFfmpeg -Arguments $decodeArguments -Description "decoding reference frames for $($case.Name)"

    $streamInfo = (& $FfprobePath -v error -select_streams v:0 -show_entries stream=codec_name,profile,width,height,pix_fmt,r_frame_rate,nb_frames -of json $mp4Path 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe failed while inspecting '$mp4Path'."
    }

    $hashes = [System.Collections.Generic.List[string]]::new()
    $stream = [System.IO.File]::OpenRead($rawPath)
    try {
        $frame = [byte[]]::new([int]$frameBytes)
        while ($stream.Position -lt $stream.Length) {
            $offset = 0
            while ($offset -lt $frame.Length) {
                $count = $stream.Read($frame, $offset, $frame.Length - $offset)
                if ($count -eq 0) {
                    throw "Reference output '$rawPath' ended with a partial YUV frame."
                }
                $offset += $count
            }

            $hash = [System.Security.Cryptography.SHA256]::HashData($frame)
            $hashes.Add([Convert]::ToHexString($hash).ToLowerInvariant())
        }
    }
    finally {
        $stream.Dispose()
    }

    if ($hashes.Count -eq 0) {
        throw "No reference frames were decoded for '$($case.Name)'."
    }

    $manifestCases += [pscustomobject]@{
        Name = $case.Name
        Description = $case.Description
        InputFilter = $case.Input
        EncodeArguments = $encodeArguments
        DecodeArguments = $decodeArguments
        StreamInfo = ($streamInfo | ConvertFrom-Json)
        Mp4File = [System.IO.Path]::GetFileName($mp4Path)
        ReferenceYuvFile = [System.IO.Path]::GetFileName($rawPath)
        Width = $case.Width
        Height = $case.Height
        ReferenceFrameCount = $hashes.Count
        FrameSha256 = @($hashes)
    }

    Write-Host "Generated $($case.Name): $($hashes.Count) reference frames"
}

$manifest = [pscustomobject]@{
    SchemaVersion = 1
    GeneratedUtc = [DateTime]::UtcNow.ToString("O")
    FfmpegExecutable = $FfmpegPath
    FfprobeExecutable = $FfprobePath
    FfmpegVersion = $ffmpegVersion
    ReferenceDecoder = "FFmpeg software H.264 decoder (version recorded above)"
    ReferencePixelFormat = "yuv420p"
    FrameHash = "SHA-256 over each complete decoded Y, Cb, Cr frame in planar order"
    FrameDimensions = "Recorded per case"
    Notes = @(
        "The generated MP4 is the encoded input corpus, not a PixoraStudio decoder output.",
        "Reference hashes are produced by decoding that exact MP4 with the recorded FFmpeg build.",
        "Retain the MP4 and this manifest together when importing a case into automated tests.",
        "Different x264/FFmpeg builds can produce different encoded samples; regenerate and record a new manifest rather than replacing hashes silently."
    )
    Cases = @($manifestCases)
}

$manifestPath = Join-Path $OutputDirectory "h264-cabac-reference-manifest.json"
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host ""
Write-Host "H.264 Main-profile CABAC reference corpus generated:"
Write-Host $OutputDirectory
Write-Host "Manifest: $manifestPath"
Write-Host "Encoder: libx264, CABAC enabled, B-frames, 3 references, 2 slices, single-threaded"
