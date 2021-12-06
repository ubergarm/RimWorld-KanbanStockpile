## v1.0.8
- #16 Add NULL check for map in case thing is coming from inventory etc
- Recompile against `0Harmony.dll` sha1sum `54a9eba73d266f1f475a6ff017d2cabd21986120`
- Recompile against `0MultiplayerAPI.dll` sha1sum `a1b16bdf024407ee22cfc01978c931ff149db9a1`

## v1.0.7
- #13 Just add version tags for Rimworld 1.3 with no code changes prepping for updates.

## v1.0.6
- #12 Stack Refill Threshold works properly now even when not using Similar Stack Limit

## v1.0.5
- #9 Add setting to limit non-stackable items made from different materials
- #10 Patch HaulToStorageJob to properly break a stack when refilling anywhere
- #11 Add setting to patch PUAH to prevent redundant/overhauling
- Fix bug where newly constructed storage did not have correct default values - credits bananasss00
- Renamed some Settings and updated default values and tooltips for best user experience

## v1.0.4
- Remove dependency on IHoldMultipleThings.dll when detecting building/deep storage vs cell storage stockpiles

## v1.0.3
- Aggressive Similar Stockpile Limiting now defaults to *ENABLED* - turn it off if it hits your performance too hard
- Aggressive Similar Stockpile Limiting now scans all outstanding reservations to prevent hauling duplicate stacks

## v1.0.2
- Add settings and feature flags with default to disable aggressive checking similar stack limit

## v1.0.1
- #1 Prevent multiple pawns from simultenously trying to haul too many similar stacks to same stockpile.
- #2 Fixup GUI widgets when installed alongside Stockpile Ranking mod.
- #3 Do not check for deep storage items when LWM Deep Storage is not installed.

## v1.0.0
- initial release
