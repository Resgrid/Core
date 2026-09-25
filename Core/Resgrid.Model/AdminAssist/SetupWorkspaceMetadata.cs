using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Versioned metadata in the existing workspace JSON column; no free text, content or credentials.</summary>
	public sealed class SetupWorkspaceMetadata
	{
		public int SchemaVersion { get; set; } = 2;
		public long ScopeRevision { get; set; }
		public Dictionary<string, SetupAreaChoice> Areas { get; set; } = new() { ["home"] = SetupAreaChoice.UseNow, ["security"] = SetupAreaChoice.UseNow };
		public Dictionary<string, SetupAreaReason> AreaReasons { get; set; } = new();
		public SetupReviewEvidence ReviewEvidence { get; set; }
		public DateTime? RevisitOnUtc { get; set; }
		public static SetupWorkspaceMetadata Read(string json)
		{
			if (json == null) return new();
			if (json.Length > 32768) throw new InvalidOperationException("Workspace metadata exceeds its bound.");
			var document = JObject.Parse(json);
			SetupWorkspaceMetadata state;
			if (document.TryGetValue(nameof(SchemaVersion), StringComparison.OrdinalIgnoreCase, out var version))
			{
				if (version.Type != JTokenType.Integer || version.Value<int>() != 2) throw new InvalidOperationException("Unsupported workspace metadata version.");
				state = document.ToObject<SetupWorkspaceMetadata>();
			}
			else state = new SetupWorkspaceMetadata { Areas = document.ToObject<Dictionary<string, SetupAreaChoice>>() };
			if (state?.Areas == null || state.ScopeRevision < 0 || state.AreaReasons == null || state.Areas.Count > 100 || state.AreaReasons.Count > 100 ||
				state.Areas.Any(a => string.IsNullOrWhiteSpace(a.Key) || a.Key.Length > 128 || !Enum.IsDefined(a.Value)) ||
				state.AreaReasons.Any(a => !state.Areas.TryGetValue(a.Key, out var choice) || choice != SetupAreaChoice.NotApplicable || !Enum.IsDefined(a.Value)))
				throw new InvalidOperationException("Invalid workspace metadata.");
			return state;
		}
		public string Serialize() => JsonConvert.SerializeObject(this);
	}
}
