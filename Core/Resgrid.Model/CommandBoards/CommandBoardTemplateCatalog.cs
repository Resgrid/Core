using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.CommandBoards
{
	/// <summary>
	/// Searchable, code-only starter command boards. Selecting one creates an unsaved definition that a
	/// department can rename, expand, trim, and change requirements on before saving.
	/// </summary>
	public static class CommandBoardTemplateCatalog
	{
		/// <summary>Lane identification palette — matches the app's LANE_COLORS swatches.</summary>
		private static readonly string[] LanePalette = { "#e74c3c", "#e67e22", "#f1c40f", "#2ecc71", "#1abc9c", "#3498db", "#9b59b6", "#7f8c8d" };

		public static IReadOnlyList<CommandBoardTemplate> All { get; } = BuildAll();

		public static CommandBoardTemplate GetById(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			return All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		public static IReadOnlyList<CommandBoardTemplate> GetByCategory(string category)
		{
			return All.Where(template => string.Equals(template.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		public static IReadOnlyList<CommandBoardTemplate> Search(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return All;

			var terms = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
			return All.Where(template => terms.All(term => template.SearchText.Contains(term))).ToList();
		}

		#region Builders

		private static CommandBoardTemplateLane L(string name, CommandNodeType laneType, string description,
			string[] unitTypes = null, string[] personnelRoles = null, bool forceRequirements = false,
			int minUnits = 0, int maxUnits = 0, int minUnitPersonnel = 0, int maxUnitPersonnel = 0, int minTimeInRole = 0, int maxTimeInRole = 0)
		{
			return new CommandBoardTemplateLane
			{
				Name = name,
				LaneType = laneType,
				Description = description,
				SuggestedUnitTypes = unitTypes ?? Array.Empty<string>(),
				SuggestedPersonnelRoles = personnelRoles ?? Array.Empty<string>(),
				ForceRequirements = forceRequirements,
				MinUnits = minUnits,
				MaxUnits = maxUnits,
				MinUnitPersonnel = minUnitPersonnel,
				MaxUnitPersonnel = maxUnitPersonnel,
				MinTimeInRole = minTimeInRole,
				MaxTimeInRole = maxTimeInRole
			};
		}

		/// <summary>
		/// Deterministic lane color: recognizable functions get a conventional color (medical red,
		/// water blue, safety yellow, staging gray); everything else cycles the palette.
		/// </summary>
		private static string AutoColor(CommandBoardTemplateLane lane, int index)
		{
			var name = lane.Name?.ToLowerInvariant() ?? string.Empty;

			if (lane.LaneType == CommandNodeType.Staging || name.Contains("staging") || name.Contains("accountability"))
				return "#7f8c8d";
			if (name.Contains("medical") || name.Contains("treatment") || name.Contains("triage") || name.Contains("aid station"))
				return "#e74c3c";
			if (name.Contains("safety"))
				return "#f1c40f";
			if (name.Contains("water") || name.Contains("boat") || name.Contains("swiftwater"))
				return "#3498db";
			if (name.Contains("rapid intervention") || name.Contains("backup"))
				return "#e67e22";

			return LanePalette[index % LanePalette.Length];
		}

		private static CommandBoardTemplate T(string id, string name, string category, string description,
			string[] keywords, bool timer, int timerMinutes, params CommandBoardTemplateLane[] lanes)
		{
			// Every example ships with lane colors: explicit colors win, the rest are auto-assigned.
			for (var i = 0; i < lanes.Length; i++)
			{
				if (string.IsNullOrWhiteSpace(lanes[i].Color))
					lanes[i].Color = AutoColor(lanes[i], i);
			}

			return new CommandBoardTemplate
			{
				Id = id,
				Name = name,
				Category = category,
				Description = description,
				Keywords = keywords,
				Timer = timer,
				TimerMinutes = timerMinutes,
				Lanes = lanes.ToList()
			};
		}

		private static IReadOnlyList<CommandBoardTemplate> BuildAll()
		{
			return new List<CommandBoardTemplate>
			{
				// ---- Fire Departments ----
				T("fire-residential-structure", "Residential Structure Fire", "Fire Departments",
					"A first-alarm residential fire board with tactical groups, water supply, rapid intervention and staging.",
					new[] { "house", "dwelling", "first alarm", "rit", "residential" }, true, 15,
					L("Fire Attack", CommandNodeType.Group, "Interior fire control and confinement.", new[] { "Engine", "Pumper" }, minUnitPersonnel: 2, maxTimeInRole: 30),
					L("Primary Search", CommandNodeType.Group, "Primary life-safety search.", new[] { "Truck", "Ladder", "Rescue" }, minUnitPersonnel: 2, maxTimeInRole: 30),
					L("Ventilation", CommandNodeType.Group, "Coordinate horizontal and vertical ventilation.", new[] { "Truck", "Ladder" }),
					L("Water Supply", CommandNodeType.Group, "Establish and maintain a sustained water source.", new[] { "Engine", "Tanker", "Tender" }, minUnits: 1),
					L("Rapid Intervention", CommandNodeType.Group, "Dedicated firefighter rescue team.", new[] { "Rescue", "Squad", "Engine" }, new[] { "Firefighter" }, minUnits: 1, minUnitPersonnel: 2),
					L("Staging", CommandNodeType.Staging, "Track unassigned responding resources.")),

				T("fire-commercial-structure", "Commercial Structure Fire", "Fire Departments",
					"A scalable commercial-building board organized by geographic divisions and functional groups.",
					new[] { "commercial", "warehouse", "high rise", "second alarm", "divisions" }, true, 15,
					L("Division A", CommandNodeType.Division, "Street/address-side operations."),
					L("Division B", CommandNodeType.Division, "Left-side exposure and operations."),
					L("Division C", CommandNodeType.Division, "Rear-side exposure and operations."),
					L("Division D", CommandNodeType.Division, "Right-side exposure and operations."),
					L("Roof Division", CommandNodeType.Division, "Roof access, ventilation and conditions.", new[] { "Truck", "Ladder" }),
					L("Search Group", CommandNodeType.Group, "Primary and secondary searches.", new[] { "Truck", "Ladder", "Rescue" }),
					L("Rapid Intervention", CommandNodeType.Group, "Dedicated firefighter rescue team.", new[] { "Rescue", "Squad", "Engine" }, minUnits: 1, minUnitPersonnel: 2),
					L("Staging", CommandNodeType.Staging, "Manage additional alarms and relief companies.")),

				T("fire-wildland-wui", "Wildland / WUI Fire", "Fire Departments",
					"A wildland-urban interface board for geographic divisions, structure protection and water support.",
					new[] { "brush", "wildfire", "wildland", "wui", "vegetation", "strike team" }, true, 30,
					L("Division Alpha", CommandNodeType.Division, "Anchor, flank and contain Division Alpha."),
					L("Division Bravo", CommandNodeType.Division, "Anchor, flank and contain Division Bravo."),
					L("Structure Protection", CommandNodeType.Group, "Assess and protect threatened structures.", new[] { "Engine", "Brush" }),
					L("Water Supply", CommandNodeType.Group, "Tender shuttle, fill sites and portable tanks.", new[] { "Tanker", "Tender" }),
					L("Dozer / Heavy Equipment", CommandNodeType.TaskForce, "Coordinate line construction and heavy equipment."),
					L("Staging", CommandNodeType.Staging, "Check in and deploy incoming resources.")),

				T("fire-hazmat", "Hazardous Materials Incident", "Fire Departments",
					"A hazmat branch layout separating entry, backup, decontamination, medical monitoring and support.",
					new[] { "hazmat", "chemical", "spill", "cbrne", "decon", "entry" }, true, 20,
					L("Hazmat Branch", CommandNodeType.Branch, "Coordinate all hazardous-materials operations."),
					L("Entry Group", CommandNodeType.Group, "Hot-zone reconnaissance, control and product identification.", new[] { "Hazmat" }, new[] { "Hazmat Technician" }, true, minUnitPersonnel: 2, maxTimeInRole: 30),
					L("Backup Group", CommandNodeType.Group, "Dedicated backup for the entry team.", new[] { "Hazmat" }, new[] { "Hazmat Technician" }, true, minUnitPersonnel: 2),
					L("Decontamination", CommandNodeType.Group, "Technical and emergency decontamination corridor."),
					L("Medical Monitoring", CommandNodeType.Group, "Pre-entry and post-entry medical monitoring.", new[] { "Ambulance", "Medic" }, new[] { "Paramedic", "EMT" }),
					L("Staging", CommandNodeType.Staging, "Cold-zone resource accountability.")),

				// ---- EMS ----
				T("ems-mass-casualty", "Mass-Casualty Incident", "EMS",
					"A standard MCI board for triage, categorized treatment, transport coordination and resource staging.",
					new[] { "mci", "multiple patients", "triage", "treatment", "transport", "disaster medical" }, true, 15,
					L("Triage Group", CommandNodeType.Group, "Initial patient sorting, tagging and patient count.", null, new[] { "Paramedic", "EMT" }),
					L("Immediate Treatment", CommandNodeType.Group, "Treatment area for immediate/red patients."),
					L("Delayed Treatment", CommandNodeType.Group, "Treatment area for delayed/yellow patients."),
					L("Minor Treatment", CommandNodeType.Group, "Treatment area for minor/green patients."),
					L("Transport Group", CommandNodeType.Group, "Ambulance loading, destination coordination and tracking.", new[] { "Ambulance", "Medic" }, minUnits: 2),
					L("Ambulance Staging", CommandNodeType.Staging, "Check in and sequence transport units.", new[] { "Ambulance", "Medic" })),

				T("ems-event-medical", "Planned Event Medical", "EMS",
					"A medical operations board for a festival, sporting event, fair or other planned gathering.",
					new[] { "festival", "concert", "sporting event", "aid station", "roving team", "special event" }, false, 0,
					L("Medical Command", CommandNodeType.Branch, "Coordinate event medical operations and venue command."),
					L("Main Aid Station", CommandNodeType.Group, "Fixed treatment and documentation location."),
					L("Roving Team Alpha", CommandNodeType.TaskForce, "Mobile first-response team.", null, new[] { "Paramedic", "EMT" }, maxTimeInRole: 120),
					L("Roving Team Bravo", CommandNodeType.TaskForce, "Mobile first-response team.", null, new[] { "Paramedic", "EMT" }, maxTimeInRole: 120),
					L("Transport Group", CommandNodeType.Group, "Coordinate ambulance access, loading and hospitals.", new[] { "Ambulance", "Medic" }),
					L("Medical Logistics", CommandNodeType.Group, "Restock supplies, oxygen, AEDs and responder rehab.")),

				// ---- Law Enforcement ----
				T("law-enforcement-tactical", "Law Enforcement Tactical Incident", "Law Enforcement",
					"A high-risk incident board for containment, tactical operations, arrest, negotiation and staging.",
					new[] { "police", "sheriff", "swat", "barricade", "hostage", "high risk" }, true, 20,
					L("Inner Perimeter", CommandNodeType.Group, "Immediate containment and direct observation."),
					L("Outer Perimeter", CommandNodeType.Group, "Traffic, public and media control outside the hot zone."),
					L("Tactical Group", CommandNodeType.Group, "Coordinate entry and tactical teams.", new[] { "SWAT", "Tactical" }, new[] { "SWAT", "Tactical Officer" }),
					L("Negotiations", CommandNodeType.Group, "Crisis communication and intelligence support."),
					L("Arrest Team", CommandNodeType.TaskForce, "Custody, search and prisoner movement."),
					L("Staging", CommandNodeType.Staging, "Check in responding personnel and specialty assets.")),

				T("law-enforcement-search", "Manhunt / Law Enforcement Search", "Law Enforcement",
					"A containment and search board for a fleeing subject, evidence search or area canvass.",
					new[] { "manhunt", "suspect", "containment", "canvass", "k9", "evidence" }, false, 0,
					L("Search Branch", CommandNodeType.Branch, "Coordinate containment and search strategy."),
					L("North Division", CommandNodeType.Division, "Northern search and containment area."),
					L("South Division", CommandNodeType.Division, "Southern search and containment area."),
					L("K9 Group", CommandNodeType.Group, "Canine search teams and flankers.", new[] { "K9" }, new[] { "K9 Handler" }),
					L("Aviation Group", CommandNodeType.Group, "Air observation and sensor coordination.", new[] { "Helicopter", "Drone", "UAS" }),
					L("Staging", CommandNodeType.Staging, "Personnel check-in, briefing and deployment.")),

				// ---- Search and Rescue ----
				T("sar-missing-person", "Wilderness Missing Person Search", "Search and Rescue",
					"A field search board for planning, hasty teams, ground teams, canine resources and subject care.",
					new[] { "sar", "lost person", "wilderness", "ground search", "hasty", "clue" }, false, 0,
					L("Search Management", CommandNodeType.Branch, "Strategy, maps, clues, team tasks and debriefs."),
					L("Hasty Team", CommandNodeType.TaskForce, "Rapid search of high-probability routes and locations."),
					L("Ground Team Alpha", CommandNodeType.TaskForce, "Assigned search segment."),
					L("Ground Team Bravo", CommandNodeType.TaskForce, "Assigned search segment."),
					L("K9 Group", CommandNodeType.Group, "Canine teams and flankers.", new[] { "K9" }, new[] { "K9 Handler" }),
					L("Medical / Extraction", CommandNodeType.Group, "Subject stabilization and evacuation planning.", new[] { "Ambulance", "Rescue" }),
					L("Staging", CommandNodeType.Staging, "Team check-in, briefing and equipment cache.")),

				T("sar-technical-rescue", "Technical / Rope Rescue", "Search and Rescue",
					"A technical rescue board separating rescue, rigging, edge safety, medical and support functions.",
					new[] { "rope", "high angle", "low angle", "cliff", "cave", "technical rescue" }, true, 20,
					L("Rescue Group", CommandNodeType.Group, "Coordinate the rescue plan and rescue team."),
					L("Rigging Team", CommandNodeType.TaskForce, "Build, inspect and operate rope systems.", null, new[] { "Rope Rescue Technician" }),
					L("Edge Team", CommandNodeType.TaskForce, "Edge protection, litter transition and communications."),
					L("Medical Group", CommandNodeType.Group, "Patient access, stabilization and packaging.", new[] { "Ambulance", "Medic" }, new[] { "Paramedic", "EMT" }),
					L("Safety", CommandNodeType.Group, "Independent hazard and system monitoring."),
					L("Staging", CommandNodeType.Staging, "Personnel and equipment accountability.")),

				T("sar-swiftwater", "Swiftwater / Flood Rescue", "Search and Rescue",
					"A water-rescue board for upstream safety, downstream containment, boat teams and shore support.",
					new[] { "swiftwater", "flood", "river", "boat", "water rescue" }, true, 15,
					L("Rescue Group", CommandNodeType.Group, "Coordinate rescue swimmers, boats and shore teams.", new[] { "Boat", "Rescue" }),
					L("Upstream Safety", CommandNodeType.Group, "Watch for and communicate upstream hazards."),
					L("Downstream Safety", CommandNodeType.Group, "Contain rescuers or subjects swept downstream."),
					L("Boat Team", CommandNodeType.TaskForce, "Boat launch, operations and recovery.", new[] { "Boat" }, minUnitPersonnel: 2),
					L("Medical Group", CommandNodeType.Group, "Patient warming, treatment and transport.", new[] { "Ambulance", "Medic" }),
					L("Staging", CommandNodeType.Staging, "Water-rescue resources and PPE accountability.")),

				// ---- Emergency Management ----
				T("em-eoc-activation", "Emergency Operations Center Activation", "Emergency Management",
					"A full EOC starter board using command and general staff functions for multi-agency coordination.",
					new[] { "eoc", "ics", "coordination", "nims", "general staff", "policy group" }, false, 0,
					L("EOC Director / Unified Command", CommandNodeType.UnifiedCommand, "Executive direction, priorities and policy coordination."),
					L("Operations Section", CommandNodeType.Branch, "Coordinate field operations and agency representatives."),
					L("Planning Section", CommandNodeType.Branch, "Situation status, resource status and action planning."),
					L("Logistics Section", CommandNodeType.Branch, "Facilities, supplies, communications and responder support."),
					L("Finance / Administration", CommandNodeType.Branch, "Time, procurement, compensation and cost tracking."),
					L("Public Information", CommandNodeType.Group, "Joint information, warnings and media coordination.", null, new[] { "Public Information Officer", "PIO" })),

				T("em-severe-weather", "Severe Weather Coordination", "Emergency Management",
					"A coordination board for tornado, hurricane, winter storm, extreme heat or other regional weather impacts.",
					new[] { "storm", "hurricane", "tornado", "winter", "weather", "damage assessment" }, false, 0,
					L("Warning and Intelligence", CommandNodeType.Group, "Forecasts, alerts, situational awareness and GIS."),
					L("Damage Assessment", CommandNodeType.Group, "Initial and detailed impact assessment."),
					L("Public Works", CommandNodeType.Branch, "Roads, utilities, debris and infrastructure priorities."),
					L("Mass Care", CommandNodeType.Branch, "Shelter, feeding and access/functional needs."),
					L("Logistics", CommandNodeType.Branch, "Resource requests, distribution and mutual aid."),
					L("Public Information", CommandNodeType.Group, "Protective actions, rumor control and media updates.")),

				// ---- Disaster Response ----
				T("disaster-field-operations", "Disaster Field Operations", "Disaster Response",
					"A field operations board for urban search and rescue, medical care, assessment, logistics and communications.",
					new[] { "disaster", "usar", "earthquake", "collapse", "task force", "deployment" }, true, 30,
					L("USAR Branch", CommandNodeType.Branch, "Coordinate search, rescue and structural specialists.", new[] { "Rescue", "USAR" }),
					L("Medical Group", CommandNodeType.Group, "Responder and survivor medical operations.", new[] { "Ambulance", "Medic" }),
					L("Damage Assessment", CommandNodeType.Group, "Rapid structural and community impact assessment."),
					L("Logistics Base", CommandNodeType.Group, "Cache, food, water, fuel and team sustainment."),
					L("Communications", CommandNodeType.Group, "Radio plan, interoperability and communications repair."),
					L("Staging", CommandNodeType.Staging, "Incoming teams, equipment and mission assignments.")),

				T("disaster-shelter-relief", "Shelter and Relief Distribution", "Disaster Response",
					"A humanitarian services board for sheltering, feeding, relief supplies, registration and site security.",
					new[] { "relief", "humanitarian", "shelter", "feeding", "distribution", "donations" }, false, 0,
					L("Shelter Operations", CommandNodeType.Branch, "Dormitory, sanitation and resident services."),
					L("Registration", CommandNodeType.Group, "Resident intake, records and reunification information."),
					L("Feeding", CommandNodeType.Group, "Meal production, dietary needs and distribution."),
					L("Medical Support", CommandNodeType.Group, "First aid, medication support and referral."),
					L("Relief Distribution", CommandNodeType.Group, "Receive, stage and distribute relief commodities."),
					L("Logistics", CommandNodeType.Group, "Supplies, facilities, staffing and transport."),
					L("Site Security", CommandNodeType.Group, "Access control, safety and traffic flow.")),

				// ---- Security Companies ----
				T("security-facility-incident", "Facility Security Incident", "Security Companies",
					"A private-security board for facility response, access control, perimeter operations and agency liaison.",
					new[] { "security", "facility", "campus", "alarm", "intrusion", "guard" }, false, 0,
					L("Security Command", CommandNodeType.UnifiedCommand, "Coordinate site leadership and public-safety agencies."),
					L("Interior Response", CommandNodeType.Group, "Assess and respond inside the facility."),
					L("Access Control", CommandNodeType.Group, "Lockdown, credentials and controlled entry."),
					L("Perimeter Group", CommandNodeType.Group, "Secure exterior zones and control pedestrians."),
					L("Agency Liaison", CommandNodeType.Group, "Meet and brief fire, EMS and law enforcement."),
					L("Staging", CommandNodeType.Staging, "Security teams and responding resources.")),

				// ---- Event Medical / Security ----
				T("event-unified-operations", "Large Event Medical and Security", "Event Medical / Security",
					"A unified event board combining medical, security, crowd, access and transport operations.",
					new[] { "concert", "festival", "fair", "stadium", "venue", "special event", "unified" }, false, 0,
					L("Event Unified Command", CommandNodeType.UnifiedCommand, "Venue, medical, security and public-safety coordination."),
					L("Medical Branch", CommandNodeType.Branch, "Aid stations, roving teams and patient tracking."),
					L("Main Aid Station", CommandNodeType.Group, "Fixed medical treatment area."),
					L("Security Branch", CommandNodeType.Branch, "Security teams and incident response."),
					L("Access Control", CommandNodeType.Group, "Credentials, gates and prohibited items."),
					L("Crowd Management", CommandNodeType.Group, "Density, queues, barriers and audience movement."),
					L("Transport / Public Safety Staging", CommandNodeType.Staging, "Ambulances and public-safety resources.")),

				T("event-rapid-response", "Event Rapid Response", "Event Medical / Security",
					"A compact board for small and medium events with roving medical, security response and venue liaison teams.",
					new[] { "event team", "roving", "first aid", "security response", "venue" }, false, 0,
					L("Event Lead", CommandNodeType.UnifiedCommand, "Single contact for venue and response partners."),
					L("Medical Team", CommandNodeType.TaskForce, "Roving first aid and patient assessment."),
					L("Security Team", CommandNodeType.TaskForce, "Roving security and crowd support."),
					L("Venue Liaison", CommandNodeType.Group, "Operations, facilities and organizer coordination."),
					L("Response Staging", CommandNodeType.Staging, "Reserve staff and transport resources.")),

				// ---- Industrial Response ----
				T("industrial-emergency", "Industrial Site Emergency", "Industrial Response",
					"An industrial response board for process isolation, fire, rescue, hazmat, medical and accountability.",
					new[] { "plant", "refinery", "factory", "industrial", "process", "brigade" }, true, 15,
					L("Industrial Command", CommandNodeType.UnifiedCommand, "Site management, emergency responders and agency coordination."),
					L("Process Isolation", CommandNodeType.Group, "Shutdown, lockout and energy/product isolation."),
					L("Fire Suppression", CommandNodeType.Group, "Industrial brigade fire control.", new[] { "Engine", "Fire Brigade" }),
					L("Rescue Group", CommandNodeType.Group, "Access, extrication and victim removal.", new[] { "Rescue" }),
					L("Hazmat Group", CommandNodeType.Group, "Product control, monitoring and decontamination.", new[] { "Hazmat" }),
					L("Medical Group", CommandNodeType.Group, "On-site treatment and transport.", new[] { "Ambulance", "Medic" }),
					L("Accountability / Staging", CommandNodeType.Staging, "Personnel accountability and resource check-in.")),

				T("industrial-confined-space", "Confined Space Rescue", "Industrial Response",
					"A confined-space board for entry, backup, rigging, atmospheric monitoring, medical and safety.",
					new[] { "confined space", "permit space", "trench", "technical rescue", "entry team" }, true, 15,
					L("Rescue Group", CommandNodeType.Group, "Coordinate the entry rescue plan."),
					L("Entry Team", CommandNodeType.TaskForce, "Enter, assess and package the victim.", null, new[] { "Confined Space Technician" }, true, minUnitPersonnel: 2, maxTimeInRole: 30),
					L("Backup Team", CommandNodeType.TaskForce, "Ready team for entry-team rescue.", null, new[] { "Confined Space Technician" }, true, minUnitPersonnel: 2),
					L("Rigging", CommandNodeType.Group, "Mechanical advantage, haul and lowering systems."),
					L("Atmospheric Monitoring", CommandNodeType.Group, "Continuous air monitoring and ventilation."),
					L("Medical", CommandNodeType.Group, "Victim and entrant medical support.", new[] { "Ambulance", "Medic" }),
					L("Safety", CommandNodeType.Group, "Permit, hazards, PPE and rescue-system oversight.")),

				// ---- Delivery Companies ----
				T("delivery-distribution-disruption", "Distribution Center Disruption", "Delivery Companies",
					"A business-continuity board for a hub shutdown, safety incident or major sorting and dispatch interruption.",
					new[] { "warehouse", "distribution", "parcel", "sort center", "business continuity", "logistics" }, false, 0,
					L("Site Command", CommandNodeType.UnifiedCommand, "Site leadership, safety and emergency-service coordination."),
					L("Life Safety", CommandNodeType.Group, "Employee accountability, first aid and hazard control."),
					L("Dock Operations", CommandNodeType.Group, "Trailer, dock and material-flow priorities."),
					L("Route Recovery", CommandNodeType.Branch, "Reassign routes, drivers, vehicles and service areas."),
					L("Customer Coordination", CommandNodeType.Group, "Service alerts and priority-customer exceptions."),
					L("Security", CommandNodeType.Group, "Access, traffic and cargo security."),
					L("Fleet / Maintenance", CommandNodeType.Group, "Vehicle availability, repairs and replacement assets.")),

				T("delivery-route-emergency", "Fleet / Route Emergency", "Delivery Companies",
					"A compact board for a vehicle crash, disabled vehicle, cargo incident or route-wide disruption.",
					new[] { "delivery", "courier", "fleet", "vehicle", "cargo", "route", "last mile" }, false, 0,
					L("Incident Lead", CommandNodeType.UnifiedCommand, "Coordinate driver, public safety and business response."),
					L("Driver Welfare", CommandNodeType.Group, "Driver contact, medical needs and family notification."),
					L("Cargo Recovery", CommandNodeType.TaskForce, "Secure, inventory and transfer cargo."),
					L("Towing / Maintenance", CommandNodeType.TaskForce, "Recover or replace the vehicle."),
					L("Route Reassignment", CommandNodeType.Group, "Transfer stops and rebalance neighboring routes."),
					L("Customer Notifications", CommandNodeType.Group, "Communicate delays and delivery exceptions."))
			};
		}

		#endregion Builders
	}
}
