using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class PersonnelRanksModel : BaseUserModel
	{
		public List<Rank> Ranks { get; set; }
	}
}