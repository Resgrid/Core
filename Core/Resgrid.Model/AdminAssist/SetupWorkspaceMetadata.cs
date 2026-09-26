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
		// Core modules start in scope for every department; an administrator can still defer any except security.
		public Dictionary<string, SetupAreaChoice> Areas { get; set; } = new()
		{
			["people"] = SetupAreaChoice.UseNow, ["security"] = SetupAreaChoice.UseNow, ["calls"] = SetupAreaChoice.UseNow, ["units"] = SetupAreaChoice.UseNow,
			["communication"] = SetupAreaChoice.UseNow, ["apps"] = SetupAreaChoice.UseNow, ["plans"] = SetupAreaChoice.UseNow
		};
		// Scope saved before areas became modules; the choice carries to every module the area became.
		private static readonly Dictionary<string, string[]> LegacyAreas = new(StringComparer.Ordinal)
		{
			["home"] = new[] { "apps" }, ["location"] = new[] { "units", "mapping" }
		};
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
			else
			{
				// Like the current format, stored choices override the core-module defaults instead of replacing them.
				state = new SetupWorkspaceMetadata();
				foreach (var (area, choice) in document.ToObject<Dictionary<string, SetupAreaChoice>>() ?? new()) state.Areas[area] = choice;
			}
			if (state?.Areas == null || state.ScopeRevision < 0 || state.AreaReasons == null || state.Areas.Count > 100 || state.AreaReasons.Count > 100 ||
				state.Areas.Any(a => string.IsNullOrWhiteSpace(a.Key) || a.Key.Length > 128 || !Enum.IsDefined(a.Value)) ||
				state.AreaReasons.Any(a => !state.Areas.TryGetValue(a.Key, out var choice) || choice != SetupAreaChoice.NotApplicable || !Enum.IsDefined(a.Value)))
				throw new InvalidOperationException("Invalid workspace metadata.");
			foreach (var (legacy, modules) in LegacyAreas)
			{
				if (!state.Areas.Remove(legacy, out var choice)) continue;
				var hasReason = state.AreaReasons.Remove(legacy, out var reason);
				// A stored legacy choice was explicit, so it wins over the defaults merged in during deserialization.
				foreach (var module in modules)
				{
					state.Areas[module] = choice;
					if (hasReason) state.AreaReasons[module] = reason; else state.AreaReasons.Remove(module);
				}
			}
			return state;
		}
		public string Serialize() => JsonConvert.SerializeObject(this);
	}
}
