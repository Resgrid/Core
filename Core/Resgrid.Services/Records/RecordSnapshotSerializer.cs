using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Builds the complete, checksummed revision snapshot (RMS plan section 4.8) and computes on-demand
	/// diffs between two snapshots. Serialization is canonical (ordered members, invariant culture, no
	/// indentation) so the same content always produces the same checksum; attachment bytes are never
	/// part of a snapshot, only their metadata.
	/// </summary>
	public static class RecordSnapshotSerializer
	{
		private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
		{
			ContractResolver = new OrderedContractResolver(),
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			NullValueHandling = NullValueHandling.Include,
			Formatting = Formatting.None,
			Culture = System.Globalization.CultureInfo.InvariantCulture
		};

		/// <summary>Fields that require RecordRestricted_View inside a revision or a diff (plan section 5.9.2, Tier 1).</summary>
		public static readonly HashSet<string> RestrictedDetailFields = new HashSet<string>(StringComparer.Ordinal)
		{
			nameof(RmsOperationalRecordDetail.CaseNumber),
			nameof(RmsOperationalRecordDetail.PronouncedDeceasedBy),
			nameof(RmsOperationalRecordDetail.BodyLocation),
			nameof(RmsOperationalRecordDetail.OtherPersonnel),
			nameof(RmsOperationalRecordDetail.Destination)
		};

		public static readonly string[] DetailFieldOrder =
		{
			nameof(RmsOperationalRecordDetail.Narrative), nameof(RmsOperationalRecordDetail.InitialReport), nameof(RmsOperationalRecordDetail.Type),
			nameof(RmsOperationalRecordDetail.Course), nameof(RmsOperationalRecordDetail.CourseCode), nameof(RmsOperationalRecordDetail.Instructors),
			nameof(RmsOperationalRecordDetail.Cause), nameof(RmsOperationalRecordDetail.InvestigatedByUserId), nameof(RmsOperationalRecordDetail.ContactName),
			nameof(RmsOperationalRecordDetail.ContactNumber), nameof(RmsOperationalRecordDetail.OtherPersonnel), nameof(RmsOperationalRecordDetail.Location),
			nameof(RmsOperationalRecordDetail.OtherAgencies), nameof(RmsOperationalRecordDetail.OtherUnits), nameof(RmsOperationalRecordDetail.BodyLocation),
			nameof(RmsOperationalRecordDetail.PronouncedDeceasedBy), nameof(RmsOperationalRecordDetail.CaseNumber), nameof(RmsOperationalRecordDetail.Destination),
			nameof(RmsOperationalRecordDetail.Facilitator), nameof(RmsOperationalRecordDetail.UnitId), nameof(RmsOperationalRecordDetail.ActivityOn),
			nameof(RmsOperationalRecordDetail.CallNumber), nameof(RmsOperationalRecordDetail.CallName), nameof(RmsOperationalRecordDetail.CallType),
			nameof(RmsOperationalRecordDetail.CallPriority), nameof(RmsOperationalRecordDetail.CallLoggedOn), nameof(RmsOperationalRecordDetail.CallAddress),
			nameof(RmsOperationalRecordDetail.CallNature)
		};

		public static RecordSnapshot Build(RecordAggregate aggregate)
		{
			if (aggregate?.Record == null)
				throw new ArgumentNullException(nameof(aggregate));

			var r = aggregate.Record;
			return new RecordSnapshot
			{
				RecordId = r.RmsOperationalRecordId,
				DepartmentId = r.DepartmentId,
				DefinitionKey = r.DefinitionKey,
				DefinitionVersion = r.DefinitionVersion,
				RecordType = r.RecordType,
				RecordNumber = r.RecordNumber,
				DraftReference = r.DraftReference,
				StationGroupId = r.StationGroupId,
				CallId = r.CallId,
				ExternalId = r.ExternalId,
				AuthorUserId = r.AuthorUserId,
				StartedOn = r.StartedOn,
				EndedOn = r.EndedOn,
				Details = aggregate.Details,
				Participants = (aggregate.Participants ?? new List<RmsRecordParticipant>()).OrderBy(p => p.Ordinal).ThenBy(p => p.UserId, StringComparer.Ordinal).ToList(),
				Units = (aggregate.Units ?? new List<RmsRecordUnitResponse>()).OrderBy(u => u.Ordinal).ThenBy(u => u.UnitId).ToList(),
				Attachments = (aggregate.Attachments ?? new List<RmsRecordAttachment>()).Select(StripBytes).OrderBy(a => a.UploadedOn).ThenBy(a => a.RmsRecordAttachmentId, StringComparer.Ordinal).ToList()
			};
		}

		public static string Serialize(RecordSnapshot snapshot)
		{
			return JsonConvert.SerializeObject(snapshot, Settings);
		}

		public static RecordSnapshot Deserialize(string json)
		{
			return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<RecordSnapshot>(json, Settings);
		}

		/// <summary>Lower-case hex SHA-256 of the UTF-8 bytes.</summary>
		public static string Checksum(string content)
		{
			using var sha = SHA256.Create();
			var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
			var sb = new StringBuilder(hash.Length * 2);
			foreach (var b in hash)
				sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		public static string Checksum(byte[] content)
		{
			using var sha = SHA256.Create();
			var hash = sha.ComputeHash(content ?? Array.Empty<byte>());
			var sb = new StringBuilder(hash.Length * 2);
			foreach (var b in hash)
				sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		/// <summary>
		/// Field-level diff grouped by section (plan section 4.8). Unchanged fields are omitted. A restricted
		/// field that changed is reported as changed with both values withheld when the viewer lacks
		/// RecordRestricted_View, so history never becomes a bypass.
		/// </summary>
		public static List<RecordFieldDiff> Diff(RecordSnapshot from, RecordSnapshot to, bool canViewRestricted)
		{
			var diffs = new List<RecordFieldDiff>();
			from = from ?? new RecordSnapshot();
			to = to ?? new RecordSnapshot();

			Compare(diffs, "Record", nameof(RecordSnapshot.RecordNumber), from.RecordNumber, to.RecordNumber, false, canViewRestricted);
			Compare(diffs, "Record", nameof(RecordSnapshot.StationGroupId), from.StationGroupId?.ToString(), to.StationGroupId?.ToString(), false, canViewRestricted);
			Compare(diffs, "Record", nameof(RecordSnapshot.CallId), from.CallId?.ToString(), to.CallId?.ToString(), false, canViewRestricted);
			Compare(diffs, "Record", nameof(RecordSnapshot.ExternalId), from.ExternalId, to.ExternalId, false, canViewRestricted);
			Compare(diffs, "Record", nameof(RecordSnapshot.StartedOn), Iso(from.StartedOn), Iso(to.StartedOn), false, canViewRestricted);
			Compare(diffs, "Record", nameof(RecordSnapshot.EndedOn), Iso(from.EndedOn), Iso(to.EndedOn), false, canViewRestricted);

			var fromDetails = from.Details ?? new RmsOperationalRecordDetail();
			var toDetails = to.Details ?? new RmsOperationalRecordDetail();
			foreach (var field in DetailFieldOrder)
			{
				var property = typeof(RmsOperationalRecordDetail).GetProperty(field);
				var oldValue = Stringify(property.GetValue(fromDetails));
				var newValue = Stringify(property.GetValue(toDetails));
				Compare(diffs, "Details", field, oldValue, newValue, RestrictedDetailFields.Contains(field), canViewRestricted);
			}

			DiffSet(diffs, "Participants", from.Participants.Select(p => p.UserId + (p.UnitId.HasValue ? "@" + p.UnitId : string.Empty) + (string.IsNullOrEmpty(p.Role) ? string.Empty : ":" + p.Role)),
				to.Participants.Select(p => p.UserId + (p.UnitId.HasValue ? "@" + p.UnitId : string.Empty) + (string.IsNullOrEmpty(p.Role) ? string.Empty : ":" + p.Role)));
			DiffSet(diffs, "Units", from.Units.Select(u => $"{u.UnitId}|{Iso(u.Dispatched)}|{Iso(u.Enroute)}|{Iso(u.OnScene)}|{Iso(u.Released)}|{Iso(u.InQuarters)}"),
				to.Units.Select(u => $"{u.UnitId}|{Iso(u.Dispatched)}|{Iso(u.Enroute)}|{Iso(u.OnScene)}|{Iso(u.Released)}|{Iso(u.InQuarters)}"));
			DiffSet(diffs, "Attachments", from.Attachments.Select(a => a.RmsRecordAttachmentId + ":" + a.Checksum), to.Attachments.Select(a => a.RmsRecordAttachmentId + ":" + a.Checksum));

			return diffs;
		}

		private static void Compare(List<RecordFieldDiff> diffs, string section, string field, string oldValue, string newValue, bool restricted, bool canViewRestricted)
		{
			if (string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal))
				return;

			var withheld = restricted && !canViewRestricted;
			diffs.Add(new RecordFieldDiff
			{
				Section = section,
				FieldKey = field,
				OldValue = withheld ? null : oldValue,
				NewValue = withheld ? null : newValue,
				Withheld = withheld
			});
		}

		private static void DiffSet(List<RecordFieldDiff> diffs, string section, IEnumerable<string> from, IEnumerable<string> to)
		{
			var a = new HashSet<string>(from ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
			var b = new HashSet<string>(to ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

			foreach (var removed in a.Except(b).OrderBy(x => x, StringComparer.Ordinal))
				diffs.Add(new RecordFieldDiff { Section = section, FieldKey = "removed", OldValue = removed, NewValue = null });

			foreach (var added in b.Except(a).OrderBy(x => x, StringComparer.Ordinal))
				diffs.Add(new RecordFieldDiff { Section = section, FieldKey = "added", OldValue = null, NewValue = added });
		}

		private static string Iso(DateTime? value)
		{
			return value?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
		}

		private static string Stringify(object value)
		{
			switch (value)
			{
				case null: return null;
				case DateTime dt: return Iso(dt);
				default: return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
			}
		}

		private static RmsRecordAttachment StripBytes(RmsRecordAttachment attachment)
		{
			return new RmsRecordAttachment
			{
				RmsRecordAttachmentId = attachment.RmsRecordAttachmentId,
				DepartmentId = attachment.DepartmentId,
				ProtectionId = attachment.ProtectionId,
				RecordId = attachment.RecordId,
				FileName = attachment.FileName,
				ContentType = attachment.ContentType,
				ByteSize = attachment.ByteSize,
				Checksum = attachment.Checksum,
				StorageReference = attachment.StorageReference,
				Description = attachment.Description,
				UploadedByUserId = attachment.UploadedByUserId,
				UploadedOn = attachment.UploadedOn,
				ScanState = attachment.ScanState,
				MetadataStripped = attachment.MetadataStripped,
				IsProtected = attachment.IsProtected,
				ProtectedCatalogVersion = attachment.ProtectedCatalogVersion,
				CreatedOn = attachment.CreatedOn,
				ModifiedOn = attachment.ModifiedOn,
				RowVersion = attachment.RowVersion,
				DeletedOn = attachment.DeletedOn
			};
		}

		private sealed class OrderedContractResolver : DefaultContractResolver
		{
			protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
			{
				return base.CreateProperties(type, memberSerialization).OrderBy(p => p.PropertyName, StringComparer.Ordinal).ToList();
			}
		}
	}
}
