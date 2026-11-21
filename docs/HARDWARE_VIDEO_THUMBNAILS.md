# Hardware-accelerated video thumbnails

PixoraStudio uses Windows Media Foundation for its optional hardware-assisted
thumbnail path. It does not invoke FFmpeg to decode video frames.

## Decode pipeline

1. Create a Direct3D 11 hardware device with video support.
2. Create and reset a Media Foundation DXGI device manager with that device.
3. Open the MP4 through the Media Foundation Source Reader.
4. Attach the DXGI device manager to the Source Reader and leave DXVA enabled.
5. Request RGB32 thumbnail output at a maximum width of 240 pixels. Media
   Foundation's advanced video processing can perform format conversion and
   resizing as part of this output path.
6. Read the frame at each requested presentation timestamp. For a final
   filmstrip position that falls after the start timestamp of the last displayed
   frame, use the last decoded frame rather than failing at end-of-stream.
7. Copy GPU-backed output through a D3D11 staging texture when necessary, then
   encode the PNG with SkiaSharp.

The Source Reader opens and demuxes the MP4 independently. PixoraStudio's
existing MP4 reader is retained for the managed fallback path.

## Fallback behaviour

The hardware path is optional. If Windows, Direct3D 11, Media Foundation, the
file, the selected decoder transform, or frame readback is unavailable or
fails, `VideoProcessingService` logs the failure and runs the existing
PixoraStudio-managed MP4/H.264 decoder instead. Cancellation is not swallowed.

A successful Source Reader run confirms that the Media Foundation path
completed with the D3D11 device manager configured and DXVA enabled. It does
not, by itself, prove that every GPU driver selected a hardware decoder
transform; driver capabilities and codec support still affect transform
selection.

## Diagnostics and validation

- Check the Visual Studio Output window for `[Media Foundation]` messages.
- During a real decode, Windows Task Manager's GPU Performance view can help
  confirm activity on the GPU's **Video Decode** engine.
- The automated tests cover Source Reader result handling, preroll samples,
  final-frame fallback, end-of-stream, decoder errors, and the managed fallback
  integration.
- CI builds and tests on Windows. A physical-machine check is still required
  to confirm DXVA behaviour and compare thumbnail-generation performance on
  the target GPU and driver.

## Requirements

- Windows with Media Foundation and Direct3D 11 support.
- A graphics driver that exposes hardware decoding for the video's codec and
  profile to Media Foundation.
- If hardware decoding is unavailable, PixoraStudio continues with its managed
  C# decoder.
