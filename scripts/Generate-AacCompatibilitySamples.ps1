[CmdletBinding()]
param(
    [string]$FfmpegPath = "ffmpeg",
    [string]$OutputDirectory = (Join-Path $env:TEMP "PixoraStudio-AAC-Compatibility")
)

$ErrorActionPreference = "Stop"

$encoderListing = & $FfmpegPath -hide_banner -encoders 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Unable to run FFmpeg at '$FfmpegPath'. Make sure FFmpeg is installed and available on PATH."
}

$encoders = @("aac")
if (($encoderListing | Out-String) -match "\blibfdk_aac\b") {
    $encoders += "libfdk_aac"
}

$sampleRates = @(
    @{ Rate = 96000; Index = 0 },
    @{ Rate = 88200; Index = 1 },
    @{ Rate = 64000; Index = 2 },
    @{ Rate = 48000; Index = 3 },
    @{ Rate = 44100; Index = 4 },
    @{ Rate = 32000; Index = 5 },
    @{ Rate = 24000; Index = 6 },
    @{ Rate = 22050; Index = 7 },
    @{ Rate = 16000; Index = 8 },
    @{ Rate = 12000; Index = 9 },
    @{ Rate = 11025; Index = 10 },
    @{ Rate = 8000; Index = 11 },
    @{ Rate = 7350; Index = 12 }
)

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$generated = 0
foreach ($encoder in $encoders) {
    foreach ($sampleRate in $sampleRates) {
        foreach ($channels in @(1, 2)) {
            $layout = if ($channels -eq 1) { "mono" } else { "stereo" }
            $fileName = "aac-$encoder-$($sampleRate.Rate)-$layout.mp4"
            $outputPath = Join-Path $OutputDirectory $fileName
            $bitrate = if ($channels -eq 1) { "96k" } else { "128k" }
            $input = "sine=frequency=997:sample_rate=$($sampleRate.Rate):duration=4"

            Write-Host "Generating $fileName"

            $arguments = @(
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-f", "lavfi",
                "-i", $input,
                "-ac", "$channels",
                "-c:a", $encoder,
                "-profile:a", "aac_low",
                "-b:a", $bitrate,
                "-movflags", "+faststart",
                $outputPath
            )

            & $FfmpegPath @arguments
            if ($LASTEXITCODE -ne 0) {
                throw "FFmpeg failed while generating '$fileName' with encoder '$encoder'."
            }

            $generated++
        }
    }
}

Write-Host ""
Write-Host "Generated $generated AAC-LC MP4 files in:"
Write-Host $OutputDirectory
Write-Host ""
Write-Host "The files cover all 13 standard AAC sampling-frequency indices in mono and stereo."
Write-Host "Available encoders tested: $($encoders -join ', ')"
