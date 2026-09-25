using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist;

public static class DiagnosticPolicy
{
	public const string Version = "diagnostics-v1";
	public static readonly string[] Flows = ["paging", "map", "access", "imports", "statuses", "coverage", "equipment", "integration"];
	public static readonly string[] Permissions = ["CreateCall", "CreateNote", "ViewPersonalInfo", "ViewGroupUsers", "ViewGroupUnits", "CanSeePersonnelLocations", "CanSeeUnitLocations"];
	public static void Validate(DiagnosticRequest r, DateTime now)
	{
		if (r == null || !Flows.Contains(r.Flow) || r.FromUtc.Kind != DateTimeKind.Utc || r.UntilUtc.Kind != DateTimeKind.Utc ||
			r.FromUtc >= r.UntilUtc || r.UntilUtc > now.AddMinutes(1) || r.FromUtc < now.AddDays(-90) || r.UntilUtc - r.FromUtc > TimeSpan.FromDays(7) ||
			r.MemberId?.Length > 128 || r.MemberId?.Any(char.IsControl) == true || r.CallId <= 0 || r.UnitId <= 0 || r.RoleId <= 0 || r.GroupId <= 0 ||
			r.Permission != null && !Permissions.Contains(r.Permission) || r.CapabilityId?.Length > 100)
			throw new ArgumentException("Invalid diagnostic scope or window.");
		bool member = !string.IsNullOrWhiteSpace(r.MemberId);
		if (r.Flow == "paging" && (!member || !r.CallId.HasValue) ||
			r.Flow is "map" or "statuses" && member == r.UnitId.HasValue ||
			r.Flow == "access" && (!member || r.Permission == null || r.CapabilityId == null) ||
			r.Flow == "coverage" && !r.RoleId.HasValue || r.Flow == "equipment" && !r.UnitId.HasValue ||
			r.Flow == "integration" && r.CapabilityId == null) throw new ArgumentException("Choose the required diagnostic subjects.");
	}
	public static string Outcome(IEnumerable<DiagnosticCheck> checks)
	{
		var rows = checks.ToArray();
		return rows.Any(c => c.Outcome == "ConfirmedCause") ? "ConfirmedCause" : rows.Any(c => c.Outcome == "PossibleCause") ? "PossibleCause" :
			rows.Length == 0 || rows.Any(c => c.Outcome == "InsufficientEvidence") ? "InsufficientEvidence" : "NoIssueFound";
	}
	public static IReadOnlyList<DiagnosticTraceAttempt> Trace(IEnumerable<DispatchTraceObservation> observations, string memberId, bool truncated)
	{
		// Deduplicate durable observation IDs before evaluating sequence coverage. Other recipients never leave the reducer.
		return observations.GroupBy(o => o.Id).Select(g => g.First()).GroupBy(o => o.AttemptId).OrderBy(g => g.Min(o => o.OccurredOnUtc)).Take(20).Select(g =>
		{
			var rows = g.OrderBy(o => o.Sequence).ToArray();
			int maximum = rows.Max(o => o.Sequence), missing = Math.Max(0, maximum - rows.Select(o => o.Sequence).Distinct().Count());
			int dropped = rows.Max(o => o.PriorDropped);
			bool complete = !truncated && missing == 0 && dropped == 0 && rows.All(o => o.Sequence > 0) &&
				rows.Select(o => o.Sequence).Distinct().Count() == rows.Length && rows[0].Stage == DispatchTraceStage.BroadcastStarted &&
				rows[^1].Stage == DispatchTraceStage.BroadcastCompleted && rows.All(o => o.ResolverVersion == DispatchRecipientResolver.Version);
			var events = rows.Where(o => o.RecipientId == memberId && o.Channel != DispatchTraceChannel.UnitPush)
				.Take(100).Select(o => new DiagnosticTraceEvent(o.Id, o.OccurredOnUtc, o.Sequence, o.Stage.ToString(), o.Channel.ToString(), o.Reason.ToString(), o.LogicalMessageId)).ToArray();
			return new DiagnosticTraceAttempt(g.Key, rows[0].ResolverVersion == DispatchRecipientResolver.Version ? DispatchRecipientResolver.Version : "unknown", rows.Length, missing, dropped,
				complete && rows.Count(o => o.RecipientId == memberId && o.Channel != DispatchTraceChannel.UnitPush) <= 100, events);
		}).ToArray();
	}
	public static DiagnosticSupportBundle Bundle(DiagnosticReport report)
	{
		// A separately constructed allowlist, never serialize the request, stored payload or entity objects.
		var checks = report.Checks.Where(c => c.Basis != "Restricted").Select(c => c with { Destination = null }).ToArray();
		var bundle = new DiagnosticSupportBundle(report.RunId, report.Flow, report.CheckedOnUtc, report.CatalogVersion,
			report.ConfigurationRevision, report.Outcome, checks, report.Changes, report.Attempts, report.TraceTruncated, report.TimelineTruncated, "");
		// Observation timestamps remain in the file; freshness reads are excluded from the review fingerprint.
		var fingerprint = bundle with { CheckedOnUtc = default, Checks = checks.Select(c => c with { AsOfUtc = c.Basis == "Historical" ? c.AsOfUtc : default }).ToArray() };
		return bundle with { PreviewDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fingerprint))).ToLowerInvariant() };
	}
}
