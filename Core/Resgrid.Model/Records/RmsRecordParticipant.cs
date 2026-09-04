using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Person participation on a Record (LogUser parity): user, display-name and group snapshots, optional
	/// unit assignment, role and participation times. Draft rows have <see cref="RevisionId"/> null;
	/// finalization copies them under the new revision so a later profile change never rewrites history.
	/// </summary>
	[Table("RmsRecordParticipants")]
	public class RmsRecordParticipant : IEntity
	{
		public string RmsRecordParticipantId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		public string RevisionId { get; set; }

		public string UserId { get; set; }

		public string DisplayNameSnapshot { get; set; }

		public int? GroupIdSnapshot { get; set; }

		public string GroupNameSnapshot { get; set; }

		/// <summary>Unit the person was assigned to for this Record, when any.</summary>
		public int? UnitId { get; set; }

		/// <summary>Instructor, Attendee, Personnel, Investigator, Officer, Facilitator, ...</summary>
		public string Role { get; set; }

		public DateTime? ParticipationStart { get; set; }

		public DateTime? ParticipationEnd { get; set; }

		/// <summary><see cref="RmsSourceKind"/> of the participation facts.</summary>
		public int SourceKind { get; set; }

		public int Ordinal { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordParticipantId; }
			set { RmsRecordParticipantId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordParticipants";

		[NotMapped]
		public string IdName => "RmsRecordParticipantId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
