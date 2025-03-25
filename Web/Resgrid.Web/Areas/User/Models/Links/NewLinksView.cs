using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Links
{
    public class NewLinksView
	{
		public string Message { get; set; }
		public int DepartmentId { get; set; }
		public string LinkCode { get; set; }
	    public DepartmentLink Link { get; set; }
    }
}