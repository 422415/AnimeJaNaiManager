# AnimeJaNai Manager

Manager for [mpv-upscale-2x_animejanai](https://github.com/the-database/mpv-upscale-2x_animejanai): edit upscaling profiles and chains, and install or remove hardware-specific components (TensorRT runtime, per-GPU kernel packs, RIFE interpolation models) with recommendations based on the detected GPU.

Formerly AnimeJaNaiConfEditor; the executable is `AnimeJaNaiManager.exe` and ships at the install root next to `mpvnet.exe`.

## Experimental GPU camera pan correction

**GPU Camera Pan Correction (Experimental)** is another option in **Profiles**.
It requires NVIDIA/TensorRT and the companion experimental player package.
Enable it, restart the player, and keep Windows at the refresh rate you want to
use (the initial target is 23.976 fps content on a 60 Hz display). It automatically
applies the required display-sync playback settings, without changing mpv.conf.
While enabled, **F8** toggles correction for comparison without changing your
saved preference. Custom F8 bindings take priority; the script registers no F8
binding when the experiment is disabled. This setting defaults to off and is
saved as `[global] camera_cadence=yes`; full-config export/import includes it.

Automatic display refresh is temporarily suspended while camera correction is
enabled. Its saved preference is retained and applies again after disabling
correction and restarting. The camera option is unavailable under DirectML so
the experiment cannot force D3D11 frames through Vulkan/CPU transfers.

## Experimental automatic display refresh

In **Profiles**, enable **Automatic Display Refresh Rate (Experimental)** and
restart the player. Windows playback measures the video's output cadence over
several seconds, matches sustained fixed-rate sections, and uses the highest
supported progressive refresh at the current resolution for variable sections.
The TV may briefly go black during a switch. The original refresh is restored
when playback ends or the player closes normally. This is off by default.

This setting requires the companion AJN player with `display-rate-match`
support and the updated `animejanai_backend.lua`. It is saved as
`[global] display_rate_match=yes` in `animejanai/animejanai.conf` and included
in full config exports. It does not change the video frame rate or enable RIFE.

![297521638-76a8db5b-8c67-4b0c-911a-9b02598fb37a](https://github.com/the-database/AnimeJaNaiConfEditor/assets/25811902/e3b5d268-794c-40d3-b0ae-2d781d98d6ae)
