using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The entity types that support User Defined Fields.
	/// </summary>
	public enum UdfEntityType
	{
		[Display(Name = "Call")]
		[Description("Call")]
		Call = 0,

		[Display(Name = "Personnel")]
		[Description("Personnel")]
		Personnel = 1,

		[Display(Name = "Unit")]
		[Description("Unit")]
		Unit = 2,

		[Display(Name = "Contact")]
		[Description("Contact")]
		Contact = 3
	}
}

