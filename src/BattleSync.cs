using System;
using System.Collections.Generic;
using System.Linq;
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
	public sealed class BattleSync : SyncChannel<BattleMessage> {
		public BattleSync() : base("trc_battle", "Battles", BattleMessage.Protocol, m => m.Encode(), BattleMessage.Decode, m => m.From) {
		}
	}

	/// <summary>Carries race messages (open races and tournaments) the same way, on their own KSL packet.</summary>
	public sealed class RaceSync : SyncChannel<RaceMessage> {
		public RaceSync() : base("trc_race", "Races", RaceMessage.Protocol, m => m.Encode(), RaceMessage.Decode, m => m.From) {
		}
	}

	/// <summary>Carries the room board (who's doing what) and shared courses, on their own KSL packet.</summary>
	public sealed class RoomSync : SyncChannel<RoomMessage> {
		public RoomSync() : base("trc_room", "Room messages", RoomMessage.Protocol, m => m.Encode(), RoomMessage.Decode, m => m.From) {
		}
	}

	/// <summary>A KSL packet that carries one kind of message: split into pieces, paced, put back together, queued for Update.</summary>
	public class SyncChannel<T> where T : class {
		private readonly string packetName_;
		private readonly string what_;
		private readonly string one_;
		private readonly byte protocol_;
		private readonly Func<T, byte[]> encode_;
		private readonly Func<byte[], T> decode_;
		private readonly Func<T, ulong> from_;

		protected SyncChannel(string packetName, string what, byte protocol, Func<T, byte[]> encode, Func<byte[], T> decode, Func<T, ulong> from) {
			packetName_ = packetName;
			what_ = what;
			one_ = what.TrimEnd('s').ToLowerInvariant();
			protocol_ = protocol;
			encode_ = encode;
			decode_ = decode;
			from_ = from;
		}

		private readonly object lock_ = new object();
		private readonly Queue<T> inbox_ = new Queue<T>();
		private readonly BattleChunks chunks_ = new BattleChunks();

		/// <summary>
		/// Pieces of big messages waiting to go (with their message's ID), and how fast they go. Tested Oct 29: a
		/// 35-piece challenge sent at 25 pieces a second almost never got there whole, so they go slower, in a new
		/// random order each time the message is sent again (so the same pieces aren't lost every time), and a
		/// message still on its way isn't queued a second time.
		/// </summary>
		private readonly Queue<(uint Id, IPacket Packet)> outgoing_ = new Queue<(uint, IPacket)>();
		private readonly Dictionary<uint, int> queuedPieces_ = new Dictionary<uint, int>();
		private readonly System.Random order_ = new System.Random();
		private const double PiecesPerSecond = 10;
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
					Status = $"{what_} need KSL's KSL.CarX extension.";
					return;
				}
				if (!Kino.Sync.TryRegisterPacket(packetName_, out packet_)) {
					Status = $"KSL didn't accept TRC Tune Loader's {one_} messages.";
					return;
				}
				Kino.Sync.SetCallback(packet_, OnPacket);
				IsAvailable = true;
				Status = $"{what_} ready.";
			} catch (Exception e) {
				Status = $"{what_} aren't available: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// Sends a message to the room: one that fits in a packet right away (0 when KSL refused it), a bigger one
		/// through <see cref="Pump"/>. Returns how many packets it takes.
		/// </summary>
		public int Send(T message) {
			if (!IsAvailable) {
				return 0;
			}
			try {
				ulong from = from_(message);
				List<BattleChunks.Piece> pieces = BattleChunks.Split(encode_(message));
				if (pieces.Count == 1) {
					return SendNow(Packet(from, pieces[0])) ? 1 : 0;
				}
				uint id = pieces[0].MessageId;
				if (queuedPieces_.ContainsKey(id)) {
					// The last copy is still going out: its pieces are as good as this one's.
					return pieces.Count;
				}
				if (outgoing_.Count + pieces.Count > MaxQueued) {
					TrcLog.Warning($"Too many {one_} packets waiting ({outgoing_.Count}): a {pieces.Count}-packet message wasn't sent.");
					return 0;
				}
				foreach (BattleChunks.Piece piece in pieces.OrderBy(_ => order_.Next())) {
					outgoing_.Enqueue((id, Packet(from, piece)));
				}
				queuedPieces_[id] = pieces.Count;
				return pieces.Count;
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't send a {one_} message: {e.Message}");
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
					var (id, packet) = outgoing_.Dequeue();
					if (queuedPieces_.TryGetValue(id, out int left)) {
						if (left <= 1) {
							queuedPieces_.Remove(id);
						} else {
							queuedPieces_[id] = left - 1;
						}
					}
					SendNow(packet);
				} catch (Exception e) {
					TrcLog.Warning($"Couldn't send a {one_} packet: {e.Message}");
				}
			}
		}

		private IPacket Packet(ulong from, BattleChunks.Piece piece) {
			IPacket packet = Kino.Sync.CreatePacket(packet_);
			packet.WriteByte(protocol_);
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
				TrcLog.Warning($"KSL didn't send a {one_} packet ({Refused} so far, {Sent} sent). Is the room's connection all right?");
			}
			return false;
		}

		/// <summary>The whole messages that arrived since the last call (on Unity's main thread).</summary>
		public List<T> Drain() {
			lock (lock_) {
				var messages = new List<T>(inbox_);
				inbox_.Clear();
				return messages;
			}
		}

		private void OnPacket(IPacket packet) {
			try {
				if (packet.ReadByte() != protocol_) {
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
					T message = decode_(whole);
					if (message != null && from_(message) == from) {
						Received++;
						inbox_.Enqueue(message);
					}
				}
			} catch (Exception e) {
				TrcLog.Warning($"Ignored a broken {one_} message: {e.Message}");
			}
		}
	}
}
