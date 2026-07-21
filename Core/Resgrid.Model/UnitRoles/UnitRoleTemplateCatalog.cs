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
				// ---- Fire Departments ----
				T("fire-engine-company", "Fire Engine Company", "Fire Departments",
					"A standard engine (pumper) company: company officer, apparatus driver/engineer and firefighters.",
					new[] { "engine", "pumper", "company", "suppression", "crew" },
					R("Company Officer", "Officer"), R("Driver/Engineer", "Driver/Operator"), R("Firefighter"), R("Firefighter")),

				T("fire-truck-company", "Fire Truck / Ladder Company", "Fire Departments",
					"A truck (ladder/aerial) company focused on search, ventilation and rescue.",
					new[] { "ladder", "truck", "aerial", "tiller", "rescue", "ventilation" },
					R("Company Officer", "Officer"), R("Driver/Operator", "Driver/Operator"), R("Firefighter"), R("Firefighter"), R("Firefighter")),

				T("fire-rescue-squad", "Rescue / Squad Company", "Fire Departments",
					"A heavy rescue or squad company for technical rescue and extrication work.",
					new[] { "rescue", "squad", "extrication", "technical", "heavy" },
					R("Rescue Officer", "Officer"), R("Driver/Operator", "Driver/Operator"), R("Rescue Technician", "Rescue Technician"), R("Rescue Technician", "Rescue Technician")),

				T("fire-brush-unit", "Brush / Wildland Unit", "Fire Departments",
					"A wildland/brush unit for vegetation and wildland-urban interface fires.",
					new[] { "brush", "wildland", "grass", "wui", "interface" },
					R("Crew Boss", "Wildland"), R("Operator", "Driver/Operator"), R("Firefighter", "Wildland"), R("Firefighter", "Wildland")),

				T("fire-command", "Fire Command Staff", "Fire Departments",
					"An incident command team for structure fires and larger incidents.",
					new[] { "command", "ics", "incident", "chief", "accountability" },
					R("Incident Commander", "Command Officer"), R("Safety Officer", "Safety Officer"), R("Operations"), R("Accountability")),

				T("fire-tanker-tender", "Tanker / Tender Crew", "Fire Departments",
					"A water-supply apparatus crew for rural and non-hydrant operations.",
					new[] { "tanker", "tender", "water shuttle", "rural", "portable tank" },
					R("Driver/Operator", "Driver/Operator"), R("Dump Site Operator"), R("Fill Site Operator")),

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

				T("ems-critical-care", "Critical Care Transport", "EMS",
					"An interfacility or specialty transport team with advanced clinical roles.",
					new[] { "critical care", "cct", "interfacility", "specialty", "transport", "nurse" },
					R("Critical Care Paramedic", "Critical Care Paramedic", required: true), R("Transport Nurse", "Nurse"), R("Driver / EMT", "EMT")),

				T("ems-mci-team", "Mass-Casualty Medical Team", "EMS",
					"A deployable medical team for triage, treatment and transport coordination.",
					new[] { "mci", "mass casualty", "triage", "treatment", "transport group" },
					R("Medical Group Supervisor", "Paramedic"), R("Triage Lead", "Paramedic"), R("Treatment Lead", "Paramedic"), R("Transport Coordinator", "EMT")),

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

				T("le-traffic-unit", "Traffic Enforcement Unit", "Law Enforcement",
					"A traffic or highway unit with enforcement, collision and traffic-control seats.",
					new[] { "traffic", "highway", "motor", "collision", "road closure" },
					R("Primary Officer", "Officer"), R("Collision Investigator"), R("Traffic Control")),

				T("le-investigations-team", "Investigations Team", "Law Enforcement",
					"An investigative unit for case leadership, evidence, interviews and scene documentation.",
					new[] { "detective", "investigation", "evidence", "crime scene", "interview" },
					R("Lead Investigator", "Detective"), R("Evidence Technician"), R("Interviewer"), R("Scene Security", "Officer")),

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

				T("sar-rope-team", "Technical / Rope Rescue Team", "Search and Rescue",
					"A high- or low-angle rescue team with rigging, edge and medical responsibilities.",
					new[] { "rope", "high angle", "low angle", "cliff", "rigging", "technical rescue" },
					R("Rescue Team Leader", "Rope Rescue Technician"), R("Lead Rigger", "Rope Rescue Technician", required: true), R("Edge Attendant"), R("Rescuer", "Rope Rescue Technician"), R("Medic", "Paramedic")),

				// ---- Emergency Management ----
				T("er-incident-command", "Incident Command Team", "Emergency Management",
					"A general ICS command and general staff structure for any incident type.",
					new[] { "ics", "command", "general staff", "nims", "unified command" },
					R("Incident Commander"), R("Operations Section Chief"), R("Planning Section Chief"), R("Logistics Section Chief"), R("Finance/Admin Section Chief")),

				T("dr-eoc-team", "Emergency Operations Center", "Emergency Management",
					"An EOC staffing template for coordinating a large-scale response.",
					new[] { "eoc", "operations center", "coordination", "activation" },
					R("EOC Director"), R("Operations"), R("Planning"), R("Logistics"), R("Public Information Officer", "PIO")),

				T("em-damage-assessment", "Damage Assessment Team", "Emergency Management",
					"A field team for rapid impact surveys, documentation and situation reporting.",
					new[] { "damage assessment", "survey", "impact", "gis", "situation report" },
					R("Team Lead"), R("Building Assessor"), R("GIS / Mapping"), R("Documentation"), R("Driver / Safety")),

				// ---- Disaster Response ----
				T("dr-usar-squad", "USAR Squad", "Disaster Response",
					"An urban search-and-rescue squad for structural collapse operations.",
					new[] { "usar", "collapse", "task force", "disaster", "shoring" },
					R("Squad Leader", "Rescue Technician"), R("Rescue Specialist", "Rescue Technician", required: true), R("Medical Specialist", "Paramedic", required: true), R("Hazmat Specialist", "Hazmat Technician")),

				T("dr-shelter-team", "Disaster Shelter Team", "Disaster Response",
					"A shelter operations team for registration, dormitory, feeding, medical and logistics support.",
					new[] { "shelter", "mass care", "evacuee", "feeding", "registration", "relief" },
					R("Shelter Manager"), R("Registration"), R("Dormitory Lead"), R("Feeding Lead"), R("Medical / First Aid"), R("Logistics")),

				T("dr-relief-distribution", "Relief Distribution Team", "Disaster Response",
					"A commodity distribution crew for receiving, inventory, traffic flow and public handoff.",
					new[] { "relief", "distribution", "commodities", "supplies", "pod", "humanitarian" },
					R("Site Lead"), R("Receiving"), R("Inventory"), R("Loader"), R("Traffic Control"), R("Public Handoff")),

				// ---- Security Companies ----
				T("sec-patrol", "Security Patrol", "Security Companies",
					"A mobile security patrol with a supervisor and officers.",
					new[] { "security", "guard", "patrol", "site", "campus" },
					R("Shift Supervisor", "Supervisor"), R("Patrol Officer", "Security Officer"), R("Patrol Officer", "Security Officer")),

				T("sec-alarm-response", "Alarm Response Team", "Security Companies",
					"A security response unit for intrusion, fire, duress and facility alarms.",
					new[] { "alarm", "intrusion", "duress", "facility", "response" },
					R("Response Lead", "Supervisor"), R("Primary Officer", "Security Officer"), R("Cover Officer", "Security Officer"), R("Control Room Liaison")),

				T("sec-protective-detail", "Protective Services Detail", "Security Companies",
					"An executive or dignitary protection team with close protection, advance and driving roles.",
					new[] { "executive protection", "vip", "dignitary", "protective detail", "advance" },
					R("Detail Leader"), R("Close Protection"), R("Advance Agent"), R("Protective Driver"), R("Medical Support")),

				// ---- Event Medical / Security ----
				T("sec-event-team", "Event Security Team", "Event Medical / Security",
					"A security detail for events and venues.",
					new[] { "event", "venue", "crowd", "detail", "guard" },
					R("Team Lead", "Supervisor"), R("Officer", "Security Officer"), R("Officer", "Security Officer"), R("Officer", "Security Officer")),

				T("event-medical-roving", "Event Roving Medical Team", "Event Medical / Security",
					"A mobile first-aid team for festivals, sporting events and large venues.",
					new[] { "event medical", "roving", "festival", "concert", "stadium", "first aid" },
					R("Team Lead", "Paramedic"), R("Medic", "Paramedic"), R("EMT", "EMT"), R("Event Liaison")),

				T("event-aid-station", "Event Medical Aid Station", "Event Medical / Security",
					"A fixed medical post for triage, treatment, documentation and transport coordination.",
					new[] { "aid station", "first aid", "treatment", "patient", "event", "transport" },
					R("Aid Station Lead", "Paramedic"), R("Triage"), R("Treatment"), R("Patient Tracking"), R("Transport Coordinator")),

				T("event-unified-team", "Event Unified Response Team", "Event Medical / Security",
					"A joint venue team combining operations, medical, security and communications seats.",
					new[] { "unified", "venue", "operations", "medical", "security", "communications" },
					R("Event Commander"), R("Venue Operations"), R("Medical Lead", "Paramedic"), R("Security Lead", "Supervisor"), R("Communications")),

				// ---- Industrial Response ----
				T("ind-fire-brigade", "Industrial Fire Brigade", "Industrial Response",
					"A plant/industrial fire brigade crew for on-site emergency response.",
					new[] { "industrial", "brigade", "plant", "refinery", "on-site" },
					R("Brigade Leader", "Fire Brigade Leader"), R("Nozzle", "Fire Brigade Member"), R("Backup", "Fire Brigade Member"), R("Pump Operator", "Driver/Operator")),

				T("ind-hazmat-team", "Hazmat Team", "Industrial Response",
					"A hazardous materials response team with certified technicians.",
					new[] { "hazmat", "hazardous materials", "decon", "spill", "cbrne" },
					R("Team Leader", "Hazmat Technician"), R("Entry Team", "Hazmat Technician", required: true), R("Entry Team", "Hazmat Technician", required: true), R("Decon"), R("Safety Officer", "Safety Officer")),

				T("ind-work-crew", "Utility / Work Crew", "Industrial Response",
					"A general industrial or utility work crew.",
					new[] { "utility", "work crew", "maintenance", "operator", "labor" },
					R("Crew Lead", "Supervisor"), R("Operator", "Equipment Operator"), R("Laborer"), R("Laborer")),

				T("ind-confined-space", "Confined Space Rescue Team", "Industrial Response",
					"An industrial confined-space team with entry, backup, rigging, monitoring and medical seats.",
					new[] { "confined space", "permit space", "entry", "rescue", "monitoring", "rigging" },
					R("Rescue Team Leader"), R("Entry Rescuer", "Confined Space Technician", required: true), R("Backup Rescuer", "Confined Space Technician", required: true), R("Rigging"), R("Atmospheric Monitor"), R("Medic", "Paramedic")),

				// ---- Delivery Companies ----
				T("log-delivery-vehicle", "Delivery Vehicle", "Delivery Companies",
					"A delivery vehicle crew with a driver and helper.",
					new[] { "delivery", "courier", "driver", "logistics", "route" },
					R("Driver", "CDL", required: true), R("Helper")),

				T("log-logistics-truck", "Logistics Truck Crew", "Delivery Companies",
					"A freight/logistics truck crew for loading and hauling.",
					new[] { "logistics", "freight", "cargo", "haul", "supply" },
					R("Driver", "CDL", required: true), R("Loader"), R("Loader")),

				T("log-last-mile", "Last-Mile Delivery Van", "Delivery Companies",
					"A parcel or courier vehicle with route, delivery and loading responsibilities.",
					new[] { "last mile", "parcel", "courier", "van", "route", "package" },
					R("Route Driver"), R("Delivery Associate"), R("Loader")),

				T("log-route-support", "Route Recovery / Support Team", "Delivery Companies",
					"A support unit for disabled vehicles, overflow routes and cargo transfers.",
					new[] { "route recovery", "fleet support", "overflow", "cargo transfer", "breakdown" },
					R("Support Lead"), R("Relief Driver"), R("Cargo Transfer"), R("Fleet Technician")),

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
