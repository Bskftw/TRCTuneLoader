// Text put into a network message is cut to a fixed length, so a long name or note can't make a message too big.
using System;

namespace TRC.TuneLoader {
	/// <summary>
	/// The one place every message's text is cut (battle, race, room and TRC report messages each pass their own
	/// limit). Changing what this does changes the bytes on the network: tools/codecheck/SelfTest.Wire.cs checks them.
	/// </summary>
	public static class MessageText {
		/// <summary>The text (null counts as ""), cut to at most <paramref name="max"/> characters.</summary>
		public static string Clip(string text, int max) {
			text = text ?? "";
			return text.Length <= max ? text : text.Substring(0, max);
		}
	}
}
