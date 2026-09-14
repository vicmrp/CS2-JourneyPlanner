# Journey Planner 1.2.0 release

The existing Paradox mod is 155518. Publish future updates with the official ModPublisher NewVersion command, not Publish.

## Build and reproduce

From the repository root, install .NET SDK 10.0.302, the CS2 1.6.0f1 modding toolchain, Node 22.21.0 and npm 10.9.4. The committed npm lockfile is restored automatically. UVM records Game.dll's SHA-256 and the .NET SDK in the receipt.

```powershell
uvm build --package artifacts/package --out artifacts/release.json
```

Here uvm means the CLI from https://vezit.net. The Release recipe includes the managed DLL, Windows native Burst DLL, JavaScript, CSS and JavaScript license file. Release uses the official postprocessor without debug symbols. The package targets the Windows Paradox listing. Debug development builds still use the toolchain's normal native targets.

UI output follows the isolated deployment directory when UVM builds. The normal dotnet build command deploys locally; close the game first. Do not regenerate or hand-edit the published package after hashing it.

## Publication

1. Run the geometry checks and test this exact release in-game.
2. Commit and push all intended source, configuration, UI lockfile and recipe changes.
3. Use uvm publish into a new empty package folder to record signed publisher hashes.
4. Rebuild the exact commit in a separate clean checkout and use uvm verify and uvm attest to record your real result.
5. Upload the exact package with the official ModPublisher NewVersion command using Properties/PublishConfiguration.xml.
6. Close the game and rename a local Mods/CS2-JourneyPlanner directory to .CS2-JourneyPlanner before subscribing.
7. Subscribe to the new version, restart and scan under Options > Unified Verified Mods. Test Journey Planner against the downloaded copy.

The author can self-verify. Another verifier should use their own GitHub account, inspect the source and independently rebuild it. Reproduction is not a security audit.

## Release notes

Performance optimized and UVM compatible. Selection uses local spatial searches; cached curves are rendered by a Burst overlay job; citizen-progress updates avoid repeated allocations. Transit lines and journey cards use the player's selected transport colours. No numeric FPS improvement is claimed.
