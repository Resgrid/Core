using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The types of a form
	/// </summary>
	public enum FormTypes
	{
		/// <summary>
		/// Form is for New Call creation
		/// </summary>
		[Display(Name = "New Call Form")]
		[Description("New Call Form")]
		NewCallForm = 0,
	}
}
