using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Request-scoped metadata, with no shared plaintext cache and no inference/provider calls.</summary>
	public sealed class ConfigurationSnapshotProvider(IEnumerable<IAdminAssistEvidenceSource> sources,
		IAdminAssistRepository repository, IRecordsAuthorizationService authorization, IAdminAssistCatalog catalog,
		TimeProvider clock) : IConfigurationSnapshotProvider
	{
		public async Task<ConfigurationSnapshot> ReadAsync(AdminAssistActor actor, CancellationToken ct = default)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			var token = timeout.Token;
			await AuthorizeAsync(actor, token);
			var now = clock.GetUtcNow().UtcDateTime;
			var before = await repository.GetConfigurationRevisionAsync(actor.DepartmentId, token);
			var evidence = new Dictionary<string, ConfigurationEvidence>(StringComparer.Ordinal);
			var sourceBudget = TimeSpan.FromSeconds(Math.Clamp(AdminAssistConfig.EvidenceSourceTimeoutSeconds, 1, 60));
			foreach (var source in sources)
			{
				token.ThrowIfCancellationRequested();
				// Each source has its own bound: one slow dependency (billing, a large roster) becomes unknown evidence for
				// that source instead of consuming the whole overview deadline.
				using var sourceTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
				sourceTimeout.CancelAfter(sourceBudget);
				try
				{
					var values = await source.ReadAsync(actor, now, sourceTimeout.Token).WaitAsync(sourceTimeout.Token);
					foreach (var value in values)
					{
						if (!source.EvidenceIds.Contains(value.Id) || evidence.ContainsKey(value.Id))
							throw new InvalidOperationException("Duplicate or undeclared evidence id.");
						evidence.Add(value.Id, value);
					}
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
				catch (OperationCanceledException)
				{
					foreach (var id in source.EvidenceIds)
						evidence[id] = new ConfigurationEvidence(id, EvidenceState.Unknown, source.SourceId,
							before.ToString(CultureInfo.InvariantCulture), now, ReasonCode: "SourceTimeout");
				}
				catch (UnauthorizedAccessException)
				{
					foreach (var id in source.EvidenceIds)
						evidence[id] = new ConfigurationEvidence(id, EvidenceState.Redacted, source.SourceId,
							before.ToString(CultureInfo.InvariantCulture), now, ReasonCode: "SourceAccessUnavailable");
				}
				catch (Exception)
				{
					// Source failures must not convert an inaccessible or failed query into a zero count.
					foreach (var id in source.EvidenceIds)
						evidence[id] = new ConfigurationEvidence(id, EvidenceState.Unknown, source.SourceId,
							before.ToString(CultureInfo.InvariantCulture), now, ReasonCode: "SourceUnavailable");
				}
			}
			var after = await repository.GetConfigurationRevisionAsync(actor.DepartmentId, token);
			await AuthorizeAsync(actor, token);
			foreach (var id in catalog.Rules.SelectMany(r => r.AppliesWhen.Concat(r.FailsWhen)).Select(c => c.EvidenceId).Distinct())
				if (!evidence.ContainsKey(id)) evidence[id] = new ConfigurationEvidence(id, EvidenceState.Unknown,
					"capability-gap", after.ToString(CultureInfo.InvariantCulture), now, ReasonCode: "EvidenceNotObserved");
			return new ConfigurationSnapshot(actor.DepartmentId, actor.UserId,
				after.ToString(CultureInfo.InvariantCulture), now, before == after,
				new ReadOnlyDictionary<string, ConfigurationEvidence>(evidence));
		}

		private async Task AuthorizeAsync(AdminAssistActor actor, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId) ||
				!await authorization.IsActiveMemberAsync(actor.UserId, actor.DepartmentId).WaitAsync(ct) ||
				!await authorization.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId).WaitAsync(ct)) throw new UnauthorizedAccessException();
		}
	}
}
