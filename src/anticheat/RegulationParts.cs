// What a regulation is made of and checks: a value's limit, a tyre bracket, a car's facts, a check's result.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>A tune value's allowed range, in the numbers CarX's tuning screen shows (mm, Nm, degrees...). Either end may be open.</summary>
	public sealed class ValueLimit {
		public string Key = "";
		public float? Min;
		public float? Max;
	}

	/// <summary>A weight class's widest tyres: cars up to <see cref="UpToKg"/> may run tyres up to <see cref="MaxWidth"/> mm.</summary>
	public sealed class TyreBracket {
		public float UpToKg;
		public float MaxWidth;
	}

	/// <summary>
	/// What a regulation is checked against: a car as CarX built it, the player's own or someone else's in the room.
	/// Anything the mod couldn't find out is left null (or 0), and shows as "couldn't check" instead of a broken rule.
	/// </summary>
	public sealed class CarFacts {
		public int CarId;
		public string CarName = "";

		/// <summary>CarX's name for the setup: Stock, Turbo, Racing, Drift, Ultimate or UltimatePro.</summary>
		public string Setup = "";

		/// <summary>Every tune setting in the numbers CarX's tuning screen shows, by setting name.</summary>
		public Dictionary<string, float> Numbers = new Dictionary<string, float>(StringComparer.Ordinal);

		/// <summary>How many decimals CarX shows each setting with (missing: not known).</summary>
		public Dictionary<string, int> Decimals = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Peak power (hp) and weight (kg) of the car as built; 0 when unknown.</summary>
		public float Hp;
		public float Mass;

		public bool? EngineSwap;

		/// <summary>CarX's drivetrain setting makes it drive other wheels than its own (RWD to AWD...).</summary>
		public bool? DrivetrainChanged;

		/// <summary>Engine, gearbox, suspension and weight values changed outside CarX's tuning (KINO), null when not compared.</summary>
		public List<string> OutsideChanges;

		/// <summary>How the body moves the wheels (widebody kits, Workshop bodies), null when unknown.</summary>
		public List<string> Stance;

		/// <summary>The player specs (their titles) whose tune is on the car, null when unknown.</summary>
		public List<string> Specs;

		/// <summary>The specs whoever is checking has (titles): a spec not among them can't be checked here. Null: all of them.</summary>
		public List<string> KnownSpecs;
	}

	/// <summary>What a car does against a regulation: the rules it breaks, and the ones that couldn't be checked.</summary>
	public sealed class RegulationResult {
		public List<string> Broken = new List<string>();
		public List<string> Unknown = new List<string>();
		public bool Passes => Broken.Count == 0;
	}
}
