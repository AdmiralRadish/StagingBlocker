# Staging Blocker

A Kerbal Space Program mod that prevents accidental staging by requiring a modifier key to be held while pressing spacebar. All 4 arrow keys pressed together will toggle the window.

## Features

- **Spacebar Protection**: Blocks spacebar staging unless a modifier key is held
- **Per-Vessel Configuration**: Set different modifier keys for different vessels
- **Multiplayer Support**: Full LunaMultiplayer compatibility with shared vessel state
- **Stock Toolbar Integration**: Uses the default KSP AppLauncher
- **Manual Stage Trigger**: Big red button in the staging manager GUI for manual stage activation
- **Window Resizing**: Drag edges to resize the staging manager window
- **Fallback Hotkey**: All 4 arrow keys pressed together will toggle the window if no toolbar

## Installation

1. Download the latest release
2. Extract the `StagingBlocker` folder to your `GameData` directory
3. Launch KSP

## Requirements

- Kerbal Space Program 1.12.5
- No hard dependencies

## Usage
### Basic Controls
- **Modifier Key** (default: Tilde ~): Hold while pressing SPACE to stage
- **Change Modifier Key**: Click "Change" in the window to rebind
- **Toggle Blocking**: Use the "Modifier Key Required" button to enable/disable
- **Manual Staging**: Click the red "ACTIVATE STAGE" button in the window
- **Toolbar Button**: Left-click to show/hide the staging manager window
- **Fallback Hotkey**: Press all 4 arrow keys ↑↓←→ together to toggle window

## Features
### Staging Manager Window
Shows the current and next stage to fire, with a large red button for manual activation. Includes:
- Current stage information
- Modifier key display and rebinding
- Per-vessel staging block toggle
- Window can be shown/hidden or closed with the X button

### LunaMultiplayer Support
When LunaMultiplayer is detected:
- Modifier key changes are shared with all connected players via scenario storage
- Each vessel's block state is synchronized across the server
- No additional configuration needed

## Configuration
The mod stores gameplay settings in the flight scenario file:
- Per-vessel modifier keys
- Per-vessel staging block states
- Per-vessel staging modes and timing values

Local window preferences are stored separately in `GameData/StagingBlocker/PluginData/StagingBlocker.xml`:
- `showWindow`: Whether the staging manager is visible
- Window position and size

Settings are loaded automatically on flight start and saved when changed.

## Development
The source code is included for reference. Compiled from C# .NET Framework 4.7.2.

### Building
dotnet build StagingBlocker\StagingBlocker.csproj -c Release


The compiled DLL will be in `StagingBlocker\bin\Release\net472\StagingBlocker.dll`

## License

Licensed under the MIT License - see LICENSE file for details.

## Credits

- Full LunaMultiplayer compatibility through KSP scenario synchronization
- Inspired by the need to prevent accidental stagings in complex vehicles
- Uses KerbalBuildTools 'https://www.nuget.org/packages/KSPBuildTools'
