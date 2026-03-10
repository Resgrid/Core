using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The supported data types for a User Defined Field.
	/// </summary>
	public enum UdfFieldDataType
	{
		[Display(Name = "Text")]
		[Description("Single-line text")]
		Text = 0,

		[Display(Name = "Number")]
		[Description("Whole number (integer)")]
		Number = 1,

		[Display(Name = "Decimal")]
		[Description("Decimal number")]
		Decimal = 2,

		[Display(Name = "Boolean")]
		[Description("Yes / No checkbox")]
		Boolean = 3,

		[Display(Name = "Date")]
		[Description("Date only")]
		Date = 4,

		[Display(Name = "Date & Time")]
		[Description("Date and time")]
		DateTime = 5,

		[Display(Name = "Dropdown")]
		[Description("Single-select dropdown list")]
		Dropdown = 6,

		[Display(Name = "Multi-Select")]
		[Description("Multi-select list")]
		MultiSelect = 7,

		[Display(Name = "Email")]
		[Description("Email address")]
		Email = 8,

		[Display(Name = "Phone")]
		[Description("Phone number")]
		Phone = 9,

		[Display(Name = "URL")]
		[Description("Web address / URL")]
		Url = 10
	}
}

