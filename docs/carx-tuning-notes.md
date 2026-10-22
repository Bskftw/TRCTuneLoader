# CarX tuning: research notes (Oct 21 2026)

From decompiling CarX v2.28.3 `Assembly-CSharp.dll` with ilspycmd 9.1. The decompiled source lives outside the repo, in `..\decompiled\Assembly-CSharp` (never commit it). Re-run after a CarX update:

```
ilspycmd -p -o ..\decompiled\Assembly-CSharp -r "<CarX>\Drift Racing Online_Data\Managed" <copy of Assembly-CSharp.dll>
```

## What a tune is

- `ProfilesInfo.Profile` (MessagePack): `id` (setup slot id), `forceDefault`, `controllers: Dictionary<string, float>`, `values: Dictionary<string, float>`.
- `controllers` maps a controller name (`front_camber`, `rear_spring_stiffness`, `gearbox3`, ...) to a **slider position from 0 to 1**, not a real-world value.
- `values` holds real values for a few discrete settings the server can limit (`traction_control`, `drivetrain_type`, `front_tire_type`, `rear_tire_type`; see `NetworkController.Set*ToServerStateLimit`).
- Each car's slider ranges come from the game database: `PlayerCarProfile.dynoInfo.controllers` (`CarProfileController`: `name`, `changers`). Each `CarProfileChanger` has `parameterId`, `values` (min..max or a fixed list), `step`, `shift`, `defaultValue`. `EvaluateOriginalValue(phase)` turns a slider position into the real value; `EvaluatePhase(value)` goes back.
- `Db.Meta.CarProfile.CarProfileParameterType` lists every real parameter (FrontSpringWay ... RearTireStretch, Gear1-12, DiffType, Mass, ...).

## Setup slots

- `CarSpecsTableContext.ProfileName`: Stock, Turbo, Racing, Drift, Ultimate, UltimatePro. Each car has its own `PlayerCarProfile` id per slot (AE86: Ultimate 119, UltimatePro 839).
- Saved per car in the player prefs: `GamePrefs.carSettings` (`PrefsCarSlotsModel`): `GetProfileIdForCar(carId)`, `GetProfileInfo(profileId, carId, customSetupId)`, `SetProfileInfo(carId, profileId, profile)`, `GetInstalledCustomProfile(carId)`.

## Applying a tune

- `CarProfile.CarAdvancedProfilesExtensions.ApplyAdvancedProfile(this RaceCar car, BaseModel db, ProfilesInfo.Profile profile, PrefsCarSlotsModel carSettings, ...)` writes every controller onto `car.GetDesc().carXDesc` (frontSuspension, rearSuspension, frontTyre, rearTyre, engine, transmission, gearBox, brakes, weight) and calls `car.SetDesc(desc)`. The class is `internal`, so call it through reflection.
- The game's Setups screen (`UISetupsContext`) and dyno (`UIDynostandContext.HandleExit`) save with `SetProfileInfo`, then apply with `ApplyAdvancedProfile`.
- Online: after saving in a room, the dyno screen calls `NetGameDynoSystem.SEND_CarSettingsChanged()` and `NetworkGame.FillLocalPlayerData()`; other players then apply `PlayerProperties.Profile` to our car. The tune loader must not call these network functions itself.

## The engine record (important)

- For setups that `CanSwap()` (Racing, Ultimate, UltimatePro), CarX also keeps an `EngineSwapData` per engine in the slot data (`GetEngineSwapDataForCar(carId, engineId)`, id 0 = stock) holding the engine and gearbox controllers: `turbo_charged`, `turbo_pressure`, `rev_limiter`, `max_torque`, `max_torque_rpm`, `final_drive`, `gearbox*`.
- For the player's own car without a custom setup, `CarVisualManager.ApplyProfile` calls `ApplyAdvancedProfile(..., updateFromCarSpecsTable: true)`, so those values come **from the engine record**, not from `profile.controllers` (see `TryGetStockSavedPhase`). Network cars and custom setups use `profile.controllers`.
- The dyno saves the record on exit with `CustomSetupSaveHelper.InitHelper(...)` + `SaveSwapIfNeeded()` (`ConstructEngineSwapData` copies those keys from the profile). The tune loader does the same.

## How the tune loader applies and checks (CarXTuneAdapter)

- Apply (main garage screen only; refused in the dyno/Setups/car list, online (`NetworkController.IsActive`), with a custom setup installed or an engine swap):
  1. `profile = GetProfileInfo(slotId, carId).Copy()`, `profile.controllers = tune values`, `SetProfileInfo(carId, slotId, profile)`.
  2. If `slot.CanSwap()`: `CustomSetupSaveHelper.InitHelper(car, slot, profile, slotId, carId, false)` then `SaveSwapIfNeeded()`.
  3. `CarProfileCacheManager.ClearCarProfile(slotId)`, `Resolve<PlayerCarFactory>().visualManager.ApplyProfile(car, slotId, null, resetCarDesc: true, isLocalPlayerCar: true, forceProfileApply: true)`, `BasePrefsModel.SaveAllQueries(false)`.
