using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Plan §9.0: the back-office-owned incident header. An activation links to a Call and an IC board read-only (either may come
	/// later or never), can be a complex of member incidents, a planned event, an EOC activation or an exercise, and moves
	/// Preparing → Active → Demobilizing → Closed → Sealed with an audited, reasoned reopen. Linking never writes the Call or board.
	/// </summary>
	public class IncidentActivationService : IIncidentActivationService
	{
		private static readonly Regex ShortCodePattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]{1,31}$", RegexOptions.Compiled);

		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentLedgerService<IncidentOperationalPeriod> _periods;
		private readonly IIncidentLedgerService<IncidentCloseoutChecklistItem> _closeout;
		private readonly IIncidentLedgerService<IncidentAuthorityProfileSnapshot> _snapshots;
		private readonly IIncidentLedgerService<IncidentOperationalSnapshot> _operationalSnapshots;
		private readonly IIncidentAuthorityService _authority;
		private readonly IIncidentPlanningService _planning;
		private readonly IIncidentOrganizationService _organization;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IIncidentEventPublisher _events;
		private readonly IIncidentAuditService _audit;
		private readonly ICallsService _calls;
		private readonly IIncidentCommandService _incidentCommand;
		private readonly IDepartmentsService _departments;
		private readonly IAddressService _addresses;

		public IncidentActivationService(IIncidentLedgerService<IncidentActivation> activations, IIncidentLedgerService<IncidentOperationalPeriod> periods,
			IIncidentLedgerService<IncidentCloseoutChecklistItem> closeout, IIncidentLedgerService<IncidentAuthorityProfileSnapshot> snapshots,
			IIncidentLedgerService<IncidentOperationalSnapshot> operationalSnapshots, IIncidentAuthorityService authority, IIncidentPlanningService planning,
			IIncidentOrganizationService organization, IIncidentBackOfficeSettingsService settings, IIncidentEventPublisher events, IIncidentAuditService audit,
			ICallsService calls, IIncidentCommandService incidentCommand, IDepartmentsService departments, IAddressService addresses)
		{
			_activations = activations;
			_periods = periods;
			_closeout = closeout;
			_snapshots = snapshots;
			_operationalSnapshots = operationalSnapshots;
			_authority = authority;
			_planning = planning;
			_organization = organization;
			_settings = settings;
			_events = events;
			_audit = audit;
			_calls = calls;
			_incidentCommand = incidentCommand;
			_departments = departments;
			_addresses = addresses;
		}

		public async Task<IReadOnlyList<IncidentActivation>> GetActivationsAsync(IncidentActor actor, bool includeClosed = false, CancellationToken cancellationToken = default)
		{
			var query = new IncidentLedgerQuery().Order("Status", false, "OpenedOn").Page(0, 1000);
			if (!includeClosed)
				query.WhereIn("Status", new[] { (int)IncidentActivationStatuses.Preparing, (int)IncidentActivationStatuses.Active, (int)IncidentActivationStatuses.Demobilizing });
			return await _activations.ListAsync(actor, query, cancellationToken);
		}

		public async Task<IncidentActivationContext> GetContextAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken);
			if (activation == null)
				return null;

			var context = new IncidentActivationContext { Activation = activation, CurrentPeriod = await _planning.GetCurrentPeriodAsync(actor, activationId, cancellationToken) };
			if (!string.IsNullOrWhiteSpace(activation.ParentActivationId))
				context.Parent = await _activations.GetAsync(actor, activation.ParentActivationId, false, cancellationToken);
			if (activation.Kind == (int)IncidentActivationKinds.Complex)
				context.Members = await _activations.ListAsync(actor, new IncidentLedgerQuery().Where("ParentActivationId", activation.IncidentActivationId).Order("Name"), cancellationToken);
			if (!string.IsNullOrWhiteSpace(activation.AuthorityProfileSnapshotId))
				context.Authority = await _snapshots.GetAsync(actor, activation.AuthorityProfileSnapshotId, false, cancellationToken);

			var latest = (await _operationalSnapshots.ListAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Order("TakenOn", true).Page(0, 1), cancellationToken)).FirstOrDefault();
			context.SnapshotOn = latest?.TakenOn;
			var defaults = await _settings.GetAsync<IncidentBackOfficeDefaults>(actor.DepartmentId);
			context.IsStale = latest == null || latest.TakenOn < DateTime.UtcNow.AddMinutes(-Math.Max(1, defaults.SnapshotStalenessMinutes));
			return context;
		}

		public async Task<IReadOnlyList<string>> GetScopeAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken);
			if (activation == null)
				return Array.Empty<string>();
			var scope = new List<string> { activation.IncidentActivationId };
			if (activation.Kind == (int)IncidentActivationKinds.Complex)
				scope.AddRange((await _activations.ListAsync(actor, new IncidentLedgerQuery().Where("ParentActivationId", activationId), cancellationToken)).Select(a => a.IncidentActivationId));
			return scope;
		}

		public async Task<IncidentActivation> OpenAsync(IncidentActor actor, IncidentActivationInput input, CancellationToken cancellationToken = default)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Name))
				throw new IncidentRuleException("NameRequired");
			var shortCode = (input.ShortCode ?? string.Empty).Trim().ToUpperInvariant();
			if (!ShortCodePattern.IsMatch(shortCode))
				throw new IncidentRuleException("ShortCodeInvalid");
			if (await _activations.CountAsync(actor, new IncidentLedgerQuery().Where("ShortCode", shortCode), cancellationToken) > 0)
				throw new IncidentRuleException("ShortCodeInUse");

			await ValidateLinksAsync(actor, null, input.CallId, input.IncidentCommandId, cancellationToken);
			if (!string.IsNullOrWhiteSpace(input.ParentActivationId))
				await ValidateParentAsync(actor, null, input.ParentActivationId, input.Kind, cancellationToken);

			var defaults = await _settings.GetAsync<IncidentBackOfficeDefaults>(actor.DepartmentId);
			var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId);
			var (currency, measurement) = await LocaleDefaultsAsync(department, defaults);
			var timeZone = string.IsNullOrWhiteSpace(input.TimeZoneId) ? department?.TimeZone : input.TimeZoneId.Trim();
			if (string.IsNullOrWhiteSpace(timeZone) || !IsKnownTimeZone(timeZone))
				timeZone = "UTC";

			var activation = new IncidentActivation
			{
				Kind = (int)input.Kind,
				Name = input.Name.Trim(),
				ShortCode = shortCode,
				ComplexityType = input.ComplexityType is >= 1 and <= 5 ? input.ComplexityType : null,
				CallId = input.CallId,
				IncidentCommandId = string.IsNullOrWhiteSpace(input.IncidentCommandId) ? null : input.IncidentCommandId,
				ParentActivationId = string.IsNullOrWhiteSpace(input.ParentActivationId) ? null : input.ParentActivationId,
				TimeZoneId = timeZone,
				Currency = NormalizeCurrency(input.Currency) ?? currency,
				MeasurementSystem = input.MeasurementSystem is "metric" or "customary" ? input.MeasurementSystem : measurement,
				Status = (int)(input.StartActive ? IncidentActivationStatuses.Active : IncidentActivationStatuses.Preparing),
				IsExercise = input.IsExercise,
				Description = input.Description,
				OpenedOn = DateTime.UtcNow,
				OpenedByUserId = actor.UserId
			};
			activation = await _activations.SaveAsync(actor, activation, null, cancellationToken);

			var snapshot = await _authority.SnapshotAsync(actor, activation.IncidentActivationId, input.AuthorityProfileId, cancellationToken);
			if (snapshot != null)
			{
				activation.AuthorityProfileSnapshotId = snapshot.IncidentAuthorityProfileSnapshotId;
				activation = await _activations.SaveAsync(actor, activation, activation.RowVersion, cancellationToken);
			}

			if ((await _organization.GetPositionsAsync(actor, null, cancellationToken)).Count == 0)
				await _organization.SeedCatalogAsync(actor, IncidentPositionSources.Nims, cancellationToken);

			if (input.CreateFirstPeriod)
			{
				var start = DateTime.UtcNow;
				var period = await _planning.CreatePeriodAsync(actor, activation.IncidentActivationId, start, start.AddHours(Math.Clamp(defaults.OperationalPeriodHours, 1, 72)), null, cancellationToken);
				if (activation.Status == (int)IncidentActivationStatuses.Active)
					await _planning.StartPeriodAsync(actor, period.IncidentOperationalPeriodId, cancellationToken);
			}

			_audit.Record(actor, AuditLogTypes.IncidentActivationOpened, activation.TableName, activation.IncidentActivationId, null, activation);
			await PublishAsync(actor, activation, "Activated", cancellationToken: cancellationToken);
			return activation;
		}

		public async Task<IncidentActivation> UpdateAsync(IncidentActor actor, IncidentActivation activation, int expectedVersion, CancellationToken cancellationToken = default)
		{
			var stored = await _activations.GetAsync(actor, activation.IncidentActivationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			if (stored.Status == (int)IncidentActivationStatuses.Sealed)
				throw new IncidentRuleException("ActivationSealed");
			// Header fields only: links, complex membership and lifecycle have their own audited operations.
			stored.Name = string.IsNullOrWhiteSpace(activation.Name) ? stored.Name : activation.Name.Trim();
			stored.ComplexityType = activation.ComplexityType is >= 1 and <= 5 ? activation.ComplexityType : null;
			stored.TimeZoneId = IsKnownTimeZone(activation.TimeZoneId) ? activation.TimeZoneId : stored.TimeZoneId;
			stored.Currency = NormalizeCurrency(activation.Currency) ?? stored.Currency;
			stored.MeasurementSystem = activation.MeasurementSystem is "metric" or "customary" ? activation.MeasurementSystem : stored.MeasurementSystem;
			stored.Description = activation.Description;
			var saved = await _activations.SaveAsync(actor, stored, expectedVersion, cancellationToken);
			_audit.Record(actor, AuditLogTypes.IncidentActivationUpdated, saved.TableName, saved.IncidentActivationId, null, saved);
			return saved;
		}

		public async Task<IncidentActivation> LinkAsync(IncidentActor actor, string activationId, int? callId, string incidentCommandId, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			await ValidateLinksAsync(actor, activationId, callId ?? activation.CallId, incidentCommandId ?? activation.IncidentCommandId, cancellationToken);
			activation.CallId = callId ?? activation.CallId;
			activation.IncidentCommandId = string.IsNullOrWhiteSpace(incidentCommandId) ? activation.IncidentCommandId : incidentCommandId;
			var saved = await _activations.SaveAsync(actor, activation, activation.RowVersion, cancellationToken);
			_audit.Record(actor, AuditLogTypes.IncidentActivationUpdated, saved.TableName, saved.IncidentActivationId, null, saved, "Linked");
			return saved;
		}

		public async Task<IncidentActivation> SetComplexAsync(IncidentActor actor, string activationId, string parentActivationId, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var parentName = (string)null;
			if (!string.IsNullOrWhiteSpace(parentActivationId))
				parentName = (await ValidateParentAsync(actor, activationId, parentActivationId, (IncidentActivationKinds)activation.Kind, cancellationToken)).Name;
			var leaving = string.IsNullOrWhiteSpace(parentActivationId) && !string.IsNullOrWhiteSpace(activation.ParentActivationId);
			if (leaving)
				parentName = (await _activations.GetAsync(actor, activation.ParentActivationId, false, cancellationToken))?.Name;
			activation.ParentActivationId = string.IsNullOrWhiteSpace(parentActivationId) ? null : parentActivationId;
			var saved = await _activations.SaveAsync(actor, activation, activation.RowVersion, cancellationToken);
			_audit.Record(actor, AuditLogTypes.IncidentActivationUpdated, saved.TableName, saved.IncidentActivationId, null, saved, leaving ? "LeftComplex" : "JoinedComplex");
			await PublishAsync(actor, saved, leaving ? "LeftComplex" : "JoinedComplex", complexName: parentName, cancellationToken: cancellationToken);
			return saved;
		}

		public async Task<IncidentActivation> ChangeStatusAsync(IncidentActor actor, string activationId, IncidentActivationStatuses status, string reason, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var from = (IncidentActivationStatuses)activation.Status;
			var reopening = status == IncidentActivationStatuses.Active && from is IncidentActivationStatuses.Closed or IncidentActivationStatuses.Sealed;
			if (reopening && string.IsNullOrWhiteSpace(reason))
				throw new IncidentRuleException("ReasonRequired");

			if (status == IncidentActivationStatuses.Closed)
			{
				// Plan §9.0/§9.16: closing requires the closeout gate or recorded waivers.
				var open = await _closeout.CountAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Where("IsBlocking", true).Where("Status", (int)IncidentGateItemStatuses.Open), cancellationToken);
				if (open > 0)
					throw new IncidentRuleException("CloseoutGateOpen", open);
			}

			var saved = await _activations.TransitionAsync(actor, activationId, (int)status, reason, null, cancellationToken);
			var now = DateTime.UtcNow;
			switch (status)
			{
				case IncidentActivationStatuses.Closed: saved.ClosedOn = now; saved.ClosedByUserId = actor.UserId; break;
				case IncidentActivationStatuses.Sealed: saved.SealedOn = now; saved.SealedByUserId = actor.UserId; break;
				case IncidentActivationStatuses.Active when reopening: saved.ReopenedOn = now; saved.ReopenedByUserId = actor.UserId; saved.ReopenReason = reason; break;
			}
			if (status is IncidentActivationStatuses.Closed or IncidentActivationStatuses.Sealed || reopening)
				saved = await _activations.SaveAsync(actor, saved, saved.RowVersion, cancellationToken);

			_audit.Record(actor, AuditLogTypes.IncidentActivationStatusChanged, saved.TableName, saved.IncidentActivationId, new { Status = from.ToString() }, new { Status = status.ToString(), Reason = reason });
			var action = status switch
			{
				IncidentActivationStatuses.Closed => "Closed",
				IncidentActivationStatuses.Sealed => "Sealed",
				IncidentActivationStatuses.Active when reopening => "Reopened",
				_ => null
			};
			if (action != null)
				await PublishAsync(actor, saved, action, cancellationToken: cancellationToken);
			return saved;
		}

		private async Task ValidateLinksAsync(IncidentActor actor, string activationId, int? callId, string incidentCommandId, CancellationToken cancellationToken)
		{
			if (callId.HasValue)
			{
				var call = await _calls.GetCallByIdAsync(callId.Value, false);
				if (call == null || call.DepartmentId != actor.DepartmentId)
					throw new IncidentRuleException("CallNotFound");
			}

			if (!string.IsNullOrWhiteSpace(incidentCommandId))
			{
				var command = await _incidentCommand.GetCommandByIdAsync(incidentCommandId);
				if (command == null || command.DepartmentId != actor.DepartmentId)
					throw new IncidentRuleException("BoardNotFound");
				if (callId.HasValue && command.CallId != callId.Value)
					throw new IncidentRuleException("BoardCallMismatch");
				// One activation per board (the filtered unique index is the backstop).
				var holders = await _activations.ListAsync(actor, new IncidentLedgerQuery().Where("IncidentCommandId", incidentCommandId), cancellationToken);
				if (holders.Any(h => h.IncidentActivationId != activationId))
					throw new IncidentRuleException("BoardAlreadyActivated");
			}
		}

		private async Task<IncidentActivation> ValidateParentAsync(IncidentActor actor, string activationId, string parentActivationId, IncidentActivationKinds kind, CancellationToken cancellationToken)
		{
			if (kind == IncidentActivationKinds.Complex)
				throw new IncidentRuleException("ComplexCannotNest");
			if (parentActivationId == activationId)
				throw new IncidentRuleException("ComplexSelf");
			var parent = await _activations.GetAsync(actor, parentActivationId, false, cancellationToken);
			if (parent == null || parent.Kind != (int)IncidentActivationKinds.Complex)
				throw new IncidentRuleException("ComplexNotFound");
			if (parent.Status is (int)IncidentActivationStatuses.Closed or (int)IncidentActivationStatuses.Sealed)
				throw new IncidentRuleException("ComplexClosed");
			return parent;
		}

		private Task PublishAsync(IncidentActor actor, IncidentActivation activation, string action, string teamName = null, string complexName = null, int assignments = 0, CancellationToken cancellationToken = default)
			=> _events.PublishAsync(actor.DepartmentId, activation, WorkflowTriggerEventType.IncidentActivationChanged, new Dictionary<string, object>
			{
				["activation.id"] = activation.IncidentActivationId,
				["activation.name"] = activation.Name,
				["activation.short_code"] = activation.ShortCode,
				["activation.kind"] = ((IncidentActivationKinds)activation.Kind).ToString(),
				["activation.is_exercise"] = activation.IsExercise,
				["action"] = action,
				["team.name"] = teamName,
				["complex.name"] = complexName,
				["assignments.count"] = assignments
			}, activation.TableName, activation.IncidentActivationId, cancellationToken);

		private async Task<(string Currency, string Measurement)> LocaleDefaultsAsync(Department department, IncidentBackOfficeDefaults defaults)
		{
			string country = null;
			try
			{
				if (department?.AddressId != null)
					country = (await _addresses.GetAddressByIdAsync(department.AddressId.Value))?.Country;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
			var (currency, measurement) = IncidentLocaleDefaults.For(country);
			return (NormalizeCurrency(defaults.DefaultCurrency) ?? currency,
				defaults.DefaultMeasurementSystem is "metric" or "customary" ? defaults.DefaultMeasurementSystem : measurement);
		}

		internal static string NormalizeCurrency(string currency)
		{
			var value = (currency ?? string.Empty).Trim().ToUpperInvariant();
			return value.Length == 3 && value.All(char.IsLetter) ? value : null;
		}

		internal static bool IsKnownTimeZone(string id)
		{
			if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
				return false;
			try
			{
				TimeZoneInfo.FindSystemTimeZoneById(id);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	/// <summary>Currency and measurement defaults from the department's country (plan §11.2: defaulted from the department).</summary>
	public static class IncidentLocaleDefaults
	{
		private static readonly HashSet<string> Euro = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"AT", "BE", "CY", "DE", "EE", "ES", "FI", "FR", "GR", "HR", "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PT", "SI", "SK",
			"AUSTRIA", "BELGIUM", "GERMANY", "SPAIN", "FINLAND", "FRANCE", "GREECE", "IRELAND", "ITALY", "NETHERLANDS", "PORTUGAL"
		};

		public static (string Currency, string Measurement) For(string country)
		{
			var c = (country ?? string.Empty).Trim().ToUpperInvariant();
			if (c is "" or "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA") return ("USD", "customary");
			if (c is "CA" or "CANADA") return ("CAD", "metric");
			if (c is "GB" or "UK" or "UNITED KINGDOM") return ("GBP", "metric");
			if (c is "AU" or "AUSTRALIA") return ("AUD", "metric");
			if (c is "NZ" or "NEW ZEALAND") return ("NZD", "metric");
			if (c is "SE" or "SWEDEN") return ("SEK", "metric");
			if (c is "PL" or "POLAND") return ("PLN", "metric");
			if (c is "UA" or "UKRAINE") return ("UAH", "metric");
			if (c is "CH" or "SWITZERLAND") return ("CHF", "metric");
			if (c is "NO" or "NORWAY") return ("NOK", "metric");
			if (c is "DK" or "DENMARK") return ("DKK", "metric");
			if (Euro.Contains(c)) return ("EUR", "metric");
			return ("USD", "metric");
		}
	}
}
