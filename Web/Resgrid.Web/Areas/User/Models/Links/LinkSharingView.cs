using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Links
{
	/// <summary>Round 10 A3: what the viewing department's side of a link shares (the Sharing page); the other side's flags are never shown as editable.</summary>
	public class LinkSharingView
	{
		public DepartmentLink Link { get; set; }
		public bool IsCreator { get; set; }
		public string OtherDepartmentName { get; set; }
		public bool Calls { get; set; }
		public bool Units { get; set; }
		public bool Personnel { get; set; }
		public bool Orders { get; set; }
		public bool Protocols { get; set; }
		public bool Training { get; set; }
	}
}
