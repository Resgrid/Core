using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The canonical kind of a department's unit type (UnitType.BaseType). A department names its unit types however it
	/// likes ("Pumper", "Quint", "Medic"); the class is how Resgrid understands them for reporting, defaults and
	/// integrations.
	/// </summary>
	public enum UnitTypeClasses
	{
		[Description("No class set. The unit type is informational only.")]
		[Display(Name = "None")]
		None = -1,

		[Description("A fire engine or pumper.")]
		[Display(Name = "Engine")]
		Engine = 1,

		[Description("An aerial apparatus: ladder, tower, platform or quint.")]
		[Display(Name = "Aerial")]
		Aerial = 2,

		[Description("A rescue, squad or technical rescue unit.")]
		[Display(Name = "Rescue")]
		Rescue = 3,

		[Description("A water tanker or tender.")]
		[Display(Name = "Tanker")]
		Tanker = 4,

		[Description("An ambulance or medic unit that can transport patients.")]
		[Display(Name = "Ambulance")]
		Ambulance = 5,

		[Description("A non-transporting medical response unit.")]
		[Display(Name = "Medical Response")]
		MedicalResponse = 6,

		[Description("A wildland, brush or vegetation fire unit.")]
		[Display(Name = "Wildland")]
		Wildland = 7,

		[Description("A hazardous materials unit.")]
		[Display(Name = "Hazardous Materials")]
		HazardousMaterials = 8,

		[Description("A command, chief or supervisor vehicle.")]
		[Display(Name = "Command")]
		Command = 9,

		[Description("A utility, support, logistics or service vehicle.")]
		[Display(Name = "Support")]
		Support = 10,

		[Description("A boat or other water craft.")]
		[Display(Name = "Watercraft")]
		Watercraft = 11,

		[Description("A helicopter, aircraft or drone.")]
		[Display(Name = "Aircraft")]
		Aircraft = 12,

		[Description("A police, patrol or security vehicle.")]
		[Display(Name = "Patrol")]
		Patrol = 13,

		[Description("Anything that fits none of the other classes.")]
		[Display(Name = "Other")]
		Other = 14
	}
}
