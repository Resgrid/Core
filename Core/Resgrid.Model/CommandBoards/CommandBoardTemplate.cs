using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.CommandBoards
{
	/// <summary>
	/// A code-defined command-board example that can be projected into an unsaved, editable
	/// <see cref="CommandDefinition"/>. Templates never contain department or database identifiers.
	/// </summary>
	public class CommandBoardTemplate
	{
		public string Id { get; set; }

		public string Name { get; set; }

		public string Category { get; set; }

		public string Description { get; set; }

		public string[] Keywords { get; set; } = Array.Empty<string>();

		public bool Timer { get; set; }

		public int TimerMinutes { get; set; }

		public List<CommandBoardTemplateLane> Lanes { get; set; } = new List<CommandBoardTemplateLane>();

		public int LaneCount => Lanes?.Count ?? 0;

		/// <summary>Searchable text used by both the catalog and the browser UI.</summary>
		public string SearchText
		{
			get
			{
				var parts = new List<string> { Name, Category, Description };

				if (Keywords != null)
					parts.AddRange(Keywords);

				if (Lanes != null)
				{
					parts.AddRange(Lanes.Select(l => l.Name));
					parts.AddRange(Lanes.Select(l => l.Description));
					parts.AddRange(Lanes.SelectMany(l => l.SuggestedUnitTypes ?? Array.Empty<string>()));
					parts.AddRange(Lanes.SelectMany(l => l.SuggestedPersonnelRoles ?? Array.Empty<string>()));
				}

				return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
			}
		}

		/// <summary>
		/// Creates an unsaved definition and matches this template's name-based suggestions to the
		/// department's configured unit types and personnel roles. Unmatched suggestions are ignored so
		/// every example remains usable by departments with different terminology.
		/// </summary>
		public CommandDefinition CreateDefinition(IEnumerable<UnitType> unitTypes, IEnumerable<PersonnelRole> personnelRoles)
		{
			var availableUnitTypes = unitTypes?.ToList() ?? new List<UnitType>();
			var availablePersonnelRoles = personnelRoles?.ToList() ?? new List<PersonnelRole>();
			var assignments = new List<CommandDefinitionRole>();

			for (var index = 0; index < (Lanes?.Count ?? 0); index++)
			{
				var lane = Lanes[index];
				var matchedUnitTypes = availableUnitTypes
					.Where(unitType => MatchesAny(unitType.Type, lane.SuggestedUnitTypes))
					.GroupBy(unitType => unitType.UnitTypeId)
					.Select(group => new CommandDefinitionRoleUnitType { UnitTypeId = group.Key })
					.ToList();
				var matchedPersonnelRoles = availablePersonnelRoles
					.Where(role => MatchesAny(role.Name, lane.SuggestedPersonnelRoles))
					.GroupBy(role => role.PersonnelRoleId)
					.Select(group => new CommandDefinitionRolePersonnelRole { PersonnelRoleId = group.Key })
					.ToList();

				assignments.Add(new CommandDefinitionRole
				{
					Name = lane.Name,
					Description = lane.Description,
					LaneType = (int)lane.LaneType,
					SortOrder = index,
					Color = lane.Color,
					MinUnits = lane.MinUnits,
					MaxUnits = lane.MaxUnits,
					MinUnitPersonnel = lane.MinUnitPersonnel,
					MaxUnitPersonnel = lane.MaxUnitPersonnel,
					MinTimeInRole = lane.MinTimeInRole,
					MaxTimeInRole = lane.MaxTimeInRole,
					RequiredUnitTypes = matchedUnitTypes,
					RequiredRoles = matchedPersonnelRoles,
					ForceRequirements = lane.ForceRequirements && (matchedUnitTypes.Count > 0 || matchedPersonnelRoles.Count > 0)
				});
			}

			return new CommandDefinition
			{
				Name = Name,
				Description = Description,
				Timer = Timer,
				TimerMinutes = TimerMinutes,
				Assignments = assignments
			};
		}

		private static bool MatchesAny(string value, IEnumerable<string> suggestions)
		{
			if (string.IsNullOrWhiteSpace(value) || suggestions == null)
				return false;

			return suggestions.Any(suggestion =>
				!string.IsNullOrWhiteSpace(suggestion) &&
				string.Equals(value.Trim(), suggestion.Trim(), StringComparison.OrdinalIgnoreCase));
		}
	}
}
