# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A colour vector shooter in the style of Sega/Gremlin's 1981 arcade game Eliminator, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. One to four players, alone against the computer's drones, together or in
versus. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it is
original: the graphics are drawn in code, and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAEliminator.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAEliminator.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAEliminator          # play it; --no-audio for silence
ELIMINATOR_SHOTS=$PWD/Images dotnet test tests/AVAEliminator.Tests --filter ScreenshotTests   # refresh the README's pictures
ELIMINATOR_PROBE=1 dotnet test tests/AVAEliminator.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                    # rebuild src/AVAEliminator/Sounds
```

- Screenshots: `ELIMINATOR_SHOTS` saves the README's four; set `ELIMINATOR_ALL_SHOTS` as well to save every one.
- Probe: `ELIMINATOR_PROBE` takes `1`, `1:5` (start at wave 5), `4` (a team of four) or `2v` (two in versus).
  `ELIMINATOR_SEEDS=1,2,3` picks the seeds and `ELIMINATOR_TRACE` adds a trace.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAEliminator/Engine/`: the game, with no Avalonia drawing in it.
  - `Entities.cs` has `Controls` (turn, thrust, fire), the game modes, the `Field` (measures and the 1/60 s
    `Step`) and the ships, drones, eliminators and shots.
  - `Fortress.cs` is the base: its grid of walls, the winding channel and the core, kept in its own frame and
    turned out onto the screen. Shots and ships bounce off it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` plays a wave: ships, the base and its eliminators, drones, lives and scoring.
  - `Pilot.cs` is the computer player, one per ship, used by the attract mode and the tests.
  - `InputHandler.cs` splits the keyboard and up to four `ControllerManager` gamepads (SDL2) between players.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAEliminator/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs the
  60 Hz timer; `GameCanvas.Draw.cs` holds the drawing.
- `src/AVAEliminator/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAEliminator/Sounds/`: the generated WAV files.
- `tests/AVAEliminator.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `Harness.cs` sets up games;
  `FortressTests`, `GameplayTests`, `InputTests`, `ScreenshotTests`, `ShiftReleaseTests` and the opt-in
  `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the game in fixed
  `Field.Step` (1/60 s) steps, so tests can play waves deterministically from a seed. Draw random numbers from
  the engine's seeded `_rng` (`new Random(seed)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Controls are turn (-1 left, 1 right), thrust and fire; thrust on a stick
  is pushing it up (negative `LeftStickY`). With one player every key works for them; with two or more the
  keyboard is split (arrows for player 1, W A D for player 2). Test new controls through `InputHandler`
  (`InputTests`), not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. SDL has one
  event queue, so `ControllerManager.PollEvents()` drains it once a frame before each pad's `Update`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAEliminator`); tests
  load the table from a temporary folder (`Harness.TempFolder()`) and run with `Silent = true`. `Calm = true`
  keeps eliminators and drones out.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
