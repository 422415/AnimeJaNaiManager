# AnimeJaNai Manager

Manager for [mpv-upscale-2x_animejanai](https://github.com/the-database/mpv-upscale-2x_animejanai): edit upscaling profiles and chains, and install or remove hardware-specific components (TensorRT runtime, per-GPU kernel packs, RIFE interpolation models) with recommendations based on the detected GPU.

Formerly AnimeJaNaiConfEditor; the executable is `AnimeJaNaiManager.exe` and ships at the install root next to `mpvnet.exe`.

## Experimental 47.952 Hz camera pan smoothing

**47.952 Hz Camera Pan Smoothing (Experimental)** is an option in **Profiles**.
It requires NVIDIA/TensorRT and the companion experimental player package.
Set Windows to **47.952 Hz**, enable it, and restart the player. The target is
23.976 fps anime on an 8K projector: camera positions update each refresh while
original source drawings retain their two-refresh cadence. Leave RIFE off.
It automatically
applies the required display-sync playback settings, without changing mpv.conf.
While enabled, **F8** toggles smoothing for comparison without changing your
saved preference. Custom F8 bindings take priority; the script registers no F8
binding when the experiment is disabled. This setting defaults to off and is
saved as `[global] camera_pan_smoothing=yes`; full-config export/import includes it.
The earlier `camera_cadence` setting does not enable this experiment.

Automatic display refresh is temporarily suspended while pan smoothing is
enabled. Its saved preference is retained and applies again after disabling
smoothing and restarting. The camera option is unavailable under DirectML so
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
