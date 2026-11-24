# Third-party notices

## aac-go

The AAC Huffman numeric code tables in
`PixoraStudio.MediaEngine/Decoders/Audio/AacHuffmanCodebookFactory.cs`
and AAC scalefactor-band boundary tables in
`PixoraStudio.MediaEngine/Models/Audio/AacScaleFactorBandTable.cs`
are adapted from corresponding table data in
[arabian9ts/aac-go](https://github.com/arabian9ts/aac-go),
Copyright 2026 arabian9ts.

The source project is licensed under the Apache License, Version 2.0.
A copy of that license is available at:
https://www.apache.org/licenses/LICENSE-2.0

Only numeric table data was adapted for the PixoraStudio C# implementation.
No Go decoder implementation code was copied into PixoraStudio.

## NAudio

PixoraStudio uses [NAudio](https://github.com/naudio/NAudio) version 2.2.1
for Windows PCM audio output.

NAudio is licensed under the MIT License.
A copy of the license is available at:
`THIRD-PARTY-LICENSES/MIT-NAudio.txt`.
