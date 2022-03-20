using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("Logs")]
	public class Log : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LogId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(4000)]
		public string Narrative { get; set; }

		public int? LogType { get; set; }

		public string ExternalId { get; set; }

		public string InitialReport { get; set; }

		public string Type { get; set; }

		[ForeignKey("StationGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? StationGroupId { get; set; }

		public virtual DepartmentGroup StationGroup { get; set; }

		public string Course { get; set; }

		public string CourseCode { get; set; }

		public string Instructors { get; set; }

		public string Cause { get; set; }

		[ForeignKey("InvestigatedBy"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string InvestigatedByUserId { get; set; }

		public virtual IdentityUser InvestigatedBy { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? EndedOn { get; set; }

		public DateTime LoggedOn { get; set; }

		[ForeignKey("LoggedBy"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string LoggedByUserId { get; set; }

		public virtual IdentityUser LoggedBy { get; set; }


		[ForeignKey("Officer"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string OfficerUserId { get; set; }

		public virtual IdentityUser Officer { get; set; }

		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? CallId { get; set; }

		public virtual Call Call { get; set; }

		public string OtherPersonnel { get; set; }

		public string Location { get; set; }

		public string OtherAgencies { get; set; }

		public string OtherUnits { get; set; }

		public string BodyLocation { get; set; }

		public string PronouncedDeceasedBy { get; set; }

		public virtual ICollection<LogUnit> Units { get; set; }

		public virtual ICollection<LogUser> Users { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LogId; }
			set { LogId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Logs";

		[NotMapped]
		public string IdName => "LogId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "StationGroup", "InvestigatedBy", "LoggedBy", "Officer", "Call", "Units", "Users" };
	}
}
