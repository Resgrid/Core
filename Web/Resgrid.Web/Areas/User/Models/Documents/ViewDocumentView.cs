using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Documents
{
	public class ViewDocumentView
	{
		public Document Document { get; set; }
		/// <summary>True when this page is showing ADP placeholders that a grant could reveal.</summary>
		public bool IsProtectedDocument { get; set; }

		public Department Department { get; set; }
		public string UploadedByName { get; set; }
		public string DescriptionHtml { get; set; }
		public bool CanEdit { get; set; }
		public bool CanDelete { get; set; }

		/// <summary>The viewer may author protocols and the document is not protected: "Move to Protocols" (P1 R-P6).</summary>
		public bool CanMoveToProtocols { get; set; }

		public string Error { get; set; }
	}
}
