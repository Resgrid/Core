using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>How the organization types are grouped in the pickers (sign-up and Department -> Settings).</summary>
	public enum DepartmentTypeGroups
	{
		FireAndRescue = 1,
		HealthAndEms = 2,
		EmergencyManagement = 3,
		SecurityAndEvents = 4,
		IndustrialAndUtilities = 5,
		ContractorsAndFieldServices = 6,
		Other = 7
	}

	/// <summary>One organization type a department can describe itself as.</summary>
	public sealed class DepartmentTypeDefinition
	{
		public DepartmentTypeDefinition(string key, string value, DepartmentTypeGroups group)
		{
			Key = key;
			Value = value;
			Group = group;
		}

		/// <summary>Stable id; the picker's display name is the <c>OrgType.{Key}</c> string in the Department resources.</summary>
		public string Key { get; }

		/// <summary>
		/// The text stored in <see cref="Department.DepartmentType"/>. Feature-flag targeting rules and the workflow variable
		/// <c>department.type</c> match on it, so never change one that has shipped.
		/// </summary>
		public string Value { get; }

		public DepartmentTypeGroups Group { get; }
	}

	/// <summary>
	/// The organization types offered at sign-up and on Department -> Settings, stored as text in
	/// <see cref="Department.DepartmentType"/>. The type is a description of the organization: it never hides or limits
	/// templates, packs, modules or suggestions. Setup suggestions for several kinds of work come from the Admin Assist
	/// operating profile, which allows more than one sector.
	/// </summary>
	public static class DepartmentTypeCatalog
	{
		/// <summary>Matches the column length on <see cref="Department.DepartmentType"/>.</summary>
		public const int MaxLength = 150;

		public static readonly IReadOnlyList<DepartmentTypeDefinition> All = new List<DepartmentTypeDefinition>
		{
			new DepartmentTypeDefinition("volunteer-fire", "Volunteer Fire", DepartmentTypeGroups.FireAndRescue),
			new DepartmentTypeDefinition("career-fire", "Career Fire", DepartmentTypeGroups.FireAndRescue),
			new DepartmentTypeDefinition("combination-fire", "Combination Fire", DepartmentTypeGroups.FireAndRescue),
			new DepartmentTypeDefinition("wildland-fire", "Wildland Fire", DepartmentTypeGroups.FireAndRescue),
			new DepartmentTypeDefinition("search-and-rescue", "Search and Rescue", DepartmentTypeGroups.FireAndRescue),
			new DepartmentTypeDefinition("hazmat", "HAZMAT", DepartmentTypeGroups.FireAndRescue),

			new DepartmentTypeDefinition("ems", "EMS", DepartmentTypeGroups.HealthAndEms),
			new DepartmentTypeDefinition("private-ambulance", "Private Ambulance and Interfacility Transport", DepartmentTypeGroups.HealthAndEms),
			new DepartmentTypeDefinition("hospital", "Hospital and Health System", DepartmentTypeGroups.HealthAndEms),
			new DepartmentTypeDefinition("community-paramedicine", "Community Paramedicine and Mobile Integrated Health", DepartmentTypeGroups.HealthAndEms),
			new DepartmentTypeDefinition("crisis-response", "Crisis and Mental Health Response", DepartmentTypeGroups.HealthAndEms),

			new DepartmentTypeDefinition("emergency-management", "Emergency Management", DepartmentTypeGroups.EmergencyManagement),
			new DepartmentTypeDefinition("public-safety", "Public Safety", DepartmentTypeGroups.EmergencyManagement),
			new DepartmentTypeDefinition("cert", "CERT", DepartmentTypeGroups.EmergencyManagement),
			new DepartmentTypeDefinition("disaster-response", "Disaster Response", DepartmentTypeGroups.EmergencyManagement),
			new DepartmentTypeDefinition("relief-org", "Relief Org", DepartmentTypeGroups.EmergencyManagement),
			new DepartmentTypeDefinition("multi-discipline", "Multi-Discipline Response", DepartmentTypeGroups.EmergencyManagement),

			new DepartmentTypeDefinition("security", "Security", DepartmentTypeGroups.SecurityAndEvents),
			new DepartmentTypeDefinition("event-venue", "Event and Venue Operations", DepartmentTypeGroups.SecurityAndEvents),

			new DepartmentTypeDefinition("industrial-emergency-response", "Industrial Emergency Response", DepartmentTypeGroups.IndustrialAndUtilities),
			new DepartmentTypeDefinition("oil-and-gas", "Oil and Gas", DepartmentTypeGroups.IndustrialAndUtilities),
			new DepartmentTypeDefinition("power", "Power", DepartmentTypeGroups.IndustrialAndUtilities),
			new DepartmentTypeDefinition("chemical", "Chemical", DepartmentTypeGroups.IndustrialAndUtilities),
			new DepartmentTypeDefinition("nuclear", "Nuclear", DepartmentTypeGroups.IndustrialAndUtilities),
			new DepartmentTypeDefinition("other-industrial", "Other Industrial", DepartmentTypeGroups.IndustrialAndUtilities),

			new DepartmentTypeDefinition("response-contractor", "Emergency Response Contractor", DepartmentTypeGroups.ContractorsAndFieldServices),
			new DepartmentTypeDefinition("drone-aerial-survey", "Drone and Aerial Survey", DepartmentTypeGroups.ContractorsAndFieldServices),
			new DepartmentTypeDefinition("repair-services", "Repair Services", DepartmentTypeGroups.ContractorsAndFieldServices),
			new DepartmentTypeDefinition("delivery-services", "Delivery Services", DepartmentTypeGroups.ContractorsAndFieldServices),
			new DepartmentTypeDefinition("bus-and-shuttle", "Bus and Shuttle", DepartmentTypeGroups.ContractorsAndFieldServices),

			new DepartmentTypeDefinition("other-public", "Other Public", DepartmentTypeGroups.Other),
			new DepartmentTypeDefinition("other-private", "Other Private", DepartmentTypeGroups.Other),
			new DepartmentTypeDefinition("other", "Other", DepartmentTypeGroups.Other)
		};

		/// <summary>The stored values, in picker order.</summary>
		public static IEnumerable<string> Values => All.Select(t => t.Value);

		/// <summary>The catalog entry whose stored value matches, ignoring case and surrounding spaces; null when none does.</summary>
		public static DepartmentTypeDefinition Find(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value.Trim();
			return All.FirstOrDefault(t => string.Equals(t.Value, trimmed, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Works out what to store when a picker posts <paramref name="postedValue"/> for a department whose type is
		/// <paramref name="currentValue"/>. A catalog value is stored in its catalog spelling. A blank post, or a post of the
		/// current value, keeps the current value as it is, so a department whose type predates this catalog (or was never
		/// set) can save its other settings without being made to pick. Anything else is rejected.
		/// </summary>
		public static bool TryResolveSelection(string currentValue, string postedValue, out string resolved)
		{
			if (string.IsNullOrWhiteSpace(postedValue))
			{
				resolved = currentValue;
				return true;
			}

			var match = Find(postedValue);
			if (match != null)
			{
				resolved = match.Value;
				return true;
			}

			if (!string.IsNullOrWhiteSpace(currentValue) && string.Equals(currentValue.Trim(), postedValue.Trim(), StringComparison.Ordinal))
			{
				resolved = currentValue;
				return true;
			}

			resolved = currentValue;
			return false;
		}
	}
}
