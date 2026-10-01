# Exosphere

Exosphere is a space-mission simulator. You fly a launch vehicle from the pad, through ascent, orbit, and reentry, in a double-precision model of the solar system. Free flight, the historical campaign, and the vehicle assembly building all use that same physics.

There is no installer yet. You play by opening the source in the Godot .NET editor.

## Screenshots

![Starship on the Starbase pad before launch](docs/screenshots/pad.png)

*Starship on the Starbase pad before launch.*

![Earth limb from low orbit](docs/screenshots/orbit.png)

*Earth limb from low orbit, with the flight HUD.*

![Cockpit view over the day side](docs/screenshots/cockpit.png)

*Cockpit view over the day side as entry interface approaches.*

![Starship at entry interface](docs/screenshots/entry.png)

*Starship at entry interface, about 67 km, nose toward Earth.*

## Play

| Need | Version |
|------|---------|
| Godot with .NET support | 4.6.3 |
| .NET SDK | 8 |

The standard Godot build, without .NET, cannot open this project.

1. Clone the repository.
2. Open this folder in Godot 4.6.3 .NET and run the project. The main scene is the flight operations menu.
3. Choose **Free Flight**, select a vehicle, review its configuration and launch. Use **Historical Campaign** for mission objectives or **Scenarios** for prepared flights.

On the pad, `L` starts the countdown, hold `Z` to throttle up, and `G` flies the ascent autopilot. `F5` quicksaves and `F9` loads. **Settings** switches the interface language. Use `Tab` and `Enter` to navigate menus; `Esc` closes a briefing or selector.

Lighting follows simulation time. Warp forward to see dawn and night. The full HUD shows solar elevation.

## Controls

### Flight

| Key | Action |
|-----|--------|
| `Z` / `X` | Throttle up / down (hold) |
| `W` `S` | Pitch |
| `A` `D` | Yaw |
| `Q` `E` | Roll |
| `T` | SAS |
| `Space` | Stage |
| `G` | Ascent autopilot |
| `H` | Gravity-turn assist |
| `L` | Countdown and launch |
| `.` `,` | Warp up / down |
| `F5` `F9` | Quicksave / quickload |
| `C` | Camera presets and cockpit |
| `V` | Vehicle assembly |
| Right-drag | Orbit camera |
| Wheel | Zoom |

Warp levels are `1`, `2`, `3`, `5`, `10`, `50`, `100`, `1000`, `10000`, and `100000`. The sim clamps warp during powered flight and inside the atmosphere.

### Map

| Key | Action |
|-----|--------|
| `M` | Toggle map |
| `1`–`6` | Target Mars, Moon, Venus, Jupiter, Mercury, Saturn |
| `Enter` | Create or apply the selected maneuver |
| `Tab` | Cycle map mode |
| `[` `]` | Adjust maneuver time |
| `Shift` | Larger maneuver step |
| `Alt` | Radial adjustment |
| `Delete` | Clear maneuver |

## What you can fly

- **Free Flight** offers ten launch vehicle presets: Starship Flight 7/12, Falcon 9 standard/extended fairings, New Glenn, Mercury-Redstone, Mercury-Atlas, Titan II, and Saturn V Apollo 8/11. Each opens at a compatible home pad with manual control.
- **Explore Flight 14** opens the featured mission briefing. The full Flight 14 simulation is in development; this entry does not launch another preset under its name.
- **Scenarios** includes Falcon 9 from Kennedy, New Glenn from Cape Canaveral, Starship Flight 7, Starship Flight 12, and a Starship entry from 70 km.
- **Historical Campaign** lists sixteen planned missions. Briefings show objectives, flight limits, progress and prerequisites. Playable definitions currently cover Freedom 7, Friendship 7, Gemini VIII, Apollo 8 and partial Apollo 11; the remaining entries are marked as planned.
- **Vehicle Assembly** builds a craft from the parts catalog and launches it to the pad.
- **Scenarios → Starship / 70 km entry interface** starts a belly-flop entry, with the flip and landing burn still left to fly.

Time warp, a navball, a cockpit, and an orbital map are available in flight. Transfer planning covers Hohmann planetary transfers and an Earth–Moon route.

## Develop

Menu capture and navigation checks: `python3 tools/menu_quick_check.py` (Godot .NET and Xvfb required). The checks use isolated user data and save real-framebuffer captures in `exports/menu-operations-review/`.

Build, architecture, data, and current limits: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

The physics model is [docs/physics/PHYSICS_MODEL.md](docs/physics/PHYSICS_MODEL.md). The product plan is [ROADMAP.md](ROADMAP.md).

Source code is [MIT](LICENSE). Fonts, terrain, and other third-party assets keep the licenses recorded in [data/licenses/assets_manifest.json](data/licenses/assets_manifest.json).

The exterior flight HUD uses a broadcast-style bottom band: independent Super Heavy
and Starship engine boards, the piloting navball, an observed-event timeline,
mission clock, surface speed, altitude, vertical speed, throttle, TWR and orbital
apsides. `[F3]` cycles Minimal → Full → Clean; Full adds diagnostics and Clean
retains the expanded attitude cluster. Cockpit and map views use their existing
instrument policies. Loaded flights without a known liftoff epoch display `SIM`
time instead of inventing `T+`.
