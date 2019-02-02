using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Connect
{
	public class IndexView
	{
		public string ImageUrl { get; set; }
		public Department Department { get; set; }
		public DepartmentProfile Profile { get; set; }
		public List<DepartmentProfileArticle> Posts { get; set; }
		public int VisiblePosts { get; set; }
		public int UnReadMessages { get; set; }
	}
}