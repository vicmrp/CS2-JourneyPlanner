# Journey Planner QoL v0.7.2 test

## Vanilla info-panel handoff

1. Open JP and select A/B normally. Vanilla info panels should remain suppressed.
2. Click a populated Start or Destination row.
3. The normal CS2 info panel should open and STAY open while JP remains visible.
4. JP temporarily yields world selection to vanilla while that panel is open.
5. Close the vanilla info panel.
6. JP should automatically resume its own map-selection mode.
7. Confirm clicking citizens/buildings again goes to JP instead of opening vanilla panels.

## Route action layout

Use a destination with a very long name.
- The journey name may wrap to multiple lines.
- Hide/Show route and Delete route must remain completely inside the JP panel.
- Buttons appear on their own action row.

## Delete route

- Delete route clears the rendered route and journey cards.
- Start and Destination remain.
- Recalculate journey rebuilds the route.

## Performance and native overlay regression

1. Load the same save and camera position for comparisons. Allow loading and texture processing to finish.
2. Compare JP closed, JP open while hovering dense streets, an A/B route visible, and that route hidden. A single FPS counter sample is not a benchmark.
3. Plan an A/B journey with a bus plus ferry transfer. Confirm it has continuous transit legs, rather than separate zero-stop rides and walking legs named after a transit tool.
4. Compare each transit leg with Better Transit View: map curves, stop circles, and the timeline should use the line's saved colour. Custom line names should appear; unnamed lines should show their mode and number.
5. Change a used line's colour/name and check that JP updates within about one second. Restore the test change before saving.
6. Check both close button and Escape; reopen JP and verify the selected endpoints remain. Test Hide/Show, Delete, and Recalculate several times.
7. Select a moving citizen. Check walking and vehicle progress, pause/unpause, and Re-follow selected citizen. The route must not regrow behind the citizen. The native citizen information panel should stay open during camera follow.
8. Inspect the game logs for JP exceptions. `CS2_JourneyPlanner.log` includes the cached curve/leg count and overlay preparation time for each calculation; this timing excludes native pathfinding and is not a frame-rate measurement.

## Automated geometry checks

Run from the repository root:

```powershell
dotnet run --project tests/JourneyPlanner.GeometryTests.csproj -c Release
```

The checks cover full and partial lanes, reverse traversal, positive distance in either direction, empty/invalid intervals, and preservation of curved geometry.
