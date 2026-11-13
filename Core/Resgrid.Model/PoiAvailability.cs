using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// How far a destination can take patients or loads right now (<see cref="PoiAvailability.Status"/>). Open is never
	/// stored: a destination with no availability row, or with an expired one, is open.
	/// </summary>
	public enum PoiAvailabilityStatuses
	{
		/// <summary>Taking everything it offers.</summary>
		Open = 0,

		/// <summary>Taking patients, but some capabilities or capacity are reduced (see the unavailable capabilities and note).</summary>
		Limited = 1,

		/// <summary>Asking crews to go elsewhere unless there is no alternative (emergency department diversion / capacity overload).</summary>
		Diversion = 2,

		/// <summary>Not taking anyone.</summary>
		Closed = 3
	}

	/// <summary>
	/// A destination's current availability (M0315), set by a dispatcher or administrator when a hospital reports capacity
	/// overload, a closed emergency department, or one service down (e.g. the cath lab). One row per POI, replaced on each
	/// change; setting it back to open deletes it, and a row past <see cref="ExpiresOn"/> reads as open. No foreign keys: POI
	/// deletes and the department purge remove the rows.
	/// </summary>
	[Table("PoiAvailabilities")]
	public class PoiAvailability : IEntity
	{
		public const int MaxNoteLength = 500;

		[Key]
		[Required]
		public string PoiAvailabilityId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int PoiId { get; set; }

		/// <summary><see cref="PoiAvailabilityStatuses"/>; never Open in a stored row.</summary>
		public int Status { get; set; }

		/// <summary>Comma-separated <see cref="PoiCapability.PoiCapabilityId"/>s the destination cannot provide right now.</summary>
		public string UnavailableCapabilityIds { get; set; }

		[MaxLength(MaxNoteLength)]
		public string Note { get; set; }

		public string SetByUserId { get; set; }

		public DateTime SetOn { get; set; }

		/// <summary>When the report lapses and the destination reads as open again; null lasts until someone changes it.</summary>
		public DateTime? ExpiresOn { get; set; }

		/// <summary>The parsed <see cref="UnavailableCapabilityIds"/>, without blanks or duplicates.</summary>
		public List<string> GetUnavailableCapabilityIds()
		{
			return PoiAvailabilityRules.ParseIds(UnavailableCapabilityIds);
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PoiAvailabilityId; }
			set { PoiAvailabilityId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "PoiAvailabilities";

		[NotMapped]
		public string IdName => "PoiAvailabilityId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Reading a <see cref="PoiAvailability"/>: what counts as current, and what a destination can still take.</summary>
	public static class PoiAvailabilityRules
	{
		/// <summary>The longest a report may run before it lapses (a week); a longer or missing expiry is kept as given.</summary>
		public static readonly TimeSpan MaxExpiry = TimeSpan.FromDays(7);

		/// <summary>The report when it is still current (not open, not expired), else null.</summary>
		public static PoiAvailability Current(PoiAvailability availability, DateTime nowUtc)
		{
			if (availability == null || availability.Status == (int)PoiAvailabilityStatuses.Open)
				return null;

			if (!Enum.IsDefined(typeof(PoiAvailabilityStatuses), availability.Status))
				return null;

			return availability.ExpiresOn.HasValue && availability.ExpiresOn.Value <= nowUtc ? null : availability;
		}

		/// <summary>The destination's current status: Open when there is no current report.</summary>
		public static PoiAvailabilityStatuses StatusOf(PoiAvailability availability, DateTime nowUtc)
		{
			var current = Current(availability, nowUtc);
			return current == null ? PoiAvailabilityStatuses.Open : (PoiAvailabilityStatuses)current.Status;
		}

		/// <summary>
		/// True when the destination can take a patient needing the capability now: it is not closed, and the capability
		/// (when one is asked for) is not reported unavailable. A destination on diversion still can, but crews are warned.
		/// </summary>
		public static bool CanProvide(PoiAvailability availability, string poiCapabilityId, DateTime nowUtc)
		{
			var current = Current(availability, nowUtc);
			if (current == null)
				return true;

			if (current.Status == (int)PoiAvailabilityStatuses.Closed)
				return false;

			if (string.IsNullOrWhiteSpace(poiCapabilityId))
				return true;

			return !current.GetUnavailableCapabilityIds().Contains(poiCapabilityId, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Comma-separated ids to a list, trimmed, without blanks or duplicates (case-insensitive).</summary>
		public static List<string> ParseIds(string ids)
		{
			if (string.IsNullOrWhiteSpace(ids))
				return new List<string>();

			return ids.Split(',')
				.Select(x => x.Trim())
				.Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}
}
