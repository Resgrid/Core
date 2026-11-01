using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>certifications.expiry (plan §7.7): the Certifications dashboard totals and the most urgent person/unit records.</summary>
	public sealed class CertificationsExpiryWidgetData
	{
		/// <summary>"Expiring" means within this many days (the department's longest notification lead).</summary>
		public int HorizonDays { get; set; }

		public int Expired { get; set; }

		public int Expiring { get; set; }

		public int Suspended { get; set; }

		public int PendingVerification { get; set; }

		/// <summary>Expired first, then soonest to expire, at most maxRows.</summary>
		public List<CertificationsExpiryItem> Items { get; set; } = new List<CertificationsExpiryItem>();
	}

	public sealed class CertificationsExpiryItem
	{
		/// <summary>"person" (SubjectId is a user id) or "unit" (SubjectId is a unit id).</summary>
		public string Scope { get; set; }

		public string SubjectId { get; set; }

		public string SubjectName { get; set; }

		public string TypeCode { get; set; }

		public string TypeName { get; set; }

		/// <summary>PersonnelCertificationStatuses (person) or UnitCertificationStatuses (unit).</summary>
		public int Status { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public int? DaysUntilExpiry { get; set; }

		/// <summary>"danger" expired, "warning" expiring.</summary>
		public string Tone { get; set; }
	}
}
