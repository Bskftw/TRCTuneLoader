// Checking cars against regulations: loading them, your car's facts and another player's, and the rule the event you're in is run to.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The Regulations tab (0.16, bskftw: for communities that don't run TRC tunes, like the Gunma Discord): make a
	/// regulation, check a car against it, share it, and pick it for a 1v1 battle, a race or a tournament. The rules
	/// and the checking are in src/anticheat/Regulation.cs; this file is the car's facts from CarX and the active rule.
	/// The pages are src/pages/RegulationsPage.cs, RegulationEditorPage.cs and RegulationLimitsEditor.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int RegulationsTab = 7;
		private const string RegulationsFolderName = "Regulations";

		private string RegulationsFolder => Path.Combine(modFolder_, RegulationsFolderName);
		private List<Regulation> regulations_ = new List<Regulation>();

		// The player's own car, checked at most every 2 s.
		private CarFacts myFacts_;
		private float myFactsUntil_;
		private List<(PlayerRow Player, RegulationResult Result)> roomChecks_ = new List<(PlayerRow, RegulationResult)>();
		private string roomCheckFor_ = "";
		private float roomCheckUntil_;

		private void LoadRegulations() {
			try {
				regulations_ = Regulation.LoadAll(RegulationsFolder);
				TrcLog.Info($"Regulations: {regulations_.Count} in {RegulationsFolder}.");
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private Regulation RegulationByCode(string code) => regulations_.FirstOrDefault(r => r.Code == code);

		// ---- The car's facts ----

		/// <summary>The player's own car as CarX built it: the setup, every tune number, power, weight, swaps and stance.</summary>
		private CarFacts MyCarFacts() {
			if (myFacts_ != null && Time.unscaledTime < myFactsUntil_) {
				return myFacts_;
			}
			myFactsUntil_ = Time.unscaledTime + 2f;
			var facts = new CarFacts();
			try {
				if (!RefreshCar()) {
					return myFacts_ = facts;
				}
				facts.CarId = car_.CarId;
				facts.CarName = car_.CarName;
				facts.Setup = car_.SlotName;
				facts.EngineSwap = !car_.StockEngine;
				if (adapter_.TryReadCurrentTune(car_, out Dictionary<string, float> tune, out _)) {
					if (adapter_.TryGetTuneNumbers(car_.SlotId, tune, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out _)) {
						facts.Numbers = numbers;
						facts.Decimals = decimals;
					}
					if (adapter_.TryCheckDrivetrain(car_, tune, withPhysics: false, out DrivetrainCheck drivetrain, out _) && drivetrain != null) {
						FillFrom(facts, drivetrain);
					}
				}
				if (adapter_.TryCheckStance(car_, out List<string> stance, out _)) {
					facts.Stance = stance;
				}
				facts.Specs = MySpecs();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			return myFacts_ = facts;
		}

		/// <summary>Another player's car, from what CarX shares and built here.</summary>
		private CarFacts FactsFor(RoomPlayer player) {
			var facts = new CarFacts { CarId = player.CarId, CarName = player.CarName, Setup = player.SlotName, EngineSwap = player.EngineId > 0 };
			try {
				if (adapter_.TryGetTuneNumbers(player.SlotId, player.SharedTune, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out _)) {
					facts.Numbers = numbers;
					facts.Decimals = decimals;
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			if (player.Drivetrain != null) {
				FillFrom(facts, player.Drivetrain);
				facts.Stance = player.StanceDifferences;
			} else if (player.StanceDifferences.Count > 0) {
				facts.Stance = player.StanceDifferences;
			}
			if (UnknownWorkshop(player) && player.StanceDifferences.Count == 0) {
				facts.Stance = null;
			}
			// Their spec, worked out here from their shared setup, for the specs this PC has.
			facts.Specs = PlayerSpecsOn(player);
			facts.KnownSpecs = specs_.Select(s => s.Title).ToList();
			return facts;
		}

		private static void FillFrom(CarFacts facts, DrivetrainCheck drivetrain) {
			facts.Hp = drivetrain.Hp;
			facts.Mass = drivetrain.Mass;
			facts.EngineSwap = drivetrain.EngineSwap;
			facts.DrivetrainChanged = drivetrain.StockDrivetrain.Length > 0;
			facts.OutsideChanges = drivetrain.Compared ? drivetrain.Differences : null;
		}

		/// <summary>The regulation the race or battle this player is in is run to, or null.</summary>
		private Regulation ActiveRegulation() {
			if (race_ != null && !race_.Ended && race_.Settings.Regulation != null) {
				return race_.Settings.Regulation;
			}
			return battle_ != null && battle_.Active ? battle_.Regulation : null;
		}

		/// <summary>Whether the player's own car meets the active regulation (false without one).</summary>
		private bool MyCarMeetsActiveRule() {
			Regulation rule = ActiveRegulation();
			if (rule == null) {
				return false;
			}
			myFactsUntil_ = 0;
			return rule.Check(MyCarFacts()).Passes;
		}
	}
}
