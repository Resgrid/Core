using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Web.Helpers;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class NewLogView : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public string Message { get; set; }
		public Log Log { get; set; }
		public SelectList Types { get; set; }
		public LogTypes LogType { get; set; }
		public Call Call { get; set; }
		public int CallId { get; set; }
		public CallPriority CallPriority { get; set; }
		public SelectList CallPriorities { get; set; }
		public Dictionary<string, string> Users { get; set; }
		public List<DepartmentGroup> Stations { get; set; }
		public SelectList CallTypes { get; set; }
		public string ErrorMessage { get; set; }

		public NewLogView()
		{
			Users = new Dictionary<string, string>();
		}

		public async Task<List<IdentityUser>> SetUsers(List<IdentityUser> users)
		{
			foreach (var u in users)
			{
				Users.Add(u.UserId, await UserHelper.GetFullNameForUser(u.UserId));
			}

			return users;
		}
	}
}
