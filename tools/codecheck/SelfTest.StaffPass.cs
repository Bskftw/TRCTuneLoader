// Staff passes: a pass signed with a test key checks out only for its own Steam ID and only untouched.
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader.CodeCheck {
	internal static partial class SelfTest {
		private static void StaffPassTests() {
			using (var rsa = new RSACryptoServiceProvider(2048)) {
				const ulong staff = 76561198000000001UL, other = 76561198000000002UL;
				byte[] signature = rsa.SignData(Encoding.UTF8.GetBytes(StaffPass.Body(staff, "Apok", "2026-11-19")), CryptoConfig.MapNameToOID("SHA256"));
				string pass = StaffPass.Make(staff, "Apok", "2026-11-19", signature);
				RSAParameters key = rsa.ExportParameters(false);

				Expect(StaffPass.IsValid(pass, staff, key), "a staff pass checks out for its own Steam ID");
				Expect(StaffPass.NameOf(pass) == "Apok", "a staff pass carries its name");
				Expect(!StaffPass.IsValid(pass, other, key), "a staff pass doesn't work for another Steam ID");
				Expect(!StaffPass.IsValid(pass.Replace("|Apok|", "|Apoc|"), staff, key), "a staff pass with a changed name fails");
				Expect(!StaffPass.IsValid(pass.Replace("2026-11-19", "2026-11-20"), staff, key), "a staff pass with a changed date fails");
				using (var stranger = new RSACryptoServiceProvider(2048)) {
					Expect(!StaffPass.IsValid(pass, staff, stranger.ExportParameters(false)), "a staff pass signed with another key fails");
				}
				Expect(!StaffPass.IsValid("", staff, key) && !StaffPass.IsValid("1|2|3", staff, key) && !StaffPass.IsValid(new string('x', 2000), staff, key),
					"empty, short and huge staff passes fail");
				Expect(!StaffPass.IsValid(pass, staff), "a pass from a test key isn't valid against the mod's real key");
			}
		}
	}
}
