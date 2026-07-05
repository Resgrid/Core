using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.UnitRoles
{
	/// <summary>
	/// A predefined, code-defined set of unit accountability roles (a "crew template") that a department
	/// can pick from instead of typing role names from scratch. Templates are defined entirely in code
	/// (see <see cref="UnitRoleTemplateCatalog"/>) and are never stored in the database. When selected, the
	/// roles are added as editable rows on the unit form so the department can rename them, change the
	/// required personnel role, add or remove seats, and then save.
	/// </summary>
	public class UnitRoleTemplate
	{
		/// <summary>Stable, unique identifier/slug (e.g. "fire-engine-company").</summary>
		public string Id { get; set; }

		/// <summary>Short, human friendly name (e.g. "Fire Engine Company").</summary>
		public string Name { get; set; }

		/// <summary>The discipline/category used for grouping and filtering (e.g. "Fire", "EMS").</summary>
		public string Category { get; set; }

		/// <summary>A one or two sentence explanation of what the crew set is and who should use it.</summary>
		public string Description { get; set; }

		/// <summary>Extra synonyms/keywords to make the template easier to find via search.</summary>
		public string[] Keywords { get; set; } = new string[0];

		/// <summary>The ordered seats/positions that make up the crew.</summary>
		public List<UnitRoleTemplateRole> Roles { get; set; } = new List<UnitRoleTemplateRole>();

		/// <summary>Number of seats in the crew.</summary>
		public int RoleCount => Roles?.Count ?? 0;

		/// <summary>
		/// A single lowercased blob of all searchable text (name, category, description, keywords and every
		/// role/seat name). Powers client-side search/filtering in the template picker.
		/// </summary>
		public string SearchText
		{
			get
			{
				var parts = new List<string> { Name, Category, Description };

				if (Keywords != null)
					parts.AddRange(Keywords);

				if (Roles != null)
				{
					parts.AddRange(Roles.Select(r => r.Name));
					parts.AddRange(Roles.Where(r => !string.IsNullOrWhiteSpace(r.SuggestedPersonnelRole)).Select(r => r.SuggestedPersonnelRole));
				}

				return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
			}
		}
	}
}
