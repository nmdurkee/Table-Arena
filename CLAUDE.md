# Demo-Viewer

Unity-based 3D replay viewer for EchoVR / nEVR telemetry data. Plays back
`.echoreplay`, `.nevrcap`, and `.tape` capture files, with planned support
for live match streaming via the nEVR platform.

## Build & Run

**Unity version:** 6000.3.4f1 (Unity 6)

Open the Unity project at `Demo Viewer/` in Unity Hub, then use the editor
or the Python build script:

```bash
python3 build.py --platform=StandaloneLinux64 --output="./Build/Linux/"
python3 build.py --platform=StandaloneWindows64 --output="./Build/Windows/"
python3 build.py --platform=StandaloneOSX --output="./Build/macOS/"
```

The main scene is `Demo Viewer/Assets/Scenes/Game Scene.unity`.

## Architecture

```
Demo Viewer/              Unity project root
  Assets/
    Scripts/
      GameManager.cs      Singleton entry point, scene/mode management
      Game.cs             Core game-state logic
      DemoStart.cs        Bootstrap / startup
      ReplayLoader/
        Replay.cs         Loads and drives .echoreplay playback
      ButterReplays/      .echoreplay format (v1-v3 decompressors)
      NevrCap/            .nevrcap protobuf-based format (v1)
        NevrCapReader.cs           Reads .nevrcap container
        NevrCapFrameConverter.cs   Converts protobuf frames to viewer model
        Telemetry.cs               Generated C# from telemetry/v1/telemetry.proto
      Tape/               .tape protobuf-based format (v2, from echotools/tape)
        TapeReader.cs              Reads .tape v2 container (Envelope stream)
        TapeFrameConverter.cs      Converts v2 frames to viewer model
        Capture.cs                 Generated C# from telemetry/v2/capture.proto
        EchoArena.cs               Generated C# from telemetry/v2/echo_arena.proto
        SpatialTypes.cs            Generated C# from spatial/v1/types.proto
      EchoVRAPI/          Data model classes (Frame, Player, Team, Disc, etc.)
      Network/            VelNet multiplayer (shared viewing sessions)
      SimpleCameraController.cs   Desktop camera rig
      VRPlayer.cs                 VR camera rig
    Scenes/
      Game Scene.unity    Primary replay scene
      Greenscreen.unity   Chroma-key compositing scene
    Plugins/
      Google.Protobuf.dll   Protobuf runtime
      ZstdSharp.dll         Zstandard decompression
    Prefabs/              Player models, arena objects
    Models/               3D assets
    Arena_V3/, Arena V4/  Arena environment meshes

build.py                  CLI build script (wraps Unity batch mode)
libs/                     Native libraries (ncurses, tinfo for signal handling)
```

## Key Concepts

- **Frame pipeline:** Replay files are loaded into `EchoVRAPI.Frame` objects.
  The viewer interpolates between frames and drives player transforms, disc
  position, and game-clock state each Unity update.
- **Replay formats:** `.echoreplay` (JSON + zstd, handled by ButterReplays),
  `.nevrcap` (protobuf v1 + zstd, handled by NevrCap/), and `.tape`
  (protobuf v2 + zstd, handled by Tape/). The `.tape` format is produced
  by `echotools/tape` (tapedeck CLI) and uses the `telemetry.v2.Envelope`
  wire format with `spatial.v1` float32 types.
- **Protobuf integration:** `Telemetry.cs` and related types are generated
  from `telemetry/v1/*.proto` definitions. The runtime DLL lives in
  `Assets/Plugins/Google.Protobuf.dll`.
- **VR + Desktop:** The viewer runs in both flat-screen and VR modes.
  VR is activated via the `-useVR` launch flag.
- **Networking:** VelNet enables shared replay sessions where multiple
  users watch the same replay together.

## Conventions

- C# naming follows Unity defaults: `PascalCase` for types and public
  members, `camelCase` for local variables and private fields.
- One MonoBehaviour per file; filename matches class name.
- Singletons use `public static T instance` (assigned in `Awake`).
- Protobuf-generated code lives under `NevrCap/` and `Tape/` and must not
  be hand-edited; regenerate from `.proto` source instead.
- Indent with 4 spaces for C#; tabs appear in some legacy files.

## Dependencies

| Dependency | Purpose |
|---|---|
| Unity 6000.3.x | Editor and runtime |
| Google.Protobuf | Protobuf deserialization for .nevrcap and .tape |
| ZstdSharp | Zstandard decompression for .echoreplay, .nevrcap, .tape |
| Newtonsoft.Json (via UPM) | JSON parsing for .echoreplay frames |
| Animation Rigging | IK for player bone rendering |
| Cinemachine | Camera system |
| URP | Rendering pipeline |
| unityutilities | Shared utility package |
| VelNet | Multiplayer shared viewing |
