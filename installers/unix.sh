#!/bin/sh
# JitenMPV installer for Linux and macOS.
#
#   curl -fsSL https://raw.githubusercontent.com/Sirush/JitenMPV/master/installers/unix.sh | sh
#
# Environment overrides:
#   JITEN_MPV_VERSION         install this release instead of the latest (e.g. 0.2.0)
#   JITEN_MPV_MPV_CONFIG_DIR  mpv config directory, when detection picks the wrong one

set -eu

REPO="Sirush/JitenMPV"

die() {
    echo "error: $*" >&2
    exit 1
}

require() {
    command -v "$1" >/dev/null 2>&1 || die "$1 is required but not installed."
}

detect_rid() {
    os=$(uname -s)
    arch=$(uname -m)

    case "$os" in
        Linux)
            case "$arch" in
                x86_64 | amd64) echo "linux-x64" ;;
                # Releases carry no linux-arm64 asset; publishing one yourself is a one-line
                # change to the CI matrix.
                aarch64 | arm64) die "no linux-arm64 release is published; build from source with: dotnet publish src/JitenMPV.App -c Release -r linux-arm64" ;;
                *) die "unsupported architecture: $arch" ;;
            esac
            ;;
        Darwin)
            case "$arch" in
                x86_64) echo "osx-x64" ;;
                arm64) echo "osx-arm64" ;;
                *) die "unsupported architecture: $arch" ;;
            esac
            ;;
        *) die "unsupported operating system: $os" ;;
    esac
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

is_wayland_session() {
    [ "${JITEN_MPV_WINDOWING:-}" != "x11" ] &&
        { [ "${XDG_SESSION_TYPE:-}" = "wayland" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; }
}

has_libx11() {
    { ldconfig -p 2>/dev/null | grep -q 'libX11\.so\.6'; } ||
        [ -e /usr/lib/libX11.so.6 ] ||
        [ -e /usr/lib64/libX11.so.6 ] ||
        [ -e /usr/lib/x86_64-linux-gnu/libX11.so.6 ]
}

require curl
require tar
command -v sha256sum >/dev/null 2>&1 || require shasum

RID=$(detect_rid)
ASSET="jiten-mpv-$RID.tar.gz"

if [ -n "${JITEN_MPV_VERSION:-}" ]; then
    TAG="v${JITEN_MPV_VERSION#v}"
else
    # An unauthenticated API call can be rate-limited per IP; the release redirect below serves the
    # same file without it, so a failure here only costs the version in the message.
    TAG=$(curl -fsSL -H 'Accept: application/vnd.github+json' \
              "https://api.github.com/repos/$REPO/releases/latest" 2>/dev/null \
          | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n1) || TAG=""
fi

if [ -n "$TAG" ]; then
    BASE="https://github.com/$REPO/releases/download/$TAG"
    echo "Installing JitenMPV $TAG ($RID)"
else
    BASE="https://github.com/$REPO/releases/latest/download"
    echo "Installing the latest JitenMPV release ($RID)"
fi

TEMP=$(mktemp -d)
trap 'rm -rf "$TEMP"' EXIT INT TERM

echo "Downloading $ASSET ..."
curl -fL --progress-bar -o "$TEMP/$ASSET" "$BASE/$ASSET"
curl -fsSL -o "$TEMP/$ASSET.sha256" "$BASE/$ASSET.sha256"

EXPECTED=$(cut -d' ' -f1 < "$TEMP/$ASSET.sha256")
ACTUAL=$(sha256_of "$TEMP/$ASSET")
[ "$EXPECTED" = "$ACTUAL" ] || die "checksum mismatch for $ASSET: expected $EXPECTED, got $ACTUAL"

tar -C "$TEMP" -xzf "$TEMP/$ASSET"

# macOS archives name the binary JitenMPV since v0.1.5; older releases and Linux use JitenMPV.App.
BIN="JitenMPV"
[ -f "$TEMP/$BIN" ] || BIN="JitenMPV.App"
[ -f "$TEMP/$BIN" ] || die "$ASSET did not contain a JitenMPV executable"
chmod +x "$TEMP/$BIN"

# Gatekeeper on macOS 26 kills binaries whose ad-hoc signature was made on another machine
# ("Killed: 9"); re-signing with this Mac's own identity is what it accepts. codesign is part
# of the base system.
if [ "$(uname -s)" = "Darwin" ]; then
    codesign --force --sign - "$TEMP/$BIN" ||
        echo "warning: re-signing failed; macOS may refuse to run JitenMPV" >&2
fi

if [ -n "${JITEN_MPV_MPV_CONFIG_DIR:-}" ]; then
    "$TEMP/$BIN" install --mpv-config-dir "$JITEN_MPV_MPV_CONFIG_DIR"
else
    "$TEMP/$BIN" install
fi

if [ "$(uname -s)" = "Linux" ]; then
    if is_wayland_session; then
        echo "Wayland: native mode enabled; exact popup capabilities are probed at runtime."
        echo "If exact placement is unavailable, use mpv through X11/XWayland."
    elif ! has_libx11; then
        echo
        echo "note: libX11 was not found; install it so the dictionary popup can follow the cursor."
    fi
fi
