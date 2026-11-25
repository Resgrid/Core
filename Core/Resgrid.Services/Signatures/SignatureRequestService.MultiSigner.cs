using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// e-Signature S3: multi-signer ordered requests and the evaluator platform, on S2's SignatureRequests/SignatureRequestSigners.
	/// <list type="bullet">
	/// <item>A request has steps. Ordered requests sign in ascending step order and steps with the same order are a parallel group;
	/// an unordered request lets every step sign at once.</item>
	/// <item>Member steps (member, witness, certifying official, evaluator, recipient) are signed in Resgrid at the step's level —
	/// L1, or L2 (a fresh step-up or a passkey bound to the request's hash), never below the role's floor — through the shared
	/// <see cref="Model.Services.ISignatureService"/>. External steps are L4: a Resgrid link where the owning add-on is active
	/// (decision 19), or in person on a member's device.</item>
	/// <item>Every signature binds to the request's artifact hash (the subject's revision hash when it was requested); a subject
	/// that moved on cannot be signed. Completion seals the evidence at L3 through S1 (fails closed: "sealing pending").</item>
	/// <item>Members are told when it is their turn and reminded by worker 90; the requester and member signers are told when the
	/// request closes. Workflow events 220-223 are reused, their payload without any contact detail.</item>
	/// </list>
	/// </summary>
	public partial class SignatureRequestService
	{
		/// <summary>The statement version of a subject kind that has no external-signer use of its own.</summary>
		public const string MultiSignerStatementVersion = "multi-signer/1";

		#region Create

		public async Task<SignatureRequestResult> CreateMultiSignerAsync(MultiSignerRequestInput input, CancellationToken cancellationToken = default)
		{
			var invalid = SignatureOrdering.Validate(input);
			if (invalid != SignatureRequestRefusals.None)
				return SignatureRequestResult.Refused(invalid);

			var info = await DescribeAsync(input.DepartmentId, input.SubjectKind, input.SubjectId, input.RequestedByUserId);
			if (info == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.SubjectNotFound);

			var steps = input.Steps;
			var externals = steps.Where(s => s.Kind == SignatureSignerKinds.External).ToList();
			var remote = externals.Count > 0 && input.ExternalChannel == SignatureRequestChannels.Remote;
			if (remote)
			{
				var refusal = await CheckRemoteAsync(input.DepartmentId, input.SubjectKind);
				if (refusal != SignatureRequestRefusals.None)
					return SignatureRequestResult.Refused(refusal);
				foreach (var step in externals)
				{
					var needs = step.OtpChannel == SignatureSignerAuthMethods.SmsOtp ? NormalizePhone(step.ExternalPhone) : NormalizeEmail(step.ExternalEmail);
					if (needs == null)
						return SignatureRequestResult.Refused(SignatureRequestRefusals.ContactRequired);
				}
			}

			// Named members are active members of the department and, where the step carries a rule, eligible now.
			var members = steps.Where(s => s.Kind == SignatureSignerKinds.Member && !string.IsNullOrWhiteSpace(s.UserId)).ToList();
			if (members.Count > 0)
			{
				var active = await _departments.GetActiveMemberUserIdsAsync(input.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var activeIds = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
				foreach (var step in members)
				{
					if (!activeIds.Contains(step.UserId.Trim()))
						return SignatureRequestResult.Refused(SignatureRequestRefusals.NotASigner);
					if (step.Eligibility != null && !step.Eligibility.IsEmpty && !await IsEligibleAsync(input.DepartmentId, step.UserId.Trim(), step.Eligibility))
						return SignatureRequestResult.Refused(SignatureRequestRefusals.NotEligible);
				}
			}

			var use = ExternalSignatureUse.For(input.SubjectKind);
			var now = DateTime.UtcNow;
			var request = await _requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = input.DepartmentId,
				SubjectKind = (int)input.SubjectKind,
				SubjectId = Clean(input.SubjectId, 128),
				RevisionId = Clean(info.RevisionId, 128),
				Title = Clean(info.Title, 256),
				ArtifactSha256 = info.RevisionSha256.ToLowerInvariant(),
				RequiredLevel = externals.Count > 0 ? (int)SignatureLevels.External
					: steps.Any(s => (int)SignatureOrdering.RoleFloor(s.Role) >= (int)SignatureLevels.StepUp || s.Level >= SignatureLevels.StepUp) ? (int)SignatureLevels.StepUp : (int)SignatureLevels.Attest,
				Ordered = input.Ordered,
				Status = remote ? (int)SignatureRequestStatuses.Draft : (int)SignatureRequestStatuses.Sent,
				Channel = remote ? (int)SignatureRequestChannels.Remote : (int)SignatureRequestChannels.InPerson,
				ExpiresOn = now.AddDays(Math.Clamp(input.ExpiresInDays <= 0 ? ESignConfig.LinkExpiryDays : input.ExpiresInDays, 1, 90)),
				ProviderKind = remote ? (int)ESignProviderKinds.Documenso : (int)ESignProviderKinds.None,
				StatementVersion = use?.StatementVersion ?? MultiSignerStatementVersion,
				StatementText = Clean(Statement(input.SubjectKind, info, input.Culture), 2000),
				CreatedByUserId = input.RequestedByUserId,
				CreatedOn = now
			}, cancellationToken, true);

			var signers = new List<SignatureRequestSigner>();
			foreach (var step in steps.OrderBy(s => s.Order))
			{
				var member = step.Kind == SignatureSignerKinds.Member;
				string email = null, phone = null;
				if (!member && remote)
				{
					email = NormalizeEmail(step.ExternalEmail);
					phone = NormalizePhone(step.ExternalPhone);
				}

				var signer = new SignatureRequestSigner
				{
					SignatureRequestId = request.SignatureRequestId,
					DepartmentId = input.DepartmentId,
					SignerOrder = input.Ordered ? step.Order : 1,
					Role = (int)step.Role,
					SignerKind = (int)step.Kind,
					UserId = member ? Clean(step.UserId, 128) : null,
					EligibilityJson = member ? step.Eligibility?.ToJson() : null,
					StepLabel = Clean(step.StepLabel, 200),
					ExternalName = member ? null : Clean(step.ExternalName, 200),
					ExternalEmailEncrypted = email == null ? null : _encryption.Encrypt(email),
					ExternalPhoneEncrypted = phone == null ? null : _encryption.Encrypt(phone),
					ContactMasked = !member && remote ? (step.OtpChannel == SignatureSignerAuthMethods.SmsOtp ? SignatureTokens.MaskPhone(phone) : SignatureTokens.MaskEmail(email)) : null,
					Status = (int)SignatureSignerStatuses.Pending,
					CreatedOn = now
				};
				signer.RequiredLevel = member ? (int)SignatureOrdering.StepLevel(new SignatureRequestSigner { SignerKind = signer.SignerKind, Role = signer.Role, RequiredLevel = (int)step.Level })
					: (int)SignatureLevels.External;
				signer.AuthMethod = member
					? (signer.RequiredLevel >= (int)SignatureLevels.StepUp ? (int)SignatureSignerAuthMethods.StepUp : (int)SignatureSignerAuthMethods.Session)
					: remote ? (int)(step.OtpChannel == SignatureSignerAuthMethods.SmsOtp ? SignatureSignerAuthMethods.SmsOtp : SignatureSignerAuthMethods.EmailOtp) : (int)SignatureSignerAuthMethods.InPerson;
				signers.Add(await _signers.InsertAsync(signer, cancellationToken, true));
			}

			if (remote)
			{
				try
				{
					var token = await _accounts.GetApiTokenAsync(input.DepartmentId);
					var pdf = RenderSigningInput(info, request, input.Culture, out var signaturePage);
					var remoteSigners = signers.Where(s => !s.IsMember).ToList();
					var envelope = await _provider.CreateEnvelopeAsync(token, new ESignEnvelopeRequest
					{
						Title = request.Title,
						ExternalId = "resgrid:" + request.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + request.SignatureRequestId.ToString(CultureInfo.InvariantCulture),
						Pdf = pdf,
						FileName = FileNameFor(request),
						Ordered = request.Ordered,
						SignaturePage = signaturePage,
						Language = LanguageOf(input.Culture),
						Recipients = remoteSigners.Select(s => new ESignRecipient
						{
							Reference = s.SignatureRequestSignerId.ToString(CultureInfo.InvariantCulture),
							Name = s.ExternalName,
							Email = Decrypt(s.ExternalEmailEncrypted),
							SigningOrder = s.SignerOrder
						}).ToList()
					}, cancellationToken);

					if (envelope == null || string.IsNullOrWhiteSpace(envelope.EnvelopeId))
						throw new InvalidOperationException("esign_provider_bad_response");
					foreach (var signer in remoteSigners)
					{
						var reference = signer.SignatureRequestSignerId.ToString(CultureInfo.InvariantCulture);
						var recipient = envelope.Recipients?.FirstOrDefault(r => r.Reference == reference);
						if (recipient == null || string.IsNullOrWhiteSpace(recipient.SigningToken))
							throw new InvalidOperationException("esign_provider_bad_response");
						signer.ProviderRecipientId = Clean(recipient.ProviderRecipientId, 128);
						signer.ProviderSigningTokenEncrypted = _encryption.Encrypt(recipient.SigningToken);
						await _signers.UpdateAsync(signer, cancellationToken, true);
					}

					request.ProviderEnvelopeId = Clean(envelope.EnvelopeId, 128);
					request.Status = (int)SignatureRequestStatuses.Sent;
					request.ReconcileAfter = now.AddMinutes(Math.Max(5, ESignConfig.ReconcileIntervalMinutes));
					request.UpdatedOn = now;
					await _requests.UpdateAsync(request, cancellationToken, true);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "The e-sign envelope for a multi-signer request could not be created; the request is voided.");
					request.Status = (int)SignatureRequestStatuses.Voided;
					request.VoidedOn = now;
					request.VoidReason = "esign_provider_failed";
					await _requests.UpdateAsync(request, CancellationToken.None, true);
					return new SignatureRequestResult { Refusal = SignatureRequestRefusals.ProviderFailed, Request = request, Signer = signers.FirstOrDefault() };
				}
			}

			Audit(request, input.RequestedByUserId, AuditLogTypes.SignatureRequestCreated, input.IpAddress, input.UserAgent);
			Publish(request, SignatureRequestChanges.Requested, signers, input.RequestedByUserId);
			var linkToken = await StartTurnsAsync(request, signers, input.Culture, input.RequestedByUserId, cancellationToken);
			return new SignatureRequestResult
			{
				Request = request,
				Signer = SignatureOrdering.OpenSteps(signers, request.Ordered).FirstOrDefault() ?? signers.FirstOrDefault(),
				LinkToken = linkToken
			};
		}

		public async Task<SignatureRequestResult> CreateEvaluationAsync(EvaluationRequestInput input, CancellationToken cancellationToken = default)
		{
			if (input == null || input.DepartmentId <= 0 || string.IsNullOrWhiteSpace(input.SubjectId) || string.IsNullOrWhiteSpace(input.RequestedByUserId))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var steps = new List<SignatureStepInput>();
			if (!string.IsNullOrWhiteSpace(input.ExternalEvaluatorName) || !string.IsNullOrWhiteSpace(input.ExternalEvaluatorEmail))
			{
				// An outside evaluator signs at L4 (remote only where the LMS add-on is active; in person always).
				steps.Add(new SignatureStepInput
				{
					Kind = SignatureSignerKinds.External, Role = SignatureSignerRoles.Evaluator, Order = 1, ExternalName = input.ExternalEvaluatorName,
					ExternalEmail = input.ExternalEvaluatorEmail, ExternalPhone = input.ExternalEvaluatorPhone, OtpChannel = input.OtpChannel
				});
			}
			else
			{
				// A member evaluator signs at L2; without a named evaluator any member the rule makes eligible may sign.
				if (string.IsNullOrWhiteSpace(input.EvaluatorUserId) && (input.Eligibility == null || input.Eligibility.IsEmpty))
					return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
				steps.Add(new SignatureStepInput
				{
					Kind = SignatureSignerKinds.Member, Role = SignatureSignerRoles.Evaluator, Order = 1, UserId = input.EvaluatorUserId,
					Eligibility = input.Eligibility, Level = SignatureLevels.StepUp
				});
			}

			if (!string.IsNullOrWhiteSpace(input.CertifyingOfficialUserId) || (input.CertifyingEligibility != null && !input.CertifyingEligibility.IsEmpty))
				steps.Add(new SignatureStepInput
				{
					Kind = SignatureSignerKinds.Member, Role = SignatureSignerRoles.CertifyingOfficial, Order = 2, UserId = input.CertifyingOfficialUserId,
					Eligibility = input.CertifyingEligibility, Level = SignatureLevels.StepUp
				});

			return await CreateMultiSignerAsync(new MultiSignerRequestInput
			{
				DepartmentId = input.DepartmentId,
				SubjectKind = input.SubjectKind,
				SubjectId = input.SubjectId,
				Ordered = true,
				ExternalChannel = input.ExternalChannel,
				Steps = steps,
				ExpiresInDays = input.ExpiresInDays,
				Culture = input.Culture,
				RequestedByUserId = input.RequestedByUserId,
				IpAddress = input.IpAddress,
				UserAgent = input.UserAgent
			}, cancellationToken);
		}

		public async Task<bool> IsEligibleAsync(int departmentId, string userId, SignerEligibility eligibility)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || eligibility == null || eligibility.IsEmpty || _eligibility == null)
				return false;
			try
			{
				return await _eligibility.IsEligibleAsync(departmentId, userId, eligibility);
			}
			catch (Exception ex)
			{
				// Unknown eligibility is no eligibility.
				Logging.LogException(ex, "Signer eligibility could not be checked.");
				return false;
			}
		}

		#endregion

		#region Member steps

		public async Task<SignatureStepView> GetStepViewAsync(int departmentId, int signatureRequestId, string userId)
		{
			var request = await GetAsync(departmentId, signatureRequestId);
			if (request == null)
				return null;

			var signers = await GetSignersAsync(departmentId, signatureRequestId);
			var info = await DescribeAsync(departmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.CreatedByUserId);
			var view = new SignatureStepView { Request = request, Signers = signers, CurrentRevisionSha256 = info?.RevisionSha256 };

			var (step, refusal) = await MemberStepAsync(request, signers, userId, null);
			view.MyStep = refusal == SignatureRequestRefusals.None ? step : null;
			view.MyWaitingStep = refusal == SignatureRequestRefusals.NotYourTurn ? step : null;
			view.MyLevel = SignatureOrdering.StepLevel(view.MyStep ?? view.MyWaitingStep);
			view.Blocked = !request.IsOpen || request.ExpiresOn <= DateTime.UtcNow ? SignatureRequestRefusals.RequestClosed
				: refusal != SignatureRequestRefusals.None ? refusal
				: info == null ? SignatureRequestRefusals.SubjectNotFound
				: view.RevisionChanged ? SignatureRequestRefusals.RevisionChanged
				: SignatureRequestRefusals.None;
			if (view.Blocked != SignatureRequestRefusals.None)
				view.MyStep = null;

			var mine = step?.SignatureRequestSignerId;
			view.OthersToSign = signers.Where(s => !s.IsDone && s.SignatureRequestSignerId != mine).OrderBy(s => s.SignerOrder).ThenBy(s => s.SignatureRequestSignerId).ToList();
			return view;
		}

		public async Task<SignatureRequestResult> SignStepAsync(SignatureStepSignInput input, CancellationToken cancellationToken = default)
		{
			var userId = input?.Proof?.UserId;
			if (input == null || input.DepartmentId <= 0 || input.SignatureRequestId <= 0 || string.IsNullOrWhiteSpace(userId))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var request = await GetAsync(input.DepartmentId, input.SignatureRequestId);
			if (request == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			if (!request.IsOpen || request.ExpiresOn <= DateTime.UtcNow)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RequestClosed);

			var signers = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
			var (step, refusal) = await MemberStepAsync(request, signers, userId, input.SignatureRequestSignerId);
			if (refusal != SignatureRequestRefusals.None)
				return SignatureRequestResult.Refused(refusal);

			// The signature binds to the request's hash; a subject that moved on is a different document.
			var info = await DescribeAsync(request.DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.CreatedByUserId);
			if (info == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.SubjectNotFound);
			if (!SignatureTokens.HashEquals(info.RevisionSha256, request.ArtifactSha256))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RevisionChanged);

			var level = SignatureOrdering.StepLevel(step);
			var signed = await _memberSignatures.SignAsync(new SignatureRequest
			{
				DepartmentId = request.DepartmentId,
				Use = SignatureUse.ForRequestStep((SignatureSubjectKinds)request.SubjectKind, SignatureOrdering.IntentFor((SignatureSignerRoles)step.Role), level,
					request.StatementVersion ?? MultiSignerStatementVersion),
				SubjectId = request.SubjectId,
				RevisionId = request.RevisionId,
				ArtifactSha256 = request.ArtifactSha256,
				StatementText = request.StatementText,
				DepartmentLevel = (int)level,
				RequirePasskey = input.RequirePasskey,
				SignatureRequestId = request.SignatureRequestId
			}, input.Proof, cancellationToken);

			if (signed == null || !signed.Succeeded)
				return SignatureRequestResult.Refused(signed?.Outcome switch
				{
					SignatureOutcomes.StepUpRequired => SignatureRequestRefusals.StepUpRequired,
					SignatureOutcomes.PasskeyRequired => SignatureRequestRefusals.PasskeyRequired,
					SignatureOutcomes.ArtifactMismatch => SignatureRequestRefusals.ArtifactMismatch,
					_ => SignatureRequestRefusals.InvalidRequest
				});

			var now = DateTime.UtcNow;
			// An "any eligible member" step records who signed it.
			step.UserId ??= userId;
			step.Status = (int)SignatureSignerStatuses.Signed;
			step.SignedOn = now;
			step.SignatureRecordId = signed.Record.SignatureRecordId;
			step.NextReminderOn = null;
			await _signers.UpdateAsync(step, cancellationToken, true);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = request.DepartmentId,
				UserId = userId,
				Type = AuditLogTypes.SignatureStepSigned,
				After = JsonConvert.SerializeObject(new
				{
					request.SignatureRequestId, step.SignatureRequestSignerId, step.SignerOrder, step.Role, request.SubjectKind, request.SubjectId,
					signed.Record.SignatureRecordId, signed.Record.Level, signed.Record.Method, request.ArtifactSha256
				}),
				Successful = true,
				IpAddress = input.Proof.IpAddress,
				UserAgent = input.Proof.UserAgent,
				ServerName = Environment.MachineName
			});

			await AfterSignatureAsync(request, userId, cancellationToken);
			return new SignatureRequestResult { Request = request, Signer = step, Record = signed.Record };
		}

		public async Task<SignatureRequestResult> DeclineStepAsync(int departmentId, int signatureRequestId, int? signatureRequestSignerId, string userId, string reason,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || signatureRequestId <= 0 || string.IsNullOrWhiteSpace(userId))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var request = await GetAsync(departmentId, signatureRequestId);
			if (request == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			if (!request.IsOpen || request.ExpiresOn <= DateTime.UtcNow)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RequestClosed);

			// A step named for the member can be declined. S4: an open ("any eligible member") step whose turn it is can be declined by a
			// member it makes eligible (an approver rejecting with a reason); the step records who declined it. A member who already
			// signed a step of the request cannot decline another.
			var signers = await GetSignersAsync(departmentId, signatureRequestId);
			var step = signers.FirstOrDefault(s => s.IsMember && !s.IsDone && SameUser(s.UserId, userId) &&
				(!signatureRequestSignerId.HasValue || s.SignatureRequestSignerId == signatureRequestSignerId.Value));
			if (step == null && !signers.Any(s => s.IsMember && s.Status == (int)SignatureSignerStatuses.Signed && SameUser(s.UserId, userId)))
			{
				foreach (var open in SignatureOrdering.OpenSteps(signers, request.Ordered).Where(s => s.IsMember && string.IsNullOrWhiteSpace(s.UserId)
					&& !string.IsNullOrWhiteSpace(s.EligibilityJson) && (!signatureRequestSignerId.HasValue || s.SignatureRequestSignerId == signatureRequestSignerId.Value)))
				{
					if (!await IsEligibleAsync(departmentId, userId, SignerEligibility.Parse(open.EligibilityJson)))
						continue;
					step = open;
					step.UserId = userId;
					break;
				}
			}
			if (step == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.NotASigner);

			var now = DateTime.UtcNow;
			var why = Clean(reason, 1000);
			step.Status = (int)SignatureSignerStatuses.Declined;
			step.DeclinedOn = now;
			step.DeclineReason = why;
			step.NextReminderOn = null;
			await _signers.UpdateAsync(step, cancellationToken, true);

			request.Status = (int)SignatureRequestStatuses.Declined;
			request.DeclinedOn = now;
			request.DeclineReason = why;
			request.ReconcileAfter = null;
			request.UpdatedOn = now;
			await _requests.UpdateAsync(request, cancellationToken, true);
			await CloseLinksAsync(request, cancellationToken);
			await DeleteEnvelopeAsync(request);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.SignatureStepDeclined,
				After = JsonConvert.SerializeObject(new { request.SignatureRequestId, step.SignatureRequestSignerId, step.SignerOrder, step.Role, request.SubjectKind, request.SubjectId }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			Audit(request, userId, AuditLogTypes.SignatureRequestDeclined, null, null);
			Publish(request, SignatureRequestChanges.Declined, signers, userId);
			await NotifyClosedAsync(request, signers, userId);
			return new SignatureRequestResult { Request = request, Signer = step };
		}

		public async Task<List<PendingSignatureItem>> GetPendingForUserAsync(int departmentId, string userId)
		{
			var items = new List<PendingSignatureItem>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return items;

			var now = DateTime.UtcNow;
			var steps = (await _signers.GetPendingMemberStepsAsync(departmentId, userId, now, 100) ?? new List<SignatureRequestSigner>())
				.Where(s => s.DepartmentId == departmentId && s.IsMember && SameUser(s.UserId, userId)).ToList();
			if (steps.Count == 0)
				return items;

			var ids = steps.Select(s => s.SignatureRequestId).Distinct().ToList();
			var requests = (await _requests.GetByIdsAsync(departmentId, ids) ?? new List<SignatureRequestRecord>()).Where(r => r.DepartmentId == departmentId)
				.ToDictionary(r => r.SignatureRequestId);
			var all = (await _signers.GetForRequestsAsync(departmentId, ids) ?? new List<SignatureRequestSigner>()).Where(s => s.DepartmentId == departmentId)
				.GroupBy(s => s.SignatureRequestId).ToDictionary(g => g.Key, g => g.ToList());

			foreach (var step in steps)
			{
				if (!requests.TryGetValue(step.SignatureRequestId, out var request) || !request.IsOpen || request.ExpiresOn <= now)
					continue;
				var group = all.TryGetValue(step.SignatureRequestId, out var list) ? list : new List<SignatureRequestSigner> { step };
				if (!SignatureOrdering.IsTurn(step, group, request.Ordered))
					continue;
				items.Add(new PendingSignatureItem
				{
					SignatureRequestId = request.SignatureRequestId,
					SignatureRequestSignerId = step.SignatureRequestSignerId,
					SubjectKind = request.SubjectKind,
					SubjectId = request.SubjectId,
					Title = request.Title,
					Role = step.Role,
					RequiredLevel = (int)SignatureOrdering.StepLevel(step),
					StepLabel = step.StepLabel,
					ExpiresOn = request.ExpiresOn,
					TurnStartedOn = step.TurnStartedOn,
					ContentCode = SignaturePolicy.ShortCode(request.ArtifactSha256)
				});
			}

			return items.OrderBy(i => i.ExpiresOn).ThenBy(i => i.SignatureRequestId).ToList();
		}

		/// <summary>
		/// The member's step in the request: one named for them, or an open ("any eligible member") step they qualify for. A member
		/// signs at most one step of a request (an evaluator cannot also certify their own evaluation).
		/// </summary>
		private async Task<(SignatureRequestSigner Step, SignatureRequestRefusals Refusal)> MemberStepAsync(SignatureRequestRecord request, List<SignatureRequestSigner> signers,
			string userId, int? signerId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return (null, SignatureRequestRefusals.NotASigner);
			if (signers.Any(s => s.IsMember && s.Status == (int)SignatureSignerStatuses.Signed && SameUser(s.UserId, userId)))
				return (null, SignatureRequestRefusals.NotASigner);

			var candidates = signers.Where(s => s.IsMember && !s.IsDone && (!signerId.HasValue || s.SignatureRequestSignerId == signerId.Value)).ToList();
			var named = candidates.Where(s => SameUser(s.UserId, userId)).OrderBy(s => s.SignerOrder).ToList();
			var namedTurn = named.FirstOrDefault(s => SignatureOrdering.IsTurn(s, signers, request.Ordered));
			if (namedTurn != null)
				return (namedTurn, SignatureRequestRefusals.None);

			var open = candidates.Where(s => string.IsNullOrWhiteSpace(s.UserId) && !string.IsNullOrWhiteSpace(s.EligibilityJson)).OrderBy(s => s.SignerOrder).ToList();
			var openTurn = open.Where(s => SignatureOrdering.IsTurn(s, signers, request.Ordered)).ToList();
			foreach (var step in openTurn)
				if (await IsEligibleAsync(request.DepartmentId, userId, SignerEligibility.Parse(step.EligibilityJson)))
					return (step, SignatureRequestRefusals.None);

			if (named.Count > 0)
				return (named[0], SignatureRequestRefusals.NotYourTurn);
			if (openTurn.Count > 0)
				return (null, SignatureRequestRefusals.NotEligible);
			if (open.Count > 0)
			{
				foreach (var step in open)
					if (await IsEligibleAsync(request.DepartmentId, userId, SignerEligibility.Parse(step.EligibilityJson)))
						return (step, SignatureRequestRefusals.NotYourTurn);
				return (null, SignatureRequestRefusals.NotEligible);
			}

			return (null, SignatureRequestRefusals.NotASigner);
		}

		private static bool SameUser(string a, string b) =>
			!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

		#endregion

		#region Offline step capture (Omni)

		/// <summary>
		/// An offline in-person capture for an existing ordered request's external step (Omni outbox): accepted only while the request
		/// is open, it is that step's turn, and both the request and the subject are still at the revision hash the device bound.
		/// </summary>
		private async Task<SignatureRequestResult> CaptureStepOfflineAsync(OfflineSignatureCapture capture, CancellationToken cancellationToken)
		{
			if (capture.DepartmentId <= 0 || capture.SignatureRequestId <= 0 || string.IsNullOrWhiteSpace(capture.CapturedByUserId) || !SignaturePolicy.IsSha256(capture.BoundRevisionSha256))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var request = await GetAsync(capture.DepartmentId, capture.SignatureRequestId.Value);
			if (request == null || request.SubjectKind != (int)capture.SubjectKind || !string.Equals(request.SubjectId, capture.SubjectId, StringComparison.OrdinalIgnoreCase)
				|| request.Channel != (int)SignatureRequestChannels.InPerson)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			if (!request.IsOpen || request.ExpiresOn <= DateTime.UtcNow)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RequestClosed);

			var name = Clean(capture.PrintedName, 200);
			var image = SignatureImage(capture.SignatureImageDataUrl);
			if (string.IsNullOrWhiteSpace(name) || image == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var info = await DescribeAsync(request.DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.CreatedByUserId);
			if (info == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.SubjectNotFound);
			if (!SignatureTokens.HashEquals(capture.BoundRevisionSha256, request.ArtifactSha256) || !SignatureTokens.HashEquals(info.RevisionSha256, request.ArtifactSha256))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RevisionChanged);

			var signers = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
			var (signer, refusal) = ExternalStep(signers, request.Ordered, capture.SignatureRequestSignerId);
			if (signer == null)
				return SignatureRequestResult.Refused(refusal);

			signer.ExternalName = name;
			signer.ConsentTextVersion = SignatureTokens.ConsentTextVersion;
			signer.ConsentedOn = capture.CapturedOnUtc ?? DateTime.UtcNow;
			var record = await RecordCaptureAsync(request, signer, SignatureMethods.InPersonOffline, name, image, capture.WitnessName, capture.WitnessUserId,
				capture.CapturedByUserId, capture.CapturedOnUtc ?? DateTime.UtcNow, capture.DeviceInfo, capture.IpAddress, capture.UserAgent, capture.BoundRevisionSha256, cancellationToken);

			await AfterSignatureAsync(request, capture.CapturedByUserId, cancellationToken);
			return new SignatureRequestResult { Request = request, Signer = signer, Record = record };
		}

		#endregion

		#region Turns, completion and reminders

		/// <summary>
		/// After any signature: complete (and seal) when every step is signed; otherwise the request is partially signed and the next
		/// group's turn starts.
		/// </summary>
		private async Task AfterSignatureAsync(SignatureRequestRecord request, string actingUserId, CancellationToken cancellationToken)
		{
			var signers = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
			if (SignatureOrdering.AllSigned(signers))
			{
				byte[] providerCompleted = null;
				// A request whose externals signed by link keeps the platform's completed document too (the last step was a member's).
				if (request.Channel == (int)SignatureRequestChannels.Remote && !string.IsNullOrWhiteSpace(request.ProviderEnvelopeId) && _provider != null && _provider.IsAvailable)
				{
					try
					{
						var token = await _accounts.GetApiTokenAsync(request.DepartmentId);
						var envelope = string.IsNullOrWhiteSpace(token) ? null : await _provider.GetEnvelopeAsync(token, request.ProviderEnvelopeId, cancellationToken);
						if (envelope?.Completed == true)
							providerCompleted = await StoreProviderDocumentsAsync(request, token, envelope, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, "The e-sign envelope could not be re-fetched at completion; the evidence is sealed without it.");
					}
				}

				await CloseLinksAsync(request, cancellationToken);
				await CompleteAsync(request, actingUserId, cancellationToken, providerCompleted);
				return;
			}

			if (request.Status != (int)SignatureRequestStatuses.PartiallySigned && signers.Any(s => s.Status == (int)SignatureSignerStatuses.Signed))
			{
				request.Status = (int)SignatureRequestStatuses.PartiallySigned;
				request.UpdatedOn = DateTime.UtcNow;
				await _requests.UpdateAsync(request, cancellationToken, true);
			}

			await StartTurnsAsync(request, signers, null, actingUserId, cancellationToken);
		}

		/// <summary>
		/// Starts the turn of every open step that has not started yet: reminders are scheduled, named members are notified (never the
		/// member who is acting right now), and a remote external signer gets their link only now. Returns the last link token issued
		/// (handed to the requester once when the link could not be delivered).
		/// </summary>
		private async Task<string> StartTurnsAsync(SignatureRequestRecord request, List<SignatureRequestSigner> signers, string culture, string actingUserId,
			CancellationToken cancellationToken)
		{
			string undeliveredLink = null;
			var notify = new List<string>();
			var now = DateTime.UtcNow;
			foreach (var signer in SignatureOrdering.OpenSteps(signers, request.Ordered).Where(s => !s.TurnStartedOn.HasValue))
			{
				signer.TurnStartedOn = now;
				signer.NextReminderOn = SignatureReminderPolicy.Next(now, signer.ReminderCount, request.ExpiresOn);
				if (signer.IsMember)
				{
					if (!string.IsNullOrWhiteSpace(signer.UserId) && !SameUser(signer.UserId, actingUserId))
						notify.Add(signer.UserId);
					await _signers.UpdateAsync(signer, cancellationToken, true);
					continue;
				}

				if (request.Channel == (int)SignatureRequestChannels.Remote)
				{
					var token = IssueLink(signer, request);
					await _signers.UpdateAsync(signer, cancellationToken, true);
					if (await SendLinkAsync(request, signer, token, culture))
						signer.LinkSentOn = DateTime.UtcNow;
					else
						undeliveredLink = token;
				}

				await _signers.UpdateAsync(signer, cancellationToken, true);
			}

			if (notify.Count > 0)
				await NotifyStepReadyAsync(request, notify, false, cancellationToken);
			return undeliveredLink;
		}

		public async Task<int> RemindDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var reminded = 0;
			foreach (var due in await _signers.GetDueRemindersAsync(utcNow, Math.Max(1, batchSize)) ?? new List<SignatureRequestSigner>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var request = await GetAsync(due.DepartmentId, due.SignatureRequestId);
					if (request == null || !request.IsOpen || request.ExpiresOn <= utcNow)
						continue;
					var signers = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
					var signer = signers.FirstOrDefault(s => s.SignatureRequestSignerId == due.SignatureRequestSignerId) ?? due;

					// Claimed first: the reminder count moves on in one conditional update, so two hosts never both send it.
					var next = signer.IsDone || !SignatureOrdering.IsTurn(signer, signers, request.Ordered) ? null
						: SignatureReminderPolicy.Next(utcNow, signer.ReminderCount + 1, request.ExpiresOn);
					if (!await _signers.ClaimReminderAsync(signer.SignatureRequestSignerId, signer.ReminderCount, next, utcNow, cancellationToken))
						continue;
					signer.ReminderCount++;
					signer.LastRemindedOn = utcNow;
					signer.NextReminderOn = next;
					if (signer.IsDone || !SignatureOrdering.IsTurn(signer, signers, request.Ordered))
						continue;

					if (signer.IsMember && !string.IsNullOrWhiteSpace(signer.UserId))
						await NotifyStepReadyAsync(request, new[] { signer.UserId }, true, cancellationToken);
					else if (!signer.IsMember && request.Channel == (int)SignatureRequestChannels.Remote && signer.Status == (int)SignatureSignerStatuses.Pending)
					{
						// A link never opened is sent again (a fresh single-use link; the old one stops working).
						var token = IssueLink(signer, request);
						await _signers.UpdateAsync(signer, cancellationToken, true);
						if (await SendLinkAsync(request, signer, token, null))
						{
							signer.LinkSentOn = DateTime.UtcNow;
							await _signers.UpdateAsync(signer, cancellationToken, true);
						}
					}
					else if (!string.IsNullOrWhiteSpace(request.CreatedByUserId))
						// An in-person signer or an open step: the requester is reminded to collect it.
						await NotifyStepReadyAsync(request, new[] { request.CreatedByUserId }, true, cancellationToken);
					reminded++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A signature reminder could not be sent.");
				}
			}

			return reminded;
		}

		private async Task NotifyStepReadyAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, bool reminder, CancellationToken cancellationToken)
		{
			if (_notifications == null || userIds == null || userIds.Count == 0)
				return;
			try
			{
				await _notifications.NotifyStepReadyAsync(request, userIds, reminder, cancellationToken);
			}
			catch (Exception ex)
			{
				// A notification failing never undoes a signature or a turn.
				Logging.LogException(ex, "A signature step notification could not be sent.");
			}
		}

		/// <summary>The requester and the member signers (except the member acting) learn that the request closed.</summary>
		private async Task NotifyClosedAsync(SignatureRequestRecord request, IEnumerable<SignatureRequestSigner> signers, string actingUserId)
		{
			if (_notifications == null || request == null)
				return;
			var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var members = (signers ?? Enumerable.Empty<SignatureRequestSigner>()).Where(s => s.IsMember && !string.IsNullOrWhiteSpace(s.UserId)).ToList();
			// A one-signer S2 request (no member step) keeps S2's behaviour: no member notification.
			if (members.Count == 0)
				return;
			if (!string.IsNullOrWhiteSpace(request.CreatedByUserId))
				recipients.Add(request.CreatedByUserId);
			foreach (var member in members)
				recipients.Add(member.UserId);
			if (!string.IsNullOrWhiteSpace(actingUserId))
				recipients.Remove(actingUserId);
			if (recipients.Count == 0)
				return;
			try
			{
				await _notifications.NotifyClosedAsync(request, recipients.ToList(), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A signature request closing notification could not be sent.");
			}
		}

		#endregion
	}
}
