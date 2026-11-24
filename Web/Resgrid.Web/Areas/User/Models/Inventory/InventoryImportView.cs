using System;
using Resgrid.Model.Inventories;

namespace Resgrid.Web.Areas.User.Models.Inventory
{
	/// <summary>Inventory → Import: the upload form, a preview of the plan, or the applied result.</summary>
	public sealed class InventoryImportView
	{
		public bool Migrated { get; set; }
		public bool CanWrite { get; set; }
		/// <summary>The last step needs the protected-data grant; the page asks for it and the next submit carries it.</summary>
		public bool Locked { get; set; }
		public string RequestId { get; set; } = Guid.NewGuid().ToString("D");
		/// <summary>The previewed file as text, posted back with the preview hash when the person applies it.</summary>
		public string Csv { get; set; }
		public string FileName { get; set; }
		public InventoryImportPreview Preview { get; set; }
		public InventoryImportResult Result { get; set; }
		public string ErrorCode { get; set; }
	}
}
