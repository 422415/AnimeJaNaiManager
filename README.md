# AnimeJaNai Manager

Manager for [mpv-upscale-2x_animejanai](https://github.com/the-database/mpv-upscale-2x_animejanai): edit upscaling profiles and chains, and install or remove hardware-specific components (TensorRT runtime, per-GPU kernel packs, RIFE interpolation models) with recommendations based on the detected GPU.

Formerly AnimeJaNaiConfEditor; the executable is `AnimeJaNaiManager.exe` and ships at the install root next to `mpvnet.exe`.

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
