using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.CustomStates
{
	/// <summary>
	/// A predefined, code-defined set of custom statuses (a "starter pack") that a department can pick
	/// from instead of building a set from a blank slate. Templates are defined entirely in code (see
	/// <see cref="CustomStateTemplateCatalog"/>) and are never stored in the database. When selected, a
	/// template is projected into a real, unsaved <see cref="CustomState"/> that the user edits (rename
	/// verbiage, recolor, delete options) and then saves through the normal create pipeline.
	/// </summary>
	public class CustomStateTemplate
	{
		/// <summary>Stable, unique identifier/slug for the template (e.g. "unit-ems-ambulance").</summary>
		public string Id { get; set; }

		/// <summary>The kind of custom state this template applies to (Unit, Personnel or Staffing).</summary>
		public CustomStateTypes Type { get; set; }

		/// <summary>Short, human friendly name (e.g. "EMS / Ambulance").</summary>
		public string Name { get; set; }

		/// <summary>The discipline/category used for grouping and filtering (e.g. "EMS", "Fire").</summary>
		public string Category { get; set; }

		/// <summary>A one or two sentence explanation of what the set is for and who should use it.</summary>
		public string Description { get; set; }

		/// <summary>Extra synonyms/keywords to make the template easier to find via search.</summary>
		public IReadOnlyList<string> Keywords { get; init; } = new string[0];

		/// <summary>The ordered buttons/options that make up the set.</summary>
		public IReadOnlyList<CustomStateTemplateDetail> Details { get; init; } = new List<CustomStateTemplateDetail>();

		/// <summary>Number of buttons/options in the set.</summary>
		public int ButtonCount => Details?.Count ?? 0;

		/// <summary>
		/// A single lowercased blob of all searchable text (name, category, description, keywords and
		/// every button label). Used to power client-side search/filtering in the template gallery.
		/// </summary>
		public string SearchText
		{
			get
			{
				var parts = new List<string> { Name, Category, Description };

				if (Keywords != null)
					parts.AddRange(Keywords);

				if (Details != null)
					parts.AddRange(Details.Select(d => d.ButtonText));

				return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
			}
		}
	}
}
