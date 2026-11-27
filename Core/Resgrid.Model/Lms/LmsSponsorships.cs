using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Model.Helpers;

namespace Resgrid.Model.Lms
{
	/// <summary>LmsSponsorships.Status. Persisted: append only.</summary>
	public enum LmsSponsorshipStatuses
	{
		Active = 0,
		Ended = 1
	}

	/// <summary>Why a sponsorship ended (LmsSponsorships.EndReason). Persisted: append only.</summary>
	public enum LmsSponsorshipEndReasons
	{
		None = 0,
		/// <summary>The sponsor stopped paying for the department.</summary>
		EndedBySponsor = 1,
		/// <summary>The sponsored department declined it.</summary>
		DeclinedBySponsored = 2,
		/// <summary>The Department Link was disabled or deleted, or the sponsor stopped sharing Training over it.</summary>
		LinkRevoked = 3
	}

	/// <summary>
	/// Sponsored seats (T5 item 2a, R-L3; decided 2026-11-24, §25.4a items 8 and 8b): a publisher with the LMS add-on pays for a subscribing
	/// department's members. The sponsored department's LMS-enabled members count in the sponsor's packs, and it gets the LMS runtime for the
	/// sponsor's course sets only (take courses; no authoring, no requirements of its own). A department on the free base plan can be
	/// sponsored; the paid-plan rule applies to the sponsor. Granted and revoked through the Department Link (the sponsor must share Training
	/// over an enabled link); the sponsored department sees who pays. Ending it lapses access with the add-on's own lapse rules, from
	/// <see cref="EndedOn"/>. No foreign keys: the department cleanup removes the rows of both parties.
	/// </summary>
	[Table("LmsSponsorships")]
	public class LmsSponsorship : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSponsorshipId { get; set; }

		/// <summary>The sponsored department.</summary>
		public int DepartmentId { get; set; }

		/// <summary>The department that pays (its LMS packs hold the seats).</summary>
		public int SponsorDepartmentId { get; set; }

		public int DepartmentLinkId { get; set; }

		/// <summary><see cref="LmsSponsorshipStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The most seats the sponsor lets the department use (null: as many as the sponsor's packs have free).</summary>
		public int? SeatLimit { get; set; }

		public DateTime StartedOn { get; set; }

		public string StartedByUserId { get; set; }

		public DateTime? EndedOn { get; set; }

		public string EndedByUserId { get; set; }

		/// <summary><see cref="LmsSponsorshipEndReasons"/>.</summary>
		public int EndReason { get; set; }

		[NotMapped] [JsonIgnore] public bool IsActive => Status == (int)LmsSponsorshipStatuses.Active;

		[NotMapped] [JsonIgnore] public object IdValue { get => LmsSponsorshipId; set => LmsSponsorshipId = (int)value; }
		[NotMapped] public string TableName => "LmsSponsorships";
		[NotMapped] public string IdName => "LmsSponsorshipId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>Why a sponsorship change was refused.</summary>
	public enum LmsSponsorshipErrors
	{
		None = 0,
		NotAuthorized,
		NotFound,
		/// <summary>No enabled link over which the sponsor shares Training with the department.</summary>
		NotShared,
		/// <summary>The sponsor has no active LMS add-on (the paid-plan rule applies to the department that pays).</summary>
		SponsorNeedsLms,
		AlreadySponsored,
		InvalidInput
	}

	/// <summary>One sponsorship as either party sees it.</summary>
	public sealed class LmsSponsorshipView
	{
		public LmsSponsorship Sponsorship { get; set; }

		public string SponsorName { get; set; }

		public string SponsoredName { get; set; }

		/// <summary>Seats the sponsored department uses now (its active LMS-enabled members).</summary>
		public int SeatsUsed { get; set; }

		/// <summary>The link is still enabled and the sponsor still shares Training over it.</summary>
		public bool LinkOk { get; set; }
	}

	/// <summary>
	/// The sponsor-side seat arithmetic (pure). The sponsor's own members come first: a sponsored department gets what is left of the
	/// sponsor's purchased seats after the sponsor's counted seats, its external-learner seats and the other sponsored departments' seats,
	/// capped by the sponsorship's seat limit.
	/// </summary>
	public static class LmsSponsorshipRules
	{
		/// <summary>The seats a sponsored department may hold in all under the sponsorship (<paramref name="sponsoredSeatsUsed"/>, its current use, is inside that figure).</summary>
		public static int CapacityFor(int sponsorPurchasedSeats, int sponsorOwnSeats, int sponsorExternalLearnerSeats, int otherSponsoredSeats, int sponsoredSeatsUsed, int? seatLimit)
		{
			// Everything the sponsor's own members, its external learners and its other sponsored departments do not hold; what this department
			// holds now is inside that (it is not counted in the others), so it keeps its seats as long as the sponsor's own members leave room.
			var capacity = Math.Max(0, sponsorPurchasedSeats - sponsorOwnSeats - sponsorExternalLearnerSeats - otherSponsoredSeats);
			if (seatLimit.HasValue)
				capacity = Math.Min(capacity, Math.Max(0, seatLimit.Value));
			return Math.Max(0, capacity);
		}

		/// <summary>The members licensed under a sponsorship: enabled first, up to the capacity (the same order as the add-on's own seats).</summary>
		public static HashSet<string> Licensed(IEnumerable<LmsMemberSeat> seats, ISet<string> activeMembers, int capacity) =>
			LmsSeatRules.Licensed(seats, activeMembers, Math.Max(0, capacity), true);

		/// <summary>Seats counted against the sponsor: every active sponsorship's counted seats, each capped by its own limit.</summary>
		public static int SponsoredSeats(IEnumerable<(int Counted, int? Limit)> sponsorships) =>
			(sponsorships ?? Enumerable.Empty<(int, int?)>()).Sum(s => s.Limit.HasValue ? Math.Min(s.Counted, Math.Max(0, s.Limit.Value)) : s.Counted);
	}
}
