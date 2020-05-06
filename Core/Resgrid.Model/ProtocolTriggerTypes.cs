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
		CallPriortyAndType = 2
	}
}
