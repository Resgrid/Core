using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Links
{
    public class LinksIndexView
    {
		public int DepartmentId { get; set; }
		public string Code { get; set; }
	    public List<DepartmentLink> Links { get; set; }
		public bool CanCreateLinks { get; set; }
    }
}