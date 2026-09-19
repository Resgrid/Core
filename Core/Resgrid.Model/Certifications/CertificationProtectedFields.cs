using System;
using System.Collections.Generic;

namespace Resgrid.Model.Certifications
{
	/// <summary>
	/// ADP catalog 27 (Workforce &amp; Business Operations plan, Phase D2; registered with M0213/M0214): the free-text and
	/// document columns of unit certification records (Operational family, like UnitLogs) and certification credit entries (Personnel family).
	/// PersonnelCertifications itself stays catalog 6. Accessor maps drive the generic RMS write/read seams.
	/// </summary>
	public static class CertificationProtectedFields
	{
		public const int CatalogVersion = 27;
		public const string UnitFamily = "Operational";
		public const string PersonnelFamily = "Personnel";
		public const string UnitDataFieldId = "unitcertifications.data";
		public const string CreditDataFieldId = "personnelcertificationcredits.data";

		public static readonly IReadOnlyDictionary<string, (Func<UnitCertification, string> Get, Action<UnitCertification, string> Set)> Unit =
			new Dictionary<string, (Func<UnitCertification, string>, Action<UnitCertification, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["unitcertifications.number"] = (u => u.Number, (u, v) => u.Number = v),
				["unitcertifications.issuedby"] = (u => u.IssuedBy, (u, v) => u.IssuedBy = v),
				["unitcertifications.notes"] = (u => u.Notes, (u, v) => u.Notes = v),
				["unitcertifications.filename"] = (u => u.FileName, (u, v) => u.FileName = v)
			};

		public static readonly IReadOnlyDictionary<string, (Func<PersonnelCertificationCredit, string> Get, Action<PersonnelCertificationCredit, string> Set)> Credit =
			new Dictionary<string, (Func<PersonnelCertificationCredit, string>, Action<PersonnelCertificationCredit, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["personnelcertificationcredits.description"] = (c => c.Description, (c, v) => c.Description = v),
				["personnelcertificationcredits.filename"] = (c => c.FileName, (c, v) => c.FileName = v)
			};

		/// <summary>(table, column, family, binary) for the catalog registration.</summary>
		public static IEnumerable<(string Table, string Column, string Family, bool Binary)> All()
		{
			yield return ("UnitCertifications", "Number", UnitFamily, false);
			yield return ("UnitCertifications", "IssuedBy", UnitFamily, false);
			yield return ("UnitCertifications", "Notes", UnitFamily, false);
			yield return ("UnitCertifications", "FileName", UnitFamily, false);
			yield return ("UnitCertifications", "Data", UnitFamily, true);
			yield return ("PersonnelCertificationCredits", "Description", PersonnelFamily, false);
			yield return ("PersonnelCertificationCredits", "FileName", PersonnelFamily, false);
			yield return ("PersonnelCertificationCredits", "Data", PersonnelFamily, true);
		}
	}
}
