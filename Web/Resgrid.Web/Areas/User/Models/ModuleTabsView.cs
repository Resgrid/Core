using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models
{
	/// <summary>
	/// A module's own navigation: the strip of sections under its page heading (Shared/_ModuleTabs, styled by
	/// module-workspace.css). Every section shows, the current one highlighted, so the strip never shifts or wraps
	/// differently from page to page. Page actions (New, Export ...) belong in the heading's action area instead.
	/// </summary>
	public sealed class ModuleTabsView
	{
		public List<ModuleTab> Tabs { get; set; } = new();
		/// <summary>
		/// Navigate with POST forms instead of links: for modules whose pages carry the Advanced Data Protection reveal
		/// grant on every request (a link cannot). Each form gets an antiforgery token and <see cref="FormClass"/>.
		/// </summary>
		public bool Post { get; set; }
		/// <summary>The class the page's script binds POST navigation by (Inventory: inventory-navigation).</summary>
		public string FormClass { get; set; }
		/// <summary>Accessible name of the strip, usually the module name.</summary>
		public string Label { get; set; }
	}

	public sealed class ModuleTab
	{
		public ModuleTab() { }
		public ModuleTab(string label, string icon, string controller, string action, bool active)
		{ Label = label; Icon = icon; Controller = controller; Action = action; Active = active; }

		public string Label { get; set; }
		/// <summary>A Font Awesome icon class, e.g. fa-cubes.</summary>
		public string Icon { get; set; }
		public string Area { get; set; } = "User";
		public string Controller { get; set; }
		public string Action { get; set; }
		public Dictionary<string, string> RouteValues { get; set; } = new();
		public bool Active { get; set; }
		/// <summary>Link target (e.g. _blank for a printable report). Ignored for POST navigation.</summary>
		public string Target { get; set; }
	}
}
