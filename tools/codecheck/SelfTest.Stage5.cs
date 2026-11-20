using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Stage 5 (0.19): every message cut short at every length (N4), and the message seal (A2).</summary>
	internal static partial class SelfTest {
		/// <summary>
		/// Every message of every kind, cut short at every length: reading it never throws, and gives a message or nothing,
		/// never something broken. Messages only ever grow at the end, so a copy that has the start reads its own part.
		/// </summary>
		private static void CutShortTests() {
			int cuts = 0, threw = 0, whole = 0, kinds = 0;
			foreach ((string name, Func<byte[]> make) in WireMessages()) {
				Func<byte[], object> decode = name.StartsWith("battle", StringComparison.Ordinal) ? d => BattleMessage.Decode(d)
					: name.StartsWith("race", StringComparison.Ordinal) ? d => RaceMessage.Decode(d)
					: name.StartsWith("room", StringComparison.Ordinal) ? (Func<byte[], object>)(d => RoomMessage.Decode(d)) : null;
				if (decode == null) {
					continue;
				}
				kinds++;
				byte[] data = make();
				if (decode(data) != null) {
					whole++;
				}
				for (int length = 0; length < data.Length; length++) {
					cuts++;
					try {
						decode(data.Take(length).ToArray());
					} catch (Exception) {
						threw++;
					}
				}
			}
			Expect(threw == 0 && whole == kinds, $"messages cut short: {cuts} cuts of {kinds} kinds, none throws, every whole one reads ({threw} threw, {whole} read)");
			// The room status's parts: one cut inside the parts still reads the status itself.
			byte[] status = new RoomMessage { Kind = RoomKind.Status, Name = "Apok", Car = "AE86", Features = "battle:5-7 lanes:5", Traffic = true }.Encode();
			RoomMessage cut = RoomMessage.Decode(status.Take(status.Length - 4).ToArray());
			Expect(cut != null && cut.Car == "AE86", "a room status cut inside its parts still reads (the parts are extra)");
		}

		private static void MessageSealTests() {
			byte[] body = Encoding.UTF8.GetBytes("a battle piece");
			var seal = MessageSeal.Make("trc_battle", body);
			var guard = new MessageSeal.Guard();
			long now = DateTime.UtcNow.Ticks;
			Expect(MessageSeal.Check("trc_battle", body, seal.Ticks, seal.Counter, seal.Tag, 1, guard, now) == MessageSeal.Result.Good, "seal: a sealed packet checks out");
			Expect(MessageSeal.Check("trc_battle", body, seal.Ticks, seal.Counter, seal.Tag, 1, guard, now) == MessageSeal.Result.Replayed, "seal: the same packet again is a replay");
			byte[] changed = Encoding.UTF8.GetBytes("a battle piecf");
			Expect(MessageSeal.Check("trc_battle", changed, seal.Ticks, seal.Counter, seal.Tag, 2, guard, now) == MessageSeal.Result.Bad, "seal: a changed packet is refused");
			Expect(MessageSeal.Check("trc_race", body, seal.Ticks, seal.Counter, seal.Tag, 3, guard, now) == MessageSeal.Result.Bad, "seal: a packet moved to another channel is refused");
			Expect(MessageSeal.Check("trc_battle", body, seal.Ticks + 1, seal.Counter, seal.Tag, 4, guard, now) == MessageSeal.Result.Bad, "seal: a changed time is refused");
			var late = MessageSeal.Make("trc_battle", body);
			Expect(MessageSeal.Check("trc_battle", body, late.Ticks, late.Counter, late.Tag, 5, guard, now + TimeSpan.FromHours(2).Ticks) == MessageSeal.Result.Stale, "seal: one far from this PC's clock is refused");
			Expect(MessageSeal.Check("trc_battle", body, 0, 0, null, 6, guard, now) == MessageSeal.Result.Unsigned, "seal: one without a seal (before 0.19) is told apart");
			var next = MessageSeal.Make("trc_battle", body);
			Expect(next.Counter != seal.Counter && MessageSeal.Check("trc_battle", body, next.Ticks, next.Counter, next.Tag, 1, guard, now) == MessageSeal.Result.Good, "seal: the next packet from the same sender is fine");

			// Reading a seal off the end of a packet: there, or not.
			var bytes = new List<byte>(BitConverter.GetBytes(seal.Ticks));
			bytes.AddRange(BitConverter.GetBytes(seal.Counter));
			bytes.AddRange(seal.Tag);
			bytes.AddRange(MessageSeal.Magic);
			var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes.ToArray()));
			var read = MessageSeal.ReadSeal(reader.ReadInt64, reader.ReadUInt32, reader.ReadBytes);
			Expect(read.Tag != null && read.Counter == seal.Counter && read.Ticks == seal.Ticks, "seal: read back off the end of a packet");
			var empty = new System.IO.BinaryReader(new System.IO.MemoryStream(new byte[0]));
			Expect(MessageSeal.ReadSeal(empty.ReadInt64, empty.ReadUInt32, empty.ReadBytes).Tag == null, "seal: nothing on the end reads as no seal");

			MessageSeal.UseSecret(Encoding.UTF8.GetBytes(new string('z', 40)));
			Expect(MessageSeal.Check("trc_battle", body, next.Ticks, next.Counter + 1, next.Tag, 7, guard, now) == MessageSeal.Result.Bad, "seal: another key's seal is refused");
		}
	}
}
