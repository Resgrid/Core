using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.UnitRoles
{
	/// <summary>
	/// The code-defined catalog of predefined unit accountability-role sets ("crew templates") that
	/// departments can pick from when defining the roles on a unit. Everything here lives in code (NOT the
	/// database) by design, mirroring <see cref="Resgrid.Model.CustomStates.CustomStateTemplateCatalog"/>.
	///
	/// Suggested personnel roles are expressed by NAME (e.g. "Paramedic") because personnel roles are
	/// per-department; they are matched to the department's existing personnel roles when a template is
	/// applied. To add a template, add it to the relevant builder below.
	/// </summary>
	public static class UnitRoleTemplateCatalog
	{
		/// <summary>All crew templates across every discipline, in display order.</summary>
		public static IReadOnlyList<UnitRoleTemplate> All { get; } = BuildAll();

		/// <summary>Returns a single template by its stable id, or null if it does not exist.</summary>
		public static UnitRoleTemplate GetById(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			return All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Returns the templates for a given discipline/category.</summary>
		public static IReadOnlyList<UnitRoleTemplate> GetByCategory(string category)
		{
			return All.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		/// <summary>
		/// Returns templates filtered by an optional free-text query (matched against the name, category,
		/// description, keywords and role names). The picker UI also filters client-side for instant results.
		/// </summary>
		public static IReadOnlyList<UnitRoleTemplate> Search(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return All;

			var terms = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

			return All.Where(t => terms.All(term => t.SearchText.Contains(term))).ToList();
		}

		#region Builders

		private static UnitRoleTemplateRole R(string name, string suggestedPersonnelRole = null, bool required = false)
			=> new UnitRoleTemplateRole(name, suggestedPersonnelRole, required);

		private static UnitRoleTemplate T(string id, string name, string category, string description, string[] keywords, params UnitRoleTemplateRole[] roles)
			=> new UnitRoleTemplate { Id = id, Name = name, Category = category, Description = description, Keywords = keywords, Roles = roles.ToList() };

		private static IReadOnlyList<UnitRoleTemplate> BuildAll()
		{
			return new List<UnitRoleTemplate>
			{
				// ---- Fire ----
				T("fire-engine-company", "Fire Engine Company", "Fire",
					"A standard engine (pumper) company: company officer, apparatus driver/engineer and firefighters.",
					new[] { "engine", "pumper", "company", "suppression", "crew" },
					R("Company Officer", "Officer"), R("Driver/Engineer", "Driver/Operator"), R("Firefighter"), R("Firefighter")),

				T("fire-truck-company", "Fire Truck / Ladder Company", "Fire",
					"A truck (ladder/aerial) company focused on search, ventilation and rescue.",
					new[] { "ladder", "truck", "aerial", "tiller", "rescue", "ventilation" },
					R("Company Officer", "Officer"), R("Driver/Operator", "Driver/Operator"), R("Firefighter"), R("Firefighter"), R("Firefighter")),

				T("fire-rescue-squad", "Rescue / Squad Company", "Fire",
					"A heavy rescue or squad company for technical rescue and extrication work.",
					new[] { "rescue", "squad", "extrication", "technical", "heavy" },
					R("Rescue Officer", "Officer"), R("Driver/Operator", "Driver/Operator"), R("Rescue Technician", "Rescue Technician"), R("Rescue Technician", "Rescue Technician")),

				T("fire-brush-unit", "Brush / Wildland Unit", "Fire",
					"A wildland/brush unit for vegetation and wildland-urban interface fires.",
					new[] { "brush", "wildland", "grass", "wui", "interface" },
					R("Crew Boss", "Wildland"), R("Operator", "Driver/Operator"), R("Firefighter", "Wildland"), R("Firefighter", "Wildland")),

				T("fire-command", "Fire Command Staff", "Fire",
					"An incident command team for structure fires and larger incidents.",
					new[] { "command", "ics", "incident", "chief", "accountability" },
					R("Incident Commander", "Command Officer"), R("Safety Officer", "Safety Officer"), R("Operations"), R("Accountability")),

				// ---- EMS ----
				T("ems-ambulance-als", "Ambulance (ALS)", "EMS",
					"An advanced life support ambulance staffed with a paramedic and a driver.",
					new[] { "ambulance", "medic", "als", "paramedic", "transport" },
					R("Lead Medic", "Paramedic", required: true), R("Driver / EMT", "EMT")),

				T("ems-ambulance-bls", "Ambulance (BLS)", "EMS",
					"A basic life support ambulance staffed with EMTs.",
					new[] { "ambulance", "bls", "emt", "basic", "transport" },
					R("Attendant", "EMT", required: true), R("Driver", "EMT")),

				T("ems-supervisor", "EMS Supervisor / Chase", "EMS",
					"A single-role EMS supervisor or chase/fly car unit.",
					new[] { "supervisor", "chase", "fly car", "qrv", "field" },
					R("Supervisor", "Paramedic")),

				// ---- Law Enforcement ----
				T("le-patrol-unit", "Patrol Unit", "Law Enforcement",
					"A patrol vehicle staffed by one or two officers.",
					new[] { "police", "patrol", "cruiser", "officer", "deputy" },
					R("Primary Officer", "Officer"), R("Secondary Officer", "Officer")),

				T("le-k9-unit", "K9 Unit", "Law Enforcement",
					"A K9 unit with a certified handler.",
					new[] { "k9", "canine", "handler", "dog" },
					R("K9 Handler", "K9 Handler", required: true), R("Cover Officer", "Officer")),

				T("le-swat-team", "SWAT / Tactical Team", "Law Enforcement",
					"A tactical team for high-risk operations, including a tactical medic.",
					new[] { "swat", "tactical", "sert", "entry", "sniper" },
					R("Team Leader", "Officer"), R("Entry"), R("Entry"), R("Sniper/Observer", "Marksman"), R("Tactical Medic", "Paramedic", required: true)),

				// ---- Search and Rescue ----
				T("sar-ground-team", "SAR Ground Team", "Search and Rescue",
					"A ground search-and-rescue team with a leader, navigator and medic.",
					new[] { "sar", "ground", "search", "field team", "hasty" },
					R("Team Leader", "Team Leader"), R("Navigator"), R("Medic", "First Aid"), R("Searcher"), R("Searcher")),

				T("sar-k9-team", "SAR K9 Team", "Search and Rescue",
					"A search dog team with handler, flanker and radio operator.",
					new[] { "sar", "k9", "canine", "air scent", "trailing", "cadaver" },
					R("K9 Handler", "K9 Handler", required: true), R("Flanker"), R("Radio Operator")),

				T("sar-swiftwater-team", "Swiftwater Rescue Team", "Search and Rescue",
					"A swiftwater/flood rescue team with certified rescue swimmers.",
					new[] { "swiftwater", "water rescue", "flood", "boat", "river" },
					R("Team Leader", "Swiftwater Technician"), R("Rescue Swimmer", "Swiftwater Technician", required: true), R("Rope Tender"), R("Spotter")),

				// ---- Emergency / Disaster Response ----
				T("er-incident-command", "Incident Command Team", "Emergency Response",
					"A general ICS command and general staff structure for any incident type.",
					new[] { "ics", "command", "general staff", "nims", "unified command" },
					R("Incident Commander"), R("Operations Section Chief"), R("Planning Section Chief"), R("Logistics Section Chief"), R("Finance/Admin Section Chief")),

				T("dr-usar-squad", "USAR Squad", "Disaster Response",
					"An urban search-and-rescue squad for structural collapse operations.",
					new[] { "usar", "collapse", "task force", "disaster", "shoring" },
					R("Squad Leader", "Rescue Technician"), R("Rescue Specialist", "Rescue Technician", required: true), R("Medical Specialist", "Paramedic", required: true), R("Hazmat Specialist", "Hazmat Technician")),

				T("dr-eoc-team", "Emergency Operations Center", "Disaster Response",
					"An EOC staffing template for coordinating a large-scale response.",
					new[] { "eoc", "operations center", "coordination", "activation" },
					R("EOC Director"), R("Operations"), R("Planning"), R("Logistics"), R("Public Information Officer", "PIO")),

				// ---- Security ----
				T("sec-patrol", "Security Patrol", "Security",
					"A mobile security patrol with a supervisor and officers.",
					new[] { "security", "guard", "patrol", "site", "campus" },
					R("Shift Supervisor", "Supervisor"), R("Patrol Officer", "Security Officer"), R("Patrol Officer", "Security Officer")),

				T("sec-event-team", "Event Security Team", "Security",
					"A security detail for events and venues.",
					new[] { "event", "venue", "crowd", "detail", "guard" },
					R("Team Lead", "Supervisor"), R("Officer", "Security Officer"), R("Officer", "Security Officer"), R("Officer", "Security Officer")),

				// ---- Industrial Management ----
				T("ind-fire-brigade", "Industrial Fire Brigade", "Industrial Management",
					"A plant/industrial fire brigade crew for on-site emergency response.",
					new[] { "industrial", "brigade", "plant", "refinery", "on-site" },
					R("Brigade Leader", "Fire Brigade Leader"), R("Nozzle", "Fire Brigade Member"), R("Backup", "Fire Brigade Member"), R("Pump Operator", "Driver/Operator")),

				T("ind-hazmat-team", "Hazmat Team", "Industrial Management",
					"A hazardous materials response team with certified technicians.",
					new[] { "hazmat", "hazardous materials", "decon", "spill", "cbrne" },
					R("Team Leader", "Hazmat Technician"), R("Entry Team", "Hazmat Technician", required: true), R("Entry Team", "Hazmat Technician", required: true), R("Decon"), R("Safety Officer", "Safety Officer")),

				T("ind-work-crew", "Utility / Work Crew", "Industrial Management",
					"A general industrial or utility work crew.",
					new[] { "utility", "work crew", "maintenance", "operator", "labor" },
					R("Crew Lead", "Supervisor"), R("Operator", "Equipment Operator"), R("Laborer"), R("Laborer")),

				// ---- Commodity Delivery / Logistics ----
				T("log-delivery-vehicle", "Delivery Vehicle", "Commodity Delivery",
					"A delivery vehicle crew with a driver and helper.",
					new[] { "delivery", "courier", "driver", "logistics", "route" },
					R("Driver", "CDL", required: true), R("Helper")),

				T("log-logistics-truck", "Logistics Truck Crew", "Commodity Delivery",
					"A freight/logistics truck crew for loading and hauling.",
					new[] { "logistics", "freight", "cargo", "haul", "supply" },
					R("Driver", "CDL", required: true), R("Loader"), R("Loader")),

				// ---- General ----
				T("gen-two-person", "Two-Person Crew", "General",
					"A simple two-person crew for any small unit.",
					new[] { "general", "basic", "two person", "pair" },
					R("Lead"), R("Assistant")),

				T("gen-four-person", "Four-Person Crew", "General",
					"A general four-person crew with a lead, driver and two members.",
					new[] { "general", "standard", "four person", "crew" },
					R("Officer / Lead"), R("Driver"), R("Crew"), R("Crew"))
			};
		}

		#endregion Builders
	}
}
