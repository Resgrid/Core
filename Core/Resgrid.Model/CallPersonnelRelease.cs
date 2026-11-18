using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// A member released (stood down) from a call that stays open (M0316): the person counterpart of
	/// <see cref="CallDispatchUnit.ReleasedOn"/>. A row of its own because a person can be on a call without a dispatch row of
	/// their own (reached through a dispatched group or role, or by a status set against the call). While the row exists the
	/// person is off the call however they were reached: no re-page or rebroadcast, no "on this call" list, no status
	/// attribution to it. Dispatching the person to the call again by name removes it. One row per call and member; no
	/// foreign keys (the department purge, UnlinkedDepartmentCleanup, removes the rows).
	/// </summary>
	[Table("CallPersonnelReleases")]
	[ProtoContract]
	public class CallPersonnelRelease : IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		public string CallPersonnelReleaseId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(3)]
		public int CallId { get; set; }

		[Required]
		[MaxLength(128)]
		[ProtoMember(4)]
		public string UserId { get; set; }

		/// <summary>When the member was released from the call, UTC.</summary>
		[ProtoMember(5)]
		public DateTime ReleasedOn { get; set; }

		/// <summary>Who released the member (a dispatcher, or the member themselves).</summary>
		[MaxLength(128)]
		[ProtoMember(6)]
		public string ReleasedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallPersonnelReleaseId; }
			set { CallPersonnelReleaseId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "CallPersonnelReleases";

		[NotMapped]
		public string IdName => "CallPersonnelReleaseId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
