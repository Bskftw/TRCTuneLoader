using System;
using System.Collections.Generic;
using KSL.API;

namespace TRC.TuneLoader {
	/// <summary>
	/// Carries battle messages between TRC Tune Loader users in the same room through KSL's mod channel
	/// (Kino.Sync, from the KSL.CarX extension), like TrcSync does for reports. It never touches CarX's own
	/// online systems. A message bigger than one packet (a course sent with a challenge) is split into pieces
	/// and put back together (BattleChunks); its pieces go out a few at a time (<see cref="Pump"/>) rather than
	/// all in one frame. Packets may arrive on another thread, so whole messages wait in a queue for the mod's
	/// Update.
	/// </summary>
	public sealed class BattleSync {
		private readonly object lock_ = new object();
		private readonly Queue<BattleMessage> inbox_ = new Queue<BattleMessage>();
		private readonly BattleChunks chunks_ = new BattleChunks();

		/// <summary>Pieces of big messages waiting to go, and how fast they go.</summary>
		private readonly Queue<IPacket> outgoing_ = new Queue<IPacket>();
		private const double PiecesPerSecond = 25;
		private const int MaxQueued = 400;
		private double pieceBudget_;
		private double lastPump_ = -1;
		private int packet_ = -1;
		private ulong me_;

		public bool IsAvailable { get; private set; }
		public string Status { get; private set; } = "Not started.";

		/// <summary>Packets KSL took and refused, and whole messages that came in: for the log.</summary>
		public int Sent { get; private set; }
		public int Refused { get; private set; }
		public int Received { get; private set; }

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

		/// <summary>
		/// Sends a message to the room: one that fits in a packet right away (0 when KSL refused it), a bigger one
		/// through <see cref="Pump"/>. Returns how many packets it takes.
		/// </summary>
		public int Send(BattleMessage message) {
			if (!IsAvailable) {
				return 0;
			}
			try {
				List<BattleChunks.Piece> pieces = BattleChunks.Split(message.Encode());
				if (pieces.Count == 1) {
					return SendNow(Packet(message.From, pieces[0])) ? 1 : 0;
				}
				if (outgoing_.Count + pieces.Count > MaxQueued) {
					TrcLog.Warning($"Too many battle packets waiting ({outgoing_.Count}): a {pieces.Count}-packet message wasn't sent.");
					return 0;
				}
				foreach (BattleChunks.Piece piece in pieces) {
					outgoing_.Enqueue(Packet(message.From, piece));
				}
				return pieces.Count;
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't send a battle message: {e.Message}");
				return 0;
			}
		}

		/// <summary>Called every frame: sends waiting pieces, <see cref="PiecesPerSecond"/> at most.</summary>
		public void Pump(double now) {
			if (outgoing_.Count == 0) {
				lastPump_ = now;
				pieceBudget_ = 1;
				return;
			}
			pieceBudget_ = Math.Min(3, pieceBudget_ + (lastPump_ < 0 ? 1 : (now - lastPump_) * PiecesPerSecond));
			lastPump_ = now;
			while (pieceBudget_ >= 1 && outgoing_.Count > 0) {
				pieceBudget_--;
				try {
					SendNow(outgoing_.Dequeue());
				} catch (Exception e) {
					TrcLog.Warning($"Couldn't send a battle packet: {e.Message}");
				}
			}
		}

		private IPacket Packet(ulong from, BattleChunks.Piece piece) {
			IPacket packet = Kino.Sync.CreatePacket(packet_);
			packet.WriteByte(BattleMessage.Protocol);
			packet.WriteUInt64(from);
			packet.WriteUInt32(piece.MessageId);
			packet.WriteInt32(piece.Index);
			packet.WriteInt32(piece.Count);
			packet.WriteInt32(piece.Data.Length);
			packet.WriteBytes(piece.Data);
			return packet;
		}

		private bool SendNow(IPacket packet) {
			if (Kino.Sync.SendToAll(packet)) {
				Sent++;
				return true;
			}
			Refused++;
			if (Refused == 1 || Refused % 100 == 0) {
				TrcLog.Warning($"KSL didn't send a battle packet ({Refused} so far, {Sent} sent). Is the room's connection all right?");
			}
			return false;
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
				var piece = new BattleChunks.Piece {
					MessageId = packet.ReadUInt32(),
					Index = packet.ReadInt32(),
					Count = packet.ReadInt32(),
				};
				int length = packet.ReadInt32();
				if (from == me_ || !BattleChunks.Valid(piece.Index, piece.Count, length)) {
					return;
				}
				piece.Data = packet.ReadBytes(length);

				lock (lock_) {
					byte[] whole = chunks_.Add(from, piece, DateTime.UtcNow);
					if (whole == null) {
						return;
					}
					BattleMessage message = BattleMessage.Decode(whole);
					if (message != null && message.From == from) {
						Received++;
						inbox_.Enqueue(message);
					}
				}
			} catch (Exception e) {
				TrcLog.Warning($"Ignored a broken battle message: {e.Message}");
			}
		}
	}
}
