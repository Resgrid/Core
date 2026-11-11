using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Web.Services.Models.v4.PersonnelStaffing;
using Resgrid.Web.Services.Models.v4.PersonnelStatuses;
using Resgrid.Web.Services.Models.v4.UnitStatus;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Idempotency keys for v4 status writes replayed from an app's offline outbox (InboundEmailReceipts kinds 7 to 9). Omni queues
	/// every unit status, personnel status and staffing write and, when the answer is lost (15 s timeout, dropped connection),
	/// sends the same body again with the same <c>EventId</c> (its outbox event id). Without a key each replay saved a second
	/// status, announced it again and re-ran call attribution, auto-close and outstanding requirements.
	/// <para>
	/// <c>EventId</c> alone is not a safe key: apps that have no outbox send it blank, and Responder puts the destination's call or
	/// station id in it, so two different statuses to the same call share one. The key therefore covers the subject (unit or
	/// member), the status type, the destination and the client's timestamps as well: a replay matches on all of them, while two
	/// real statuses differ at least in the time they were set. Without a client timestamp a replay cannot be told apart from a
	/// deliberate repeat, so such a write is not tracked. Null means untracked: the write is saved as it always was.
	/// </para>
	/// </summary>
	public static class StatusWriteReplayKeys
	{
		private const char Separator = '\u001f';

		public static string ForUnitStatus(int unitId, UnitStatusInput input)
		{
			if (input == null)
				return null;

			return Build(input.EventId, unitId.ToString(CultureInfo.InvariantCulture), input.Type, input.Timestamp, input.TimestampUtc,
				input.RespondingTo, input.RespondingToType);
		}

		public static string ForPersonStatus(SavePersonStatusInput input)
		{
			if (input == null)
				return null;

			return Build(input.EventId, input.UserId, input.Type, input.Timestamp, input.TimestampUtc, input.RespondingTo, input.RespondingToType);
		}

		public static string ForPersonStaffing(SavePersonnelStaffingInput input)
		{
			if (input == null)
				return null;

			return Build(input.EventId, input.UserId, input.Type, input.Timestamp, input.TimestampUtc, null, null);
		}

		/// <summary>
		/// "sha256:{hex}" of the fields that identify the write, so the receipt key ("{kind}:{departmentId}:sha256:{hex}") always
		/// fits the column and carries no member id or client text.
		/// </summary>
		private static string Build(string eventId, string subject, string type, DateTime? timestamp, DateTime? timestampUtc,
			string respondingTo, int? respondingToType)
		{
			var key = eventId?.Trim();
			if (string.IsNullOrEmpty(key) || (!timestamp.HasValue && !timestampUtc.HasValue))
				return null;

			var canonical = string.Join(Separator,
				key,
				subject?.Trim() ?? string.Empty,
				type?.Trim() ?? string.Empty,
				Format(timestamp),
				Format(timestampUtc),
				respondingTo?.Trim() ?? string.Empty,
				respondingToType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

			return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
		}

		private static string Format(DateTime? value) =>
			value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty;
	}
}
