using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Inventory
{
	public class ViewEntryView
	{
		public string Name { get; set; }
		public Department Department { get; set; }
		public Model.Inventory Inventory { get; set; }
	}
}
