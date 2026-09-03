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
		Contact = 3,

		/// <summary>
		/// A Records (RMS) Record. UDF definitions for Records are scoped to a definition key and version,
		/// and UDF values never enter a standardized export or submission payload (RMS plan section 4.1).
		/// </summary>
		[Display(Name = "Record")]
		[Description("Record")]
		Record = 4
	}
}

