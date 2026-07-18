using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Documents
{
	public class ViewDocumentView
	{
		public Document Document { get; set; }
		public Department Department { get; set; }
		public string UploadedByName { get; set; }
		public string DescriptionHtml { get; set; }
		public bool CanEdit { get; set; }
		public bool CanDelete { get; set; }
	}
}