- Read (what CarX applies): every controller of the slot's `dynoInfo`: the profile value, else the default phase for single-changer controllers (`EvaluatePhase(defaultValue)`, as `ApplyAdvancedProfile` does); then, with no custom setup, a `CanSwap` slot and the stock engine, engine keys from the engine record.
- Displayed numbers follow `DynoParamsItemViewModel.GetControllerOutputValue`: `EvaluateValue(phase) * multiplier` formatted with `parameter.format` plus `parameter.units`; `diff_type` as the `CarX.DiffType` name (CarX.Plugins.Core.dll), parameter id 63 as RWD/AWD/4WD, id 65 Standard/Sequential, groups and percent controllers as phase x 100 %, bool parameters Yes/No.
- Garage screens: `States.mainCurrent is GarageGUIState`; car = `FindAnyObjectByType<GarageCar3dViewContext>().currentCar`; open screens via `((BaseGameState)States.mainCurrent).contextOwner.Get<T>().isActive` for `UIMainMenuContext`, `UIGarageContext`, `UICarSelectorContext`, `UISetupsContext`, `UIDynostandContext`; dyno state is `DynoMenuState`.

## Loading into CarX's tuning screen (dyno), also in online rooms

- `UIDynostandContext` (found with `FindAnyObjectByType`, open when `isActive`) holds `profileInfoPreview` (what the sliders show), `profileInfoCached` (the saved tune), `carId`, `profileId`, `userCar`, `currentCategory`.
- The tune loader replaces `profileInfoPreview.controllers` with the tune's values, rebuilds the slider list with the dyno's private `FillParamsListsByCategory(currentCategory)` and `UpdateToeCamberTopAndBottomLists()` (by reflection; the items cache their value when built), then calls the public `ApplyChanges()`. That's the same state as moving every slider by hand.
- Nothing is saved by the mod. Leaving the tuning screen runs CarX's `HandleExit`: "save changes?" → `SetProfileInfo(preview)`, and in a room `NetGameDynoSystem.SEND_CarSettingsChanged()` + `FillLocalPlayerData()`; `ExitDyno` saves the engine record with `SaveSwapIfNeeded`. So other players get the new setup through CarX's own path.
- The live-check reference is recorded right after `ApplyChanges()` in the dyno.
- CarX's diff settings (`diff_type`, `diff_preload`, `diff_lock_power`, `diff_lock_coast`, `diff_lock_coef`) set the rear diff and copy the value to the front one (`ApplyAdvancedProfile`). On track, an RWD car's front `frontDiffPreload` came back at the stock value (100 instead of 500) after loading a V3.0 tune (clutch-type diff) in a room dyno; where it's reset wasn't found (not in `Car.cs` or `ApplyAdvancedProfile`). The live check skips the diff the drivetrain doesn't use (`transmission.gearType`: front diff on `GEAR_RWD`, rear diff on `GEAR_FWD`). AWD/4WD cars still check both; watch for the same reset there.

## Body kits and stance (widebody)

- `CarVisualManager.ApplyBodyParts` first calls `ApplyProfile(..., resetCarDesc: true)` (tune on the stock wheel base from the setup's asset), then runs every installed part's `Customization.CarPartCommand` (`CarPart.cmds`) on the `BaseCar.Desc` and calls `SetDesc`. Targets that move wheels: `baseY`, `frontBaseWidth`, `frontBaseZ`, `rearBaseWidth`, `rearBaseZ` (types Set/Add/Mul). Widebody kits use these; the tune isn't touched.
- So a bare `ApplyProfile` drops the kit's stance until the car is dressed again: the tune loader calls `GarageCar3dViewContext.ApplyDefaultCustomization()` after applying (what CarX's `GotoGarage` does).
- Stock stance = `CarProfilesAsset.LoadDB().Find(slot.asset)` (`StaticDictionariesHolder.AssetOnlyMode` is always true) plus the commands of the default parts (`CarPartDatabase.EquipDefaults(carId, set)` → `ConvertToPartList`). Live stance = `raceCar.GetDesc()`. Installed parts: `GamePrefs.carSettings.GetVisualForCar(carId).parts`; other players: `PlayerProperties.VisualSettings.parts` and their `NetworkPlayer.userCar` as built on this PC.

## Engine, gearbox and drivetrain

