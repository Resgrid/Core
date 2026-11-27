using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{

	public enum ProtocolTriggerTypes
	{
		/// <summary>
		/// Call Priority
		/// </summary>
		[Display(Name = "Call Priority")]
		[Description("Call Priority")]
		CallPriorty = 0,

		/// <summary>
		/// Call TYpe
		/// </summary>
		[Display(Name = "Call Type")]
		[Description("Call Type")]
		CallType = 1,

		/// <summary>
		/// Call Priority and Call Type
		/// </summary>
		[Display(Name = "Call Priority & Type")]
		[Description("Call Priority & Type")]
		CallPriortyAndType = 2,

		/// <summary>
		/// Protocols P4: any call, narrowed only by the trigger's conditions (zone, schedule, keywords, POI type, intake source);
		/// <see cref="DispatchTriggerBases.Conditions"/>.
		/// </summary>
		[Display(Name = "Any call (conditions)")]
		[Description("Any call (conditions)")]
		Conditions = 3
	}
}
