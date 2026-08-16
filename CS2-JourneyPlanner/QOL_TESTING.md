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
