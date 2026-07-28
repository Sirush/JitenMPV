# JitenMPV installer for Windows.
#
#   irm https://raw.githubusercontent.com/Sirush/JitenMPV/master/installers/windows.ps1 | iex
#
# Environment overrides:
#   JITEN_MPV_VERSION         install this release instead of the latest (e.g. 0.2.0)
#   JITEN_MPV_MPV_CONFIG_DIR  mpv config directory, when detection picks the wrong one

# Runs inside a scope of its own: piped into iex this executes in the caller's live session, where
# leaking preference variables is rude and `exit` would close their window. Errors use throw.
& {
    $ErrorActionPreference = 'Stop'

    # Invoke-WebRequest redraws a progress bar per chunk on Windows PowerShell 5.1, which turns a
    # 46 MB download into a multi-minute one.
    $ProgressPreference = 'SilentlyContinue'

    # 5.1 still negotiates SSL3/TLS1.0 by default; github.com accepts neither.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $repo = 'Sirush/JitenMPV'
    # Windows on ARM runs the x64 build under emulation, so there is only ever one asset to pick.
    $asset = 'jiten-mpv-win-x64.zip'

    $tag = $null
    if ($env:JITEN_MPV_VERSION) {
        $tag = 'v' + $env:JITEN_MPV_VERSION.TrimStart('v')
    }
    else {
        try {
            $tag = (Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" `
                        -Headers @{ 'User-Agent' = 'jiten-mpv-installer' }).tag_name
        }
        catch {
            # An unauthenticated API call can be rate-limited per IP; the release redirect below
            # serves the same file without it.
            $tag = $null
        }
    }

    if ($tag) {
        $base = "https://github.com/$repo/releases/download/$tag"
        Write-Host "Installing JitenMPV $tag"
    }
    else {
        $base = "https://github.com/$repo/releases/latest/download"
        Write-Host 'Installing the latest JitenMPV release'
    }

    $temp = Join-Path ([IO.Path]::GetTempPath()) ("jiten-mpv-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null

    try {
        $archive = Join-Path $temp $asset
        $checksum = "$archive.sha256"

        Write-Host "Downloading $asset ..."
        Invoke-WebRequest "$base/$asset" -OutFile $archive
        Invoke-WebRequest "$base/$asset.sha256" -OutFile $checksum

        $expected = ((Get-Content $checksum -Raw).Trim() -split '\s+')[0]
        $actual = (Get-FileHash $archive -Algorithm SHA256).Hash
        if ($actual -ine $expected) {
            throw "Checksum mismatch for ${asset}: expected $expected, got $actual"
        }

        $unpacked = Join-Path $temp 'unpacked'
        Expand-Archive -Path $archive -DestinationPath $unpacked -Force

        $exe = Join-Path $unpacked 'JitenMPV.App.exe'
        if (-not (Test-Path $exe)) { throw "$asset did not contain JitenMPV.App.exe" }

        $installArgs = @('install')
        if ($env:JITEN_MPV_MPV_CONFIG_DIR) {
            $installArgs += @('--mpv-config-dir', $env:JITEN_MPV_MPV_CONFIG_DIR)
        }

        # JitenMPV.App is a GUI-subsystem executable: the shell does not wait for it and its console
        # output is only reachable through redirection.
        $stdout = Join-Path $temp 'install.out'
        $stderr = Join-Path $temp 'install.err'
        $process = Start-Process -FilePath $exe -ArgumentList $installArgs -Wait -PassThru `
            -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr

        Get-Content $stdout -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
        Get-Content $stderr -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }

        if ($process.ExitCode -ne 0) {
            throw 'Install failed. If mpv or JitenMPV is running, close it and run this again.'
        }
    }
    finally {
        Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
    }
}
