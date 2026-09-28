# Portaloo v1 runtime asset

These files are unchanged copies of the approved project-owned portaloo v1 handoff at
`C:\Users\99wil\Documents\ChatGPT\Festival Tycoon\portaloo-asset-v1`.
The frozen `manifest.json` SHA-256 is
`202f64bc62ec83f032d93ece9d8bec5a7730a69bcca877efe952d826faf8840b`.
The Blender source, dimension/collision checks and render review remain with that
handoff. The `.glb.import` and extracted embedded-palette PNG files are generated
by Godot 4.7.2, as with the other runtime environmental assets in this project.

The game uses the main cabin GLB's `DoorPivot` and built-in `IndicatorFree`, and
switches to the separate occupied GLB at `OccupancySocket`. Only one indicator is
visible at a time. There is no generated whole-cabin collision hull.
