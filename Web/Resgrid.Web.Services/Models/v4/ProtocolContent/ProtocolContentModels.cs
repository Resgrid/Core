using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.ProtocolContent
{
	/// <summary>The offline manifest (P3): every readable protocol in force, its version hash and the SHA-256 of each file it needs.</summary>
	public class GetProtocolOfflineManifestResult : StandardApiResponseV4Base
	{
		public ProtocolOfflineManifest Data { get; set; }
	}

	/// <summary>"You are viewing v3 — v4 is effective since …": whether the version a device shows is still in force.</summary>
	public class GetProtocolStalenessResult : StandardApiResponseV4Base
	{
		public ProtocolStaleness Data { get; set; }
	}

	/// <summary>A version as a reader is shown it (P3): the approved translation in the asked language, or the source.</summary>
	public class GetProtocolVersionContentResult : StandardApiResponseV4Base
	{
		public ProtocolVersionContentData Data { get; set; }
	}

	public class ProtocolVersionContentData
	{
		public string ProtocolId { get; set; }
		public string ProtocolVersionId { get; set; }
		public string Label { get; set; }
		public DateTime? EffectiveOn { get; set; }
		/// <summary>The source version's hash (what its approvals and acknowledgments are bound to).</summary>
		public string ContentHash { get; set; }
		/// <summary>The language of what is shown, and whether it is a translation.</summary>
		public string Locale { get; set; }
		public string TranslationId { get; set; }
		/// <summary>The translation's own hash when a translation is shown; null otherwise.</summary>
		public string TranslationContentHash { get; set; }
		/// <summary>True when the asked language has no approved translation and the source is shown.</summary>
		public bool IsFallback { get; set; }
		public List<string> AvailableLocales { get; set; } = new List<string>();
		/// <summary>The content sanitized for output (block anchors b-{key} kept).</summary>
		public string ContentHtml { get; set; }
		/// <summary>The block document (schema 2) or the P1 html document (schema 1).</summary>
		public string ContentJson { get; set; }
		/// <summary>The department's dosing-tool link for medication blocks (setting 139); null when none. Doses are never computed.</summary>
		public string DosingToolUrl { get; set; }
		public string DeepLink { get; set; }
		public List<ProtocolStepResultData> Steps { get; set; } = new List<ProtocolStepResultData>();
	}

	public class ProtocolStepResultData
	{
		public string Id { get; set; }
		public string Text { get; set; }
		public string Kind { get; set; }
		public string Group { get; set; }
		public string Anchor { get; set; }
		public int Order { get; set; }
		/// <summary><see cref="ProtocolStepStates"/> on a call; 0 elsewhere.</summary>
		public int State { get; set; }
		public DateTime? At { get; set; }
		public string ByUserId { get; set; }
		public string Note { get; set; }
	}

	/// <summary>A call's step-through checklists (and the incident SOP checklists running on it).</summary>
	public class GetCallProtocolChecklistsResult : StandardApiResponseV4Base
	{
		public List<CallProtocolChecklistResultData> Data { get; set; } = new List<CallProtocolChecklistResultData>();
	}

	public class CallProtocolChecklistResult : StandardApiResponseV4Base
	{
		public CallProtocolChecklistResultData Data { get; set; }
		/// <summary>Why the step was refused (<see cref="ProtocolStepErrors"/> name), null on success.</summary>
		public string Error { get; set; }
	}

	public class CallProtocolChecklistResultData
	{
		public string CallId { get; set; }
		public string CallProtocolId { get; set; }
		public string ProtocolId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public int Kind { get; set; }
		public bool IsIncidentSop { get; set; }
		public string ProtocolVersionId { get; set; }
		public string VersionLabel { get; set; }
		public DateTime? StartedOn { get; set; }
		public string StartedByUserId { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string CompletedByUserId { get; set; }
		public int Done { get; set; }
		public int Total { get; set; }
		public List<ProtocolStepResultData> Steps { get; set; } = new List<ProtocolStepResultData>();
	}

	/// <summary>The Procedure protocols that can be started on a call as incident SOP checklists.</summary>
	public class GetSopLibraryResult : StandardApiResponseV4Base
	{
		public List<SopLibraryItemData> Data { get; set; } = new List<SopLibraryItemData>();
	}

	public class SopLibraryItemData
	{
		public string ProtocolId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public string VersionId { get; set; }
		public string ContentHash { get; set; }
	}

	public class StartCallChecklistInput
	{
		public int CallId { get; set; }
		public int ProtocolId { get; set; }
	}

	/// <summary>One step event; <see cref="DeviceTimestamp"/> is when the device recorded it (kept for an offline replay, never used to order).</summary>
	public class RecordCallStepInput
	{
		public int CallId { get; set; }
		public int CallProtocolId { get; set; }
		public string StepId { get; set; }
		public int State { get; set; }
		public string Note { get; set; }
		public DateTime? DeviceTimestamp { get; set; }

		/// <summary>Optional: the device's own id for this event. A replay with an id already recorded is ignored (offline queues, round 5F).</summary>
		public string ClientStepId { get; set; }
	}

	public class CompleteCallChecklistInput
	{
		public int CallId { get; set; }
		public int CallProtocolId { get; set; }
	}
}
