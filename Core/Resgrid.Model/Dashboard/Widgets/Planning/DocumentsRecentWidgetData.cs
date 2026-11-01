using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// documents.recent (plan §7.6): the newest documents (admins-only documents for department admins only, as on the
	/// Documents page). Names and categories only: the ADP-catalogued description, file name and bytes are never selected.
	/// </summary>
	public sealed class DocumentsRecentWidgetData
	{
		public List<DocumentsRecentItem> Items { get; set; } = new List<DocumentsRecentItem>();
	}

	public sealed class DocumentsRecentItem
	{
		public int DocumentId { get; set; }

		public string Name { get; set; }

		public string Category { get; set; }

		/// <summary>MIME type.</summary>
		public string Type { get; set; }

		/// <summary>Font Awesome 4.7 class for the type ("fa fa-file-pdf-o").</summary>
		public string Icon { get; set; }

		public bool AdminsOnly { get; set; }

		public DateTime AddedOnUtc { get; set; }

		public string Url { get; set; }
	}
}
