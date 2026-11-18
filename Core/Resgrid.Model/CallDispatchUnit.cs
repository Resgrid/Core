using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatchUnits")]
	[ProtoContract]
	public class CallDispatchUnit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchUnitId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ForeignKey("Unit"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(3)]
		public int UnitId { get; set; }

		public virtual Unit Unit { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		public DateTime DispatchedOn { get; set; }

		/// <summary>
		/// Optional response code the unit was sent with (DispatchResponseCode.Id in the department's catalog). The code and
		/// its name are snapshotted beside it, so a later catalog edit never rewrites history. Shown to crews only when the
		/// department's codes setting has ShowToCrews on.
		/// </summary>
		[ProtoMember(8)]
		public int? ResponseCodeId { get; set; }

		[MaxLength(16)]
		[ProtoMember(9)]
		public string ResponseCode { get; set; }

		[MaxLength(100)]
		[ProtoMember(10)]
		public string ResponseCodeName { get; set; }

		/// <summary>
		/// When a dispatcher released (stood down) this unit from the call while the call stayed open (M0316), UTC; null while
		/// the unit is still on the call. A released dispatch stays on the call's record, so reports keep the dispatch time and
		/// the stand-down, but it is no longer live: no re-notification, no "on this call" list, no auto-close or status
		/// attribution. Re-dispatching the unit to the call clears it (<see cref="Redispatch"/>).
		/// </summary>
		[ProtoMember(11)]
		public DateTime? ReleasedOn { get; set; }

		/// <summary>Who released the unit from the call (<see cref="ReleasedOn"/>).</summary>
		[MaxLength(128)]
		[ProtoMember(12)]
		public string ReleasedByUserId { get; set; }

		/// <summary>True when the unit was released from the call and has not been re-dispatched to it since.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool IsReleased => ReleasedOn.HasValue;

		/// <summary>Stands the unit down from the call: the dispatch stays on the record but is no longer live.</summary>
		public void Release(DateTime releasedOnUtc, string releasedByUserId)
		{
			ReleasedOn = releasedOnUtc;
			ReleasedByUserId = releasedByUserId;
		}

		/// <summary>
		/// Sends a released unit back to the call: the dispatch is live again from <paramref name="dispatchedOnUtc"/>, as a
		/// re-page of a unit still on the call would record it.
		/// </summary>
		public void Redispatch(DateTime dispatchedOnUtc)
		{
			ReleasedOn = null;
			ReleasedByUserId = null;
			LastDispatchedOn = dispatchedOnUtc;
			DispatchCount++;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallDispatchUnitId; }
			set { CallDispatchUnitId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallDispatchUnits";

		[NotMapped]
		public string IdName => "CallDispatchUnitId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Unit", "Call", "IsReleased" };
	}
}
