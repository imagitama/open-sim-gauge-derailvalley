# OpenSimGauge Derail Valley

A panel for [OpenSimGauge](https://github.com/imagitama/open-sim-gauge) for the game Derail Valley.

Depends on [this Derail Valley mod](https://github.com/imagitama/derail-valley-websocket).

Built for OpenSimGauge 0.0.13.

## Usage

**Ensure you have installed the required Derail Valley mod!**

1. Extract the ZIP contents somewhere
2. Drag `client` folder into your OpenSimGauge client folder
3. Drag `server` folder into your OpenSimGauge server folder
4. Launch the game normally
5. Launch OpenSimGauge normally

If it doesn't work ensure that your `server.json` has:

```json
{
  "source": "DerailValley"
}
```

and your client has the correct panels and gauges.

## Development

Created with VSCode (with C# and C# Dev Kit extensions) and dotnet CLI.

1. Ensure `server/data-sources/DerailValley/lib/OpenSimGaugeAbstractions.dll` exists and is up to date
2. Open `server/data-sources/DerailValley` in VSCode and make changes
3. Run `build.sh` (or the dotnet CLI commands manually) to build it for distribution 