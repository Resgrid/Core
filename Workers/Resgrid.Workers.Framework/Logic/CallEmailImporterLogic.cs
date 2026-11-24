using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Identity;

namespace Resgrid.Workers.Framework.Logic
{
	public class CallEmailImporterLogic
	{
		/// <summary>How long one attempt waits for another email with the same CAD call ID, and how many attempts an email gets.</summary>
		private static readonly TimeSpan BusyLockTimeout = TimeSpan.FromSeconds(30);
		private const int BusyAttempts = 3;

		/// <summary>
		/// Whether an imported (dispatched, not pending) call pages anyone: members, or units the format resolved (a unit-only page such
		/// as "Units: Engine 4" is a real dispatch). A call with neither is not created, as before.
		/// </summary>
		public static bool HasRecipients(Call call)
		{
			return call != null && ((call.Dispatches != null && call.Dispatches.Any()) || (call.UnitDispatches != null && call.UnitDispatches.Any()));
		}

		/// <summary>One mailbox delivery: its message id, else (a server that gives none) the call it reads as.</summary>
		public static string DeliveryKey(CallEmail email, Call parsed)
		{
			if (!String.IsNullOrWhiteSpace(email?.MessageId))
				return "id\n" + email.MessageId.Trim();

			return "text\n" + (parsed?.Name ?? string.Empty) + "\n" + (parsed?.NatureOfCall ?? string.Empty);
		}

