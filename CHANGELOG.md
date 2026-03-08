# Changelog
## [1.0.0] - 2026-03-08

### Added
- Initial release of Staging Blocker
- Spacebar staging protection with configurable modifier key
- Per-vessel modifier key configuration
- Per-vessel staging block toggle (enable/disable protection per vessel)
- Staging Manager GUI with manual stage trigger button
- Stock AppLauncher integration
- Window resizing and repositioning
- Full LunaMultiplayer support with shared vessel state
- Fallback hotkey (↑↓←→ together) to toggle window if toolbar fails
- Detailed logging for debugging

### Features
- **Safe Default**: Staging is blocked by default; toggle per-vessel
- **Multiplayer Ready**: LunaMultiplayer automatically detects and shares per-vessel state
- **Persistent Settings**: All settings saved to flight scenario, survive scene reloads
- **Error Resilient**: Comprehensive exception handling throughout
- **Vessel-Aware**: Different settings for each vessel with automatic load/save on switch
- **Keyboard Rebinding**: Change modifier key in-game via the GUI

## What's Next

### Potential Future Features
- Configurable UI position persistence
- Per-vessel stage sequence visualization
- Integration with staging-related mods
- Customizable button colors
- Localizations

## Known Issues

- None reported in initial release

## Support

For issues, questions, or suggestions:
- Check the README.md for usage and troubleshooting
- Review debug logs if encountering problems
