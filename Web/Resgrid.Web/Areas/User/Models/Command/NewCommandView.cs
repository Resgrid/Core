using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Command
{
	public class NewCommandView
	{
		public string Message { get; set; }
		public CommandDefinition Command { get; set; }
		public List<CallType> CallTypes { get; set; }
		public SelectList Types { get; set; }
		public int SelectedType { get; set; }
		public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();
		public List<PersonnelRole> PersonnelRoles { get; set; } = new List<PersonnelRole>();
	}
}