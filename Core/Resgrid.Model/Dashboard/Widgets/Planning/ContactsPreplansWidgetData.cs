using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// contacts.preplans (plan §7.6, §9A.2): contacts whose pre-plan review date has passed. Pre-plan content never appears;
	/// a person contact's name is ADP-catalogued (<c>contacts.firstname/lastname</c>), the company name is not.
	/// </summary>
	public sealed class ContactsPreplansWidgetData
	{
		public int DueCount { get; set; }

		/// <summary>Longest overdue first, capped.</summary>
		public List<ContactsPreplanItem> Items { get; set; } = new List<ContactsPreplanItem>();
	}

	public sealed class ContactsPreplanItem
	{
		public string ContactId { get; set; }

		/// <summary>0 = person, 1 = company.</summary>
		public int ContactType { get; set; }

		/// <summary>The company name when set (never protected); otherwise the person's name, hidden while the department enforces ADP and no grant revealed it.</summary>
		public DashboardProtectedText Name { get; set; }

		public DateTime NextReviewDueUtc { get; set; }

		public int DaysOverdue { get; set; }

		public string Url { get; set; }
	}
}
