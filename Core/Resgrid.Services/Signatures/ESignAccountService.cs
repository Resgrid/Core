using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// The department's Documenso team and its team-scoped API token (one team per department, one organisation per cluster,
	/// plan §22.3). The token is encrypted with the department key (IEncryptionService.EncryptForDepartment, the connector-secret
	/// pattern) and never shown again: only its last four characters are kept in clear.
	/// </summary>
	public class ESignAccountService : IESignAccountService
	{
		private readonly IESignProviderAccountsRepository _accounts;
		private readonly IEncryptionService _encryption;
		private readonly IDepartmentsService _departments;
		private readonly IESignProvider _provider;
		private readonly IEventAggregator _eventAggregator;

		public ESignAccountService(IESignProviderAccountsRepository accounts, IEncryptionService encryption, IDepartmentsService departments, IESignProvider provider,
			IEventAggregator eventAggregator)
		{
			_accounts = accounts;
			_encryption = encryption;
			_departments = departments;
			_provider = provider;
			_eventAggregator = eventAggregator;
		}

		public bool IsPlatformConfigured() => _provider != null && _provider.IsAvailable;

		public async Task<ESignProviderAccount> GetAsync(int departmentId)
		{
			if (departmentId <= 0)
				return null;
			var account = await _accounts.GetForDepartmentAsync(departmentId, (int)ESignProviderKinds.Documenso);
			return account != null && account.DepartmentId == departmentId ? account : null;
		}

		public async Task<ESignProviderAccount> SaveAsync(int departmentId, string teamId, string apiToken, bool isActive, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return null;

			var now = DateTime.UtcNow;
			var account = await GetAsync(departmentId) ?? new ESignProviderAccount
			{
				DepartmentId = departmentId,
				ProviderKind = (int)ESignProviderKinds.Documenso,
				CreatedByUserId = userId,
				CreatedOn = now
			};
			var before = account.ESignProviderAccountId > 0 ? JsonConvert.SerializeObject(new { account.TeamId, account.TokenHint, account.IsActive }) : null;

			account.TeamId = string.IsNullOrWhiteSpace(teamId) ? null : (teamId.Trim().Length > 128 ? teamId.Trim().Substring(0, 128) : teamId.Trim());
			account.IsActive = isActive;
			if (!string.IsNullOrWhiteSpace(apiToken))
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				var token = apiToken.Trim();
				account.ApiTokenEncrypted = _encryption.EncryptForDepartment(token, departmentId, department?.Code);
				account.TokenHint = token.Length <= 4 ? "****" : token.Substring(token.Length - 4);
			}
			account.UpdatedByUserId = userId;
			account.UpdatedOn = now;

			var saved = account.ESignProviderAccountId > 0
				? await _accounts.UpdateAsync(account, cancellationToken, true)
				: await _accounts.InsertAsync(account, cancellationToken, true);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.ESignAccountChanged,
				Before = before,
				// Never the token: its last four characters only.
				After = JsonConvert.SerializeObject(new { saved.TeamId, saved.TokenHint, saved.IsActive }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return saved;
		}

		public async Task<bool> RemoveAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var account = await GetAsync(departmentId);
			if (account == null)
				return false;

			var removed = await _accounts.DeleteAsync(account, cancellationToken);
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.ESignAccountChanged,
				Before = JsonConvert.SerializeObject(new { account.TeamId, account.TokenHint, account.IsActive }),
				After = null,
				Successful = removed,
				ServerName = Environment.MachineName
			});
			return removed;
		}

		public async Task<string> GetApiTokenAsync(int departmentId)
		{
			var account = await GetAsync(departmentId);
			if (account == null || !account.IsActive || string.IsNullOrWhiteSpace(account.ApiTokenEncrypted))
				return null;

			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				return _encryption.DecryptForDepartment(account.ApiTokenEncrypted, departmentId, department?.Code);
			}
			catch (Exception ex)
			{
				// An unreadable token is "not configured" (remote signing refuses), never an exception at the signer.
				Logging.LogException(ex, "The department's e-sign API token could not be decrypted.");
				return null;
			}
		}
	}

	/// <summary>
	/// Webhook hints from the e-sign platform (Resgrid.Web.Eventing). Idempotent by the provider's event id; the hint keeps only
	/// the envelope id, and worker 90 re-fetches the envelope before anything changes.
	/// </summary>
	public class ESignWebhookService : IESignWebhookService
	{
		private readonly IESignWebhookEventsRepository _events;

		public ESignWebhookService(IESignWebhookEventsRepository events)
		{
			_events = events;
		}

		public async Task<bool> RecordHintAsync(ESignProviderKinds provider, string providerEventId, string eventType, string envelopeId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(providerEventId) || string.IsNullOrWhiteSpace(envelopeId))
				return false;

			var id = ESignWebhookPolicy.IdFor(provider, providerEventId);
			return await _events.TryInsertAsync(new ESignWebhookEvent
			{
				ESignWebhookEventId = id,
				ProviderKind = (int)provider,
				ProviderEventId = id.Substring(id.IndexOf(':') + 1),
				EventType = Clip(eventType, 64),
				EnvelopeId = Clip(envelopeId, 128),
				State = (int)ESignWebhookEventStates.Pending,
				ReceivedOn = DateTime.UtcNow,
				Attempts = 0
			}, cancellationToken);
		}

		public Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
			_events.PurgeReceivedBeforeAsync(utcNow - ESignWebhookPolicy.Retention, cancellationToken);

		private static string Clip(string value, int max) => string.IsNullOrWhiteSpace(value) ? null : (value.Trim().Length > max ? value.Trim().Substring(0, max) : value.Trim());
	}
}
