Kanban Stockpile
===
RimWorld mod adding `Stack Refill Threshold` and `Similar Stack Limit` to
stockpiles and deep storage inspired by `Kanban Logistic Control` systems.

## Features
#### Stack Refill Threshold
* Just like setting "pause when satisfied" for a bill — but for hauling to stockpile stacks!
* Defaults to 100% which gives the same as behavior as vanilla.

*Example*: Set your RimFridge Important dining room stockpile to 20%
and it won't trigger hauling jobs for any specific stack until there
are less than or equal to 2 meals left in it (meal stack size is 10).

*Note*: This feature works in deep storage stockpiles, however colonists
will only refill partial stacks using an existing partial stack that can
fit. They aren't smart enough to break up a full stack into one small
enough to fit perfectly in deep storage.  *(they are smart enough to
do this for a regular vanilla stockpile however)*

#### Similar Stack Limit
* No more than `Similar Stack Limit` stacks of a thing are allowed in the stockpile.
* Defaults to `OFF` which gives the same behavior as vanilla.

*Example*: Set your Medicine Cabinent Preferred hospital stockpile to a
`Similar Stack Limit` of 1 and you will get no more than a single stack
of any medicine and drug type now instead of clogging it up nothing but
rotting herbal medicine and smoke leaf joints!

#### First Class Multiplayer Support
* Data stored using determenistic dictionary keys and all state mutations properly sync'd!
* All development done in a multiplayer context locally with arbiter to minimize potential desyncs.

## Full Example
Setup a raw ingredients food Preferred stockpile in the kitchen next to
your stove. Set the `Stack Refill Threshold` to 0% and the `Similar Stack
Limit` to 1. This way you will get a variety of fresh ingredients close
to the cook and will reduce spoilage because the stack is not refilled
until it has been completely used up.

## Performance
* Uses `for` loops similar to vanilla style code for basic `C#` optimization
* Skips hot code paths anytime a stockpile is set to default values
* Avoid using high values of `Similar Stack Limit` in large stockpiles as it must scan every thing in every cell

## Compatible Mods
* `LWM Deep Storage` above `KanbanStockpile` in mod load order required for `Stack Refill Threshold` in deep storage
* *(`Similar Stack Limit` works natively with deep storage like stockpiles even without `IHoldMultiplethings` component)*
* Please comment below with results if you test this mod with your own favorite storage mods, thanks!

## Credits
Original idea and inspiration came from my failed attempt to multiplayer patch Satisfied Storage.
* [SatisfiedStorage](https://steamcommunity.com/sharedfiles/filedetails/?id=2003354028) - hoop
* [Hauling Hysteresis](https://steamcommunity.com/sharedfiles/filedetails/?id=784324350) - Vendan
* [Rimworld Search Agency](https://steamcommunity.com/sharedfiles/filedetails/?id=726479594) - Killface

Inspiration for the `Similar Stack Limit` feature came directly from the great Variety Matters Stockpile.
* [VarietyMattersStockpile](https://steamcommunity.com/workshop/filedetails/?id=2266068546) - Cozar

Deep storage stockpiles implementing the `IHoldMultiplethings` component are a *must* in any modpack.
* [LWM's Deep Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=1617282896) - Little White Mouse
* [PickupAndHaul](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058) - Mehni

I got most of the GUI hooks and all the Transpiler stuff from a great and beautiful mod: Stockpile Ranking.
* [Stockpile Ranking](https://steamcommunity.com/sharedfiles/filedetails/?id=1558464886) - Uuugggg aka AlexTD

This mod relies heavily on Harmony for ease of patching.
* [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2040656402) - pardeike

I hang out occasionally with some great folks over at the [Multiplayer Mod Discord](https://discord.gg/JCmNG4j).
* [Multiplayer Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=1752864297) - https://discord.gg/JCmNG4j

## Mod Packs
Subscribe to and like my multiplayer compatible configs and modpack
* [Happy Accidents](https://steamcommunity.com/sharedfiles/filedetails/?id=2257918295)

If you play Minecraft check out my magic themed sky island modpack
* [Sky Magic Islands](https://www.curseforge.com/minecraft/modpacks/sky-magic-islands)

## References
* [steam workshop rimworld mod KanbanStockpile](TBD)
* [github.com/ubergarm/RimWorld-KanbanStockpile](https://github.com/ubergarm/RimWorld-KanbanStockpile)
* [github.com/ubergarm/monodevelop](https://github.com/ubergarm/monodevelop)
* [Kanban](https://en.wikipedia.org/wiki/Kanban)

## Keywords
```
#rimworld #rimworld 1.2 #rimworld mod #rimworld mods #rimworld mods 1.2
#kanban #kanbanstockpile #kanban stockpile #kanbanstockpiles #storage settings
#kanban stockpiles #kanbanstorage #kanban storage #SatisfiedStorage
#VarietyMattersStockpile #rimworld search agency #hauling hysteresis
#stockpile hyesteresis #rimworld stockpile #rimworld storage settings
#transport kanban system #kanban logistics control #stockpile dupe limit
```
