using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calendar
{
	public class NewTypeView
	{
		public string Message { get; set; }
		public CalendarItemType Type { get; set; }

		/// <summary>Training T1 (R-T9): the department's training categories for "Counts as training" (a removed one only when stored).</summary>
		public List<TrainingCategory> TrainingCategories { get; set; } = new List<TrainingCategory>();
	}
}
