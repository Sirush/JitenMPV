# JitenMPV

An mpv plugin that colours Japanese subtitles by how well you know each word, powered by [**Jiten**](https://jiten.moe).

Subtitles are parsed as they play, each word coloured by its state in your Jiten account. Hover a word for its dictionary entry, review it, or mine it to Jiten with a screenshot, audio clip and sentence.

![An anime frame in mpv with a Japanese subtitle whose words are coloured by knowledge state, and a dictionary popup for 魔女 showing furigana, pitch accent, frequency rank and review buttons](docs/images/main.jpg)

## Features

- Subtitle colouring by **word state**, with themes and per-state customization
- Dictionary popup on hover, with furigana, pitch accent and frequency
- **One-click mining** to a word list with the subtitle as example sentence
- Media mining: screenshot, audio clip, animated clip, sentence context (requires [Jiten+ subscription](https://jiten.moe/jiten-plus))
- **i+1 detection** and **frequency marking**
**Blur words** depending on their status to help you rely on subtitles less
- Tons of settings for a customised experience

## Requirements

- [mpv](https://mpv.io/)
- A [Jiten](https://jiten.moe) account and API key
- ffmpeg, for audio and clip mining. JitenMPV can download it for you.
- Windows, Linux or macOS. On Linux the dictionary popup needs an X11 session; under Wayland use XWayland.

## Installation

### Windows

Download **`JitenMPV-Setup.exe`** from the [releases page](https://github.com/Sirush/JitenMPV/releases) and run it. Then start mpv and press `Ctrl+J`.

It installs per-user, needs no administrator rights, and puts nothing system-wide. It is not code-signed, so Windows will warn that it does not recognise the publisher: choose **More info**, then **Run anyway**.

If you would rather not see that warning, or prefer a scriptable install, this one-liner does the same thing:

```powershell
irm https://raw.githubusercontent.com/Sirush/JitenMPV/master/installers/windows.ps1 | iex
```

### Linux and macOS

```sh
curl -fsSL https://raw.githubusercontent.com/Sirush/JitenMPV/master/installers/unix.sh | sh
```

Then start mpv and press `Ctrl+J`. Nothing needs administrator rights and nothing is installed system-wide.

The install scripts download the latest release, check it against its published SHA-256, and install it. Two environment variables adjust what they do:

- `JITEN_MPV_VERSION=0.2.0` installs that release instead of the latest.
- `JITEN_MPV_MPV_CONFIG_DIR=/path/to/mpv` overrides the mpv config directory. The installer prints the one it picked, so set this if that is not where your mpv reads its config from.

### Installing manually

Every release also carries a plain archive — `jiten-mpv-win-x64.zip` / `jiten-mpv-linux-x64.tar.gz` / `jiten-mpv-osx-*.tar.gz` — holding one self-contained executable, with no runtime to install. It is the same build the setup program and the scripts fetch.

Download the archive for your platform from the [releases page](https://github.com/Sirush/JitenMPV/releases), extract it, and run `JitenMPV.App` with no arguments. It offers to install itself, showing the directory it will write the mpv script to.

Each archive also contains `jiten-mpv.lua`. You never need it — the installer writes its own copy — but it is there if you would rather drop the script into `portable_config\scripts` yourself and keep everything under your own control.

For scripted installs, `JitenMPV.App install` does the same without a window (`--mpv-config-dir <path>`, `--dry-run`, `--quiet`). On Windows the prompt returns before it finishes, since the executable has no console of its own.

On macOS, download with `curl` rather than a browser. Browser downloads are quarantined and macOS 15 removed the right-click-Open bypass; if you already have a quarantined copy, `xattr -d com.apple.quarantine JitenMPV.App` clears it.

### Where things go

| | Windows | Linux and macOS |
|---|---|---|
| Program | `%APPDATA%\jiten-mpv\` | `~/.local/share/jiten-mpv/` (`$XDG_DATA_HOME`) |
| Settings | `%APPDATA%\jiten-mpv\` | `~/.config/jiten-mpv/` (`$XDG_CONFIG_HOME`) |
| mpv script | `%APPDATA%\mpv\scripts\`, or `portable_config\scripts\` beside `mpv.exe` | `~/.config/mpv/scripts/` |

Set `JITEN_MPV_EXE` if you keep the executable somewhere else.

### Updating and uninstalling

JitenMPV checks for new releases once a day and tells you in mpv and in the settings window. Nothing is downloaded until you press Install update. Turn the check off under Settings > Advanced.

Re-running the install command above also updates, and works when the in-app updater cannot.

`JitenMPV.App uninstall` removes the mpv script; add `--all` to delete the program as well. Settings are always kept.

### Building from source

```sh
dotnet publish src/JitenMPV.App -c Release -r win-x64 -o publish
```

Use `linux-x64`, `osx-x64` or `osx-arm64` in place of `win-x64`. This produces one self-contained executable in `publish/`; run `JitenMPV.App install` from there to place it and the mpv script.

## Setup

Press `Ctrl+J` during playback to open the settings window, and paste your API key from the bottom of the [Jiten settings page](https://jiten.moe/settings).

If ffmpeg is missing, the same screen offers a one-click download. Without it, subtitle coloring and screenshots still work, but audio and clip mining do not.

## Usage

- `Ctrl+J` opens settings
- `F10` starts JitenMPV, or restarts it if it stopped
- Hover a word for its dictionary entry, click to interact

JitenMPV starts automatically when a file is opened. Turn that off under Settings > Advanced if you would rather start it with `F10` only.

## Development

`test-mpv.bat <video>` builds the solution and launches mpv with the script and binary from the working tree.

## Contributing

Issues and ideas are welcome. There is also a [Discord server](https://discord.gg/cZWM7b4wzk).

## License

[Apache 2.0](LICENSE).

JitenMPV does not bundle ffmpeg and is not a derivative of it. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
