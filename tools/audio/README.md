# Audio tools
- `python3 tools/audio/generate_audio.py` regenerates every OGG in `Assets/Audio` (fixed seed). Needs numpy, scipy, and ffmpeg with libvorbis. It doesn't touch `.meta` files.
- `python3 tools/audio/generate_wind.py` regenerates the Free Flight `SFX/broom_wind_loop.ogg` and `SFX/broom_turn_whoosh.ogg` (fixed seed).
- `python3 tools/audio/verify_audio.py` decodes each OGG and prints duration, size, DC, peak, RMS, edge values, and loop-seam jumps. It exits 1 if a peak is above -1.5 dBFS or a loop seam is larger than the clip's 99th-percentile sample step.
See the LOOPS section in `CREDITS.md` for which clips must loop.
