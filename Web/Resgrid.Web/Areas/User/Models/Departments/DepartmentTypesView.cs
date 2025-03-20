using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class DepartmentTypesView
	{
		public List<DepartmentCertificationType> CertificationTypes { get; set; }
		public string NewCertificationType { get; set; }
		public string Message { get; set; }
		public List<UnitType> UnitTypes { get; set; }
		public string NewUnitType { get; set; }
		public int UnitCustomStatesId { get; set; }
		public int UnitType { get; set; }
		public int UnitTypeIcon { get; set; }
		public List<CallType> CallTypes { get; set; }
		public string NewCallType { get; set; }
		public int CallTypeIcon { get; set; }
		public List<CustomState> States { get; set; }
		public List<DepartmentCallPriority> CallPriorites { get; set; }
		public List<DocumentCategory> DocumentCategories { get; set; }
		public List<NoteCategory> NoteCategories { get; set; }
		public List<ContactNoteType> ContactNoteTypes { get; set; }
	}
}
