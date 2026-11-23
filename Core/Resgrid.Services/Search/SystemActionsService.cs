using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services.Search
{
	/// <summary>
	/// Searches the system-functionality catalog for one caller (plan R3 "Action" family). The catalog is static and
	/// small; entries the caller may not use are removed before scoring, so the palette never advertises a page the
	/// caller cannot open. Scoring is deterministic: exact title, title prefix, per-token prefix on title / keywords /
	/// description, and a one-edit fuzzy match for tokens of four characters or more.
	/// </summary>
	public class SystemActionsService : ISystemActionsService
	{
		private readonly IFeatureToggleService _featureToggles;
		private readonly IRecordsCutoverService _recordsCutover;
		private readonly Lazy<IDispatchAccessService> _dispatchAccess;
		private readonly Lazy<IDispatcherPresenceService> _dispatcherPresence;
		private readonly Lazy<IAuthorizationService> _authorization;

		/// <summary>
		/// The desk services are only for the dispatcher desk entries' access rules (lazy: the palette rarely needs them). Without
		/// them those entries are hidden from everyone but department admins.
		/// </summary>
		public SystemActionsService(IFeatureToggleService featureToggles, IRecordsCutoverService recordsCutover,
			Lazy<IDispatchAccessService> dispatchAccess = null, Lazy<IDispatcherPresenceService> dispatcherPresence = null,
			Lazy<IAuthorizationService> authorization = null)
		{
			_featureToggles = featureToggles ?? throw new ArgumentNullException(nameof(featureToggles));
			_recordsCutover = recordsCutover ?? throw new ArgumentNullException(nameof(recordsCutover));
			_dispatchAccess = dispatchAccess;
			_dispatcherPresence = dispatcherPresence;
			_authorization = authorization;
		}

		public async Task<List<SystemActionHit>> SearchAsync(string text, SearchPrincipal principal, int max = 8, CancellationToken cancellationToken = default)
		{
			if (principal == null)
				return new List<SystemActionHit>();

			var allowed = await AllowedAsync(principal, cancellationToken);
			var tokens = Tokenize(text);
			if (tokens.Count == 0)
				return allowed.Select(a => ToHit(a, principal, 0f)).Take(Math.Max(1, max)).ToList();

			var scored = new List<(SystemActionDefinition def, float score)>();
			foreach (var def in allowed)
			{
				var score = Score(def, tokens, text);
				if (score > 0f)
					scored.Add((def, score));
			}

			return scored
				.OrderByDescending(s => s.score)
				.ThenBy(s => s.def.Title, StringComparer.OrdinalIgnoreCase)
				.Take(Math.Max(1, max))
				.Select(s => ToHit(s.def, principal, s.score))
				.ToList();
		}

		public async Task<List<SystemActionHit>> ListAsync(SearchPrincipal principal, CancellationToken cancellationToken = default)
		{
			if (principal == null)
				return new List<SystemActionHit>();
			var allowed = await AllowedAsync(principal, cancellationToken);
			return allowed.Select(a => ToHit(a, principal, 0f)).ToList();
		}

		private async Task<List<SystemActionDefinition>> AllowedAsync(SearchPrincipal principal, CancellationToken cancellationToken)
		{
			var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			async Task<bool> FlagAsync(string key)
			{
				if (string.IsNullOrWhiteSpace(key))
					return true;
				if (flags.TryGetValue(key, out var known))
					return known;
				bool value;
				try { value = await _featureToggles.IsEnabledAsync(key, principal.DepartmentId); }
				catch (Exception ex) { Logging.LogException(ex, $"Feature flag {key} could not be evaluated for the command palette; treating as off."); value = false; }
				flags[key] = value;
				return value;
			}

			// The cutover, not the Records.System flag, is what makes legacy Logs read-only: a department with the flag
			// on but Records not yet activated is still writing Logs.
			bool? legacyWritesBlocked = null;
			async Task<bool> LegacyWritesBlockedAsync()
			{
				if (legacyWritesBlocked.HasValue)
					return legacyWritesBlocked.Value;
				try { legacyWritesBlocked = await _recordsCutover.AreLegacyWritesBlockedAsync(principal.DepartmentId); }
				catch (Exception ex) { Logging.LogException(ex, "Records cutover state could not be evaluated for the command palette; hiding legacy Logs writes."); legacyWritesBlocked = true; }
				return legacyWritesBlocked.Value;
			}

			var rules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			var allowed = new List<SystemActionDefinition>();
			foreach (var def in SystemActionCatalog.All)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (def.DepartmentAdminOnly && !principal.IsDepartmentAdmin)
					continue;
				if (!string.IsNullOrWhiteSpace(def.ClaimResource) && !principal.IsDepartmentAdmin && !principal.HasResourceClaim(def.ClaimResource, def.ClaimAction ?? SystemActionCatalog.View))
					continue;
				if (!principal.ModuleEnabled(def.Module))
					continue;
				if (def.HiddenAfterRecordsCutover && await LegacyWritesBlockedAsync())
					continue;
				if (!await FlagAsync(def.FeatureFlag))
					continue;
				if (!await AccessRuleAsync(def.AccessRule))
					continue;
				allowed.Add(def);
			}
			return allowed;

			async Task<bool> AccessRuleAsync(string rule)
			{
				if (string.IsNullOrWhiteSpace(rule) || principal.IsDepartmentAdmin)
					return true;
				if (rules.TryGetValue(rule, out var known))
					return known;

				bool value;
				try { value = await EvaluateAccessRuleAsync(rule, principal); }
				catch (Exception ex) { Logging.LogException(ex, $"Access rule {rule} could not be evaluated for the command palette; hiding its entries."); value = false; }
				rules[rule] = value;
				return value;
			}
		}

		/// <summary>The destination pages' own per-person checks (MVC DispatcherDeskController: DeskAccess.CanView, Activity).</summary>
		private async Task<bool> EvaluateAccessRuleAsync(string rule, SearchPrincipal principal)
		{
			if (string.IsNullOrWhiteSpace(principal.UserId))
				return false;

			switch (rule)
			{
				case SystemActionAccessRules.DispatcherSupervisor:
					return _dispatcherPresence != null && await _dispatcherPresence.Value.CanSuperviseAsync(principal.DepartmentId, principal.UserId);

				case SystemActionAccessRules.DispatcherDesk:
					if (_dispatchAccess != null && await _dispatchAccess.Value.CanUseDispatchAsync(principal.DepartmentId, principal.UserId))
						return true;
					// The desk supervisor check first: a failing shift-scope lookup must not hide the desk from an on-duty desk
					// supervisor the desk page itself admits (CanSuperviseAsync swallows its own scope failure).
					if (_dispatcherPresence != null && await _dispatcherPresence.Value.CanSuperviseAsync(principal.DepartmentId, principal.UserId))
						return true;
					return _authorization != null && (await _authorization.Value.GetShiftManagementScopeAsync(principal.UserId, principal.DepartmentId))?.IsSupervisor == true;

				default:
					return false; // an unknown rule fails closed
			}
		}

		private static SystemActionHit ToHit(SystemActionDefinition def, SearchPrincipal principal, float score)
		{
			var path = (def.WebPath ?? string.Empty).Replace("{userId}", Uri.EscapeDataString(principal.UserId ?? string.Empty));
			return new SystemActionHit
			{
				Key = def.Key,
				Title = def.Title,
				Description = def.Description,
				Category = def.Category,
				Url = (SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + path,
				Score = score
			};
		}

		/// <summary>Visible for tests.</summary>
		public static float Score(SystemActionDefinition def, List<string> tokens, string rawText)
		{
			var title = (def.Title ?? string.Empty).ToLowerInvariant();
			var titleWords = Words(title);
			var keywordWords = (def.Keywords ?? Array.Empty<string>()).SelectMany(k => Words(k.ToLowerInvariant())).ToList();
			var descriptionWords = Words((def.Description ?? string.Empty).ToLowerInvariant());
			var key = (def.Key ?? string.Empty).ToLowerInvariant();
			var normalized = string.Join(" ", tokens);

			var score = 0f;
			if (title == normalized || key == normalized)
				score += 10f;
			else if (title.StartsWith(normalized, StringComparison.Ordinal))
				score += 6f;

			foreach (var token in tokens)
			{
				var best = 0f;
				if (titleWords.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
					best = Math.Max(best, 3f);
				if (key == token || key.StartsWith(token, StringComparison.Ordinal))
					best = Math.Max(best, 3f);
				if (keywordWords.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
					best = Math.Max(best, 2.5f);
				if (descriptionWords.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
					best = Math.Max(best, 1f);
				if (best == 0f && token.Length >= 4)
				{
					if (titleWords.Concat(keywordWords).Any(w => w.Length >= 4 && WithinOneEdit(token, w.Length > token.Length + 1 ? w.Substring(0, Math.Min(w.Length, token.Length + 1)) : w)))
						best = 1.5f;
				}
				if (best == 0f)
				{
					// Intent words ("new call", "open inbox", "my profile") narrow when they match and are ignored when
					// they do not; every other token must match something.
					if (IntentWords.Contains(token))
						continue;
					return 0f;
				}
				score += best;
			}

			return score;
		}

		private static readonly HashSet<string> IntentWords = new HashSet<string>(StringComparer.Ordinal)
		{
			"new", "create", "add", "open", "view", "show", "go", "to", "the", "my", "manage", "edit", "list", "see", "find"
		};

		public static List<string> Tokenize(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return new List<string>();
			return Words(text.Trim().TrimStart('/').ToLowerInvariant()).Take(8).ToList();
		}

		private static List<string> Words(string text)
		{
			var words = new List<string>();
			if (string.IsNullOrEmpty(text))
				return words;
			var current = new System.Text.StringBuilder();
			foreach (var ch in text)
			{
				if (char.IsLetterOrDigit(ch))
				{
					current.Append(ch);
				}
				else if (current.Length > 0)
				{
					words.Add(current.ToString());
					current.Clear();
				}
			}
			if (current.Length > 0)
				words.Add(current.ToString());
			return words;
		}

		/// <summary>Damerau-free Levenshtein bound of one, early exit.</summary>
		public static bool WithinOneEdit(string a, string b)
		{
			if (a == b) return true;
			if (Math.Abs(a.Length - b.Length) > 1) return false;
			int i = 0, j = 0, edits = 0;
			while (i < a.Length && j < b.Length)
			{
				if (a[i] == b[j]) { i++; j++; continue; }
				if (++edits > 1) return false;
				if (a.Length > b.Length) i++;
				else if (a.Length < b.Length) j++;
				else { i++; j++; }
			}
			edits += (a.Length - i) + (b.Length - j);
			return edits <= 1;
		}
	}
}
