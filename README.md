# OpenSimGauge Derail Valley

A panel for [OpenSimGauge](https://github.com/imagitama/open-sim-gauge) for the game Derail Valley.

Depends on [this Derail Valley mod](https://github.com/imagitama/derail-valley-websocket).

Built for OpenSimGauge 0.0.8

## Usage

**Ensure you have installed the required Derail Valley mod.**

1. Extract ZIP somewhere
2. Copy `server/` into your OpenSimGauge server directory
3. Copy `client/` into your OpenSimGauge client directory
4. Update your server config to use `"DerailValley"` source
5. Update your client config to use the Derail Valley panels
6. Launch Derail Valley normally
7. Launch your OpenSimGauge server and client normally

## Development

Created with VSCode (with C# and C# Dev Kit extensions) and dotnet CLI.

1. Rename `Directory.Build.props.example` without `.example` and edit to make your paths work
2. Open `data-sources/DerailValley` in VSCode and make changes
3. Run `build.sh` or use dotnet CLI to build as a DL and place it into the appropriate spot
