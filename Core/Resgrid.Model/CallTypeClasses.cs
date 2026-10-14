using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The canonical kind of a department's call type (CallType.BaseType), a deliberately coarse set that works in any
	/// country and discipline. A department names its call types however it likes ("Structure Fire", "Code 3 Medical",
	/// "Alarm Activation"); the class is how Resgrid understands them for reporting, defaults and integrations (NERIS
	/// incident types are mapped separately and only where they apply).
	/// </summary>
	public enum CallTypeClasses
	{
		[Description("No class set. The call type is informational only.")]
		[Display(Name = "None")]
		None = -1,

		[Description("A fire: structure, vehicle, vegetation, rubbish or any other fire.")]
		[Display(Name = "Fire")]
		Fire = 1,

		[Description("A medical emergency or patient care call.")]
		[Display(Name = "Medical")]
		Medical = 2,

		[Description("A rescue: technical, water, rope, confined space, entrapment or search.")]
		[Display(Name = "Rescue")]
		Rescue = 3,

		[Description("A traffic collision or other vehicle incident.")]
		[Display(Name = "Traffic Collision")]
		TrafficCollision = 4,

		[Description("Hazardous materials: a leak, spill, gas odor or release.")]
		[Display(Name = "Hazardous Materials")]
		HazardousMaterials = 5,

		[Description("An automatic or manual alarm activation, including false alarms.")]
		[Display(Name = "Alarm")]
		Alarm = 6,

		[Description("A non-emergency service or public assistance call.")]
		[Display(Name = "Service Call")]
		ServiceCall = 7,

		[Description("A law enforcement, security or public order call.")]
		[Display(Name = "Law Enforcement")]
		LawEnforcement = 8,

		[Description("A natural disaster, severe weather or other large-scale event.")]
		[Display(Name = "Disaster or Weather")]
		DisasterWeather = 9,

		[Description("Mutual aid or assistance given to another agency.")]
		[Display(Name = "Mutual Aid")]
		MutualAid = 10,

		[Description("Training, drills, standby, planned events and other non-incident activity.")]
		[Display(Name = "Non-Emergency Activity")]
		NonEmergencyActivity = 11,

		[Description("Anything that fits none of the other classes.")]
		[Display(Name = "Other")]
		Other = 12
	}
}
