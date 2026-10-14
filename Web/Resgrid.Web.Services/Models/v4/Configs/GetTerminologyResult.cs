using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Configs
{
	/// <summary>
	/// The department's own names for modules and built-in record types (Department -> Terminology). Apps show
	/// <see cref="DepartmentTermData.Name"/> when it is set, else their own (localized) name for the term.
	/// </summary>
	public class GetTerminologyResult : StandardApiResponseV4Base
	{
		/// <summary>One entry per term the department can rename, renamed or not.</summary>
		public List<DepartmentTermData> Data { get; set; }

		public GetTerminologyResult()
		{
			Data = new List<DepartmentTermData>();
		}
	}

	/// <summary>A term a department can rename.</summary>
	public class DepartmentTermData
	{
		/// <summary>Stable term key, e.g. "module.records" or "record-type.run". Never changes.</summary>
		public string Key { get; set; }

		/// <summary>"Module" or "RecordType".</summary>
		public string Category { get; set; }

		/// <summary>Resgrid's English name for the term.</summary>
		public string DefaultName { get; set; }

		/// <summary>The department's name for the term; null when it uses Resgrid's.</summary>
		public string Name { get; set; }
	}
}
