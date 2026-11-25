using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>
	/// The statements protocol signatures carry (stored with each signature). The web passes them in the signer's language from
	/// its resources, with the same placeholders; these English forms are the fallback (v4, workers).
	/// </summary>
	public static class ProtocolSignatureStatements
	{
		/// <summary>{0} code, {1} name, {2} content short code, {3} step, {4} steps.</summary>
		public const string ApprovalFormat = "I approve this revision of protocol {0} \"{1}\" (content {2}) for publication, as step {3} of {4} of its review.";

		/// <summary>{0} code, {1} name, {2} version, {3} content short code.</summary>
		public const string ReviewFormat = "I have reviewed protocol {0} \"{1}\" version {2} (content {3}) and it remains correct without change.";

		public static string Approval(DispatchProtocol protocol, ProtocolVersion version, int step, int steps, string format = null) =>
			Format(format, ApprovalFormat, protocol?.Code, protocol?.Name, SignaturePolicy.ShortCode(version?.ContentHash), step, steps);

		public static string Review(DispatchProtocol protocol, ProtocolVersion version, string format = null) =>
			Format(format, ReviewFormat, protocol?.Code, protocol?.Name, version?.Label, SignaturePolicy.ShortCode(version?.ContentHash));

		/// <summary>Read-and-sign (P2). {0} code, {1} name, {2} version, {3} content short code.</summary>
		public const string ReadAndSignFormat = "I have read protocol {0} \"{1}\" version {2} (content {3}), I understand it and I will follow it.";

		/// <summary>The approval of a priority bulletin issued before approval (P2 R-P7). {0} code, {1} name, {2} content short code.</summary>
		public const string BulletinCountersignFormat = "I approve priority bulletin {0} \"{1}\" (content {2}), issued before its approval.";

		public static string ReadAndSign(DispatchProtocol protocol, ProtocolVersion version, string format = null) =>
			Format(format, ReadAndSignFormat, protocol?.Code, protocol?.Name, version?.Label, SignaturePolicy.ShortCode(version?.ContentHash));

		public static string BulletinCountersign(DispatchProtocol protocol, ProtocolVersion version, string format = null) =>
			Format(format, BulletinCountersignFormat, protocol?.Code, protocol?.Name, SignaturePolicy.ShortCode(version?.ContentHash));

		/// <summary>A content translation approval (P3). {0} code, {1} name, {2} version, {3} language, {4} translation content short code.</summary>
		public const string TranslationApprovalFormat = "I approve the {3} translation of protocol {0} \"{1}\" version {2} (translation content {4}) for members reading in that language.";

		public static string TranslationApproval(DispatchProtocol protocol, ProtocolVersion version, ProtocolVersionTranslation translation, string format = null) =>
			Format(format, TranslationApprovalFormat, protocol?.Code, protocol?.Name, version?.Label, translation?.Locale, SignaturePolicy.ShortCode(translation?.ContentHash));

		private static string Format(string format, string fallback, params object[] values)
		{
			try
			{
				return string.Format(CultureInfo.CurrentCulture, string.IsNullOrWhiteSpace(format) ? fallback : format, values);
			}
			catch (System.FormatException)
			{
				return string.Format(CultureInfo.InvariantCulture, fallback, values);
			}
		}
	}
}