		public async Task<Tuple<bool, string>> Process(CallEmailQueueItem item)
		{
			bool success = true;
			string result = "";

			// Own scope per item: root-scope services would share the process-wide root unit of work.
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			var _callEmailProvider = scope.Resolve<ICallEmailProvider>();

			if (!String.IsNullOrWhiteSpace(item?.EmailSettings?.Hostname))
			{
				// ADP department operation lock: email-call ingestion is deferred while the department
				// is locked (plan section 20.2). The check MUST run before touching the mail server —
				// the POP fetch removes messages from the mailbox, so deferring here leaves them
				// queued server-side to be ingested after the lock releases.
				if (await DepartmentLockGuard.IsDepartmentLockedAsync(item.EmailSettings.DepartmentId))
					return new Tuple<bool, string>(true, $"deferred: department {item.EmailSettings.DepartmentId} is locked");

				CallEmailsResult emailResult = _callEmailProvider.GetAllCallEmailsFromServer(item.EmailSettings);

				if (emailResult?.Emails != null && emailResult.Emails.Count > 0)
				{
					var _callsService = scope.Resolve<ICallsService>();
					var _queueService = scope.Resolve<IQueueService>();
					var _departmentsService = scope.Resolve<IDepartmentsService>();
					var _userProfileService = scope.Resolve<IUserProfileService>();
					var _departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();
					var _unitsService = scope.Resolve<IUnitsService>();

					// Ran into an issue where the department users didn't come back. We can't put the email back in the POP
					// email box so just added some simple retry logic here.
					List<IdentityUser> departmentUsers = await _departmentsService.GetAllUsersForDepartmentAsync(item.EmailSettings.DepartmentId, true);
					var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(item.EmailSettings.DepartmentId);

					int retry = 0;
					while (retry < 3 && departmentUsers == null)
					{
						Thread.Sleep(150);
						departmentUsers =await  _departmentsService.GetAllUsersForDepartmentAsync(item.EmailSettings.DepartmentId, true);
						retry++;
					}

					var units = await _unitsService.GetUnitsForDepartmentAsync(item.EmailSettings.Department.DepartmentId);
					var callTypes = await _callsService.GetCallTypesForDepartmentAsync(item.EmailSettings.Department.DepartmentId);
					var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(item.EmailSettings.Department.DepartmentId);
					// The default priority, else the department's High priority, else the built-in High (CallPriorityMatch).
					int defaultPriority = CallPriorityMatch.DefaultForImport(priorities);
					var departmentTextNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.EmailSettings.DepartmentId);

					// The department's email call mode (D10): Pending / EnhancedAi hold each call for a dispatcher instead of paging. The
					// emails are already off the mail server, so anything going wrong here falls back to dispatching them as before.
					IDispatcherEmailCallService emailCallService = null;
					var emailCallMode = DispatcherEmailCallModes.AutoDispatch;
					try
					{
						emailCallService = scope.Resolve<IDispatcherEmailCallService>();
						emailCallMode = await emailCallService.GetModeAsync(item.EmailSettings.DepartmentId);
					}
					catch (Exception modeEx)
					{
						Logging.LogException(modeEx);
					}

					var holdAsPending = emailCallService != null && emailCallMode != DispatcherEmailCallModes.AutoDispatch;
					var enrich = emailCallService != null
						? await emailCallService.ShouldEnrichAsync(item.EmailSettings.DepartmentId, item.EmailSettings.FormatType, emailCallMode)
						: item.EmailSettings.FormatType == (int)CallEmailTypes.AI;

					// A new call is saved and dispatched as the importer always did; the CAD update engine decides whether one is needed.
					async Task<Call> createImportedCall(Call call, string sender)
					{
						// Adding this in here to try and fix the error below with ObjectContext issues.
						var newCall = CreateNewCallFromCall(call);

						if (holdAsPending)
						{
							// A dispatcher picks the resources of a pending call, so one without a dispatch list is kept too.
							emailCallService.HoldAsPending(newCall);
							var pendingCall = await _callsService.SaveCallAsync(newCall);
							await emailCallService.OnPendingCallSavedAsync(pendingCall, sender, call.Name);

							if (enrich)
								await QueueImportedCallEnrichmentAsync(scope, _queueService, pendingCall, sender, heldForDispatcher: true);

							return pendingCall;
						}

						if (!HasRecipients(newCall))
						{
							// As the importer always did: a call that would page nobody is not created (the format resolved no one).
							Logging.LogInfo($"Imported email for department {item.EmailSettings.DepartmentId}: the format resolved no members or units; no call was created.");
							return null;
						}

						var savedCall = await _callsService.SaveCallAsync(newCall);

						// Run card auto-dispatch for imported email calls.
						try
						{
							var featureToggleService = scope.Resolve<IFeatureToggleService>();
							if (await featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, savedCall.DepartmentId))
							{
								var dispatchRecommendationService = scope.Resolve<IDispatchRecommendationService>();
								var recommendation = await dispatchRecommendationService.EnrichCallForDispatchAsync(savedCall, 1, true);

								if (recommendation.MatchedRunCardId.HasValue && recommendation.AutoDispatch && recommendation.HasRecommendations)
								{
									savedCall = await _callsService.SaveCallAsync(savedCall);
									await dispatchRecommendationService.RecordActivationAsync(savedCall, recommendation, null);
								}
							}
						}
						catch (Exception recEx)
						{
							// A recommendation failure must never block the imported call's dispatch.
							Logging.LogException(recEx);
						}

						var cqi = new CallQueueItem();
						cqi.Call = savedCall;

						cqi.Profiles = profiles.Values.ToList();
						cqi.DepartmentTextNumber = departmentTextNumber;

						await _queueService.EnqueueCallBroadcastAsync(cqi);

						// AI dispatch, Enrich mode: enrichment follows the normal dispatch and can never hold it up.
						if (enrich)
							await QueueImportedCallEnrichmentAsync(scope, _queueService, savedCall, sender);

						return savedCall;
					}

					var emailCallUpdates = scope.Resolve<IEmailCallUpdateService>();

					// The same delivery fetched twice in one pass is imported once: by its message id, else (no id) by what it says.
					var seen = new HashSet<string>(StringComparer.Ordinal);
					foreach (var email in emailResult.Emails)
					{
						try
						{
							var parsed = await emailCallUpdates.ParseAsync(item.EmailSettings.FormatType, email, item.EmailSettings.Department.ManagingUserId,
								departmentUsers, item.EmailSettings.Department, units, defaultPriority, priorities, callTypes);

							// A close or cancel the format does not read as a call still closes the call carrying its CAD call ID, as on the
							// dispatch address; it never creates one. Anything else the format found nothing in is skipped, as before.
							EmailCallUpdateConfig closeOnlyConfig = null;
							if (parsed?.Call == null)
							{
								closeOnlyConfig = await _departmentSettingsService.GetEmailCallUpdateConfigAsync(item.EmailSettings.DepartmentId, item.EmailSettings.FormatType);
								parsed = CadEmailText.CloseOnlyParse(closeOnlyConfig, email, item.EmailSettings.DepartmentId, defaultPriority);
								if (parsed == null)
									continue;
							}

							parsed.Call.DepartmentId = item.EmailSettings.DepartmentId;
							var delivery = DeliveryKey(email, parsed.Call);
							if (seen.Contains(delivery))
								continue;

							// Sender, for the AI dispatch sender allowlist (never put on the bus).
							var sender = string.IsNullOrWhiteSpace(email.From) ? null : email.From;

							// A follow-up email for an open call (same CAD call ID) updates it in place (cad-email-call-updates-plan.md; this
							// path used to copy a matched call into a second, new call).
							var request = new EmailCallUpdateRequest
							{
								DepartmentId = item.EmailSettings.DepartmentId,
								Department = item.EmailSettings.Department,
								FormatType = item.EmailSettings.FormatType,
								Email = email,
								Parsed = parsed,
								Source = CallEmailUpdateSources.MailboxImport,
								Config = closeOnlyConfig,
								LockTimeout = BusyLockTimeout
							};

							Func<Call, Task<Call>> create = closeOnlyConfig == null ? call => createImportedCall(call, sender) : call => Task.FromResult<Call>(null);
							var update = await emailCallUpdates.ProcessAsync(request, create);

							// Another email for the same CAD call held the lock: it is handling that incident, so this one never becomes a second
							// call (that would page the incident twice). The message is already off the mail server, so it is tried again here.
							for (var attempt = 1; update.Outcome == CallEmailUpdateOutcomes.Busy && attempt < BusyAttempts; attempt++)
								update = await emailCallUpdates.ProcessAsync(request, create);

							if (update.Outcome == CallEmailUpdateOutcomes.Busy)
							{
								success = false;
								result = $"CAD call {update.CadCallId} stayed locked; the email from the mailbox was not applied.";
								Logging.LogError($"Imported email for department {item.EmailSettings.DepartmentId}: CAD call {update.CadCallId} stayed locked after {BusyAttempts} attempts; the email was not applied.");
								continue;
							}

							seen.Add(delivery);
						}
						catch (Exception ex)
						{
							// The rest of the mailbox is still imported; the batch reports the failure.
							success = false;
							result = ex.ToString();
							Logging.LogException(ex);
						}
					}

					await _departmentsService.SaveDepartmentEmailSettingsAsync(emailResult.EmailSettings);
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		// heldForDispatcher: the dispatcher desk's email call mode held the call pending, so the worker enriches it while it waits.
		private static async Task QueueImportedCallEnrichmentAsync(ILifetimeScope scope, IQueueService queueService, Call savedCall, string sender, bool heldForDispatcher = false)
		{
			if (!Config.AiDispatchConfig.EnrichEnabled || savedCall == null || savedCall.CallId <= 0)
				return;

			try
			{
				var aiDispatchAdmin = scope.Resolve<Resgrid.Model.AiDispatch.IAiDispatchAdminService>();
				await queueService.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
				{
					DepartmentId = savedCall.DepartmentId, CallId = savedCall.CallId, Channel = 3, QueuedOnUtc = DateTime.UtcNow,
					SenderNotAllowed = !await aiDispatchAdmin.IsSenderAllowedAsync(savedCall.DepartmentId, sender),
					HeldForDispatcher = heldForDispatcher
				});
			}
			catch (Exception aiEx)
			{
				Logging.LogException(aiEx);
			}
		}

