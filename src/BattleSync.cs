using System;
using System.Collections.Generic;
using KSL.API;

namespace TRC.TuneLoader {
	/// <summary>
	/// Carries battle messages between TRC Tune Loader users in the same room through KSL's mod channel
	/// (Kino.Sync, from the KSL.CarX extension), like TrcSync does for reports. It never touches CarX's own
	/// online systems. A message bigger than one packet (a course sent with a challenge) is split into pieces
	/// and put back together. Packets may arrive on another thread, so whole messages wait in a queue for the
	/// mod's Update.
	/// </summary>
	public sealed class BattleSync {
		private const int ChunkBytes = 3000;
		private const int MaxChunks = 256;
		private static readonly TimeSpan PartialTimeout = TimeSpan.FromSeconds(30);

		private sealed class Partial {
			public byte[][] Chunks;
			public int Received;
			public DateTime Started;
		}

		private readonly object lock_ = new object();
		private readonly Queue<BattleMessage> inbox_ = new Queue<BattleMessage>();
		private readonly Dictionary<string, Partial> partials_ = new Dictionary<string, Partial>(StringComparer.Ordinal);
		private int packet_ = -1;
		private uint nextSeq_;
		private ulong me_;

		public bool IsAvailable { get; private set; }
		public string Status { get; private set; } = "Not started.";

		public void Start(ulong me) {
			me_ = me;
			try {
				if (!Kino.Sync.IsAvailable) {
					Status = "Battles need KSL's KSL.CarX extension.";
					return;
				}
				if (!Kino.Sync.TryRegisterPacket("trc_battle", out packet_)) {
					Status = "KSL didn't accept TRC Tune Loader's battle messages.";
					return;
				}
				Kino.Sync.SetCallback(packet_, OnPacket);
				IsAvailable = true;
				Status = "Battles ready.";
			} catch (Exception e) {
				Status = $"Battles aren't available: {e.Message}";
				TrcLog.Error(e);
			}
		}

		public void Send(BattleMessage message) {
			if (!IsAvailable) {
				return;
			}
			try {
				byte[] data = message.Encode();
				uint seq = ++nextSeq_;
				int count = Math.Max(1, (data.Length + ChunkBytes - 1) / ChunkBytes);
				for (int i = 0; i < count; i++) {
					int offset = i * ChunkBytes;
					int length = Math.Min(ChunkBytes, data.Length - offset);
					var part = new byte[length];
					Array.Copy(data, offset, part, 0, length);

					IPacket packet = Kino.Sync.CreatePacket(packet_);
					packet.WriteByte(BattleMessage.Protocol);
					packet.WriteUInt64(message.From);
					packet.WriteUInt32(seq);
					packet.WriteInt32(i);
					packet.WriteInt32(count);
					packet.WriteInt32(length);
					packet.WriteBytes(part);
					Kino.Sync.SendToAll(packet);
				}
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't send a battle message: {e.Message}");
			}
		}

		/// <summary>The whole messages that arrived since the last call (on Unity's main thread).</summary>
		public List<BattleMessage> Drain() {
			lock (lock_) {
				var messages = new List<BattleMessage>(inbox_);
				inbox_.Clear();
				return messages;
			}
		}

		private void OnPacket(IPacket packet) {
			try {
				if (packet.ReadByte() != BattleMessage.Protocol) {
					return;
				}
				ulong from = packet.ReadUInt64();
				uint seq = packet.ReadUInt32();
				int index = packet.ReadInt32();
				int count = packet.ReadInt32();
				int length = packet.ReadInt32();
				if (from == me_ || count < 1 || count > MaxChunks || index < 0 || index >= count || length < 0 || length > ChunkBytes) {
					return;
				}
				byte[] part = packet.ReadBytes(length);

				byte[] whole = count == 1 ? part : Assemble(from, seq, index, count, part);
				if (whole == null) {
					return;
				}
				BattleMessage message = BattleMessage.Decode(whole);
				if (message != null && message.From == from) {
					lock (lock_) {
						inbox_.Enqueue(message);
					}
				}
			} catch (Exception e) {
				TrcLog.Warning($"Ignored a broken battle message: {e.Message}");
			}
		}

		/// <summary>Keeps a piece of a split message; returns the whole message once every piece is in.</summary>
		private byte[] Assemble(ulong from, uint seq, int index, int count, byte[] part) {
			lock (lock_) {
				DateTime now = DateTime.UtcNow;
				var stale = new List<string>();
				foreach (var pair in partials_) {
					if (now - pair.Value.Started > PartialTimeout) {
						stale.Add(pair.Key);
					}
				}
				stale.ForEach(k => partials_.Remove(k));

				string key = $"{from}:{seq}";
				if (!partials_.TryGetValue(key, out Partial partial)) {
					partials_[key] = partial = new Partial { Chunks = new byte[count][], Started = now };
				}
				if (partial.Chunks.Length != count || partial.Chunks[index] != null) {
					return null;
				}
				partial.Chunks[index] = part;
				partial.Received++;
				if (partial.Received < count) {
					return null;
				}

				partials_.Remove(key);
				int total = 0;
				foreach (byte[] chunk in partial.Chunks) {
					total += chunk.Length;
				}
				var whole = new byte[total];
				int at = 0;
				foreach (byte[] chunk in partial.Chunks) {
					Array.Copy(chunk, 0, whole, at, chunk.Length);
					at += chunk.Length;
				}
				return whole;
			}
		}
	}
}
