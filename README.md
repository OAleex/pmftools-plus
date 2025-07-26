# pmftools plus

Tools for handling and converting PSP `.pmf` movie files, including enhanced automation for UMD Stream Composer with customizable bitrate settings.

## Tools Included

* `psmfdump`: Extract `.264` and `.oma` streams from `.pmf` files.
* `Mps2Pmf`: Convert `.mps` files to `.pmf` format.
* `AutoUscV2`: Automates UMD Stream Composer with bitrate customization.
* `get_duration`: Python-based duration detection tool.

## Download

You can find a prebuilt package in the releases section.

## Usage

### 🔄 Convert PMF to MOV

```
Step 1: Place your `.pmf` files into the `input/pmf` folder  
Step 2: Run `1 - Pmf2Mov.bat`  
Step 3: The converted `.mov` files will appear in `output/mov`
```

This uses `psmfdump` to extract audio/video and `ffmpeg` to convert to MOV format.

### 🎬 Convert MOV to PMF (via AVI + AutoUsc)

```
Step 1: Place your edited `.mov` files into `input/mov_edited`  
Step 2: Run `2 - Mov2Pmf.bat`  
Step 3: Converted `.pmf` files will appear in `output/pmf`
```

You can edit the default bitrate values directly in the batch file:
```batch
set AVERAGEBITRATE=1000
set MAXBITRATE=2000
```

## Requirements

* `ffmpeg.exe`
* `psmfdump.exe`
* `AutoUscV2.exe` (custom wrapper for UMD Stream Composer)
* `Mps2Pmf.exe`
* `get_duration.exe` (Python-based duration detection tool)

### UMD Stream Composer Setup

Before using this tool, you must search and download "UMD Stream Composer" from the Internet and place the program folder in the `tools` directory. The structure should be:

```
tools/
├── Umd Stream Composer/
│   └── bin/
│       ├── UmdStream.exe (renamed - preferred)
│       ├── UmdStreamComposer.exe (original - fallback)
│       └── (other UMD Stream Composer files)
├── AutoUscV2.exe
└── (other tools)
```

Alternatively, you can use the `-x|--executable` option to specify a custom location for the UmdStreamComposer.exe.

**Important notes:**
- You MUST run UMD Stream Composer manually at least once and ensure it works properly on your computer
- UMD Stream Composer version 1.5 RC4 is proven to work with this tool
- If both executables exist, the tool automatically prioritizes `UmdStream.exe` (renamed version) over `UmdStreamComposer.exe` (original)

## Why This Modified Version?

The original pmftools by LITTOMA converts PMF files to MP4 format, which is good for general use but doesn't preserve 100% quality and fidelity for those seeking maximum video quality.

### Problems with UMD Stream Composer:
- **DirectDraw device error**: The program shows a "DirectDraw device does not support overlays" dialog that needs to be dismissed
- **File naming issues**: The program often needs to be renamed from `UmdStreamComposer.exe` to `UmdStream.exe` for proper operation (the tool automatically detects both but prioritizes the renamed version)
- **Manifest cleanup**: Sometimes needs manifest file deletion for clean execution

### Improvements in This Version:
- **MOV format support**: Preserves higher quality compared to MP4 conversion
- **Automated UMD Stream Composer handling**: Deals with DirectDraw dialogs and file management automatically
- **Custom get_duration tool**: Python-based duration detection to overcome various compatibility issues

## Credits

**Original project**: [TeamPBCN/pmftools](https://github.com/TeamPBCN/pmftools/) by **LITTOMA**  
**Verified by**: **吉🐣 PRODUCTION**

MIT License – see LICENSE for details.