using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>
	/// The statement a certification verification signature carries (S0). The web passes it in the officer's language from the
	/// Signatures resources (same placeholders); this English form is the fallback (v4). The type is the department's
	/// certification type name, never the record's own (possibly protected) text.
	/// </summary>
	public static class CertificationStatements
	{
		/// <summary>{0} record id, {1} type, {2} received date, {3} expiry date.</summary>
		public const string VerificationFormat = "I verify certification record {0} ({1}, received {2}, expires {3}): I checked it and it is genuine.";

		public static string Verification(PersonnelCertification certification, string typeName, string format = null, string none = "-")
		{
			var values = new object[]
			{
				certification?.PersonnelCertificationId,
				string.IsNullOrWhiteSpace(typeName) ? none : typeName,
				certification?.RecievedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? none,
				certification?.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? none
			};

			try
			{
				return string.Format(CultureInfo.CurrentCulture, string.IsNullOrWhiteSpace(format) ? VerificationFormat : format, values);
			}
			catch (System.FormatException)
			{
				return string.Format(CultureInfo.InvariantCulture, VerificationFormat, values);
			}
		}
	}
}
