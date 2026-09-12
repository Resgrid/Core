using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Console.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Console.Commands
{
	/// <summary>
	///     Reads and sets feature toggles without going through the admin UI or the API.
	///     <para>
	///     Two write scopes, matching the two things an operator actually needs. With no --DepartmentId,
	///     --On/--Off sets the flag's global default, which is what every department that has no override
	///     of its own resolves to. With --DepartmentId, it writes that department's override, which wins
	///     over the percentage rollout and any targeting rules. --Clear removes an override and drops the
	///     department back onto the global answer.
	///     </para>
	///     <para>
	///     --List shows the flags that exist in the database; --Keys shows the keys application code
	///     actually gates on, which is the list to check when a toggle "does nothing" because no
	///     migration ever seeded a row for it.
	///     </para>
	///     <para>
	///     Writes go through <see cref="IFeatureToggleService" />, so they invalidate the shared flag cache
	///     and emit the same audit event the admin API does; web, API and worker nodes pick the change up
	///     on their next read rather than after the cache duration expires.
	///     </para>
	/// </summary>
	public sealed class FeatureFlagsCommand(
		ILogger<FeatureFlagsCommand> logger,
		IFeatureToggleService featureToggleService,
		IDepartmentsService departmentsService) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			try
			{
				if (!FeatureFlagsConfig.FeatureFlagsEnabled)
					logger.LogWarning(
						"FeatureFlagsConfig.FeatureFlagsEnabled is false. The whole toggle subsystem is off for this install, so every flag reads as disabled no matter what is set here.");

				var key = GetValue(args, "Key");
				var userId = GetValue(args, "UserId");

				if (!TryGetInt(args, "DepartmentId", out var departmentId))
					return Fail("--DepartmentId must be a whole number.");

				var on = HasSwitch(args, "On");
				var off = HasSwitch(args, "Off");

				if (on && off)
					return Fail("--On and --Off cannot both be passed.");

				if (HasSwitch(args, "Rollout"))
					return await SetRolloutAsync(args, key, userId, cancellationToken);

				if (HasSwitch(args, "Clear"))
					return await ClearOverrideAsync(key, departmentId, userId, cancellationToken);

				if (on || off)
					return await SetAsync(args, key, departmentId, on, userId, cancellationToken);

				if (HasSwitch(args, "Keys"))
					return await ListKeysAsync();

				if (HasSwitch(args, "Show"))
					return await ShowAsync(key, departmentId);

				return await ListAsync(args, departmentId);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error working with the feature toggles, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}
		}

		#region Actions

		/// <summary>
		///     Lists every flag with its global default and, when a department is given, what that
		///     department actually resolves to and which rule decided it.
		/// </summary>
		private async Task<ExitCode> ListAsync(string[] args, int? departmentId)
		{
			var includeArchived = HasSwitch(args, "IncludeArchived");
			var filter = GetValue(args, "Filter");

			var flags = await featureToggleService.GetAllFlagsAsync(includeArchived, bypassCache: true);

			if (flags == null || flags.Count == 0)
			{
				logger.LogWarning("No feature flags are defined. Flags are seeded by migrations, so check that the database is up to date.");
				return ExitCode.Success;
			}

			if (!string.IsNullOrWhiteSpace(filter))
				flags = flags.Where(f => Contains(f.FlagKey, filter) || Contains(f.Name, filter) || Contains(f.Category, filter)).ToList();

			if (flags.Count == 0)
			{
				logger.LogWarning("No feature flags matched the filter '{Filter}'.", filter);
				return ExitCode.Success;
			}

			if (departmentId.HasValue)
			{
				var department = await GetDepartmentAsync(departmentId.Value);

				if (department == null)
					return Fail($"Department {departmentId.Value} was not found.");

				Write($"Feature toggles as resolved for department {departmentId.Value} ({department.Name}):");
			}
			else
			{
				Write("Feature toggles (global defaults). Pass --DepartmentId=N to see what a department resolves to.");
			}

			Write("");

			foreach (var flag in flags.OrderBy(f => f.FlagKey, StringComparer.OrdinalIgnoreCase))
			{
				var line = $"{flag.FlagKey.PadRight(44)} global={State(flag.IsEnabledGlobally)}{Rollout(flag)}{Archived(flag)}";

				if (departmentId.HasValue)
				{
					var evaluation = await featureToggleService.EvaluateFreshAsync(flag.FlagKey, departmentId.Value);
					line += $"  ->  department={State(evaluation.IsEnabled)} ({evaluation.Source})";
				}

				Write(line);
			}

			Write("");
			Write($"{flags.Count} flag(s).");

			return ExitCode.Success;
		}

		/// <summary>
		///     Lists every flag key application code reads (the <see cref="FeatureFlagKeys" /> constants)
		///     against the rows that exist, so a key that code gates on but no migration has seeded shows up
		///     as unseeded rather than silently falling back to a code default. Flags in the database with no
		///     matching constant are listed after, since those are client-only or retired keys.
		/// </summary>
		private async Task<ExitCode> ListKeysAsync()
		{
			var known = KnownKeys();
			var flags = await featureToggleService.GetAllFlagsAsync(includeArchived: true, bypassCache: true) ?? new List<FeatureFlag>();

			Write("Feature flag keys read by application code:");
			Write("");

			foreach (var key in known)
			{
				var flag = flags.FirstOrDefault(f => string.Equals(f.FlagKey, key, StringComparison.OrdinalIgnoreCase));

				Write(flag == null
					? $"{key.PadRight(44)} NOT SEEDED - no row exists, so code falls back to its own default"
					: $"{key.PadRight(44)} global={State(flag.IsEnabledGlobally)}{Rollout(flag)}{Archived(flag)}");
			}

			var unlisted = flags
				.Where(f => !known.Any(k => string.Equals(k, f.FlagKey, StringComparison.OrdinalIgnoreCase)))
				.OrderBy(f => f.FlagKey, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (unlisted.Count > 0)
			{
				Write("");
				Write("Flags in the database with no matching key constant (client-only or retired):");
				Write("");

				foreach (var flag in unlisted)
					Write($"{flag.FlagKey.PadRight(44)} global={State(flag.IsEnabledGlobally)}{Rollout(flag)}{Archived(flag)}");
			}

			Write("");
			Write($"{known.Count} key(s) in code, {flags.Count} flag(s) in the database.");

			return ExitCode.Success;
		}

		/// <summary>Prints one flag in full: definition, overrides, targeting rules and prerequisites.</summary>
		private async Task<ExitCode> ShowAsync(string key, int? departmentId)
		{
			if (string.IsNullOrWhiteSpace(key))
				return Fail("--Show needs a flag key: --Show --Key=Chat.System");

			var flag = await featureToggleService.GetFlagByKeyAsync(key, bypassCache: true);

			if (flag == null)
				return FlagNotFound(key);

			Write($"{flag.FlagKey} - {flag.Name}");

			if (!string.IsNullOrWhiteSpace(flag.Description))
				Write($"  {flag.Description}");

			Write($"  Global default:       {State(flag.IsEnabledGlobally)}");
			Write($"  Rollout:              {(flag.RolloutPercentage.HasValue ? flag.RolloutPercentage.Value + "%" : "100% (no staged rollout)")}");
			Write($"  Value type:           {(FeatureFlagValueTypes)flag.FlagType}");
			Write($"  Category:             {flag.Category ?? "(none)"}");
			Write($"  Archived:             {flag.IsArchived}");
			Write($"  Permanent:            {flag.IsPermanent}");

			if (flag.MinimumPlanType.HasValue)
				Write($"  Minimum plan:         {flag.MinimumPlanType.Value}");

			if (flag.EnableOn.HasValue || flag.DisableOn.HasValue)
				Write($"  Schedule:             {Stamp(flag.EnableOn)} -> {Stamp(flag.DisableOn)}");

			Write($"  Last evaluated:       {Stamp(flag.LastEvaluatedOn)}");

			var prerequisites = await featureToggleService.GetPrerequisitesForFlagAsync(flag.FlagKey);

			if (prerequisites != null && prerequisites.Count > 0)
			{
				Write("  Prerequisites (all must be satisfied before this flag can be on):");

				foreach (var prerequisite in prerequisites)
					Write($"    requires flag id {prerequisite.RequiredFeatureFlagId} = {prerequisite.RequiredValue ?? "true"}");
			}

			var rules = await featureToggleService.GetTargetingRulesForFlagAsync(flag.FlagKey);

			if (rules != null && rules.Count > 0)
				Write($"  Targeting rules:      {rules.Count} (managed from the admin UI)");

			var overrides = await featureToggleService.GetOverridesForFlagAsync(flag.FlagKey);
			Write($"  Department overrides: {(overrides == null ? 0 : overrides.Count)}");

			if (overrides != null)
			{
				foreach (var item in overrides.OrderBy(o => o.DepartmentId))
					Write($"    department {item.DepartmentId}: {State(item.IsEnabled)}{Expiry(item.ExpiresOn)}{Reason(item.Reason)}");
			}

			if (departmentId.HasValue)
			{
				var department = await GetDepartmentAsync(departmentId.Value);

				if (department == null)
					return Fail($"Department {departmentId.Value} was not found.");

				Write("");
				await WriteEvaluationAsync(flag.FlagKey, departmentId.Value, department.Name);
			}

			return ExitCode.Success;
		}

		/// <summary>Sets the global default (no --DepartmentId) or a single department's override.</summary>
		private async Task<ExitCode> SetAsync(string[] args, string key, int? departmentId, bool enabled, string userId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(key))
				return Fail("--On/--Off needs a flag key: --On --Key=Chat.System");

			if (!departmentId.HasValue)
			{
				var flag = await featureToggleService.SetGlobalEnabledAsync(key, enabled, userId, cancellationToken);

				if (flag == null)
					return FlagNotFound(key);

				Write($"Global default for '{flag.FlagKey}' is now {State(flag.IsEnabledGlobally)}, for every department that has no override of its own.");

				if (flag.IsEnabledGlobally && flag.RolloutPercentage.HasValue && flag.RolloutPercentage.Value < 100)
					logger.LogWarning(
						"The flag also has a {Percentage}% staged rollout, so it is only on for departments inside that bucket. Use --Rollout=100 to reach everyone.",
						flag.RolloutPercentage.Value);

				if (flag.IsEnabledGlobally && flag.IsArchived)
					logger.LogWarning("The flag is archived, which short-circuits evaluation to off. Un-archive it from the admin UI before it will read as on.");

				Write("Flag cache invalidated; web, API and worker nodes pick this up on their next read.");

				return ExitCode.Success;
			}

			var department = await GetDepartmentAsync(departmentId.Value);

			if (department == null)
				return Fail($"Department {departmentId.Value} was not found.");

			if (!TryGetExpiry(args, out var expiresOn, out var expiryError))
				return Fail(expiryError);

			var value = GetValue(args, "Value");
			var reason = GetValue(args, "Reason") ?? "Set from the Resgrid Console";

			try
			{
				await featureToggleService.SetDepartmentOverrideAsync(key, departmentId.Value, enabled, value, reason, expiresOn, userId, cancellationToken);
			}
			catch (InvalidOperationException)
			{
				return FlagNotFound(key);
			}

			Write($"Override for '{key}' on department {departmentId.Value} ({department.Name}) is now {State(enabled)}{Expiry(expiresOn)}.");
			await WriteEvaluationAsync(key, departmentId.Value, department.Name);

			return ExitCode.Success;
		}

		/// <summary>Removes a department's override so it falls back to the global default and rollout.</summary>
		private async Task<ExitCode> ClearOverrideAsync(string key, int? departmentId, string userId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(key))
				return Fail("--Clear needs a flag key: --Clear --Key=Chat.System --DepartmentId=1");

			if (!departmentId.HasValue)
				return Fail("--Clear removes a department override, so it needs --DepartmentId=N. To turn a flag off everywhere use --Off with no --DepartmentId.");

			var department = await GetDepartmentAsync(departmentId.Value);

			if (department == null)
				return Fail($"Department {departmentId.Value} was not found.");

			var removed = await featureToggleService.RemoveDepartmentOverrideAsync(key, departmentId.Value, userId, cancellationToken);

			if (!removed)
			{
				logger.LogWarning("Department {DepartmentId} has no override for '{Key}' (or the flag does not exist); nothing was changed.", departmentId.Value, key);
				return ExitCode.Success;
			}

			Write($"Removed the '{key}' override on department {departmentId.Value} ({department.Name}); it now follows the global default.");
			await WriteEvaluationAsync(key, departmentId.Value, department.Name);

			return ExitCode.Success;
		}

		/// <summary>Sets the staged rollout percentage, which only applies to departments with no override.</summary>
		private async Task<ExitCode> SetRolloutAsync(string[] args, string key, string userId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(key))
				return Fail("--Rollout needs a flag key: --Rollout=25 --Key=Chat.System");

			var raw = GetValue(args, "Rollout") ?? GetValue(args, "Percentage");

			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percentage) || percentage < 0 || percentage > 100)
				return Fail("--Rollout needs a percentage between 0 and 100, as --Rollout=25 or --Rollout --Percentage=25.");

			var flag = await featureToggleService.SetRolloutPercentageAsync(key, percentage, userId, cancellationToken);

			if (flag == null)
				return FlagNotFound(key);

			Write($"Rollout for '{flag.FlagKey}' is now {flag.RolloutPercentage}%, on top of a global default of {State(flag.IsEnabledGlobally)}.");

			if (!flag.IsEnabledGlobally)
				logger.LogWarning("The global default is off, so the rollout has no effect until the flag is turned on with --On.");

			return ExitCode.Success;
		}

		#endregion

		#region Output helpers

		private async Task WriteEvaluationAsync(string key, int departmentId, string departmentName)
		{
			var evaluation = await featureToggleService.EvaluateFreshAsync(key, departmentId);

			Write($"Department {departmentId} ({departmentName}) now resolves '{key}' to {State(evaluation.IsEnabled)}, decided by {evaluation.Source}" +
				  (evaluation.MatchedRuleId.HasValue ? $" (targeting rule {evaluation.MatchedRuleId.Value})." : "."));

			if (evaluation.ValueType != FeatureFlagValueTypes.Boolean)
				Write($"Resolved value: {evaluation.Value ?? "(null)"}");
		}

		/// <summary>
		///     Reports an unknown key along with the keys application code actually reads, since a key with
		///     no seeded row is the usual reason a toggle appears to do nothing.
		/// </summary>
		private ExitCode FlagNotFound(string key)
		{
			logger.LogError("No feature flag exists with the key '{Key}'. Flags are seeded by migrations, not created here.", key);

			var known = KnownKeys();

			if (known.Count > 0)
			{
				Write("Keys read by application code (--Keys shows which of these are seeded):");

				foreach (var item in known)
					Write($"  {item}");
			}

			return ExitCode.Failed;
		}

		/// <summary>The flag keys declared in <see cref="FeatureFlagKeys" />, which is what code gates on.</summary>
		private static List<string> KnownKeys() =>
			typeof(FeatureFlagKeys)
				.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
				.Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
				.Select(f => (string)f.GetRawConstantValue())
				.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
				.ToList();

		private ExitCode Fail(string message)
		{
			logger.LogError("{Line}", message);
			return ExitCode.Failed;
		}

		/// <summary>Writes a line as-is, so a value containing braces is never read as a log template.</summary>
		private void Write(string line) => logger.LogInformation("{Line}", line);

		private static string State(bool enabled) => enabled ? "ON" : "OFF";

		private static string Stamp(DateTime? value) => value.HasValue ? value.Value.ToString("u", CultureInfo.InvariantCulture) : "(none)";

		private static string Rollout(FeatureFlag flag) =>
			flag.RolloutPercentage.HasValue && flag.RolloutPercentage.Value < 100 ? $"  rollout={flag.RolloutPercentage.Value}%" : string.Empty;

		private static string Archived(FeatureFlag flag) => flag.IsArchived ? "  [archived]" : string.Empty;

		private static string Expiry(DateTime? expiresOn) => expiresOn.HasValue ? $", expiring {Stamp(expiresOn)}" : string.Empty;

		private static string Reason(string reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : $" - {reason}";

		private static bool Contains(string value, string filter) =>
			!string.IsNullOrWhiteSpace(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

		#endregion

		#region Lookups and argument parsing

		private async Task<Department> GetDepartmentAsync(int departmentId)
		{
			var department = await departmentsService.GetDepartmentByIdAsync(departmentId);

			// A cached blank entity deserializes to a non-null Department with a zero id, so the id is what
			// is checked here and not just the reference.
			return department == null || department.DepartmentId <= 0 ? null : department;
		}

		private static bool TryGetExpiry(string[] args, out DateTime? expiresOn, out string error)
		{
			expiresOn = null;
			error = null;

			var raw = GetValue(args, "ExpiresOn");

			if (string.IsNullOrWhiteSpace(raw))
				return true;

			// Anything without an offset is read as UTC, which is what the override column stores.
			if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
			{
				error = $"--ExpiresOn '{raw}' is not a date. Use an ISO form such as --ExpiresOn=2026-12-31 or --ExpiresOn=2026-12-31T18:00:00Z (UTC).";
				return false;
			}

			expiresOn = parsed;
			return true;
		}

		private static bool TryGetInt(string[] args, string name, out int? value)
		{
			value = null;

			var raw = GetValue(args, name);

			if (string.IsNullOrWhiteSpace(raw))
				return true;

			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
				return false;

			value = parsed;
			return true;
		}

		/// <summary>Reads "--Name=value" or "--Name value" out of the raw argument list.</summary>
		private static string GetValue(string[] args, string name)
		{
			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];

				if (!arg.StartsWith("--", StringComparison.Ordinal))
					continue;

				var token = arg.Substring(2);
				var separator = token.IndexOf('=');

				if (separator >= 0)
				{
					if (string.Equals(token.Substring(0, separator), name, StringComparison.OrdinalIgnoreCase))
						return Unquote(token.Substring(separator + 1));

					continue;
				}

				if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase) &&
					i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
					return Unquote(args[i + 1]);
			}

			return null;
		}

		/// <summary>True when "--Name" is present, unless it was explicitly passed as "--Name=false".</summary>
		private static bool HasSwitch(string[] args, string name)
		{
			foreach (var arg in args)
			{
				if (!arg.StartsWith("--", StringComparison.Ordinal))
					continue;

				var token = arg.Substring(2);
				var separator = token.IndexOf('=');
				var switchName = separator >= 0 ? token.Substring(0, separator) : token;

				if (!string.Equals(switchName, name, StringComparison.OrdinalIgnoreCase))
					continue;

				return separator < 0 || !string.Equals(token.Substring(separator + 1), "false", StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}

		private static string Unquote(string value)
		{
			if (value != null && value.Length > 1 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
				return value.Substring(1, value.Length - 2);

			return value;
		}

		#endregion
	}
}
