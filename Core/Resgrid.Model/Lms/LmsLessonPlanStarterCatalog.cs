using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>A Resgrid-authored starter lesson plan: structure and checklists only (no standard's text).</summary>
	public sealed class LmsLessonPlanStarter
	{
		public string Key { get; set; }
		/// <summary>The English title applied to the department's plan (the catalog page shows the localized one by key).</summary>
		public string Title { get; set; }
		public LmsLessonPlanContent Content { get; set; }
	}

	/// <summary>
	/// The starter lesson plans (§13 T4 item 5, R-C6) for the sessions departments document most carefully: live-fire evolutions (the
	/// burn-plan checks, roles and ratios a department records), emergency vehicle operation (hours and road miles), SCBA proficiency,
	/// annual hose, ladder and pump evolutions, and a physical ability test of timed stations. Written by Resgrid as structure and
	/// checklists in plain language; they paraphrase no standard. Applying one makes a department draft the department then edits.
	/// </summary>
	public static class LmsLessonPlanStarterCatalog
	{
		public static readonly IReadOnlyList<LmsLessonPlanStarter> All = new List<LmsLessonPlanStarter>
		{
			new LmsLessonPlanStarter
			{
				Key = "live-fire",
				Title = "Live-fire evolution (burn plan)",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Advance and operate a charged attack line under realistic heat and smoke", "Keep crew integrity and radio discipline inside the structure",
						"Coordinate fire attack with ventilation and search", "Recognise changing fire conditions and withdraw on the instructor's signal" },
					Roles = { "Instructor in charge", "Safety officer (no other duty)", "Ignition officer", "Interior instructor for each crew", "Backup line crew",
						"Rapid intervention crew", "Water supply officer", "EMS standby" },
					Materials = { "Planned fuel only (wood pallets, untreated straw or excelsior), listed by quantity", "Accountability board and passports", "Thermal imaging camera",
						"Radios on the assigned channel", "Rehab supplies (water, shade or heat, seating)" },
					Props = { "Burn building or acquired structure, inspected and prepared", "Attack line and separate backup line", "Two independent water supplies" },
					StudentsPerInstructor = 5,
					DurationMinutes = 240,
					SafetyPlan = "The instructor in charge and the safety officer walk the structure before every evolution. One fire is lit at a time, only by the ignition officer, and only on the instructor in charge's order. Any participant may call a stop; the evacuation signal and the assembly point are briefed and repeated back. Participants work in crews with an interior instructor and leave as a crew. The safety officer may stop the evolution at any time.",
					PreBriefChecklist = { "Structure or prop inspected and hazards removed; findings recorded", "Fuel limited to the planned materials; no flammable liquids, no unknown materials",
						"Two independent water supplies established and flow checked", "Attack and backup lines charged, staffed and checked", "Rapid intervention crew staged with its equipment",
						"EMS on scene with transport available", "Every participant's PPE and SCBA inspected; cylinders full", "Participants' prerequisite training verified",
						"Exits, evacuation signal and assembly point briefed and repeated back", "Weather and wind checked and recorded", "Ignition officer named; ignition only on order",
						"Accountability system in place; personnel count taken", "Walk-through completed by every participant", "Radio check on the assigned channel", "Rehab area set up" },
					Notes = "Record each evolution: the fuel used, the start and end times, who was inside, and any stop called."
				}
			},
			new LmsLessonPlanStarter
			{
				Key = "evoc",
				Title = "Emergency vehicle operation (EVO)",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Complete a pre-trip inspection of the apparatus", "Back the apparatus with a spotter using agreed signals",
						"Drive a serpentine and a confined-space turnaround without touching a cone", "Clear an intersection safely in emergency mode", "Stop smoothly from speed in a marked distance" },
					Roles = { "Lead driving instructor", "Spotters", "Course safety officer" },
					Materials = { "Traffic cones and a measuring wheel", "Vehicle inspection check sheet", "Driver log for hours behind the wheel and road miles" },
					Props = { "Closed driving course", "Apparatus to be driven" },
					StudentsPerInstructor = 2,
					DurationMinutes = 180,
					SafetyPlan = "The course is closed to other traffic. Course speeds are set before the session and briefed. Seatbelts are worn by everyone on board. Backing always uses a spotter in view of the driver's mirror; the driver stops whenever the spotter is out of sight.",
					PreBriefChecklist = { "Pre-trip inspection completed and signed", "Course laid out and measured", "Spotter signals briefed", "Course speeds briefed", "Seatbelts on for everyone on board",
						"Driver log ready to record hours and road miles" },
					Notes = "Record for each driver the hours behind the wheel and the road miles driven."
				}
			},
			new LmsLessonPlanStarter
			{
				Key = "scba",
				Title = "SCBA proficiency",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Inspect an SCBA and record the result", "Don the SCBA and face piece within the department's time target", "Check the face piece seal",
						"Manage air and report air levels on request", "Perform the department's emergency procedures (low air, entanglement, mayday)", "Change a cylinder with a partner" },
					Roles = { "Lead instructor", "Assistant instructor timing the stations" },
					Materials = { "SCBA for each participant, cylinders full", "Stopwatch", "Inspection forms" },
					Props = { "Entanglement prop", "Obscured face pieces or a smoke-free maze" },
					StudentsPerInstructor = 6,
					DurationMinutes = 120,
					SafetyPlan = "Participants confirm medical clearance for respirator use. Any participant may remove the face piece and stop at any time. Instructors watch for heat stress and fatigue.",
					PreBriefChecklist = { "Medical clearance for respirator use confirmed", "Every SCBA inspected; cylinders full", "Face piece fit checked for each participant", "Emergency procedures briefed",
						"Stop signal agreed" }
				}
			},
			new LmsLessonPlanStarter
			{
				Key = "hose-evolutions",
				Title = "Annual hose evolutions",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Deploy and advance a pre-connected line", "Advance a line up a stairwell", "Connect to a standpipe and advance from it", "Load hose in the department's loads",
						"Replace a burst length" },
					Roles = { "Lead instructor", "Pump operator", "Safety officer" },
					Materials = { "Attack hose and nozzles", "Standpipe kit", "Spanners and hose clamps" },
					Props = { "Engine with water", "Stairwell or tower", "Standpipe outlet" },
					StudentsPerInstructor = 6,
					DurationMinutes = 180,
					SafetyPlan = "Lines are charged only on the instructor's order. Nozzle reaction is briefed before charging. Participants wear full PPE for the advances.",
					PreBriefChecklist = { "Hose and nozzles inspected", "Water supply established", "Charge only on order briefed", "PPE worn" }
				}
			},
			new LmsLessonPlanStarter
			{
				Key = "ladder-evolutions",
				Title = "Annual ladder evolutions",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Carry and raise ground ladders with one and with two members", "Place a ladder for rescue and for ventilation", "Climb and work from a ladder with a leg lock",
						"Heel and secure a ladder" },
					Roles = { "Lead instructor", "Safety officer" },
					Materials = { "Ground ladders of each length carried", "Ladder inspection form" },
					Props = { "Training tower or building with windows and a roof" },
					StudentsPerInstructor = 6,
					DurationMinutes = 150,
					SafetyPlan = "Ladders are inspected before use. Overhead hazards are checked before every raise. A ladder is always heeled or secured before anyone climbs.",
					PreBriefChecklist = { "Ladders inspected", "Overhead hazards checked", "Climbing safety briefed", "PPE worn" }
				}
			},
			new LmsLessonPlanStarter
			{
				Key = "pump-evolutions",
				Title = "Annual pump evolutions",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Establish water supply from a hydrant", "Draft from a static source", "Pump the correct pressure for the lines in use", "Operate in a relay with another engine",
						"Change from tank to hydrant water without interrupting flow" },
					Roles = { "Lead instructor", "Pump operators", "Hydrant and supply crew" },
					Materials = { "Supply and attack hose", "Hard suction and strainer", "Pump chart or friction loss reference" },
					Props = { "Engine", "Hydrant", "Static water source" },
					StudentsPerInstructor = 3,
					DurationMinutes = 180,
					SafetyPlan = "Pressures are raised slowly and only when lines are staffed. Hose is never stepped over while charged near a pump panel. Hearing protection is worn at the pump.",
					PreBriefChecklist = { "Pump and gauges checked", "Water sources confirmed", "Hearing protection available", "Line staffing confirmed before charging" }
				}
			},
			new LmsLessonPlanStarter
			{
				Key = "physical-ability",
				Title = "Physical ability test (timed stations)",
				Content = new LmsLessonPlanContent
				{
					Objectives = { "Complete each station within the department's time", "Complete the full course within the department's overall time" },
					Roles = { "Test proctor", "Station timers", "EMS standby" },
					Materials = { "Stopwatches and score sheets", "Weighted vest", "Charged or dry hose for the drag", "Equipment for the carry", "Rescue mannequin" },
					Props = { "Stair climber or stairwell", "Ladder for the raise", "Forcible entry simulator", "Search maze", "Ceiling pull device" },
					StudentsPerInstructor = 4,
					DurationMinutes = 120,
					SafetyPlan = "Candidates confirm medical clearance. A supervised warm-up comes first. EMS stands by throughout; any candidate may stop, and a proctor stops a candidate showing distress.",
					PreBriefChecklist = { "Medical clearance confirmed for each candidate", "Stations set up and checked", "Station times written on the score sheets", "Warm-up completed",
						"EMS standing by" },
					Notes = "Stations: stair climb with weight, hose drag, equipment carry, ladder raise and extension, forcible entry, search, rescue drag, ceiling pull. Set the times per station in the department's policy."
				}
			}
		};

		public static LmsLessonPlanStarter Find(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(s => string.Equals(s.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
	}
}
