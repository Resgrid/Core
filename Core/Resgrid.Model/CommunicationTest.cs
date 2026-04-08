using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CommunicationTests")]
	public class CommunicationTest : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid CommunicationTestId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(500)]
		public string Name { get; set; }

		[MaxLength(4000)]
		public string Description { get; set; }

		public int ScheduleType { get; set; }

		public bool Sunday { get; set; }

		public bool Monday { get; set; }

		public bool Tuesday { get; set; }

		public bool Wednesday { get; set; }

		public bool Thursday { get; set; }

		public bool Friday { get; set; }

		public bool Saturday { get; set; }

		public int? DayOfMonth { get; set; }

		[MaxLength(50)]
		public string Time { get; set; }

		public bool TestSms { get; set; }

		public bool TestEmail { get; set; }

		public bool TestVoice { get; set; }

		public bool TestPush { get; set; }

		public bool Active { get; set; }

		[Required]
		[MaxLength(128)]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int ResponseWindowMinutes { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommunicationTestId == Guid.Empty ? null : (object)CommunicationTestId.ToString(); }
			set { CommunicationTestId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "CommunicationTests";

		[NotMapped]
		public string IdName => "CommunicationTestId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