		/// <summary>A fresh copy of the parsed call to save (no row instance is shared with the parse): its members, units and attachments.</summary>
		public static Call CreateNewCallFromCall(Call call)
		{
			Call newCall = new Call();
			newCall.Number = call.Number;
			newCall.DepartmentId = call.DepartmentId;
			newCall.ReportingUserId = call.ReportingUserId;
			newCall.Priority = call.Priority;
			newCall.IsCritical = call.IsCritical;
			newCall.Type = call.Type;
			newCall.IncidentNumber = call.IncidentNumber;
			newCall.ExternalIdentifier = call.ExternalIdentifier;
			newCall.ReferenceNumber = call.ReferenceNumber;
			newCall.CallTypeId = call.CallTypeId;
			newCall.W3W = call.W3W;
			newCall.Name = call.Name;
			newCall.NatureOfCall = call.NatureOfCall;
			newCall.MapPage = call.MapPage;
			newCall.Notes = call.Notes;
			newCall.CompletedNotes = call.CompletedNotes;
			newCall.Address = call.Address;
			newCall.GeoLocationData = call.GeoLocationData;
			newCall.LoggedOn = call.LoggedOn;
			newCall.ClosedByUserId = call.ClosedByUserId;
			newCall.ClosedOn = call.ClosedOn;
			newCall.State = call.State;
			newCall.IsDeleted = call.IsDeleted;
			newCall.CallSource = call.CallSource;
			newCall.SourceIdentifier = call.SourceIdentifier;
			newCall.State = call.State;

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				newCall.Dispatches = new List<CallDispatch>();

				foreach (var callDispatch in call.Dispatches)
				{
					var cd = new CallDispatch();
					cd.UserId = callDispatch.UserId;

					newCall.Dispatches.Add(cd);
				}
			}

			// Units the format resolved (a Labeled Fields "Units: Engine 4", Parkland's units) stay assigned on the imported call.
			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				newCall.UnitDispatches = new List<CallDispatchUnit>();

				foreach (var unitDispatch in call.UnitDispatches)
				{
					if (newCall.UnitDispatches.Any(x => x.UnitId == unitDispatch.UnitId))
						continue;

					newCall.UnitDispatches.Add(new CallDispatchUnit { UnitId = unitDispatch.UnitId, LastDispatchedOn = unitDispatch.LastDispatchedOn });
				}
			}

			if (call.Attachments != null && call.Attachments.Any())
			{
				newCall.Attachments = new List<CallAttachment>();

				foreach (var callAttahment in call.Attachments)
				{
					var ca = new CallAttachment();
					ca.CallAttachmentType = callAttahment.CallAttachmentType;
					ca.Data = callAttahment.Data;
					ca.FileName = callAttahment.FileName;

					newCall.Attachments.Add(ca);
				}
			}

			return newCall;
		}
	}
}
