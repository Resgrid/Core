using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Command
{
	public class NewCommandView
	{
		/// <summary>Lane identification palette — keep in sync with the IC app's LANE_COLORS swatches.</summary>
		public static readonly IReadOnlyList<(string Name, string Value)> LaneColors = new List<(string, string)>
		{
			("Red", "#e74c3c"),
			("Orange", "#e67e22"),
			("Yellow", "#f1c40f"),
			("Green", "#2ecc71"),
			("Teal", "#1abc9c"),
			("Blue", "#3498db"),
			("Purple", "#9b59b6"),
			("Gray", "#7f8c8d"),
		};

		public string Message { get; set; }
		public CommandDefinition Command { get; set; }
		public List<CallType> CallTypes { get; set; }
		public SelectList Types { get; set; }
		public int SelectedType { get; set; }
		public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();
		public List<PersonnelRole> PersonnelRoles { get; set; } = new List<PersonnelRole>();
	}
}