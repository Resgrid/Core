using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Selects the field set / standardized mapping used when exporting incident or personnel data.
	/// </summary>
	public enum ExportProfile
	{
		/// <summary>Generic Resgrid field set (all available columns).</summary>
		[Description("Generic")]
		[Display(Name = "Generic")]
		Generic = 0,

		/// <summary>National Fire Incident Reporting System (NFIRS) field mapping (fire).</summary>
		[Description("NFIRS")]
		[Display(Name = "NFIRS")]
		Nfirs = 1,

		/// <summary>National EMS Information System (NEMSIS) field mapping (EMS).</summary>
		[Description("NEMSIS")]
		[Display(Name = "NEMSIS")]
		Nemsis = 2
	}
}
