# Third-party notices

JitenMPV itself is licensed under the [Apache License 2.0](LICENSE). This file covers the third-party software it uses but does not include.

## ffmpeg

JitenMPV uses **ffmpeg** for audio and clip mining, for burning subtitles into screenshots, and for extracting embedded subtitle tracks.

**JitenMPV does not include, bundle, or distribute ffmpeg.** No release asset contains an ffmpeg binary. ffmpeg reaches your machine only in one of these ways:

- it was already installed on your system, by you or by your distribution; or
- you pressed **Download ffmpeg** in JitenMPV's settings, which downloads a prebuilt binary directly from the [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) GitHub release to a folder JitenMPV owns.

In the second case the transfer is between your machine and GitHub. JitenMPV records the exact asset URL and build provenance in `SOURCE.txt` next to the binary it installs.

**JitenMPV is not a derivative work of ffmpeg.** It never links against libavcodec, libavformat or any other ffmpeg library. It launches `ffmpeg` as a separate process and communicates with it over the command line and pipes, at arm's length. JitenMPV's Apache 2.0 license is therefore unaffected by ffmpeg's.

**The builds JitenMPV downloads are LGPL builds.** JitenMPV deliberately fetches BtbN's `-lgpl` variants rather than the GPL ones. Nothing JitenMPV asks ffmpeg to do requires a GPL-only component: mining uses `libwebp` / `libwebp_anim` (BSD), `libopus` (BSD) and the native `png` encoder, none of which are GPL-encumbered. Verified against the installed build on 2026-07-27: `libwebp_anim`, `libwebp`, `libopus` and `png` encoders and the `ogg`, `webp` and `srt` muxers are all present, while `libx264` and `libx265` are absent, as expected in an LGPL build.

- ffmpeg homepage and license terms: <https://ffmpeg.org/legal.html>
- ffmpeg source: <https://ffmpeg.org/download.html>
- Build scripts used for the downloadable binaries: <https://github.com/BtbN/FFmpeg-Builds>

ffmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.

### If ffmpeg is ever bundled

Bundling an ffmpeg binary in a JitenMPV release would make this project a distributor of that binary, and LGPL section 4 obligations would attach: shipping the LGPL text, providing or offering the corresponding source for that exact build, and stating any modifications. That is the principal reason JitenMPV downloads on demand instead.

## mpv

JitenMPV is a plugin for [mpv](https://mpv.io/) and ships a Lua script that mpv loads. It is distributed alongside mpv, not as part of it, and does not incorporate mpv source. mpv is licensed GPL-2.0-or-later (with LGPL-2.1-or-later parts).
