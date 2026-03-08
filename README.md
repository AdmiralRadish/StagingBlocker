# Staging Blocker

A Kerbal Space Program mod that prevents accidental staging by requiring a modifier key to be held while pressing spacebar.

## Features

- **Spacebar Protection**: Blocks spacebar staging unless a modifier key is held
- **Per-Vessel Configuration**: Set different modifier keys for different vessels
- **Multiplayer Support**: Full LunaMultiplayer compatibility with shared vessel state
- **Optional Toolbar Integration**: Works with both stock AppLauncher and ToolbarController
- **Manual Stage Trigger**: Big red button in the staging manager GUI for manual stage activation
- **Window Resizing**: Drag edges to resize the staging manager window
- **Fallback Hotkey**: All 4 arrow keys pressed together will toggle the window if toolbar fails

## Installation

1. Download the latest release
2. Extract the `StagingBlocker` folder to your `GameData` directory
3. Launch KSP

## Requirements

- Kerbal Space Program 1.8 or higher (tested up to 1.12.5)
- No hard dependencies (ToolbarController is optional)

## Usage

### Basic Controls
- **Modifier Key** (default: Tilde ~): Hold while pressing SPACE to stage
- **Change Modifier Key**: Click "Change" in the window to rebind
- **Toggle Blocking**: Use the "Modifier Key Required" button to enable/disable
- **Manual Staging**: Click the red "ACTIVATE STAGE" button in the window

### Toolbar
- Left-click the toolbar button to show/hide the staging manager window
- If the toolbar button becomes unresponsive, press all 4 arrow keys ↑↓←→ together

### Per-Vessel Settings
- Each vessel saves its own modifier key preference
- Settings persist across flights
- In LunaMultiplayer, per-vessel settings are shared across all players

## Features in Detail

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

### Toolbar Options
- **Stock AppLauncher**: Always available, works in Flight and Map View
- **ToolbarController**: Optional; if installed, adds button to custom toolbar
  - Persists across vessel switches
  - Survives scene reloads

## Configuration

The mod stores all settings in the flight scenario file:
- `showWindow`: Whether the staging manager is visible
- Per-vessel modifier keys
- Per-vessel staging block states

Settings are loaded automatically on flight start and saved when changed.

## Troubleshooting

### Toolbar Button Stops Working After Vessel Switch
- Press all 4 arrow keys (↑↓←→) together to toggle the window
- This is rare with the latest version, which uses static delegates

### Modifier Key Not Working
- Verify the modifier key is set correctly in the window
- Check that "Modifier Key Required" toggle is ENABLED (red button)
- Try changing to a different modifier key

### DLL Not Loading
- Ensure the DLL is in `GameData/StagingBlocker/Plugins/`
- Check the KSP debug log for errors
- Verify you're using a compatible KSP version

## Development

The source code is included for reference. Compiled from C# .NET Framework 4.7.2.

### Building
```
dotnet build StagingBlocker\StagingBlocker.csproj -c Release
```

The compiled DLL will be in `StagingBlocker\bin\Release\net472\StagingBlocker.dll`

## License

Licensed under the MIT License - see LICENSE file for details.

## Credits

- Uses reflection to detect and integrate with ToolbarController (optional)
- Full LunaMultiplayer compatibility through KSP scenario synchronization
- Inspired by the need to prevent accidental stagings in complex vehicles

## Changelog

See CHANGELOG.md for version history and updates.
