using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>certifications.mine (plan §7.7): the viewer's own certifications that are expired or expire within the horizon.</summary>
	public sealed class CertificationsMineWidgetData
	{
		public int HorizonDays { get; set; }

		/// <summary>All of the viewer's live certifications in this department.</summary>
		public int Total { get; set; }

		public int Expired { get; set; }

		public int Expiring { get; set; }

		/// <summary>Expired first, then soonest to expire.</summary>
		public List<CertificationsMineItem> Items { get; set; } = new List<CertificationsMineItem>();
	}

	public sealed class CertificationsMineItem
	{
		public int CertificationId { get; set; }

		/// <summary>The certification's name (the number and document are protected and never included).</summary>
		public string Name { get; set; }

		/// <summary>PersonnelCertificationStatuses.</summary>
		public int Status { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public int? DaysUntilExpiry { get; set; }

		public string Tone { get; set; }
	}
}
