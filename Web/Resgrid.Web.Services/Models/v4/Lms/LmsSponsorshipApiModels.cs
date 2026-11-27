using System;
using System.Collections.Generic;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	/// <summary>One LMS sponsorship the department gives or receives (round 10 A3, T5 sponsored seats).</summary>
	public class LmsSponsorshipData
	{
		public int LmsSponsorshipId { get; set; }
		/// <summary>The sponsored department (its LMS-enabled members count in the sponsor's packs).</summary>
		public int SponsoredDepartmentId { get; set; }
		public string SponsoredName { get; set; }
		public int SponsorDepartmentId { get; set; }
		public string SponsorName { get; set; }
		/// <summary>Active or Ended.</summary>
		public string Status { get; set; }
		/// <summary>The most seats the sponsor gives (null: as many as its packs leave after its own members).</summary>
		public int? SeatLimit { get; set; }
		public int SeatsUsed { get; set; }
		public bool LinkOk { get; set; }
		public DateTime StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public string EndReason { get; set; }

		public static LmsSponsorshipData From(LmsSponsorshipView view) => view?.Sponsorship == null ? null : new LmsSponsorshipData
		{
			LmsSponsorshipId = view.Sponsorship.LmsSponsorshipId,
			SponsoredDepartmentId = view.Sponsorship.DepartmentId,
			SponsoredName = view.SponsoredName,
			SponsorDepartmentId = view.Sponsorship.SponsorDepartmentId,
			SponsorName = view.SponsorName,
			Status = ((LmsSponsorshipStatuses)view.Sponsorship.Status).ToString(),
			SeatLimit = view.Sponsorship.SeatLimit,
			SeatsUsed = view.SeatsUsed,
			LinkOk = view.LinkOk,
			StartedOn = view.Sponsorship.StartedOn,
			EndedOn = view.Sponsorship.EndedOn,
			EndReason = view.Sponsorship.EndedOn.HasValue ? ((LmsSponsorshipEndReasons)view.Sponsorship.EndReason).ToString() : null
		};
	}

	public class LmsSponsorshipsData
	{
		public List<LmsSponsorshipData> Sponsorships { get; set; } = new List<LmsSponsorshipData>();
		/// <summary>The department's own paying sponsorship (null when none): who pays and the seats it gives.</summary>
		public LmsSponsorshipGrantData Grant { get; set; }
	}

	public class LmsSponsorshipGrantData
	{
		public int LmsSponsorshipId { get; set; }
		public string SponsorName { get; set; }
		public int Capacity { get; set; }
	}

	public class LmsSponsorshipsResult : StandardApiResponseV4Base
	{
		public LmsSponsorshipsData Data { get; set; } = new LmsSponsorshipsData();
	}

	public class LmsSponsorInput
	{
		/// <summary>The linked department to sponsor (the link must share Training from this department).</summary>
		public int SponsoredDepartmentId { get; set; }
		public int? SeatLimit { get; set; }
	}

	public class LmsSponsorshipSeatLimitInput
	{
		public int LmsSponsorshipId { get; set; }
		public int? SeatLimit { get; set; }
	}

	public class LmsSponsorshipIdInput
	{
		public int LmsSponsorshipId { get; set; }
	}

	/// <summary>The outcome of a sponsorship operation: "success", or Status "failure" with the LmsSponsorshipErrors code in Error.</summary>
	public class LmsSponsorshipActionResult : StandardApiResponseV4Base
	{
		public string Error { get; set; }
	}
}
