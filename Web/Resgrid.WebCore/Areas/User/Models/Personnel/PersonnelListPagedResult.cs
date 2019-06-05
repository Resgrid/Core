using Resgrid.Web.Areas.User.Models.Personnel;
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Personnel
{
	public class PersonnelListPagedResult
	{
		public List<PersonnelForListJson> Data { get; set; }
		public int Page { get; set; }
		public int Total { get; set; }
	}
}
