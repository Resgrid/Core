using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	[ProtoContract]
	public class ShiftQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[ProtoMember(2)]
		public int ShiftSignupTradeId { get; set; }

		[ProtoMember(3)]
		public string SourceUserId { get; set; }

		[ProtoMember(4)]
		public int Type { get; set; }

		[ProtoMember(5)]
		public string DepartmentNumber { get; set; }

		[ProtoMember(6)]
		public int ShiftId { get; set; }

		[ProtoMember(7)]
		public int ShiftSignupId { get; set; }

		/// <summary>Cancelled static shift (workshift) day trades: the day, since the trade row is deleted.</summary>
		[ProtoMember(8)]
		public string WorkshiftDayId { get; set; }

		/// <summary>Cancelled trades: the requester and applicants to tell, since the trade row is deleted.</summary>
		[ProtoMember(9)]
		public List<string> UserIds { get; set; }

		/// <summary>Cancelled trades: who cancelled it (the requester or a supervisor).</summary>
		[ProtoMember(10)]
		public string ActingUserId { get; set; }
	}

	public enum ShiftQueueTypes
	{
		TradeRequested = 0,
		TradeProposed = 1,
		TradeFilled = 2,
		TradeRejected = 3,
		ShiftCreated = 4,
		ShiftUpdated = 5,
		ShiftDaysAdded = 6,
		SignupPendingApproval = 7,
		SignupReviewed = 8,
		TradePendingApproval = 9,
		TradeReviewed = 10,
		DayAssigned = 11,
		DayRemoved = 12,
		TradeCancelled = 13
	}
}