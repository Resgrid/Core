using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	/// <summary>Department -> Terminology (setting 118): the department's own names for modules and built-in record types.</summary>
	public class TerminologyView : BaseUserModel
	{
		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		/// <summary>One row per <see cref="DepartmentTerms.All"/> entry, in catalog order.</summary>
		public List<TerminologyRow> Terms { get; set; } = new List<TerminologyRow>();

		public static TerminologyView From(DepartmentTerminologyConfig config)
		{
			config ??= new DepartmentTerminologyConfig();
			return new TerminologyView
			{
				Terms = DepartmentTerms.All.Select(t => new TerminologyRow
				{
					Key = t.Key,
					Category = t.Category,
					HasSingular = t.HasSingular,
					Text = config.TextFor(t.Key),
					Singular = config.SavedSingularFor(t.Key)
				}).ToList()
			};
		}
	}

	public class TerminologyRow
	{
		public string Key { get; set; }
		public DepartmentTermCategories Category { get; set; }

		/// <summary>The department's name; blank uses Resgrid's.</summary>
		public string Text { get; set; }

		/// <summary>Whether the term also has a word for one item (Units: Unit).</summary>
		public bool HasSingular { get; set; }

		/// <summary>The department's word for one item; blank guesses it from <see cref="Text"/>.</summary>
		public string Singular { get; set; }
	}
}
