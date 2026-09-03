using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// First-class typed detail for the locked Logs-parity definitions (Run, Training, Work, Meeting,
	/// Coroner, Callback, Unit Activity) plus the Call snapshot captured at authoring time. One row per
	/// Record with <see cref="RevisionId"/> null is the working draft; one immutable row per revision is
	/// written at finalize/amend. Locked definitions never use the generic RmsRecordValues table (plan
	/// section 5.3). Protected-candidate columns (narrative, initial report, cause, contact details,
	/// location, body/deceased fields, case number, destination) are envelope-capable text; IsProtected
	/// and ProtectedCatalogVersion ship inert.
	/// </summary>
	[Table("RmsOperationalRecordDetails")]
	public class RmsOperationalRecordDetail : IEntity
	{
		public string RmsOperationalRecordDetailId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		/// <summary>Null = the working draft row; otherwise the immutable snapshot for that revision.</summary>
		public string RevisionId { get; set; }

		public string Narrative { get; set; }

		public string InitialReport { get; set; }

		/// <summary>Run type / meeting type free text (Logs parity Type column).</summary>
		public string Type { get; set; }

		public string Course { get; set; }

		public string CourseCode { get; set; }

		public string Instructors { get; set; }

		public string Cause { get; set; }

		public string InvestigatedByUserId { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public string OtherPersonnel { get; set; }

		public string Location { get; set; }

		public string OtherAgencies { get; set; }

		public string OtherUnits { get; set; }

		public string BodyLocation { get; set; }

		public string PronouncedDeceasedBy { get; set; }

		public string CaseNumber { get; set; }

		public string Destination { get; set; }

		public string Facilitator { get; set; }

		/// <summary>Unit Activity subject unit.</summary>
		public int? UnitId { get; set; }

		/// <summary>Unit Activity timestamp (UnitLog.Timestamp parity).</summary>
		public DateTime? ActivityOn { get; set; }

		public string CallNumber { get; set; }

		public string CallName { get; set; }

		public string CallType { get; set; }

		public int? CallPriority { get; set; }

		public DateTime? CallLoggedOn { get; set; }

		public string CallAddress { get; set; }

		public string CallNature { get; set; }

		/// <summary>ADP row marker: true once cataloged values carry rgdp envelopes. Inert (false) until enrollment.</summary>
		public bool IsProtected { get; set; }

		/// <summary>
		/// Inert until Protected Data enrollment (plan section 5.9.1): null today; a protected write nulls the typed
		/// columns and stores rgdp:1:{keyVersion}:{base64(nonce|tag|ciphertext)} here, so enrolling never needs a
		/// schema change on a populated table.
		/// </summary>
		public string ProtectedEnvelope { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsOperationalRecordDetailId; }
			set { RmsOperationalRecordDetailId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsOperationalRecordDetails";

		[NotMapped]
		public string IdName => "RmsOperationalRecordDetailId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
