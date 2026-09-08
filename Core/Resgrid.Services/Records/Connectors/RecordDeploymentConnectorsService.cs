using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records.Connectors
{
	/// <summary>
	/// External ordering-system connectors (RMS plan section 4.1). The plan permits a connector only with a
	/// documented API, credentials, rate limits, source terms, reconciliation and explicit read/write authority,
	/// and forbids screen-scraping and inferred order updates. This service is where each of those is enforced:
	/// a connector cannot be enabled without acknowledged terms and read authority; write authority is refused
	/// outright; every fetch counts against the connector's hourly limit; an order the department has not seen
	/// is provisioned from the feed, a changed order becomes a new versioned snapshot, a new request becomes a
	/// Requested fill, and every other disagreement is surfaced to a person rather than applied.
	/// </summary>
	public class RecordDeploymentConnectorsService : IRecordDeploymentConnectorsService
	{
		/// <summary>Pages one run will follow; a source that never ends its cursor cannot run a worker forever.</summary>
		public const int MaxPagesPerRun = 20;

		private readonly IRmsExternalOrderConnectorsRepository _connectors;
		private readonly IRmsExternalOrderConnectorRunsRepository _runs;
		private readonly IRmsExternalOrdersRepository _orders;
		private readonly IRmsExternalOrderFillsRepository _fills;
		private readonly IRecordDeploymentsService _deployments;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IReadOnlyDictionary<string, IExternalOrderFeedProvider> _providers;

		public RecordDeploymentConnectorsService(IRmsExternalOrderConnectorsRepository connectors, IRmsExternalOrderConnectorRunsRepository runs, IRmsExternalOrdersRepository orders,
			IRmsExternalOrderFillsRepository fills, IRecordDeploymentsService deployments, IRecordsAuthorizationService authorization, IRmsAccessAuditsRepository audits,
			IEnumerable<IExternalOrderFeedProvider> providers)
		{
			_connectors = connectors;
			_runs = runs;
			_orders = orders;
			_fills = fills;
			_deployments = deployments;
			_authorization = authorization;
			_audits = audits;
			_providers = (providers ?? Enumerable.Empty<IExternalOrderFeedProvider>()).ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
		}

		#region Management

		public async Task<List<RmsExternalOrderConnector>> ListAsync(int departmentId, string userId)
		{
			await RequireAdminAsync(departmentId, userId);
			return (await _connectors.GetForDepartmentAsync(departmentId))?.Where(c => !c.DeletedOn.HasValue).OrderBy(c => c.Name).Select(Scrub).ToList() ?? new List<RmsExternalOrderConnector>();
		}

		public async Task<RmsExternalOrderConnector> GetAsync(int departmentId, string userId, string connectorId)
		{
			await RequireAdminAsync(departmentId, userId);
			var connector = await _connectors.GetByIdForDepartmentAsync(departmentId, connectorId);
			return connector == null || connector.DeletedOn.HasValue ? null : Scrub(connector);
		}

		public async Task<RecordDeploymentConnectorCreated> CreateAsync(int departmentId, string userId, RecordDeploymentConnectorInput input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			RequireConfigured();
			if (input == null) throw new ArgumentNullException(nameof(input));
			var provider = RequireProvider(input.ProviderKey);
			var now = DateTime.UtcNow;
			var token = NewInboundToken();

			var connector = new RmsExternalOrderConnector
			{
				RmsExternalOrderConnectorId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				ProviderKey = provider.Key,
				InboundTokenHash = Hash(token),
				IsEnabled = false,
				CreatedOn = now,
				CreatedByUserId = userId,
				ModifiedOn = now,
				ModifiedByUserId = userId,
				RowVersion = 1
			};
			Apply(connector, input, provider, userId);
			if (!string.IsNullOrEmpty(input.Credential))
				connector.CredentialCiphertext = SymmetricEncryption.Encrypt(input.Credential, RecordsConnectorConfig.CredentialPassphrase);

			await _connectors.InsertAsync(connector, cancellationToken, true);
			await AuditAsync(departmentId, userId, connector, "Create connector", cancellationToken, new { connector.ProviderKey, connector.BaseUrl, connector.ReadEnabled });
			return new RecordDeploymentConnectorCreated { Connector = Scrub(connector), InboundToken = token };
		}

		public async Task<RmsExternalOrderConnector> UpdateAsync(int departmentId, string userId, string connectorId, long expectedRowVersion, RecordDeploymentConnectorInput input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var connector = await LoadAsync(departmentId, connectorId);
			if (connector.RowVersion != expectedRowVersion) throw new RecordConcurrencyException(connectorId, expectedRowVersion, connector.RowVersion);
			var provider = RequireProvider(input.ProviderKey ?? connector.ProviderKey);

			Apply(connector, input, provider, userId);
			if (!string.IsNullOrEmpty(input.Credential))
			{
				RequireConfigured();
				connector.CredentialCiphertext = SymmetricEncryption.Encrypt(input.Credential, RecordsConnectorConfig.CredentialPassphrase);
			}
			// A change to where or how the connector reads means the terms it agreed to may no longer describe the
			// source; the acknowledgement is dropped and the connector waits for a person to confirm again.
			if (connector.IsEnabled && !connector.IsReadyToRun)
				connector.IsEnabled = false;

			await SaveAsync(connector, userId, cancellationToken);
			await AuditAsync(departmentId, userId, connector, "Update connector", cancellationToken, new { connector.ProviderKey, connector.BaseUrl, connector.ReadEnabled, connector.IsEnabled });
			return Scrub(connector);
		}

		public async Task<RmsExternalOrderConnector> SetEnabledAsync(int departmentId, string userId, string connectorId, bool enabled, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var connector = await LoadAsync(departmentId, connectorId);
			if (enabled)
			{
				RequireConfigured();
				if (!connector.TermsAcknowledgedOn.HasValue) throw new InvalidOperationException("Acknowledge the source's terms before enabling the connector.");
				if (!connector.ReadEnabled) throw new InvalidOperationException("The connector has no read authority; nothing would be imported.");
				if (RequiresCredential(connector) && string.IsNullOrEmpty(connector.CredentialCiphertext)) throw new InvalidOperationException("The connector's credential kind needs a stored credential.");
				connector.ConsecutiveFailures = 0;
				connector.LastError = null;
			}
			connector.IsEnabled = enabled;
			await SaveAsync(connector, userId, cancellationToken);
			await AuditAsync(departmentId, userId, connector, enabled ? "Enable connector" : "Disable connector", cancellationToken);
			return Scrub(connector);
		}

		public async Task<RmsExternalOrderConnector> AcknowledgeTermsAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var connector = await LoadAsync(departmentId, connectorId);
			if (string.IsNullOrWhiteSpace(connector.TermsReference)) throw new InvalidOperationException("Record where the source's terms are before acknowledging them.");
			connector.TermsAcknowledgedOn = DateTime.UtcNow;
			connector.TermsAcknowledgedByUserId = userId;
			await SaveAsync(connector, userId, cancellationToken);
			await AuditAsync(departmentId, userId, connector, "Acknowledge source terms", cancellationToken, new { connector.TermsReference });
			return Scrub(connector);
		}

		public async Task<string> RotateInboundTokenAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var connector = await LoadAsync(departmentId, connectorId);
			var token = NewInboundToken();
			connector.InboundTokenHash = Hash(token);
			await SaveAsync(connector, userId, cancellationToken);
			await AuditAsync(departmentId, userId, connector, "Rotate inbound token", cancellationToken);
			return token;
		}

		public async Task DeleteAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var connector = await LoadAsync(departmentId, connectorId);
			connector.IsEnabled = false;
			connector.DeletedOn = DateTime.UtcNow;
			// The credential and the token die with the connector; orders it imported keep their snapshots.
			connector.CredentialCiphertext = null;
			connector.InboundTokenHash = null;
			await SaveAsync(connector, userId, cancellationToken);
			await AuditAsync(departmentId, userId, connector, "Delete connector", cancellationToken);
		}

		#endregion

		#region Running

		public async Task<RecordDeploymentConnectorRunResult> RunAsync(int departmentId, string userId, string connectorId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var connector = await LoadAsync(departmentId, connectorId);
			return await ExecuteAsync(connector, RmsConnectorRunTriggers.Manual, userId, null, cancellationToken);
		}

		public async Task<RecordDeploymentConnectorRunResult> ImportInboundAsync(string connectorId, string inboundToken, string feedJson, CancellationToken cancellationToken = default)
		{
			var connector = string.IsNullOrWhiteSpace(connectorId) ? null : await _connectors.GetByIdAsync(connectorId);
			// The same refusal for an unknown connector and a wrong token: an attacker learns nothing from the difference.
			if (connector == null || connector.DeletedOn.HasValue || string.IsNullOrEmpty(connector.InboundTokenHash) || string.IsNullOrEmpty(inboundToken) || !FixedTimeEquals(Hash(inboundToken), connector.InboundTokenHash))
				throw new UnauthorizedAccessException("The connector token was not accepted.");
			return await ExecuteAsync(connector, RmsConnectorRunTriggers.Inbound, null, feedJson, cancellationToken);
		}

		public async Task<int> RunDueAsync(CancellationToken cancellationToken = default)
		{
			if (!RecordsConnectorConfig.Enabled)
				return 0;
			var due = (await _connectors.GetDueAsync(DateTime.UtcNow, 50))?.ToList() ?? new List<RmsExternalOrderConnector>();
			var ran = 0;
			foreach (var connector in due)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					await ExecuteAsync(connector, RmsConnectorRunTriggers.Poll, null, null, cancellationToken);
					ran++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Connector poll failed for " + connector.RmsExternalOrderConnectorId);
				}
			}
			return ran;
		}

		/// <summary>One run: gates, fetch (unless the feed was pushed), import, bookkeeping. Every outcome leaves a run row.</summary>
		private async Task<RecordDeploymentConnectorRunResult> ExecuteAsync(RmsExternalOrderConnector connector, string trigger, string triggeredByUserId, string pushedFeed, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var run = new RmsExternalOrderConnectorRun
			{
				RmsExternalOrderConnectorRunId = Guid.NewGuid().ToString(), DepartmentId = connector.DepartmentId, RmsExternalOrderConnectorId = connector.RmsExternalOrderConnectorId,
				Trigger = trigger, TriggeredByUserId = triggeredByUserId, StartedOn = now, Outcome = RmsConnectorRunOutcomes.Ok
			};
			var result = new RecordDeploymentConnectorRunResult { Run = run };
			// The administrator who set the connector up (or last acknowledged its terms) is the authority every
			// import acts under; a poll has no person of its own.
			var actor = triggeredByUserId ?? connector.TermsAcknowledgedByUserId ?? connector.CreatedByUserId;

			try
			{
				if (!RecordsConnectorConfig.Enabled)
					return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Disabled, "Connectors are switched off for this installation.", cancellationToken);
				if (!connector.IsReadyToRun)
					return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Disabled, "The connector is not enabled, has no read authority, or its terms are unacknowledged.", cancellationToken);
				if (connector.WriteEnabled)
					return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Rejected, "Write authority is not granted in this release.", cancellationToken);

				// One runner at a time. A poll, a manual run and a push can all reach the same connector, and each of
				// them spends the hourly budget and writes the cursor back, so the run is claimed on the row first:
				// the loser is refused rather than fetching the same pages and overwriting the winner's bookkeeping.
				if (!await _connectors.TryBumpRowVersionAsync(connector.DepartmentId, connector.RmsExternalOrderConnectorId, connector.RowVersion, cancellationToken))
					return await RefuseClaimAsync(run, result, cancellationToken);
				connector.RowVersion += 1;

				var provider = RequireProvider(connector.ProviderKey);
				var pages = new List<ExternalOrderFeed>();
				if (pushedFeed != null)
				{
					// A push counts against the same hourly limit as a poll; a chatty source cannot bypass it by pushing.
					if (!TakeRequestSlot(connector, now))
						return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.RateLimited, $"The connector's limit of {connector.MaxRequestsPerHour} requests per hour is spent.", cancellationToken);
					run.RequestCount++;
					var pushed = ExternalOrderFeedContract.Parse(pushedFeed, out var problems);
					if (pushed == null)
						return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Rejected, string.Join(" ", problems), cancellationToken);
					pages.Add(pushed);
				}
				else
				{
					var credential = string.IsNullOrEmpty(connector.CredentialCiphertext) ? null : SymmetricEncryption.Decrypt(connector.CredentialCiphertext, RecordsConnectorConfig.CredentialPassphrase);
					var cursor = connector.LastCursor;
					for (var page = 0; page < MaxPagesPerRun; page++)
					{
						if (!TakeRequestSlot(connector, now))
						{
							if (pages.Count == 0)
								return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.RateLimited, $"The connector's limit of {connector.MaxRequestsPerHour} requests per hour is spent.", cancellationToken);
							result.Messages.Add("Stopped at the hourly request limit; the rest follows on the next run.");
							break;
						}
						run.RequestCount++;
						var body = await provider.FetchAsync(connector, credential, cursor, cancellationToken);
						var feed = ExternalOrderFeedContract.Parse(body, out var problems);
						if (feed == null)
							return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Rejected, string.Join(" ", problems), cancellationToken);
						pages.Add(feed);
						cursor = feed.Cursor;
						if (string.IsNullOrWhiteSpace(cursor))
							break;
					}
					connector.LastCursor = cursor;
				}

				// The department's orders are read once for the whole run rather than once per page: the match is by
				// order number and scheme, and a twenty-page run would otherwise re-read the department's entire
				// order history twenty times. Orders created by this run are added to the list as they are made.
				var known = (await _orders.GetForDepartmentAsync(connector.DepartmentId, true))?.Where(o => !o.DeletedOn.HasValue).ToList() ?? new List<RmsExternalOrder>();
				foreach (var feed in pages)
				{
					run.SourceVersion = feed.Source?.Version ?? run.SourceVersion;
					await ImportFeedAsync(connector, provider, feed, actor, run, result, known, cancellationToken);
				}

				connector.LastSuccessOn = DateTime.UtcNow;
				connector.ConsecutiveFailures = 0;
				connector.LastError = null;
				return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Ok, null, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Connector run failed for " + connector.RmsExternalOrderConnectorId);
				connector.ConsecutiveFailures++;
				if (connector.ConsecutiveFailures >= RecordsConnectorConfig.DisableAfterConsecutiveFailures)
				{
					// A source that keeps failing is switched off rather than hammered; an administrator re-enables it.
					connector.IsEnabled = false;
					result.Messages.Add("The connector was disabled after repeated failures.");
				}
				return await FinishAsync(connector, run, result, RmsConnectorRunOutcomes.Failed, ex.Message, cancellationToken);
			}
		}

		/// <summary>
		/// A run that lost the claim leaves its run row and nothing else: the connector row belongs to whoever holds
		/// the claim, so writing this run's stale copy of it is exactly what the claim exists to prevent.
		/// </summary>
		private async Task<RecordDeploymentConnectorRunResult> RefuseClaimAsync(RmsExternalOrderConnectorRun run, RecordDeploymentConnectorRunResult result, CancellationToken cancellationToken)
		{
			const string error = "The connector is already running; this run was skipped.";
			run.Outcome = RmsConnectorRunOutcomes.Rejected;
			run.Error = error;
			run.FinishedOn = DateTime.UtcNow;
			try
			{
				await _runs.InsertAsync(run, cancellationToken, true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Connector run bookkeeping failed for " + run.RmsExternalOrderConnectorId);
			}
			result.Messages.Add(error);
			return result;
		}

		private async Task<RecordDeploymentConnectorRunResult> FinishAsync(RmsExternalOrderConnector connector, RmsExternalOrderConnectorRun run, RecordDeploymentConnectorRunResult result, string outcome, string error, CancellationToken cancellationToken)
		{
			run.Outcome = outcome;
			run.Error = Trim(error, 1000);
			run.FinishedOn = DateTime.UtcNow;
			connector.LastPolledOn = run.StartedOn;
			if (error != null && outcome != RmsConnectorRunOutcomes.Ok)
				connector.LastError = Trim(error, 500);
			try
			{
				await _runs.InsertAsync(run, cancellationToken, true);
				await _runs.TrimAsync(connector.DepartmentId, connector.RmsExternalOrderConnectorId, RecordsConnectorConfig.RunHistoryToKeep, cancellationToken);
				connector.ModifiedOn = DateTime.UtcNow;
				connector.RowVersion += 1;
				await _connectors.UpdateAsync(connector, cancellationToken, true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Connector run bookkeeping failed for " + connector.RmsExternalOrderConnectorId);
			}
			if (!string.IsNullOrEmpty(error))
				result.Messages.Add(error);
			return result;
		}

		/// <summary>Hourly window: the first request after an hour opens a new window; a spent window refuses.</summary>
		private static bool TakeRequestSlot(RmsExternalOrderConnector connector, DateTime now)
		{
			if (!connector.RateWindowStartedOn.HasValue || now - connector.RateWindowStartedOn.Value >= TimeSpan.FromHours(1))
			{
				connector.RateWindowStartedOn = now;
				connector.RequestsThisHour = 0;
			}
			if (connector.MaxRequestsPerHour > 0 && connector.RequestsThisHour >= connector.MaxRequestsPerHour)
				return false;
			connector.RequestsThisHour++;
			return true;
		}

		#endregion

		#region Import

		/// <summary>
		/// The import rules, in the plan's words: the source stays authoritative for what was ordered; a later
		/// import lands as a new versioned snapshot rather than overwriting signed history; nothing local is
		/// transitioned on the source's say-so. So an unseen order is provisioned, a changed order gets a new
		/// snapshot, an unseen request becomes a Requested fill, and everything else is reconciliation.
		/// </summary>
		private async Task ImportFeedAsync(RmsExternalOrderConnector connector, IExternalOrderFeedProvider provider, ExternalOrderFeed feed, string actor, RmsExternalOrderConnectorRun run, RecordDeploymentConnectorRunResult result, List<RmsExternalOrder> existing, CancellationToken cancellationToken)
		{
			var scheme = string.IsNullOrWhiteSpace(connector.SourceScheme) ? provider.DefaultScheme : connector.SourceScheme;

			foreach (var order in feed.Orders)
			{
				cancellationToken.ThrowIfCancellationRequested();
				run.OrdersSeen++;
				var problems = provider.ValidateOrder(order);
				if (problems.Count > 0)
				{
					run.Rejected++;
					result.Messages.Add($"Order {order.OrderNumber}: {string.Join(" ", problems)}");
					continue;
				}

				var snapshot = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(order, Formatting.None));
				var version = string.IsNullOrWhiteSpace(order.SourceVersion) ? feed.Source?.Version : order.SourceVersion.Trim();
				var match = existing.FirstOrDefault(o => string.Equals(o.OrderNumber, order.OrderNumber.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(o.SourceScheme, scheme, StringComparison.OrdinalIgnoreCase));

				if (match == null)
				{
					var input = ToCreateInput(connector, provider, scheme, order, snapshot, version);
					var created = await _deployments.CreateFromExternalOrderAsync(connector.DepartmentId, actor, input, cancellationToken);
					existing.Add(created.Order);
					run.OrdersCreated++;
					run.RequestsAdded += input.Fills.Count;
					await AuditAsync(connector.DepartmentId, actor, connector, $"Import order {order.OrderNumber} ({run.Trigger})", cancellationToken, new { created.Order.RmsExternalOrderId, version });
					continue;
				}

				if (match.Status == (int)RmsExternalOrderStatus.ClosedOut)
				{
					// A closed-out deployment is signed history; the source's later view is a reconciliation note, not a change.
					run.Unchanged++;
					continue;
				}

				var checksum = RecordSnapshotSerializer.Checksum(snapshot);
				var changed = !string.Equals(match.ArtifactChecksum, checksum, StringComparison.Ordinal);
				if (changed)
				{
					await _deployments.RecordSourceSnapshotAsync(connector.DepartmentId, actor, match.RmsExternalOrderId, version, snapshot, FileNameFor(scheme, order.OrderNumber, version), "application/json", cancellationToken);
					run.SnapshotsRecorded++;
				}
				else
				{
					run.Unchanged++;
				}

				var fills = (await _fills.GetForOrderAsync(connector.DepartmentId, match.RmsExternalOrderId))?.ToList() ?? new List<RmsExternalOrderFill>();
				foreach (var request in order.Requests)
				{
					if (fills.Any(f => string.Equals(f.RequestNumber, request.RequestNumber?.Trim(), StringComparison.OrdinalIgnoreCase)))
						continue;
					if (string.Equals(request.Status, ExternalOrderFeedContract.RequestStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
						continue;
					fills.Add(await _deployments.AddFillAsync(connector.DepartmentId, actor, match.RmsExternalOrderId, ToFillInput(request), cancellationToken));
					run.RequestsAdded++;
				}

				// What is left after the source's own facts have landed is for a person.
				run.Conflicts += Reconcile(connector, match, order, fills).Count;
			}
		}

		private static RecordDeploymentCreateInput ToCreateInput(RmsExternalOrderConnector connector, IExternalOrderFeedProvider provider, string scheme, ExternalOrderFeedOrder order, byte[] snapshot, string version)
		{
			var profile = string.IsNullOrWhiteSpace(connector.ProfileKey) ? provider.DefaultProfileKey : connector.ProfileKey;
			return new RecordDeploymentCreateInput
			{
				ProfileKey = profile, SourceScheme = scheme, SourceSystem = connector.SourceSystem, OrderNumber = order.OrderNumber?.Trim(), IncidentName = order.IncidentName?.Trim(),
				IncidentNumber = order.IncidentNumber, IncidentCountry = order.IncidentCountry, IncidentSubdivision = order.IncidentSubdivision, OrderingOffice = order.OrderingOffice,
				DispatchOffice = order.DispatchOffice, RequestingAgency = order.RequestingAgency, ReceivingAgency = order.ReceivingAgency, SendingAgency = order.SendingAgency,
				CostCode = order.CostCode, AgreementReference = order.AgreementReference, CurrencyCode = order.CurrencyCode, MeasurementSystem = order.MeasurementSystem,
				TimeZoneId = order.TimeZoneId, SourceCapturedOn = order.CapturedOn?.UtcDateTime, SourceVersion = version,
				ArtifactData = snapshot, ArtifactFileName = FileNameFor(scheme, order.OrderNumber, version), ArtifactContentType = "application/json",
				ArtifactSafeUrl = order.Artifact?.Url,
				ConnectorId = connector.RmsExternalOrderConnectorId, OwnershipMarker = RmsExternalOrderOwnership.Connector,
				Fills = order.Requests.Where(r => !string.Equals(r.Status, ExternalOrderFeedContract.RequestStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)).Select(ToFillInput).ToList()
			};
		}

		private static RecordDeploymentFillInput ToFillInput(ExternalOrderFeedRequest request) => new RecordDeploymentFillInput
		{
			RequestNumber = request.RequestNumber?.Trim(), ParentRequestNumber = request.ParentRequestNumber, RequestCategory = request.Category, FillNumber = request.FillNumber,
			ResourceKind = request.ResourceKind, ResourceType = request.ResourceType, ResourceTypeScheme = request.ResourceTypeScheme, Position = request.Position, PositionScheme = request.PositionScheme,
			IsTrainee = request.IsTrainee, HomeUnit = request.HomeUnit, HostAgency = request.HostAgency, AgencyUnitId = request.AgencyUnitId, PointOfHire = request.PointOfHire,
			CostCode = request.CostCode, AgreementReference = request.AgreementReference, RequestedOn = request.RequestedOn?.UtcDateTime, NeededOn = request.NeededOn?.UtcDateTime
		};

		private static string FileNameFor(string scheme, string orderNumber, string version)
		{
			var safe = new string((scheme + "-" + orderNumber).Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());
			return safe + (string.IsNullOrWhiteSpace(version) ? string.Empty : "-v" + new string(version.Where(char.IsLetterOrDigit).ToArray())) + ".json";
		}

		#endregion

		#region Reconciliation

		public async Task<List<RmsExternalOrderConnectorRun>> GetRunsAsync(int departmentId, string userId, string connectorId, int take)
		{
			await RequireAdminAsync(departmentId, userId);
			return (await _runs.GetForConnectorAsync(departmentId, connectorId, Math.Max(1, Math.Min(500, take))))?.ToList() ?? new List<RmsExternalOrderConnectorRun>();
		}

		public async Task<List<RecordDeploymentReconciliationItem>> GetReconciliationAsync(int departmentId, string userId, string connectorId = null)
		{
			await RequireAdminAsync(departmentId, userId);
			var orders = (await _orders.GetForDepartmentAsync(departmentId, false))?.Where(o => !o.DeletedOn.HasValue && string.Equals(o.OwnershipMarker, RmsExternalOrderOwnership.Connector, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<RmsExternalOrder>();
			if (!string.IsNullOrWhiteSpace(connectorId))
				orders = orders.Where(o => string.Equals(o.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase)).ToList();

			var items = new List<RecordDeploymentReconciliationItem>();
			foreach (var order in orders)
			{
				var artifact = await _orders.GetArtifactAsync(departmentId, order.RmsExternalOrderId);
				if (artifact == null || artifact.Length == 0) continue;
				ExternalOrderFeedOrder source;
				try { source = JsonConvert.DeserializeObject<ExternalOrderFeedOrder>(Encoding.UTF8.GetString(artifact)); }
				catch (JsonException) { continue; }
				if (source == null) continue;
				var fills = (await _fills.GetForOrderAsync(departmentId, order.RmsExternalOrderId))?.ToList() ?? new List<RmsExternalOrderFill>();
				var connector = new RmsExternalOrderConnector { RmsExternalOrderConnectorId = order.ConnectorId, DepartmentId = departmentId };
				items.AddRange(Reconcile(connector, order, source, fills));
			}
			return items;
		}

		/// <summary>Pure comparison of one source order against the department's own record; shown, never applied.</summary>
		public static List<RecordDeploymentReconciliationItem> Reconcile(RmsExternalOrderConnector connector, RmsExternalOrder order, ExternalOrderFeedOrder source, List<RmsExternalOrderFill> fills)
		{
			var items = new List<RecordDeploymentReconciliationItem>();
			if (order == null || source == null) return items;
			fills ??= new List<RmsExternalOrderFill>();

			RecordDeploymentReconciliationItem Item(string kind, string requestNumber, string sourceStatus, string localStatus) => new RecordDeploymentReconciliationItem
			{
				ConnectorId = connector?.RmsExternalOrderConnectorId, OrderId = order.RmsExternalOrderId, RecordId = order.RecordId, OrderNumber = order.OrderNumber, RequestNumber = requestNumber,
				Kind = kind, SourceStatus = sourceStatus, LocalStatus = localStatus, SourceVersion = source.SourceVersion, SourceCapturedOn = source.CapturedOn?.UtcDateTime
			};

			var sourceStatus = (source.Status ?? string.Empty).Trim().ToLowerInvariant();
			var outstanding = fills.Where(f => f.Status != (int)RmsDeploymentFillStatus.Declined && f.Status != (int)RmsDeploymentFillStatus.Returned).ToList();
			if ((sourceStatus == ExternalOrderFeedContract.OrderStatuses.Released || sourceStatus == ExternalOrderFeedContract.OrderStatuses.Closed) && outstanding.Count > 0)
			{
				// The plan's own warning: an external release flag never returns a resource. It is a note for a person.
				items.Add(Item(sourceStatus == ExternalOrderFeedContract.OrderStatuses.Closed ? RecordDeploymentReconciliationItem.SourceClosedLocalOpen : RecordDeploymentReconciliationItem.SourceReleasedLocalOut, null, sourceStatus, ((RmsExternalOrderStatus)order.Status).ToString().ToLowerInvariant()));
			}

			foreach (var request in source.Requests ?? new List<ExternalOrderFeedRequest>())
			{
				var fill = fills.FirstOrDefault(f => string.Equals(f.RequestNumber, request.RequestNumber?.Trim(), StringComparison.OrdinalIgnoreCase));
				var requestStatus = (request.Status ?? ExternalOrderFeedContract.RequestStatuses.Requested).Trim().ToLowerInvariant();
				if (fill == null)
				{
					if (requestStatus != ExternalOrderFeedContract.RequestStatuses.Cancelled)
						items.Add(Item(RecordDeploymentReconciliationItem.SourceRequestMissingLocally, request.RequestNumber, requestStatus, null));
					continue;
				}
				var local = ExternalOrderFeedContract.LocalStatusOf((RmsDeploymentFillStatus)fill.Status);
				if (requestStatus == ExternalOrderFeedContract.RequestStatuses.Cancelled && fill.Status != (int)RmsDeploymentFillStatus.Declined)
				{
					items.Add(Item(RecordDeploymentReconciliationItem.SourceStatusAhead, request.RequestNumber, requestStatus, local));
					continue;
				}
				var sourceRank = ExternalOrderFeedContract.RequestStatuses.Rank(requestStatus);
				var localRank = ExternalOrderFeedContract.RequestStatuses.Rank(local);
				if (fill.Status == (int)RmsDeploymentFillStatus.Returned || sourceRank == 0 || localRank == 0) continue;
				if (sourceRank > localRank) items.Add(Item(RecordDeploymentReconciliationItem.SourceStatusAhead, request.RequestNumber, requestStatus, local));
				else if (sourceRank < localRank) items.Add(Item(RecordDeploymentReconciliationItem.SourceStatusBehind, request.RequestNumber, requestStatus, local));
			}

			foreach (var fill in fills)
			{
				if (fill.Status == (int)RmsDeploymentFillStatus.Declined) continue;
				if (!(source.Requests ?? new List<ExternalOrderFeedRequest>()).Any(r => string.Equals(r.RequestNumber?.Trim(), fill.RequestNumber, StringComparison.OrdinalIgnoreCase)))
					items.Add(Item(RecordDeploymentReconciliationItem.LocalFillMissingInSource, fill.RequestNumber, null, ExternalOrderFeedContract.LocalStatusOf((RmsDeploymentFillStatus)fill.Status)));
			}

			return items;
		}

		#endregion

		#region Helpers

		private async Task RequireAdminAsync(int departmentId, string userId)
		{
			if (!await _authorization.IsDepartmentAdminAsync(userId, departmentId))
				throw new UnauthorizedAccessException("External order connectors are department administration only.");
		}

		private static void RequireConfigured()
		{
			if (!RecordsConnectorConfig.Enabled) throw new InvalidOperationException("External order connectors are switched off for this installation.");
			if (string.IsNullOrWhiteSpace(RecordsConnectorConfig.CredentialPassphrase)) throw new InvalidOperationException("No credential passphrase is configured; connector credentials cannot be stored.");
		}

		private IExternalOrderFeedProvider RequireProvider(string key)
		{
			if (!RmsExternalOrderConnectorProviders.IsKnown(key) || !_providers.TryGetValue(key.Trim(), out var provider))
				throw new ArgumentException($"'{key}' is not a connector provider ({string.Join(", ", RmsExternalOrderConnectorProviders.All)}).");
			return provider;
		}

		private async Task<RmsExternalOrderConnector> LoadAsync(int departmentId, string connectorId)
		{
			var connector = string.IsNullOrWhiteSpace(connectorId) ? null : await _connectors.GetByIdForDepartmentAsync(departmentId, connectorId);
			if (connector == null || connector.DeletedOn.HasValue) throw new ArgumentException("Unknown connector.", nameof(connectorId));
			return connector;
		}

		private static void Apply(RmsExternalOrderConnector connector, RecordDeploymentConnectorInput input, IExternalOrderFeedProvider provider, string userId)
		{
			if (input.WriteEnabled) throw new InvalidOperationException("Write authority to an external ordering system is not granted in this release; the plan permits no external writes.");
			if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("A connector needs a name.", nameof(input));
			var baseUrl = (input.BaseUrl ?? string.Empty).Trim();
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) throw new ArgumentException("The feed root must be an absolute URL.", nameof(input));
			if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) && !(RecordsConnectorConfig.AllowHttp && string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)))
				throw new ArgumentException("The feed root must be https.", nameof(input));
			// Refused at the point a person sets it rather than only at fetch time, so an internal target is an
			// error on the form instead of a run that quietly probed something.
			ExternalFeedDestination.RequireAllowedUrl(uri);
			if (!RmsConnectorCredentialKinds.IsKnown(input.CredentialKind)) throw new ArgumentException("Choose a credential kind: none, bearer or header.", nameof(input));
			if (string.Equals(input.CredentialKind, RmsConnectorCredentialKinds.Header, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(input.CredentialHeaderName))
				throw new ArgumentException("A header credential needs the header name.", nameof(input));
			var profile = string.IsNullOrWhiteSpace(input.ProfileKey) ? provider.DefaultProfileKey : input.ProfileKey.Trim().ToLowerInvariant();
			if (!RmsDeploymentProfiles.IsKnown(profile)) throw new ArgumentException($"'{input.ProfileKey}' is not a deployment profile.", nameof(input));

			var previousBase = connector.BaseUrl;
			connector.ProviderKey = provider.Key;
			connector.Name = input.Name.Trim();
			connector.SourceSystem = Trim(input.SourceSystem, 200) ?? provider.Key;
			connector.SourceScheme = string.IsNullOrWhiteSpace(input.SourceScheme) ? provider.DefaultScheme : input.SourceScheme.Trim().ToLowerInvariant();
			connector.ProfileKey = profile;
			connector.BaseUrl = baseUrl;
			connector.CredentialKind = input.CredentialKind.Trim().ToLowerInvariant();
			connector.CredentialHeaderName = Trim(input.CredentialHeaderName, 100);
			connector.ReadEnabled = input.ReadEnabled;
			connector.WriteEnabled = false;
			connector.PollIntervalMinutes = Math.Max(RecordsConnectorConfig.MinPollIntervalMinutes, input.PollIntervalMinutes);
			connector.MaxRequestsPerHour = input.MaxRequestsPerHour > 0 ? Math.Min(600, input.MaxRequestsPerHour) : RecordsConnectorConfig.DefaultMaxRequestsPerHour;
			connector.TermsReference = Trim(input.TermsReference, 500);
			// Terms are acknowledged for a source; pointing the connector somewhere else needs a fresh acknowledgement.
			if (previousBase != null && !string.Equals(previousBase, baseUrl, StringComparison.OrdinalIgnoreCase))
			{
				connector.TermsAcknowledgedOn = null;
				connector.TermsAcknowledgedByUserId = null;
			}
			connector.ModifiedByUserId = userId;
		}

		private static bool RequiresCredential(RmsExternalOrderConnector connector) => !string.Equals(connector.CredentialKind, RmsConnectorCredentialKinds.None, StringComparison.OrdinalIgnoreCase);

		private async Task SaveAsync(RmsExternalOrderConnector connector, string userId, CancellationToken cancellationToken)
		{
			connector.ModifiedOn = DateTime.UtcNow;
			connector.ModifiedByUserId = userId;
			connector.RowVersion += 1;
			await _connectors.UpdateAsync(connector, cancellationToken, true);
		}

		/// <summary>The stored ciphertext and token hash never leave the service. A copy is scrubbed, never the row itself.</summary>
		private static RmsExternalOrderConnector Scrub(RmsExternalOrderConnector connector)
		{
			if (connector == null) return null;
			var copy = JsonConvert.DeserializeObject<RmsExternalOrderConnector>(JsonConvert.SerializeObject(connector));
			copy.CredentialCiphertext = string.IsNullOrEmpty(connector.CredentialCiphertext) ? null : "stored";
			copy.InboundTokenHash = string.IsNullOrEmpty(connector.InboundTokenHash) ? null : "set";
			return copy;
		}

		private Task AuditAsync(int departmentId, string userId, RmsExternalOrderConnector connector, string purpose, CancellationToken cancellationToken, object detail = null)
			=> _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId, Action = (int)RmsAccessAuditAction.Admin, ActorUserId = userId, Purpose = purpose, OriginClient = (int)RmsOriginClient.System, Successful = true,
				OccurredOn = DateTime.UtcNow, CorrelationId = connector.RmsExternalOrderConnectorId, DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);

		private static string NewInboundToken()
		{
			var bytes = new byte[32];
			RandomNumberGenerator.Fill(bytes);
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		public static string Hash(string token)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? string.Empty))).ToLowerInvariant();
		}

		private static bool FixedTimeEquals(string left, string right)
			=> left != null && right != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

		private static string Trim(string value, int max) => string.IsNullOrWhiteSpace(value) ? null : (value.Trim().Length > max ? value.Trim().Substring(0, max) : value.Trim());

		#endregion
	}
}