- `ApplyProfile` → `car.InitDesc(slot.asset)` (CarX's data: `CarProfilesAsset.Find(asset)`, copied into the car) → `ApplyAdvancedProfile`, which for a stock engine sets `engine.maxTorque/maxTorqueRPM/revLimiter/turboCharged/turboPressure`, `gearBox.finalDrive` and `gearBox.gears[n]` (Gear1-12) from the tune with `changer.EvaluateOriginalValue(value)`, and `transmission.gearType` from the DrivetrainType setting (`DrivetrainSwitchDynoParamViewModel.GetDrivetrainType`: 0 RWD, 1 AWD, 2 4WD; it also sets aero downforce). Engine inertia, engine braking, cut/idle RPM, torque curve, reverse gear, clutch torque and transmission loss stay at CarX's data.
- A CarX engine swap (`CarSwap`, `car.swap.engineId > 0`) sets the engine values and inertia/braking/cut/idle RPM from the swap engine (`CarSwap.SetupEngineParams`), and final drive/gears from the swap's saved controllers.
- KINO 3.39 (`kino/mods/kino.dll`, native) has a Swaps module: "engine swap" and "gearbox swap" per player, logged in `kino/output.log` (`[Swaps]: Added engine swap to '<player>' (<car>), engine: Stock`). It isn't readable from C#.
- `CarX.Car` (via `BaseCar.carX`) has getters that read the physics engine itself: `finaldrive`, `numGears`, `gearType`, `clutchMaxTorque`, `engineMaxTorque`, `engineRPMAtMaxTorque`, `engineRevLimiter`, `engineInertiaRatio`, `engineBrakeTorqueRatio`, `engineCutRPM`, `engineIdleRPM`, `GetGearRatio(n)`.

## Steam Workshop car mods

- CarX's Workshop "car" items (`SteamWorkshopCarManager`, also mod.io) are body parts for an existing car, read from the item's `car.xml` by `CarPartDatabase.ImportExternalData`. Each part has `external = true` and `packetid` = the Workshop item ID. They keep the base car's physics and tune; the only physical change they can make is through `CarPartCommand` (ride height `baseY`, track widths, axle positions), the same as a widebody kit. Kit wheels and textures are visual.
- So the stance check already catches a Workshop body that moves the wheels, on the player's own car and on any room car that CarX built with that body.
- In a room, `SmartfoxPlayerProperties.VisualSettings` carries Workshop parts as `networkparts` (Workshop ID → part IDs, `MutateToNetworkParts`). A player who hasn't downloaded that item, or has the `globals.workshopNetworkCars` option off, sees the car built with the stock body (`CarVisualManager`, `isNetworkCar && networkparts.Count > 0` → `EquipDefaults`), so the stance check sees stock. The Workshop IDs in `networkparts` (plus `parts` that are `external`, after `MutateToLocalParts`) still show that a Workshop body is used.
- CarX builds a car's stance as: base car data (`CarProfilesAsset` for the setup), then every installed part's commands in `ConvertToPartList` order (`CarVisualManager.ApplyBodyParts`). When this PC has the item (`CarPartDatabase.externalDatabases[id].nativeParts`), the tune loader does the same with the player's full part list to get their real stance. A Workshop title is only available for items this PC is subscribed to (`UGCServiceProvider.instance.GetManagerBy(Cars, provider).TryGetInstalledItem(id).title`); otherwise the part names are shown.

## Finding the player's car

- Services: `DI.DependencyInjector.Resolve<T>()` for `BaseModel`, `GamePrefs`, `PlayerCarFactory`.
- On track: `States.mainCurrent` is a `BaseRaceModeState` with `userCar`. Online: `SyncMultiplayer.NetworkController.InstanceGame.LocalPlayer.userCar`.
- Garage: `GarageCar3dViewContext.currentCar` (MonoBehaviour; `UIMainMenuContext.userCar` returns it).
- `RaceCar.metaInfo` (`PlayerCar`: `id`, `name`), `RaceCar.profileMetaInfo` (`PlayerCarProfile`: `id`, `name`), `RaceCar.isNetworkCar` (false for our own car).

## KINO `.knd` tune files

Little-endian binary, strings are .NET `BinaryWriter` style (7-bit length + UTF-8):

1. 4 bytes: format/version (seen `00 01 26 03`, `00 02 22 03`, `00 04 25 03`)
2. int32 car id (`PlayerCar.id`, AE86 = 3, 180SX = 19)
3. int32 setup slot id (`PlayerCarProfile.id`)
4. string slot name (`Ultimate`, `UltimatePro`)
5. int32 count, then `count` x (string controller name, float32 slider position): the `Profile.controllers` dictionary
6. Engine section: int32 engine id (0 = stock), int32 count, then (string, float32) pairs (`max_torque` ... `final_drive`, `gearbox1-6`): `EngineSwapData` controllers
7. A short tail (int32 -1 and zeros) not decoded yet

KINO saves tunes in `kino\mods\KN_Base\tunes\`. `KndFile` (in the mod) and `tools/tune_code.py` read this layout with the same rules.
