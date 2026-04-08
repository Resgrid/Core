using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CommunicationTestResults")]
	public class CommunicationTestResult : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid CommunicationTestResultId { get; set; }

		[Required]
		public Guid CommunicationTestRunId { get; set; }

		[ForeignKey("CommunicationTestRunId")]
		public virtual CommunicationTestRun CommunicationTestRun { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		public int Channel { get; set; }

		[MaxLength(500)]
		public string ContactValue { get; set; }

		[MaxLength(200)]
		public string ContactCarrier { get; set; }

		public int VerificationStatus { get; set; }

		public bool SendAttempted { get; set; }

		public bool SendSucceeded { get; set; }

		public DateTime? SentOn { get; set; }

		public bool Responded { get; set; }

		public DateTime? RespondedOn { get; set; }

		[MaxLength(128)]
		public string ResponseToken { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommunicationTestResultId == Guid.Empty ? null : (object)CommunicationTestResultId.ToString(); }
			set { CommunicationTestResultId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "CommunicationTestResults";

		[NotMapped]
		public string IdName => "CommunicationTestResultId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CommunicationTestRun" };
	}
}
