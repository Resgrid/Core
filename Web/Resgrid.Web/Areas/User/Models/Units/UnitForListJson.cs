namespace Resgrid.Web.Areas.User.Models.Units
{
	public class UnitForListJson
	{
		public int UnitId { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public string Station { get; set; }

		/// <summary>The unit's station group; null for a unit without one.</summary>
		public int? StationGroupId { get; set; }
		public int StateId { get; set; }
		public string State { get; set; }
		public string StateColor { get; set; }
		public string TextColor { get; set; }
		public string Timestamp { get; set; }
		public string Eta { get; set; }

		/// <summary>Root-relative URL of the unit type's icon (uploaded, else built-in); null when the type has none.</summary>
		public string TypeIconUrl { get; set; }
	}
}