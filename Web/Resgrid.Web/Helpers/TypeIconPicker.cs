namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// What the shared custom icon picker (Areas/User/Views/Shared/_TypeIconPicker.cshtml) shows for one unit type, call
	/// type, POI type or personnel role form: the file input, the format and size guidance, the browser-side checks and a
	/// live preview of the icon on a light and a dark map, in a list and as the stored 128 px image.
	/// </summary>
	public class TypeIconPicker
	{
		/// <summary>Name and id of the file input; the controllers read it with <see cref="TypeIconUploads"/>.</summary>
		public string FieldName { get; set; } = TypeIconUploads.FileField;

		/// <summary>Name of the "remove the custom icon" checkbox, or null for a form that cannot remove one (new types).</summary>
		public string RemoveFieldName { get; set; }

		/// <summary>The checkbox's posted state, so a form returned with an error keeps it.</summary>
		public bool RemoveChecked { get; set; }

		/// <summary>The type's uploaded icon (root-relative web URL), or null.</summary>
		public string CurrentIconUrl { get; set; }

		/// <summary>The icon shown when there is no uploaded one (a built-in map icon), or null for a plain default marker.</summary>
		public string DefaultIconUrl { get; set; }

		/// <summary>
		/// Id of the page's built-in map icon &lt;select&gt;, when it has one: the default preview follows it, using the
		/// option text as the /images/Mapping file name the way the existing icon preview does.
		/// </summary>
		public string DefaultIconSelectId { get; set; }

		/// <summary>The marker the map draws when the select is on Default (no built-in icon), or null.</summary>
		public string BaseIconUrl { get; set; }

		/// <summary>IconUpload resource key of the help line under the label (e.g. "UnitTypeIconHelp").</summary>
		public string HelpKey { get; set; }

		/// <summary>Id of the page's name input; the previews label the icon with what is typed there.</summary>
		public string SampleNameInputId { get; set; }

		/// <summary>The preview label when the name input is empty or absent.</summary>
		public string SampleName { get; set; }

		/// <summary>An error to show under the input that is not in ModelState (the POI View Type page passes it in TempData).</summary>
		public string ErrorMessage { get; set; }

		/// <summary>True for the horizontal form layout (label in col-sm-2); false to stack the label above the input.</summary>
		public bool Horizontal { get; set; } = true;
	}
}
