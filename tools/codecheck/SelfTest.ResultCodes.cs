namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Result codes for the season bot (Stage 3.5): a round trip, any change refused, another key refused. Python's copy: TRCSeasonBot/tests.</summary>
	internal static partial class SelfTest {
		private static void ResultCodeTests() {
			const string line = "{\"v\":1,\"type\":\"battle\",\"course_name\":\"Akina Downhill ü\",\"winner\":\"76561197985782280\"}";
			string code = ResultCode.Make(line);
			Expect(ResultCode.TryRead(code, out string back) && back == line, "result codes: the line comes back exactly (with non-English letters)");
			Expect(code.StartsWith("TRCR1.") && code.IndexOfAny(new[] { '+', '/', '=', ' ' }) < 0, "result codes: safe to paste (no +, /, = or spaces)");

			string body = code.Split('.')[1];
			char swap = body[10] == 'A' ? 'B' : 'A';
			string changed = code.Replace(body, body.Substring(0, 10) + swap + body.Substring(11));
			Expect(!ResultCode.TryRead(changed, out _), "result codes: one letter changed is refused");
			Expect(!ResultCode.TryRead("TRCR1.abc", out _) && !ResultCode.TryRead("", out _) && !ResultCode.TryRead(null, out _) && !ResultCode.TryRead("TRCR1.***.***", out _),
				"result codes: broken codes are refused, not crashed on");

			ResultCode.UseKey(new string('k', 40));
			Expect(!ResultCode.TryRead(code, out _), "result codes: a code signed with another key is refused");
			Expect(ResultCode.TryRead(ResultCode.Make(line), out _), "result codes: the same key reads its own codes");
		}
	}
}
