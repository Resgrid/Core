using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>Department policies as JSON settings (plan §16). A missing or unreadable row is the documented default.</summary>
	public class IncidentBackOfficeSettingsService : IIncidentBackOfficeSettingsService
	{
		private static readonly Dictionary<Type, DepartmentSettingTypes> Types = new Dictionary<Type, DepartmentSettingTypes>
		{
			[typeof(IncidentBackOfficeDefaults)] = DepartmentSettingTypes.IncidentBackOfficeDefaults,
			[typeof(IncidentEnrollmentPolicy)] = DepartmentSettingTypes.IncidentEnrollmentPolicy,
			[typeof(IncidentNotificationRoutingPolicy)] = DepartmentSettingTypes.IncidentNotificationRouting,
			[typeof(IncidentForecastPolicy)] = DepartmentSettingTypes.IncidentForecastPolicy,
			[typeof(IncidentAssignmentRulePolicy)] = DepartmentSettingTypes.IncidentAssignmentRulePolicy,
			[typeof(IncidentVendorPolicy)] = DepartmentSettingTypes.IncidentVendorPolicy,
			[typeof(IncidentSelfServicePolicy)] = DepartmentSettingTypes.IncidentSelfServicePolicy,
			[typeof(IncidentFinancePolicy)] = DepartmentSettingTypes.IncidentFinancePolicy,
			[typeof(IncidentRetentionPolicy)] = DepartmentSettingTypes.IncidentRetentionPolicy,
			[typeof(CoordinationBoardDefaults)] = DepartmentSettingTypes.CoordinationBoardDefaults
		};

		private const string CacheKey = "IboPolicy_{0}_{1}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(1);

		private readonly IDepartmentSettingsService _settings;
		private readonly ICacheProvider _cache;
		private readonly IIncidentAuditService _audit;

		public IncidentBackOfficeSettingsService(IDepartmentSettingsService settings, ICacheProvider cache, IIncidentAuditService audit)
		{
			_settings = settings;
			_cache = cache;
			_audit = audit;
		}

		public DepartmentSettingTypes SettingTypeFor<T>() =>
			Types.TryGetValue(typeof(T), out var type) ? type : throw new ArgumentException($"{typeof(T).Name} is not an Incident Back Office policy.");

		public async Task<T> GetAsync<T>(int departmentId, bool bypassCache = false) where T : class, new()
		{
			var type = SettingTypeFor<T>();
			async Task<string> read() => (await _settings.GetSettingByTypeAsync(departmentId, type))?.Setting ?? string.Empty;

			string json;
			try
			{
				json = !bypassCache && Config.SystemBehaviorConfig.CacheEnabled
					? await _cache.RetrieveAsync(string.Format(CacheKey, (int)type, departmentId), read, CacheLength)
					: await read();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new T();
			}

			if (string.IsNullOrWhiteSpace(json))
				return new T();
			try
			{
				return JsonConvert.DeserializeObject<T>(json) ?? new T();
			}
			catch (JsonException)
			{
				return new T();
			}
		}

		public async Task<T> SaveAsync<T>(int departmentId, T policy, IncidentActor actor, CancellationToken cancellationToken = default) where T : class, new()
		{
			var type = SettingTypeFor<T>();
			var before = await GetAsync<T>(departmentId, true);
			await _settings.SaveOrUpdateSettingAsync(departmentId, JsonConvert.SerializeObject(policy ?? new T()), type, cancellationToken);
			await _cache.RemoveAsync(string.Format(CacheKey, (int)type, departmentId));
			_audit.Record(actor, AuditLogTypes.IncidentBackOfficeSettingsChanged, "DepartmentSettings", ((int)type).ToString(), before, policy);
			return policy;
		}
	}

	/// <summary>Plan §13.7 emission: one outbox row per trigger in the caller's transaction, producer "IncidentBackOffice".</summary>
	public class IncidentEventPublisher : IIncidentEventPublisher
	{
		private readonly IDomainEventOutboxService _outbox;
		private readonly Model.Repositories.IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly Model.Repositories.IIncidentLedgerRepository<IncidentOperationalPeriod> _periods;

		public IncidentEventPublisher(IDomainEventOutboxService outbox, Model.Repositories.IIncidentLedgerRepository<IncidentActivation> activations,
			Model.Repositories.IIncidentLedgerRepository<IncidentOperationalPeriod> periods)
		{
			_outbox = outbox;
			_activations = activations;
			_periods = periods;
		}

		public async Task PublishAsync(int departmentId, IncidentActivation activation, WorkflowTriggerEventType trigger, IDictionary<string, object> variables,
			string aggregateType, string aggregateId, CancellationToken cancellationToken = default)
		{
			try
			{
				var payload = new Dictionary<string, object>(StringComparer.Ordinal);
				var allowed = new HashSet<string>(IncidentBackOfficeWorkflowPayload.For(trigger).Select(v => v.Variable), StringComparer.Ordinal);

				if (activation != null && !IncidentBackOfficeWorkflowPayload.IsDepartmentScoped((int)trigger))
				{
					payload["incident.id"] = activation.IncidentActivationId;
					payload["incident.name"] = activation.Name;
					payload["incident.short_code"] = activation.ShortCode;
					payload["incident.kind"] = ((IncidentActivationKinds)activation.Kind).ToString();
					payload["incident.is_exercise"] = activation.IsExercise;
					payload["incident.call_id"] = activation.CallId;
					if (!string.IsNullOrWhiteSpace(activation.ParentActivationId))
						payload["incident.complex_name"] = (await _activations.GetForDepartmentAsync(departmentId, activation.ParentActivationId, true, cancellationToken))?.Name;
					var current = (await _periods.ListAsync(departmentId, IncidentLedgerQueryFor(activation.IncidentActivationId), cancellationToken)).FirstOrDefault();
					payload["incident.period.number"] = current?.Number;
				}

				if (variables != null)
					foreach (var (key, value) in variables)
						if (allowed.Contains(key))
							payload[key] = value is Enum e ? e.ToString() : value;

				await _outbox.EnqueueAsync(departmentId, IncidentBackOfficeWorkflowPayload.Producer, new DomainEventEnvelope
				{
					EventName = trigger.ToString(),
					AggregateType = aggregateType,
					AggregateId = aggregateId,
					AggregateVersion = 0,
					Trigger = trigger,
					OccurredOn = DateTime.UtcNow,
					CorrelationId = activation?.IncidentActivationId ?? aggregateId,
					Payload = payload
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Incident Back Office {trigger} for {aggregateType} {aggregateId} could not be published.");
			}
		}

		private static Model.Repositories.IncidentLedgerQuery IncidentLedgerQueryFor(string activationId)
		{
			var query = Model.Repositories.IncidentLedgerQuery.ForActivation(activationId).Where("Status", (int)IncidentPeriodStatuses.Current).Order("Number", true);
			query.Take = 1;
			return query;
		}
	}

	/// <summary>Plan §15.4: ledger mutations through AuditLog (AuditEvent on the aggregator, as LogsController.NewLog does).</summary>
	public class IncidentAuditService : IIncidentAuditService
	{
		private readonly IEventAggregator _eventAggregator;

		public IncidentAuditService(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;
		}

		public void Record(IncidentActor actor, AuditLogTypes type, string table, string id, object before = null, object after = null, string note = null)
		{
			if (actor == null)
				return;
			try
			{
				var body = new { Table = table, Id = id, ActingPosition = actor.ActingPositionCode, Note = note, Value = Safe(after) };
				_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					Type = type,
					Successful = true,
					IpAddress = actor.IpAddress,
					UserAgent = actor.UserAgent,
					ServerName = Environment.MachineName,
					Before = before == null ? null : JsonConvert.SerializeObject(new { Table = table, Id = id, Value = Safe(before) }),
					After = JsonConvert.SerializeObject(body)
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Incident Back Office audit {type} for {table} {id} could not be recorded.");
			}
		}

		private static readonly HashSet<string> ProtectedColumns = new HashSet<string>(
			IncidentBackOfficeProtectedFields.All().Select(f => f.Table + "." + f.Column), StringComparer.OrdinalIgnoreCase);

		/// <summary>An audit body never carries an ADP-catalogued value (plan §15.3a): catalogued columns of a ledger row are dropped.</summary>
		internal static object Safe(object value)
		{
			if (value is not IIncidentLedgerEntity entity)
				return value;
			var json = Newtonsoft.Json.Linq.JObject.FromObject(value);
			foreach (var property in json.Properties().ToList())
				if (ProtectedColumns.Contains(entity.TableName + "." + property.Name))
					property.Remove();
			return json;
		}
	}
}
