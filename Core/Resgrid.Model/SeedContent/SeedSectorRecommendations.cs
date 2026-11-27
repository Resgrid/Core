using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Model
{
	/// <summary>
	/// Which seeded templates suit which kinds of organization (round 9C, sector neutrality). Suggestions follow the department's operating
	/// profile (setting 110 archetypes: fire, EMS, search and rescue, industrial, hazardous materials, security, law enforcement, business …),
	/// never the organization type (DepartmentTypeCatalogTests: the type gates and suggests nothing). A suggestion only orders a library and
	/// marks a card "suggested": every template stays available to every department, and a department without a profile sees the library
	/// in its plain order. An empty list means the template suits every kind of organization.
	/// </summary>
	public static class SeedSectorRecommendations
	{
		private const string Fire = "fire", Ems = "ems", Sar = "sar", Response = "emergency-response", Hazmat = "hazmat", Industrial = "industrial",
			Security = "security", LawEnforcement = "law-enforcement", Business = "business";

		private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			// Protocol template library.
			[SeedContentLocalization.ProtocolTemplates + ":fire-sog-starter"] = new[] { Fire },
			[SeedContentLocalization.ProtocolTemplates + ":ems-clinical-structure"] = new[] { Ems },
			[SeedContentLocalization.ProtocolTemplates + ":sar-starter"] = new[] { Sar },
			[SeedContentLocalization.ProtocolTemplates + ":industrial-hazard-starter"] = new[] { Industrial, Hazmat },
			[SeedContentLocalization.ProtocolTemplates + ":business-continuity-starter"] = new[] { Business, Response },
			[SeedContentLocalization.ProtocolTemplates + ":law-enforcement-policy-starter"] = new[] { LawEnforcement },
			[SeedContentLocalization.ProtocolTemplates + ":security-post-orders-starter"] = new[] { Security },
			[SeedContentLocalization.ProtocolTemplates + ":workplace-policies-starter"] = new[] { Business, Industrial },

			// Starter courses.
			[SeedContentLocalization.StarterCourses + ":driver-evo-basics"] = new[] { Fire, Ems },
			[SeedContentLocalization.StarterCourses + ":incident-safety-basics"] = new[] { Fire, Ems, Sar, Response, Hazmat, Industrial },
			[SeedContentLocalization.StarterCourses + ":hazmat-awareness-outline"] = new[] { Fire, Hazmat, Industrial, Response },
			[SeedContentLocalization.StarterCourses + ":workplace-onboarding"] = new[] { Business, Industrial, Security },
			[SeedContentLocalization.StarterCourses + ":confined-space-awareness"] = new[] { Industrial, Hazmat },
			[SeedContentLocalization.StarterCourses + ":lockout-tagout-awareness"] = new[] { Industrial },
			[SeedContentLocalization.StarterCourses + ":hazwoper-refresher-outline"] = new[] { Hazmat, Industrial },
			[SeedContentLocalization.StarterCourses + ":security-officer-orientation"] = new[] { Security },
			[SeedContentLocalization.StarterCourses + ":annual-policy-review"] = new[] { LawEnforcement, Security },

			// Training category templates.
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.IsoFsrs] = new[] { Fire },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.NremtNccp] = new[] { Ems },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.OshaAnnual] = new[] { Hazmat, Industrial },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.WorkplaceCompliance] = new[] { Business },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.IndustrialSafety] = new[] { Industrial, Hazmat },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.SarTeam] = new[] { Sar },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.LawEnforcementInService] = new[] { LawEnforcement },
			[SeedContentLocalization.CategoryTemplates + ":" + TrainingCategoryTemplateCatalog.SecurityLicensing] = new[] { Security }
		};

		/// <summary>The archetype codes a template suits (empty: every kind of organization, or not listed).</summary>
		public static IReadOnlyList<string> SectorsFor(string catalog, string key) =>
			key != null && Map.TryGetValue(catalog + ":" + key, out var codes) ? codes : Array.Empty<string>();

		/// <summary>Every catalog:key with sectors (for the guard that every code is a real archetype).</summary>
		public static IReadOnlyDictionary<string, string[]> All => Map;

		/// <summary>True when the profile names one of the template's sectors (a template for everyone is never singled out).</summary>
		public static bool IsSuggested(string catalog, string key, DepartmentOperatingProfile profile)
		{
			var archetypes = profile?.Archetypes;
			if (archetypes == null || archetypes.Count == 0)
				return false;
			return SectorsFor(catalog, key).Any(code => archetypes.Contains(code, StringComparer.Ordinal));
		}

		/// <summary>The keys of the suggested templates.</summary>
		public static ISet<string> Suggested(string catalog, IEnumerable<string> keys, DepartmentOperatingProfile profile) =>
			new HashSet<string>((keys ?? Enumerable.Empty<string>()).Where(k => IsSuggested(catalog, k, profile)), StringComparer.Ordinal);

		/// <summary>The items with the suggested ones first; the order within each group is kept.</summary>
		public static List<T> SuggestedFirst<T>(IEnumerable<T> items, string catalog, Func<T, string> key, DepartmentOperatingProfile profile)
		{
			var list = (items ?? Enumerable.Empty<T>()).ToList();
			return list.Select((item, index) => (item, index, suggested: IsSuggested(catalog, key(item), profile)))
				.OrderBy(x => x.suggested ? 0 : 1).ThenBy(x => x.index).Select(x => x.item).ToList();
		}
	}
}
