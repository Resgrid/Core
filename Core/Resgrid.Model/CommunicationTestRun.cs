using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CommunicationTestRuns")]
	public class CommunicationTestRun : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid CommunicationTestRunId { get; set; }

		[Required]
		public Guid CommunicationTestId { get; set; }

		[ForeignKey("CommunicationTestId")]
		public virtual CommunicationTest CommunicationTest { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[MaxLength(128)]
		public string InitiatedByUserId { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public int Status { get; set; }

		[Required]
		[MaxLength(20)]
		public string RunCode { get; set; }

		public int TotalUsersTested { get; set; }

		public int TotalResponses { get; set; }

		/// <summary>
		/// Snapshot of the audience this run was started for, taken from the test's targeting at
		/// start time. A JSON array of user ids when the test was targeted, the JSON literal
		/// <c>null</c> when it was untargeted (whole department), and a NULL column only for runs
		/// created before snapshots existed. Frozen so that editing a test's targets while its run
		/// is queued cannot change who that run tests.
		/// </summary>
		public string TargetedUserIds { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommunicationTestRunId == Guid.Empty ? null : (object)CommunicationTestRunId.ToString(); }
			set { CommunicationTestRunId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "CommunicationTestRuns";

		[NotMapped]
		public string IdName => "CommunicationTestRunId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CommunicationTest" };
	}
}
