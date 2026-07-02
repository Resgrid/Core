using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.CustomStates
{
	/// <summary>
	/// The code-defined catalog of predefined custom-state templates ("starter packs") that departments
	/// can pick from when setting up Unit, Personnel or Staffing custom statuses. Everything here lives in
	/// code (NOT the database) by design, mirroring the pattern used by other static reference data such
	/// as <see cref="Resgrid.Model.Reporting.AvailabilityMatrix"/>.
	///
	/// To add a template, add it to the relevant builder below. Each template projects into an unsaved
	/// <see cref="CustomState"/> the user can edit before saving, so button text/colors are just sensible
	/// starting points. Base types come from <see cref="ActionBaseTypes"/> so the platform understands
	/// what each status means operationally.
	/// </summary>
	public static class CustomStateTemplateCatalog
	{
		#region Palette

		// A small, consistent palette so templates look good out of the box. Backgrounds pair with a
		// readable text color (white on dark, black on light).
		private const string Green = "#5CB85C";     // available / in service
		private const string DarkGreen = "#449D44"; // cleared / completed / at hospital
		private const string Blue = "#428BCA";      // dispatched / en route / returning
		private const string Orange = "#F0AD4E";    // responding (light -> black text)
		private const string Red = "#D9534F";       // on scene / deployed
		private const string DarkRed = "#C9302C";   // not responding / recall
		private const string Purple = "#8E44AD";    // transporting
		private const string Teal = "#17A2B8";      // patient contact / delivering
		private const string Indigo = "#6F42C1";    // investigating / searching
		private const string Yellow = "#FFC107";    // staging / partial (light -> black text)
		private const string Cyan = "#5BC0DE";      // loading / standby (light -> black text)
		private const string Slate = "#34495E";     // on patrol
		private const string Brown = "#8A6D3B";     // maintenance
		private const string Gray = "#777777";      // unavailable / out of service
		private const string LightGray = "#ADADAD"; // on break (light -> black text)
		private const string White = "#FFFFFF";
		private const string Black = "#000000";

		#endregion Palette

		/// <summary>All templates across every type, in display order.</summary>
		public static IReadOnlyList<CustomStateTemplate> All { get; } = BuildAll();

		/// <summary>Returns the templates for a given custom state type (Unit, Personnel or Staffing).</summary>
		public static IReadOnlyList<CustomStateTemplate> GetByType(CustomStateTypes type)
		{
			return All.Where(t => t.Type == type).ToList();
		}

		/// <summary>Returns a single template by its stable id, or null if it does not exist.</summary>
		public static CustomStateTemplate GetById(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			return All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Returns templates filtered by type and an optional free-text query (matched against the name,
		/// category, description, keywords and button labels). Provided for server-side/API use; the
		/// gallery UI also filters client-side for instant results.
		/// </summary>
		public static IReadOnlyList<CustomStateTemplate> Search(CustomStateTypes type, string query)
		{
			var results = All.Where(t => t.Type == type);

			if (!string.IsNullOrWhiteSpace(query))
			{
				var terms = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
				results = results.Where(t => terms.All(term => t.SearchText.Contains(term)));
			}

			return results.ToList();
		}

		#region Builders

		private static IReadOnlyList<CustomStateTemplate> BuildAll()
		{
			var templates = new List<CustomStateTemplate>();
			templates.AddRange(BuildUnitTemplates());
			templates.AddRange(BuildPersonnelTemplates());
			templates.AddRange(BuildStaffingTemplates());
			return templates;
		}

		private static IReadOnlyList<CustomStateTemplate> BuildUnitTemplates()
		{
			return new List<CustomStateTemplate>
			{
				new CustomStateTemplate
				{
					Id = "unit-ems-ambulance",
					Type = CustomStateTypes.Unit,
					Name = "EMS / Ambulance",
					Category = "EMS",
					Description = "Patient-transport lifecycle for ambulances and medic units: dispatch, response, patient care, transport to hospital and return to service.",
					Keywords = new[] { "ambulance", "medic", "paramedic", "als", "bls", "medical", "rescue" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Dispatched", ActionBaseTypes.Dispatched, Blue),
						D(2, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(3, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(4, "At Patient", ActionBaseTypes.AtPatient, Teal, gps: true),
						D(5, "Transporting", ActionBaseTypes.Transporting, Purple, gps: true, note: CustomStateNoteTypes.Optional),
						D(6, "At Hospital", ActionBaseTypes.AtHospital, DarkGreen, gps: true),
						D(7, "Returning", ActionBaseTypes.Returning, Blue, gps: true),
						D(8, "Out of Service", ActionBaseTypes.Unavailable, Gray, note: CustomStateNoteTypes.Optional)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-fire-engine",
					Type = CustomStateTypes.Unit,
					Name = "Fire Apparatus / Engine",
					Category = "Fire",
					Description = "Fire apparatus response cycle: dispatch, response, on scene, staging and return to quarters.",
					Keywords = new[] { "engine", "truck", "ladder", "apparatus", "rescue", "brush", "tanker" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Dispatched", ActionBaseTypes.Dispatched, Blue),
						D(2, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(3, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(4, "Staging", ActionBaseTypes.Staging, Yellow, Black, gps: true),
						D(5, "Returning", ActionBaseTypes.Returning, Blue, gps: true),
						D(6, "Out of Service", ActionBaseTypes.Unavailable, Gray, note: CustomStateNoteTypes.Optional)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-law-enforcement",
					Type = CustomStateTypes.Unit,
					Name = "Law Enforcement",
					Category = "Law Enforcement",
					Description = "Patrol and response cycle for law-enforcement units, including patrol, investigation, subject contact and prisoner transport.",
					Keywords = new[] { "police", "sheriff", "patrol", "officer", "deputy", "cruiser", "prisoner" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "On Patrol", ActionBaseTypes.OnPatrol, Slate, gps: true),
						D(2, "Dispatched", ActionBaseTypes.Dispatched, Blue),
						D(3, "En Route", ActionBaseTypes.Enroute, Orange, Black, gps: true),
						D(4, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(5, "Investigating", ActionBaseTypes.Investigating, Indigo, gps: true),
						D(6, "Made Contact", ActionBaseTypes.MadeContact, Teal, gps: true),
						D(7, "Transporting", ActionBaseTypes.Transporting, Purple, gps: true, note: CustomStateNoteTypes.Optional),
						D(8, "Out of Service", ActionBaseTypes.Unavailable, Gray, note: CustomStateNoteTypes.Optional)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-sar",
					Type = CustomStateTypes.Unit,
					Name = "Search & Rescue",
					Category = "Search and Rescue",
					Description = "Field team lifecycle for search-and-rescue units: response, staging, active search, subject contact and transport.",
					Keywords = new[] { "sar", "search", "rescue", "team", "ground", "wilderness", "subject" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(2, "Staging", ActionBaseTypes.Staging, Yellow, Black, gps: true),
						D(3, "Searching", ActionBaseTypes.Searching, Indigo, gps: true),
						D(4, "Subject Contact", ActionBaseTypes.MadeContact, Teal, gps: true),
						D(5, "Transporting", ActionBaseTypes.Transporting, Purple, gps: true, note: CustomStateNoteTypes.Optional),
						D(6, "Returning", ActionBaseTypes.Returning, Blue, gps: true),
						D(7, "Out of Service", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-emergency-response",
					Type = CustomStateTypes.Unit,
					Name = "Emergency Response (General)",
					Category = "Emergency Response",
					Description = "A general-purpose response cycle suitable for most emergency-response units when a discipline-specific set is not needed.",
					Keywords = new[] { "general", "response", "generic", "standard", "basic" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Dispatched", ActionBaseTypes.Dispatched, Blue),
						D(2, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(3, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(4, "Staging", ActionBaseTypes.Staging, Yellow, Black, gps: true),
						D(5, "Cleared", ActionBaseTypes.Cleared, DarkGreen),
						D(6, "Out of Service", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-disaster-response",
					Type = CustomStateTypes.Unit,
					Name = "Disaster Response",
					Category = "Disaster Response",
					Description = "Deployment cycle for disaster and large-scale incident resources: mobilizing, staging, standby, deployment and demobilization.",
					Keywords = new[] { "disaster", "deployment", "mobilize", "task force", "ems", "fema", "strike team" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Mobilizing", ActionBaseTypes.Enroute, Blue, gps: true),
						D(2, "Staging", ActionBaseTypes.Staging, Yellow, Black, gps: true),
						D(3, "Standby", ActionBaseTypes.Standby, Cyan, Black),
						D(4, "Deployed", ActionBaseTypes.OnScene, Red, gps: true),
						D(5, "Returning", ActionBaseTypes.Returning, Blue, gps: true),
						D(6, "Out of Service", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-security",
					Type = CustomStateTypes.Unit,
					Name = "Security",
					Category = "Security",
					Description = "Patrol and response cycle for security units and mobile patrols, including investigation and contact.",
					Keywords = new[] { "guard", "patrol", "site", "facility", "campus", "officer" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "In Service", ActionBaseTypes.Available, Green),
						D(1, "On Patrol", ActionBaseTypes.OnPatrol, Slate, gps: true),
						D(2, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(3, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(4, "Investigating", ActionBaseTypes.Investigating, Indigo, gps: true),
						D(5, "Made Contact", ActionBaseTypes.MadeContact, Teal, gps: true),
						D(6, "Out of Service", ActionBaseTypes.Unavailable, Gray, note: CustomStateNoteTypes.Optional)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-industrial-fleet",
					Type = CustomStateTypes.Unit,
					Name = "Industrial / Fleet",
					Category = "Industrial Management",
					Description = "Work and availability cycle for industrial vehicles, plant equipment and fleet assets, including loading, breaks and maintenance.",
					Keywords = new[] { "fleet", "vehicle", "equipment", "plant", "asset", "operator", "utility" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "En Route", ActionBaseTypes.Enroute, Blue, gps: true),
						D(2, "On Site", ActionBaseTypes.OnScene, Red, gps: true),
						D(3, "Loading", ActionBaseTypes.Loading, Cyan, Black, gps: true),
						D(4, "On Break", ActionBaseTypes.OnBreak, LightGray, Black),
						D(5, "Maintenance", ActionBaseTypes.Maintenance, Brown, note: CustomStateNoteTypes.Optional),
						D(6, "Out of Service", ActionBaseTypes.Unavailable, Gray, note: CustomStateNoteTypes.Optional)
					}
				},
				new CustomStateTemplate
				{
					Id = "unit-commodity-delivery",
					Type = CustomStateTypes.Unit,
					Name = "Commodity Delivery / Logistics",
					Category = "Commodity Delivery",
					Description = "End-to-end delivery cycle for logistics and commodity-delivery vehicles: loading, transit, delivery and return.",
					Keywords = new[] { "delivery", "logistics", "freight", "cargo", "supply", "transport", "courier", "goods" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Loading", ActionBaseTypes.Loading, Cyan, Black, gps: true),
						D(2, "En Route", ActionBaseTypes.Enroute, Blue, gps: true),
						D(3, "Transporting", ActionBaseTypes.Transporting, Purple, gps: true),
						D(4, "Delivering", ActionBaseTypes.Delivering, Teal, gps: true, note: CustomStateNoteTypes.Optional),
						D(5, "Delivered", ActionBaseTypes.Completed, DarkGreen),
						D(6, "Returning", ActionBaseTypes.Returning, Blue, gps: true),
						D(7, "Out of Service", ActionBaseTypes.Unavailable, Gray)
					}
				}
			};
		}

		private static IReadOnlyList<CustomStateTemplate> BuildPersonnelTemplates()
		{
			return new List<CustomStateTemplate>
			{
				new CustomStateTemplate
				{
					Id = "personnel-firefighter",
					Type = CustomStateTypes.Personnel,
					Name = "Firefighter",
					Category = "Fire",
					Description = "Actions a firefighter taps to signal their response, including responding to the station or directly to the scene.",
					Keywords = new[] { "fire", "ff", "responder", "station", "scene" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(1, "Responding to Station", ActionBaseTypes.Responding, Blue, gps: true),
						D(2, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(3, "Available", ActionBaseTypes.Available, Green),
						D(4, "Not Responding", ActionBaseTypes.NotResponding, DarkRed, note: CustomStateNoteTypes.Optional),
						D(5, "Unavailable", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "personnel-ems",
					Type = CustomStateTypes.Personnel,
					Name = "EMS Responder",
					Category = "EMS",
					Description = "Actions an EMS provider taps through a patient-care and transport call.",
					Keywords = new[] { "medic", "paramedic", "emt", "ambulance", "medical" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(2, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(3, "At Patient", ActionBaseTypes.AtPatient, Teal, gps: true),
						D(4, "Transporting", ActionBaseTypes.Transporting, Purple, gps: true),
						D(5, "Unavailable", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "personnel-law-enforcement",
					Type = CustomStateTypes.Personnel,
					Name = "Law Enforcement Officer",
					Category = "Law Enforcement",
					Description = "Actions an officer taps while on patrol and responding to calls.",
					Keywords = new[] { "police", "deputy", "officer", "patrol", "sheriff" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "On Patrol", ActionBaseTypes.OnPatrol, Slate, gps: true),
						D(2, "En Route", ActionBaseTypes.Enroute, Orange, Black, gps: true),
						D(3, "On Scene", ActionBaseTypes.OnScene, Red, gps: true),
						D(4, "Investigating", ActionBaseTypes.Investigating, Indigo, gps: true),
						D(5, "Off Duty", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "personnel-sar",
					Type = CustomStateTypes.Personnel,
					Name = "Search & Rescue Member",
					Category = "Search and Rescue",
					Description = "Actions a search-and-rescue team member taps through a callout and active search.",
					Keywords = new[] { "sar", "search", "rescue", "team", "ground" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.Available, Green),
						D(1, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(2, "Staging", ActionBaseTypes.Staging, Yellow, Black, gps: true),
						D(3, "Searching", ActionBaseTypes.Searching, Indigo, gps: true),
						D(4, "Made Contact", ActionBaseTypes.MadeContact, Teal, gps: true),
						D(5, "Unavailable", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "personnel-volunteer",
					Type = CustomStateTypes.Personnel,
					Name = "Volunteer Responder",
					Category = "Emergency Response",
					Description = "A simple response set for volunteers who respond either to the station or directly to the scene.",
					Keywords = new[] { "volunteer", "pov", "station", "scene", "poc" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Responding to Scene", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(1, "Responding to Station", ActionBaseTypes.Responding, Blue, gps: true),
						D(2, "Available", ActionBaseTypes.Available, Green),
						D(3, "Not Responding", ActionBaseTypes.NotResponding, DarkRed, note: CustomStateNoteTypes.Optional),
						D(4, "Unavailable", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "personnel-security",
					Type = CustomStateTypes.Personnel,
					Name = "Security Officer",
					Category = "Security",
					Description = "Shift and patrol actions for a security officer, including patrol, response and breaks.",
					Keywords = new[] { "guard", "patrol", "shift", "site", "officer" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "On Duty", ActionBaseTypes.Available, Green),
						D(1, "On Patrol", ActionBaseTypes.OnPatrol, Slate, gps: true),
						D(2, "Responding", ActionBaseTypes.Responding, Orange, Black, gps: true),
						D(3, "On Break", ActionBaseTypes.OnBreak, LightGray, Black),
						D(4, "Off Duty", ActionBaseTypes.Unavailable, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "personnel-industrial",
					Type = CustomStateTypes.Personnel,
					Name = "Industrial Worker",
					Category = "Industrial Management",
					Description = "Shift and task actions for plant, utility and industrial workers, including tasks, breaks and maintenance.",
					Keywords = new[] { "worker", "operator", "plant", "shift", "crew", "utility" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "On Shift", ActionBaseTypes.Available, Green),
						D(1, "On Task", ActionBaseTypes.OnScene, Orange, Black, gps: true),
						D(2, "On Break", ActionBaseTypes.OnBreak, LightGray, Black),
						D(3, "Maintenance", ActionBaseTypes.Maintenance, Brown),
						D(4, "Off Shift", ActionBaseTypes.Unavailable, Gray)
					}
				}
			};
		}

		private static IReadOnlyList<CustomStateTemplate> BuildStaffingTemplates()
		{
			// Personnel "staffing" is an INDIVIDUAL person's availability/staffing status (e.g. "I'm
			// available for calls" or "I'm away") -- NOT the group/crew staffing of a unit (that is computed
			// from unit roles, see UnitRoleStaffingResult). These are personal availability sets, so they do
			// not carry an operational base type, GPS or call/station detail association.
			return new List<CustomStateTemplate>
			{
				new CustomStateTemplate
				{
					Id = "staffing-standard",
					Type = CustomStateTypes.Staffing,
					Name = "Standard Availability",
					Category = "General",
					Description = "A simple personal availability set: whether you are available to be assigned to calls or not.",
					Keywords = new[] { "general", "standard", "available", "availability", "basic" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.None, Green),
						D(1, "Delayed", ActionBaseTypes.None, Yellow, Black),
						D(2, "Unavailable", ActionBaseTypes.None, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "staffing-detailed",
					Type = CustomStateTypes.Staffing,
					Name = "Detailed Availability",
					Category = "General",
					Description = "A more granular personal availability set for members who want to signal delayed, committed or away in addition to available.",
					Keywords = new[] { "detailed", "available", "committed", "delayed", "away", "availability" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.None, Green),
						D(1, "Delayed", ActionBaseTypes.None, Yellow, Black),
						D(2, "Committed", ActionBaseTypes.None, Orange, Black),
						D(3, "Away", ActionBaseTypes.None, LightGray, Black),
						D(4, "Unavailable", ActionBaseTypes.None, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "staffing-duty",
					Type = CustomStateTypes.Staffing,
					Name = "On / Off Duty",
					Category = "Security",
					Description = "A shift-oriented personal availability set for members who work scheduled duty, including on-call and breaks.",
					Keywords = new[] { "duty", "shift", "on call", "off duty", "break", "security", "industrial" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "On Duty", ActionBaseTypes.None, Green),
						D(1, "On Call", ActionBaseTypes.None, Blue),
						D(2, "On Break", ActionBaseTypes.None, LightGray, Black),
						D(3, "Off Duty", ActionBaseTypes.None, Gray)
					}
				},
				new CustomStateTemplate
				{
					Id = "staffing-volunteer",
					Type = CustomStateTypes.Staffing,
					Name = "Volunteer Availability",
					Category = "Emergency Response",
					Description = "A personal availability set for volunteer members to indicate whether they can take calls right now.",
					Keywords = new[] { "volunteer", "available", "committed", "not available", "availability" },
					Details = new List<CustomStateTemplateDetail>
					{
						D(0, "Available", ActionBaseTypes.None, Green),
						D(1, "Delayed", ActionBaseTypes.None, Yellow, Black),
						D(2, "Committed", ActionBaseTypes.None, Orange, Black),
						D(3, "Not Available", ActionBaseTypes.None, Gray)
					}
				}
			};
		}

		/// <summary>Compact factory for a template button with sensible defaults.</summary>
		private static CustomStateTemplateDetail D(int order, string text, ActionBaseTypes baseType, string bg,
			string fg = White, bool gps = false, CustomStateNoteTypes note = CustomStateNoteTypes.None,
			CustomStateDetailTypes detail = CustomStateDetailTypes.None)
		{
			return new CustomStateTemplateDetail
			{
				Order = order,
				ButtonText = text,
				BaseType = baseType,
				ButtonColor = bg,
				TextColor = fg,
				GpsRequired = gps,
				NoteType = note,
				DetailType = detail
			};
		}

		#endregion Builders
	}
}
