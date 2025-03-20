using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Documents
{
	public class IndexView
	{
		public string UserId { get; set; }
		public Department Department { get; set; }
		public List<Document> Documents { get; set; }
		public List<DocumentCategory> Categories { get; set; }
		public string SelectedType { get; set; }
		public string SelectedCategory { get; set; }
	}
}
