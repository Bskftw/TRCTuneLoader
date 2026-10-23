using System;
using System.Collections.Generic;

namespace TRC.TuneLoader {
	/// <summary>
	/// Splits a battle message into pieces that each fit in one ordinary network packet, and puts them back
	/// together. A message's ID comes from its bytes, so when the same message is sent again (a challenge is,
	/// every few seconds, until it's answered), pieces from different sends fill in each other's gaps instead of
	/// each send having to arrive whole. Nothing here knows about KSL, so the self-test can lose pieces on purpose.
	/// </summary>
	public sealed class BattleChunks {
		/// <summary>
		/// Bytes of message per piece. In the first test (0.10.0, Oct 23) single small packets got through every
		/// time, while a challenge sent as 3000-byte pieces never did; this is smaller than the reports TrcSync
		/// already sends fine.
		/// </summary>
		public const int ChunkBytes = 500;
		public const int MaxChunks = 256;
		private static readonly TimeSpan PartialTimeout = TimeSpan.FromSeconds(60);

		public struct Piece {
			public uint MessageId;
			public int Index;
			public int Count;
			public byte[] Data;
		}

		private sealed class Partial {
			public byte[][] Chunks;
			public int Received;
			public DateTime Started;
		}

		private readonly Dictionary<string, Partial> partials_ = new Dictionary<string, Partial>(StringComparer.Ordinal);

		public static List<Piece> Split(byte[] data) {
			uint id = IdOf(data);
			int count = Math.Max(1, (data.Length + ChunkBytes - 1) / ChunkBytes);
			var pieces = new List<Piece>(count);
			for (int i = 0; i < count; i++) {
				int offset = i * ChunkBytes;
				int length = Math.Min(ChunkBytes, data.Length - offset);
				var part = new byte[length];
				Array.Copy(data, offset, part, 0, length);
				pieces.Add(new Piece { MessageId = id, Index = i, Count = count, Data = part });
			}
			return pieces;
		}

		/// <summary>FNV-1a over the bytes and their length: the same message always gets the same ID.</summary>
		public static uint IdOf(byte[] data) {
			uint hash = 2166136261;
			foreach (byte b in data) {
				hash = (hash ^ b) * 16777619;
			}
			return (hash ^ (uint)data.Length) * 16777619;
		}

		/// <summary>Whether a piece's numbers make sense (anything else is ignored).</summary>
		public static bool Valid(int index, int count, int length) =>
			count >= 1 && count <= MaxChunks && index >= 0 && index < count && length >= 0 && length <= ChunkBytes;

		/// <summary>Keeps a piece; returns the whole message once every piece is in, else null.</summary>
		public byte[] Add(ulong from, Piece piece, DateTime now) {
			if (piece.Count == 1) {
				return piece.Data;
			}

			var stale = new List<string>();
			foreach (var pair in partials_) {
				if (now - pair.Value.Started > PartialTimeout) {
					stale.Add(pair.Key);
				}
			}
			stale.ForEach(k => partials_.Remove(k));

			string key = $"{from}:{piece.MessageId}:{piece.Count}";
			if (!partials_.TryGetValue(key, out Partial partial)) {
				partials_[key] = partial = new Partial { Chunks = new byte[piece.Count][], Started = now };
			}
			if (partial.Chunks[piece.Index] != null) {
				return null;
			}
			partial.Chunks[piece.Index] = piece.Data;
			partial.Received++;
			if (partial.Received < piece.Count) {
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
			// Pieces from different messages can share an ID by chance: only a whole that hashes right counts.
			return IdOf(whole) == piece.MessageId ? whole : null;
		}
	}
}
