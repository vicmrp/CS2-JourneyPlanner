# Journey Planner for Cities: Skylines II

Journey Planner lets you inspect a citizen's current native journey or plan an A-to-B trip between buildings using Cities: Skylines II path data.

## Version 1.0

- Select citizens directly while JP is open.
- Existing citizen journeys are rendered automatically.
- Select one building for Start and another for Destination.
- A-to-B journeys calculate automatically.
- Displays walking and public-transport legs.
- Draws the route in the city.
- Click populated Start/Destination rows to open the normal CS2 info panel.
- Hide/show or delete a rendered route.
- Recalculate without clearing Start/Destination.
- JP button integrates into the top-right toolbar.
- ESC closes Journey Planner.

See `RELEASE.md` for publishing instructions.

## Performance and route colours

- Hover selection uses CS2's existing spatial search trees instead of copying every positioned object in the city each frame. Clicks always resolve a fresh selection.
- Route curves are cached once and drawn by a Burst job through CS2's native overlay renderer. No per-path GameObjects, LineRenderers, or Materials are created.
- Transit curves, stop markers, and journey cards use the player's saved line colour. Colour/name changes refresh once per second without recalculating the route.
- Transit recognition uses route components and prefab transport types, including both waypoints and segments, so custom line names do not affect classification.
- Walking distance uses the actual partial/reversed lane interval. Citizen progress no longer allocates replacement point arrays.

Close the game before running the normal `dotnet build -c Debug`: the modding toolchain deploys into the local Mods directory and cannot replace a loaded native DLL. Restart CS2 after deployment to load C# changes.

From the repository root, run `dotnet run --project tests/JourneyPlanner.GeometryTests.csproj -c Release` for the geometry regression checks. See `QOL_TESTING.md` for in-game checks.

## Reproducible release 1.2.0

Version 1.2.0 is performance optimized and UVM compatible. See RELEASE.md for the pinned SDK, Node/npm, game toolchain, Windows package outputs and independent verification workflow. The root uvm.json covers the managed DLL, native Burst library and UI assets. Signed results are public at https://vezit.net. Reproduced bytes do not establish safety.
