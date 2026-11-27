using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Who a piece of department content is for. Shared by Training audiences (T0) and Protocol audiences (P1), resolved by
	/// <see cref="Services.IContentAudienceService"/>. Persisted as an int in each owner's audience table: append only.
	/// </summary>
	public enum ContentAudienceKinds
	{
		/// <summary>Every active member of the department. RefId is null.</summary>
		Everyone = 0,

		/// <summary>Members of a department group. RefId is the DepartmentGroupId.</summary>
		Group = 1,

		/// <summary>Members of a personnel role. RefId is the PersonnelRoleId.</summary>
		Role = 2,

		/// <summary>Holders of a current (active or trainee) certification of a type. RefId is the DepartmentCertificationTypeId.</summary>
		CertificationType = 3,

		/// <summary>Members riding a unit of a type (the unit's active roles). RefId is the UnitTypeId.</summary>
		UnitType = 4,

		/// <summary>Standing members of a shift. RefId is the ShiftId.</summary>
		Shift = 5,

		/// <summary>One member. RefId is the user id.</summary>
		User = 6
	}

	/// <summary>One audience rule: a kind and the id it refers to.</summary>
	public interface IContentAudienceRule
	{
		int Kind { get; }

		string RefId { get; }
	}

	/// <summary>An audience rule that is not stored (previews, tests, API input).</summary>
	public class ContentAudienceRule : IContentAudienceRule
	{
		public ContentAudienceRule() { }

		public ContentAudienceRule(ContentAudienceKinds kind, string refId = null)
		{
			Kind = (int)kind;
			RefId = refId;
		}

		public int Kind { get; set; }

		public string RefId { get; set; }
	}

	/// <summary>Why a member an officer picked is not addressed (T-D16: the officer is told at save time).</summary>
	public enum ContentAudienceSkipReasons
	{
		NotInDepartment = 0,
		Deleted = 1,
		Disabled = 2,
		Hidden = 3,
		NotActivated = 4,

		/// <summary>
		/// Round 10 A7 (§17.4): LMS assignments only: the member holds no LMS seat, so they cannot be enrolled in a department course or path
		/// (the Academy and baseline trainings never skip for this). They are assigned once a seat is enabled and the assignment runs again.
		/// </summary>
		NotLicensed = 5
	}

	public class ContentAudienceSkip
	{
		public string UserId { get; set; }

		public ContentAudienceSkipReasons Reason { get; set; }
	}

	/// <summary>The active members an audience resolves to, and the picked members it could not address.</summary>
	public class ContentAudienceResolution
	{
		public HashSet<string> MemberUserIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public List<ContentAudienceSkip> Skipped { get; set; } = new List<ContentAudienceSkip>();
	}

	/// <summary>
	/// What one member belongs to, read once, so testing that member against many trainings' rules costs no queries.
	/// </summary>
	public class ContentAudienceMemberFacts
	{
		public string UserId { get; set; }

		/// <summary>An active member (not deleted, disabled, hidden or dormant). Inactive members match no audience.</summary>
		public bool IsActive { get; set; }

		public HashSet<int> GroupIds { get; set; } = new HashSet<int>();

		public HashSet<int> RoleIds { get; set; } = new HashSet<int>();

		public HashSet<int> CertificationTypeIds { get; set; } = new HashSet<int>();

		public HashSet<int> UnitTypeIds { get; set; } = new HashSet<int>();

		public HashSet<int> ShiftIds { get; set; } = new HashSet<int>();
	}

	/// <summary>Pure matching of one member's facts against audience rules (no database).</summary>
	public static class ContentAudienceMatcher
	{
		public static bool Matches(ContentAudienceMemberFacts facts, IEnumerable<IContentAudienceRule> rules)
		{
			if (facts == null || !facts.IsActive || string.IsNullOrWhiteSpace(facts.UserId) || rules == null)
				return false;

			foreach (var rule in rules)
			{
				if (rule == null)
					continue;

				switch ((ContentAudienceKinds)rule.Kind)
				{
					case ContentAudienceKinds.Everyone:
						return true;
					case ContentAudienceKinds.User:
						if (string.Equals(rule.RefId, facts.UserId, StringComparison.OrdinalIgnoreCase)) return true;
						break;
					case ContentAudienceKinds.Group:
						if (TryId(rule, out var groupId) && facts.GroupIds.Contains(groupId)) return true;
						break;
					case ContentAudienceKinds.Role:
						if (TryId(rule, out var roleId) && facts.RoleIds.Contains(roleId)) return true;
						break;
					case ContentAudienceKinds.CertificationType:
						if (TryId(rule, out var typeId) && facts.CertificationTypeIds.Contains(typeId)) return true;
						break;
					case ContentAudienceKinds.UnitType:
						if (TryId(rule, out var unitTypeId) && facts.UnitTypeIds.Contains(unitTypeId)) return true;
						break;
					case ContentAudienceKinds.Shift:
						if (TryId(rule, out var shiftId) && facts.ShiftIds.Contains(shiftId)) return true;
						break;
				}
			}

			return false;
		}

		/// <summary>The numeric id of a group, role, certification type, unit type or shift rule.</summary>
		public static bool TryId(IContentAudienceRule rule, out int id)
		{
			id = 0;
			return rule?.RefId != null && int.TryParse(rule.RefId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id) && id > 0;
		}

		/// <summary>Rules de-duplicated by kind and id (user ids without regard to case), Everyone absorbing the rest.</summary>
		public static List<ContentAudienceRule> Normalize(IEnumerable<IContentAudienceRule> rules)
		{
			var list = new List<ContentAudienceRule>();
			if (rules == null)
				return list;

			var all = rules.Where(r => r != null && Enum.IsDefined(typeof(ContentAudienceKinds), r.Kind)).ToList();
			if (all.Any(r => r.Kind == (int)ContentAudienceKinds.Everyone))
				return new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Everyone) };

			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var rule in all)
			{
				var refId = rule.RefId?.Trim();
				if (string.IsNullOrWhiteSpace(refId))
					continue;
				if (rule.Kind != (int)ContentAudienceKinds.User && !TryId(new ContentAudienceRule { Kind = rule.Kind, RefId = refId }, out _))
					continue;
				if (seen.Add(rule.Kind + ":" + refId))
					list.Add(new ContentAudienceRule { Kind = rule.Kind, RefId = refId });
			}

			return list;
		}
	}

	/// <summary>Where a membership change came from (diagnostics only). Append only.</summary>
	public enum ContentAudienceChangeSources
	{
		Membership = 0,
		Group = 1,
		Role = 2,
		Certification = 3,
		Unit = 4,
		Shift = 5
	}

	/// <summary>
	/// A queued membership change (group, role, shift, certification, unit crew, joining the department) for one member, or
	/// for a whole department when <see cref="UserId"/> is null. Worker 83 re-evaluates the audiences of open dynamic
	/// content for exactly these members, so it never sweeps every department.
	/// </summary>
	[Table("ContentAudienceChanges")]
	public class ContentAudienceChange : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentAudienceChangeId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public int Source { get; set; }

		public DateTime QueuedOn { get; set; }

		public DateTime? ProcessedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContentAudienceChangeId; }
			set { ContentAudienceChangeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ContentAudienceChanges";

		[NotMapped]
		public string IdName => "ContentAudienceChangeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
