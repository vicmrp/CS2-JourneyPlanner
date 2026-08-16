# Journey Planner 1.0 release

## Before publishing

1. Test this exact Release build in-game.
2. Confirm JP opens/closes from the top-right button and ESC.
3. Confirm citizen selection automatically renders an existing native journey.
4. Confirm building A -> building B calculates automatically.
5. Confirm normal CS2 info panels open from the populated Start/Destination rows.
6. Confirm Hide route, Show route, Delete route and Recalculate journey all work.
7. Check Player.log for repeated Journey Planner exceptions.

## Build

```powershell
dotnet clean .\CS2-JourneyPlanner.csproj
dotnet build .\CS2-JourneyPlanner.csproj -c Release
```

## Publish as a NEW mod

`Properties\PublishConfiguration.xml` intentionally has an empty ModId.

Use the **PublishNewMod** profile:

```powershell
dotnet publish .\CS2-JourneyPlanner.csproj -p:PublishProfile=Properties\PublishProfiles\PublishNewMod.pubxml
```

Or in Visual Studio:

1. Right-click **CS2-JourneyPlanner**
2. Choose **Publish**
3. Select **PublishNewMod**
4. Publish
5. Sign in to Paradox Mods if requested

After the first successful publish, Paradox assigns a ModId.

## IMPORTANT after the first publish

Put the assigned ID into:

```xml
<ModId Value="YOUR_NEW_MOD_ID" />
```

Then future releases must use **PublishNewVersion**, not PublishNewMod.

## Included publishing assets

- `Properties\Thumbnail.png`
- `Properties\Screenshot1.png`
- `Properties\Screenshot2.png`
- `Properties\Screenshot3.png`
- `Properties\PromotionalGuide.png` (optional; not referenced by PublishConfiguration.xml)
