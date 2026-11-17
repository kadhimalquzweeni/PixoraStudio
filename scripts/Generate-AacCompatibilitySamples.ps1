[CmdletBinding()]
param(
    [string]$FfmpegPath = "ffmpeg",
    [string]$OutputDirectory = (Join-Path $env:TEMP "PixoraStudio-AAC-Compatibility"),
    [switch]$IncludeStressCases,
    [ValidateRange(5, 300)]
    [int]$StressDurationSeconds = 30
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

    if ($IncludeStressCases) {
        foreach ($rate in @(44100, 48000)) {
            foreach ($channels in @(1, 2)) {
                $layout = if ($channels -eq 1) { "mono" } else { "stereo" }
                $bitrates = if ($channels -eq 1) {
                    @("48k", "64k", "96k", "128k")
                }
                else {
                    @("64k", "96k", "128k", "192k", "256k")
                }

                foreach ($profile in @("tone", "multitone", "pink-noise")) {
                    switch ($profile) {
                        "tone" {
                            $input = "sine=frequency=997:sample_rate=$rate:duration=$StressDurationSeconds"
                        }
                        "multitone" {
                            $input = "aevalsrc=0.25*sin(2*PI*440*t)+0.2*sin(2*PI*997*t)+0.15*sin(2*PI*6000*t):s=$rate:d=$StressDurationSeconds"
                        }
                        "pink-noise" {
                            $input = "anoisesrc=color=pink:amplitude=0.2:sample_rate=$rate:duration=$StressDurationSeconds"
                        }
                    }

                    foreach ($bitrate in $bitrates) {
                        $fileName = "aac-stress-$encoder-$rate-$layout-$profile-$bitrate-$($StressDurationSeconds)s.mp4"
                        $outputPath = Join-Path $OutputDirectory $fileName

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
        }
    }
}

Write-Host ""
Write-Host "Generated $generated AAC-LC MP4 files in:"
Write-Host $OutputDirectory
Write-Host ""
Write-Host "Base matrix: all 13 standard AAC sampling-frequency indices in mono and stereo."
if ($IncludeStressCases) {
    Write-Host "Stress matrix: 44.1/48 kHz, tone/multitone/pink-noise, varied bitrates, and $StressDurationSeconds-second tracks."
}
Write-Host "Available encoders tested: $($encoders -join ', ')"
