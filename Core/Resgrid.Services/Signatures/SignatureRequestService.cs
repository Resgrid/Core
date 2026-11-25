using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Localization.Areas.User.SystemMessages;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// L4 external signers (e-Signature S2). Remote signing goes through Documenso CE behind the Resgrid signing page (consent and a
	/// Resgrid email/SMS one-time code before the embedded signer loads); in-person signing is native (drawn signature + printed
	/// name, optional witness) and needs no platform. Every signature binds to the subject's revision hash; a completed request is
	/// sealed at L3 with the Resgrid audit page. Webhooks are hints only: the envelope is re-fetched and only that state counts.
	/// </summary>
	public partial class SignatureRequestService : ISignatureRequestService
	{
		public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);
		public const int InPersonExpiryHours = 24;
		public const int MaxSignatureImageBytes = 512 * 1024;
		public const int CodeMinutes = 10;

		private readonly ISignatureRequestsRepository _requests;
		private readonly ISignatureRequestSignersRepository _signers;
		private readonly ISignatureCapturesRepository _captures;
		private readonly ISignatureRecordsRepository _records;
		private readonly IESignWebhookEventsRepository _webhookEvents;
		private readonly IRmsSignaturesRepository _rmsSignatures;
		private readonly IEnumerable<ISignatureSubjectSource> _sources;
		private readonly IESignProvider _provider;
		private readonly IESignAccountService _accounts;
		private readonly IDocumentSealingService _sealing;
		private readonly IPdfProvider _pdf;
		private readonly IEncryptionService _encryption;
		private readonly IEmailSender _emailSender;
		private readonly ITextMessageProvider _textMessages;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IBusinessOperationsAccessService _businessOperations;
		private readonly IReadinessAccessService _readiness;
		private readonly IIncidentBackOfficeAccessService _incidentBackOffice;
		private readonly ILmsAccessService _lms;
		private readonly ICacheProvider _cache;
		private readonly IEventAggregator _eventAggregator;
		private readonly ISignatureService _memberSignatures;
		private readonly ISignerEligibilityService _eligibility;
		private readonly ISignatureNotificationService _notifications;

		public SignatureRequestService(ISignatureRequestsRepository requests, ISignatureRequestSignersRepository signers, ISignatureCapturesRepository captures,
			ISignatureRecordsRepository records, IESignWebhookEventsRepository webhookEvents, IRmsSignaturesRepository rmsSignatures,
			IEnumerable<ISignatureSubjectSource> sources, IESignProvider provider, IESignAccountService accounts, IDocumentSealingService sealing, IPdfProvider pdf,
			IEncryptionService encryption, IEmailSender emailSender, ITextMessageProvider textMessages, IDepartmentsService departments,
			IDepartmentSettingsService departmentSettings, IBusinessOperationsAccessService businessOperations, IReadinessAccessService readiness,
			IIncidentBackOfficeAccessService incidentBackOffice, ILmsAccessService lms, ICacheProvider cache, IEventAggregator eventAggregator,
			ISignatureService memberSignatures, ISignerEligibilityService eligibility, ISignatureNotificationService notifications)
		{
			_memberSignatures = memberSignatures;
			_eligibility = eligibility;
			_notifications = notifications;
			_requests = requests;
			_signers = signers;
			_captures = captures;
			_records = records;
			_webhookEvents = webhookEvents;
			_rmsSignatures = rmsSignatures;
			_sources = sources ?? Enumerable.Empty<ISignatureSubjectSource>();
			_provider = provider;
			_accounts = accounts;
			_sealing = sealing;
			_pdf = pdf;
			_encryption = encryption;
			_emailSender = emailSender;
			_textMessages = textMessages;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_businessOperations = businessOperations;
			_readiness = readiness;
			_incidentBackOffice = incidentBackOffice;
			_lms = lms;
			_cache = cache;
			_eventAggregator = eventAggregator;
		}

		#region Subjects and packaging

		private ISignatureSubjectSource SourceFor(SignatureSubjectKinds kind) => _sources.FirstOrDefault(s => s.Kind == kind);

		public async Task<SignatureSubjectInfo> DescribeSubjectAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId)
		{
			return await DescribeAsync(departmentId, kind, subjectId, userId);
		}

		private async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId)
		{
			var source = SourceFor(kind);
			if (source == null || departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return null;
			try
			{
				var info = await source.DescribeAsync(departmentId, subjectId, userId);
				return info != null && SignaturePolicy.IsSha256(info.RevisionSha256) ? info : null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A signature subject could not be read.");
				return null;
			}
		}

		public async Task<SignatureRequestRefusals> CheckRemoteAsync(int departmentId, SignatureSubjectKinds kind)
		{
			var use = ExternalSignatureUse.For(kind);
			if (use == null || departmentId <= 0)
				return SignatureRequestRefusals.InvalidRequest;

			// Decision 19: remote L4 is part of the paid add-on that owns the document; no owning add-on, no remote signing.
			if (use.OwningAddon == null || !await AddonActiveAsync(departmentId, use.OwningAddon.Value))
				return SignatureRequestRefusals.RemoteNeedsAddon;

			if (_provider == null || !_provider.IsAvailable)
				return SignatureRequestRefusals.ProviderUnavailable;

			var account = await _accounts.GetAsync(departmentId);
			if (account == null || !account.IsActive || string.IsNullOrWhiteSpace(account.ApiTokenEncrypted))
				return SignatureRequestRefusals.ProviderUnavailable;

			return SignatureRequestRefusals.None;
		}

		/// <summary>The owning add-on's own access service (fails closed: any error is "not active").</summary>
		private async Task<bool> AddonActiveAsync(int departmentId, PlanAddonTypes addon)
		{
			try
			{
				switch (addon)
				{
					case PlanAddonTypes.BusinessOperations:
						return await _businessOperations.HasActiveAddonAsync(departmentId);
					case PlanAddonTypes.ReadinessPro:
						return await _readiness.CanUseMaintenanceAsync(departmentId);
					case PlanAddonTypes.IncidentBackOffice:
						return await _incidentBackOffice.HasActiveAddonAsync(departmentId);
					case PlanAddonTypes.Lms:
						return await _lms.GetActiveAddonStateAsync(departmentId) == true;
					default:
						return false;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An add-on state could not be read for external signing; remote signing stays off.");
				return false;
			}
		}

		#endregion

		#region Requests

		public async Task<SignatureRequestResult> CreateAsync(ExternalSignatureRequestInput input, CancellationToken cancellationToken = default)
		{
			var use = input == null ? null : ExternalSignatureUse.For(input.SubjectKind);
			if (use == null || input.DepartmentId <= 0 || string.IsNullOrWhiteSpace(input.SubjectId) || string.IsNullOrWhiteSpace(input.RequestedByUserId))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var remote = input.Channel == SignatureRequestChannels.Remote;
			var name = Clean(input.SignerName, 200);
			if (remote && string.IsNullOrWhiteSpace(name))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var info = await DescribeAsync(input.DepartmentId, input.SubjectKind, input.SubjectId, input.RequestedByUserId);
			if (info == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.SubjectNotFound);

			string email = null, phone = null;
			if (remote)
			{
				var refusal = await CheckRemoteAsync(input.DepartmentId, input.SubjectKind);
				if (refusal != SignatureRequestRefusals.None)
					return SignatureRequestResult.Refused(refusal);

				email = NormalizeEmail(input.SignerEmail);
				phone = NormalizePhone(input.SignerPhone);
				var needs = input.OtpChannel == SignatureSignerAuthMethods.SmsOtp ? phone : email;
				if (needs == null)
					return SignatureRequestResult.Refused(SignatureRequestRefusals.ContactRequired);
			}

			var now = DateTime.UtcNow;
			var request = await _requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = input.DepartmentId,
				SubjectKind = (int)input.SubjectKind,
				SubjectId = Clean(input.SubjectId, 128),
				RevisionId = Clean(info.RevisionId, 128),
				Title = Clean(info.Title, 256),
				ArtifactSha256 = info.RevisionSha256.ToLowerInvariant(),
				RequiredLevel = (int)SignatureLevels.External,
				Ordered = false,
				Status = remote ? (int)SignatureRequestStatuses.Draft : (int)SignatureRequestStatuses.Sent,
				Channel = (int)input.Channel,
				ExpiresOn = remote ? now.AddDays(Math.Clamp(input.ExpiresInDays <= 0 ? ESignConfig.LinkExpiryDays : input.ExpiresInDays, 1, 60)) : now.AddHours(InPersonExpiryHours),
				ProviderKind = remote ? (int)ESignProviderKinds.Documenso : (int)ESignProviderKinds.None,
				StatementVersion = use.StatementVersion,
				StatementText = Clean(Statement(input.SubjectKind, info, input.Culture), 2000),
				CreatedByUserId = input.RequestedByUserId,
				CreatedOn = now
			}, cancellationToken, true);

			var signer = await _signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = request.SignatureRequestId,
				DepartmentId = input.DepartmentId,
				SignerOrder = 1,
				Role = (int)(input.Role ?? use.DefaultRole),
				ExternalName = name,
				ExternalEmailEncrypted = email == null ? null : _encryption.Encrypt(email),
				ExternalPhoneEncrypted = phone == null ? null : _encryption.Encrypt(phone),
				ContactMasked = remote ? (input.OtpChannel == SignatureSignerAuthMethods.SmsOtp ? SignatureTokens.MaskPhone(phone) : SignatureTokens.MaskEmail(email)) : null,
				AuthMethod = remote ? (int)(input.OtpChannel == SignatureSignerAuthMethods.SmsOtp ? SignatureSignerAuthMethods.SmsOtp : SignatureSignerAuthMethods.EmailOtp) : (int)SignatureSignerAuthMethods.InPerson,
				Status = (int)SignatureSignerStatuses.Pending,
				CreatedOn = now
			}, cancellationToken, true);

			var result = new SignatureRequestResult { Request = request, Signer = signer };
			if (remote)
			{
				try
				{
					var token = await _accounts.GetApiTokenAsync(input.DepartmentId);
					var pdf = RenderSigningInput(info, request, input.Culture, out var signaturePage);
					var envelope = await _provider.CreateEnvelopeAsync(token, new ESignEnvelopeRequest
					{
						Title = request.Title,
						ExternalId = "resgrid:" + request.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + request.SignatureRequestId.ToString(CultureInfo.InvariantCulture),
						Pdf = pdf,
						FileName = FileNameFor(request),
						Ordered = false,
						SignaturePage = signaturePage,
						Language = LanguageOf(input.Culture),
						Recipients = new List<ESignRecipient>
						{
							new ESignRecipient { Reference = signer.SignatureRequestSignerId.ToString(CultureInfo.InvariantCulture), Name = name, Email = email, SigningOrder = 1 }
						}
					}, cancellationToken);

					var recipient = envelope?.Recipients?.FirstOrDefault(r => r.Reference == signer.SignatureRequestSignerId.ToString(CultureInfo.InvariantCulture)) ?? envelope?.Recipients?.FirstOrDefault();
					if (envelope == null || string.IsNullOrWhiteSpace(envelope.EnvelopeId) || recipient == null || string.IsNullOrWhiteSpace(recipient.SigningToken))
						throw new InvalidOperationException("esign_provider_bad_response");

					request.ProviderEnvelopeId = Clean(envelope.EnvelopeId, 128);
					request.Status = (int)SignatureRequestStatuses.Sent;
					request.ReconcileAfter = now.AddMinutes(Math.Max(5, ESignConfig.ReconcileIntervalMinutes));
					request.UpdatedOn = now;
					signer.ProviderRecipientId = Clean(recipient.ProviderRecipientId, 128);
					signer.ProviderSigningTokenEncrypted = _encryption.Encrypt(recipient.SigningToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "The e-sign envelope could not be created; the request is voided.");
					request.Status = (int)SignatureRequestStatuses.Voided;
					request.VoidedOn = now;
					request.VoidReason = "esign_provider_failed";
					await _requests.UpdateAsync(request, CancellationToken.None, true);
					return new SignatureRequestResult { Refusal = SignatureRequestRefusals.ProviderFailed, Request = request, Signer = signer };
				}

				result.LinkToken = IssueLink(signer, request);
				await _requests.UpdateAsync(request, cancellationToken, true);
				await _signers.UpdateAsync(signer, cancellationToken, true);
				if (await SendLinkAsync(request, signer, result.LinkToken, input.Culture))
				{
					signer.LinkSentOn = DateTime.UtcNow;
					await _signers.UpdateAsync(signer, cancellationToken, true);
				}
			}

			Audit(request, input.RequestedByUserId, AuditLogTypes.SignatureRequestCreated, input.IpAddress, input.UserAgent);
			Publish(request, SignatureRequestChanges.Requested, new List<SignatureRequestSigner> { signer }, input.RequestedByUserId);
			return result;
		}

		public async Task<SignatureRequestResult> ResendAsync(int departmentId, int signatureRequestId, string userId, CancellationToken cancellationToken = default)
		{
			var request = await GetAsync(departmentId, signatureRequestId);
			if (request == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			if (!request.IsOpen || request.Channel != (int)SignatureRequestChannels.Remote || request.ExpiresOn <= DateTime.UtcNow)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RequestClosed);

			// S3: only an external signer whose turn it is gets a link (member steps sign in Resgrid; later steps wait their turn).
			var all = await GetSignersAsync(departmentId, signatureRequestId);
			var signer = all.FirstOrDefault(s => !s.IsMember && !s.IsDone && SignatureOrdering.IsTurn(s, all, request.Ordered));
			if (signer == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RequestClosed);

			// A fresh link: the earlier one (and any session it opened) stops working.
			var token = IssueLink(signer, request);
			signer.SessionHash = null;
			signer.SessionExpiresOn = null;
			await _signers.UpdateAsync(signer, cancellationToken, true);
			if (await SendLinkAsync(request, signer, token, null))
			{
				signer.LinkSentOn = DateTime.UtcNow;
				await _signers.UpdateAsync(signer, cancellationToken, true);
			}

			Audit(request, userId, AuditLogTypes.SignatureRequestResent, null, null);
			return new SignatureRequestResult { Request = request, Signer = signer, LinkToken = token };
		}

		public async Task<SignatureRequestRecord> VoidAsync(int departmentId, int signatureRequestId, string userId, string reason, CancellationToken cancellationToken = default)
		{
			var request = await GetAsync(departmentId, signatureRequestId);
			if (request == null || !request.IsOpen)
				return request;

			request.Status = (int)SignatureRequestStatuses.Voided;
			request.VoidedOn = DateTime.UtcNow;
			request.VoidedByUserId = userId;
			request.VoidReason = Clean(reason, 1000);
			request.UpdatedOn = DateTime.UtcNow;
			await _requests.UpdateAsync(request, cancellationToken, true);
			await CloseLinksAsync(request, cancellationToken);
			await DeleteEnvelopeAsync(request);
			Audit(request, userId, AuditLogTypes.SignatureRequestVoided, null, null);
			await NotifyClosedAsync(request, await GetSignersAsync(departmentId, signatureRequestId), userId);
			return request;
		}

		public async Task<SignatureRequestRecord> GetAsync(int departmentId, int signatureRequestId)
		{
			if (departmentId <= 0 || signatureRequestId <= 0)
				return null;
			var request = await _requests.GetByIdForDepartmentAsync(departmentId, signatureRequestId);
			return request != null && request.DepartmentId == departmentId ? request : null;
		}

		public async Task<List<SignatureRequestSigner>> GetSignersAsync(int departmentId, int signatureRequestId)
		{
			if (departmentId <= 0 || signatureRequestId <= 0)
				return new List<SignatureRequestSigner>();
			return (await _signers.GetForRequestAsync(departmentId, signatureRequestId) ?? new List<SignatureRequestSigner>())
				.Where(s => s.DepartmentId == departmentId && s.SignatureRequestId == signatureRequestId).ToList();
		}

		public async Task<SubjectSignatureSummary> GetSubjectSummaryAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId)
		{
			var summary = new SubjectSignatureSummary { Kind = kind, SubjectId = subjectId };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return summary;

			var info = await DescribeAsync(departmentId, kind, subjectId, userId);
			summary.CurrentRevisionSha256 = info?.RevisionSha256;
			summary.Requests = (await _requests.GetForSubjectAsync(departmentId, (int)kind, subjectId) ?? new List<SignatureRequestRecord>()).Where(r => r.DepartmentId == departmentId).ToList();
			summary.Signers = summary.Requests.Count == 0 ? new List<SignatureRequestSigner>() :
				(await _signers.GetForRequestsAsync(departmentId, summary.Requests.Select(r => r.SignatureRequestId)) ?? new List<SignatureRequestSigner>()).Where(s => s.DepartmentId == departmentId).ToList();

			var records = (await _records.GetForSubjectAsync(departmentId, (int)kind, subjectId) ?? new List<SignatureRecord>()).Where(r => r.DepartmentId == departmentId).ToList();
			summary.Signatures = records.Select(r => Verify(r, info?.RevisionSha256)).ToList();
			summary.LatestSealed = await _sealing.GetLatestAsync(departmentId, kind, subjectId, null, SealedDocumentRoles.Sealed);
			summary.RemoteRefusal = await CheckRemoteAsync(departmentId, kind);
			summary.RemoteAvailable = summary.RemoteRefusal == SignatureRequestRefusals.None;
			return summary;
		}

		/// <summary>A signature against the subject's current hash (the S0 rule: it binds to the hash, never to the live row).</summary>
		public static SignatureVerification Verify(SignatureRecord record, string currentSha256)
		{
			var verification = new SignatureVerification { Record = record, CurrentSha256 = currentSha256 };
			if (record.VoidedOn.HasValue)
				verification.State = SignatureVerificationStates.Voided;
			else if (string.IsNullOrWhiteSpace(currentSha256))
				verification.State = SignatureVerificationStates.SubjectMissing;
			else if (string.Equals(record.ArtifactSha256, currentSha256, StringComparison.OrdinalIgnoreCase))
				verification.State = SignatureVerificationStates.Valid;
			else
				verification.State = SignatureVerificationStates.ArtifactChanged;
			return verification;
		}

		public async Task<List<SignatureVerification>> GetRecordSignaturesProjectionAsync(int departmentId, string recordId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(recordId))
				return new List<SignatureVerification>();

			// Decision 21: Records keeps RmsSignatures. They are read here and shown in the shared list's shape; nothing is written.
			var rows = await _rmsSignatures.GetForRecordAsync(departmentId, recordId) ?? Enumerable.Empty<RmsSignature>();
			return rows.Where(r => r != null && r.DepartmentId == departmentId).Select(RmsSignatureProjection.ToVerification).ToList();
		}

		#endregion

		#region In person

		public async Task<SignatureRequestResult> CaptureInPersonAsync(InPersonSignatureCapture capture, CancellationToken cancellationToken = default)
		{
			if (capture == null || capture.DepartmentId <= 0 || capture.SignatureRequestId <= 0 || string.IsNullOrWhiteSpace(capture.CapturedByUserId))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			if (!capture.ConsentGiven)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var request = await GetAsync(capture.DepartmentId, capture.SignatureRequestId);
			if (request == null || request.Channel != (int)SignatureRequestChannels.InPerson)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			if (!request.IsOpen || request.ExpiresOn <= DateTime.UtcNow)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RequestClosed);

			var name = Clean(capture.PrintedName, 200);
			var image = SignatureImage(capture.SignatureImageDataUrl);
			if (string.IsNullOrWhiteSpace(name) || image == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			// Bound to the revision hash the page showed, and the subject must still be at it.
			var info = await DescribeAsync(request.DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, capture.CapturedByUserId);
			if (info == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.SubjectNotFound);
			if (!SignatureTokens.HashEquals(capture.BoundRevisionSha256, request.ArtifactSha256) || !SignatureTokens.HashEquals(info.RevisionSha256, request.ArtifactSha256))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RevisionChanged);

			// S3: the external step whose turn it is (an ordered request's earlier member steps must be signed first).
			var all = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
			var (signer, stepRefusal) = ExternalStep(all, request.Ordered, capture.SignatureRequestSignerId);
			if (signer == null)
				return SignatureRequestResult.Refused(stepRefusal);

			signer.ExternalName = name;
			signer.ConsentTextVersion = SignatureTokens.ConsentTextVersion;
			signer.ConsentedOn = DateTime.UtcNow;
			signer.ConsentIpAddress = Clean(capture.IpAddress, 64);

			var record = await RecordCaptureAsync(request, signer, SignatureMethods.InPersonOnline, name, image, capture.WitnessName, capture.WitnessUserId,
				capture.CapturedByUserId, DateTime.UtcNow, null, capture.IpAddress, capture.UserAgent, request.ArtifactSha256, cancellationToken);

			await AfterSignatureAsync(request, capture.CapturedByUserId, cancellationToken);
			return new SignatureRequestResult { Request = request, Signer = signer, Record = record };
		}

		/// <summary>
		/// The external step a capture answers: the one named (it must be external, unsigned and its turn), or the first external step
		/// whose turn it is.
		/// </summary>
		private static (SignatureRequestSigner, SignatureRequestRefusals) ExternalStep(List<SignatureRequestSigner> signers, bool ordered, int? signerId)
		{
			if (signerId.HasValue)
			{
				var named = signers.FirstOrDefault(s => s.SignatureRequestSignerId == signerId.Value);
				if (named == null || named.IsMember)
					return (null, SignatureRequestRefusals.NotASigner);
				if (named.IsDone)
					return (null, SignatureRequestRefusals.RequestClosed);
				return SignatureOrdering.IsTurn(named, signers, ordered) ? (named, SignatureRequestRefusals.None) : (null, SignatureRequestRefusals.NotYourTurn);
			}

			var open = signers.FirstOrDefault(s => !s.IsMember && !s.IsDone && SignatureOrdering.IsTurn(s, signers, ordered));
			if (open != null)
				return (open, SignatureRequestRefusals.None);
			return signers.Any(s => !s.IsMember && !s.IsDone) ? (null, SignatureRequestRefusals.NotYourTurn) : (null, SignatureRequestRefusals.RequestClosed);
		}

		public async Task<SignatureRequestResult> CaptureOfflineAsync(OfflineSignatureCapture capture, CancellationToken cancellationToken = default)
		{
			// S3: an offline capture for an existing (ordered) request's external step.
			if (capture?.SignatureRequestId != null)
				return await CaptureStepOfflineAsync(capture, cancellationToken);

			var use = capture == null ? null : ExternalSignatureUse.For(capture.SubjectKind);
			if (use == null || capture.DepartmentId <= 0 || string.IsNullOrWhiteSpace(capture.SubjectId) || string.IsNullOrWhiteSpace(capture.CapturedByUserId)
				|| !SignaturePolicy.IsSha256(capture.BoundRevisionSha256))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var name = Clean(capture.PrintedName, 200);
			var image = SignatureImage(capture.SignatureImageDataUrl);
			if (string.IsNullOrWhiteSpace(name) || image == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);

			var info = await DescribeAsync(capture.DepartmentId, capture.SubjectKind, capture.SubjectId, capture.CapturedByUserId);
			if (info == null)
				return SignatureRequestResult.Refused(SignatureRequestRefusals.SubjectNotFound);

			// The device bound the signature to the revision it showed while offline; a revision made since is a different document.
			if (!SignatureTokens.HashEquals(info.RevisionSha256, capture.BoundRevisionSha256))
				return SignatureRequestResult.Refused(SignatureRequestRefusals.RevisionChanged);

			var now = DateTime.UtcNow;
			var request = await _requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = capture.DepartmentId,
				SubjectKind = (int)capture.SubjectKind,
				SubjectId = Clean(capture.SubjectId, 128),
				RevisionId = Clean(info.RevisionId, 128),
				Title = Clean(info.Title, 256),
				ArtifactSha256 = info.RevisionSha256.ToLowerInvariant(),
				RequiredLevel = (int)SignatureLevels.External,
				Status = (int)SignatureRequestStatuses.Sent,
				Channel = (int)SignatureRequestChannels.InPerson,
				ExpiresOn = now.AddHours(InPersonExpiryHours),
				ProviderKind = (int)ESignProviderKinds.None,
				StatementVersion = use.StatementVersion,
				StatementText = Clean(Statement(capture.SubjectKind, info, null), 2000),
				CreatedByUserId = capture.CapturedByUserId,
				CreatedOn = now
			}, cancellationToken, true);

			var signer = await _signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = request.SignatureRequestId,
				DepartmentId = capture.DepartmentId,
				SignerOrder = 1,
				Role = (int)(capture.Role ?? use.DefaultRole),
				ExternalName = name,
				AuthMethod = (int)SignatureSignerAuthMethods.InPerson,
				Status = (int)SignatureSignerStatuses.Pending,
				ConsentTextVersion = SignatureTokens.ConsentTextVersion,
				ConsentedOn = capture.CapturedOnUtc ?? now,
				CreatedOn = now
			}, cancellationToken, true);

			Publish(request, SignatureRequestChanges.Requested, new List<SignatureRequestSigner> { signer }, capture.CapturedByUserId);
			var record = await RecordCaptureAsync(request, signer, SignatureMethods.InPersonOffline, name, image, capture.WitnessName, capture.WitnessUserId,
				capture.CapturedByUserId, capture.CapturedOnUtc ?? now, capture.DeviceInfo, capture.IpAddress, capture.UserAgent, capture.BoundRevisionSha256, cancellationToken);

			await CompleteAsync(request, capture.CapturedByUserId, cancellationToken);
			return new SignatureRequestResult { Request = request, Signer = signer, Record = record };
		}

		private async Task<SignatureRecord> RecordCaptureAsync(SignatureRequestRecord request, SignatureRequestSigner signer, SignatureMethods method, string name, byte[] image,
			string witnessName, string witnessUserId, string capturedBy, DateTime capturedOn, string deviceInfo, string ipAddress, string userAgent, string boundSha256,
			CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var record = await _records.InsertAsync(new SignatureRecord
			{
				DepartmentId = request.DepartmentId,
				SubjectKind = request.SubjectKind,
				SubjectId = request.SubjectId,
				RevisionId = request.RevisionId,
				SignerUserId = null,
				ExternalSignerName = name,
				SignerNameSnapshot = name,
				SignerRoleSnapshot = ((SignatureSignerRoles)signer.Role).ToString(),
				Intent = (int)SignatureIntents.Attest,
				StatementVersion = request.StatementVersion,
				StatementText = request.StatementText,
				Level = (int)SignatureLevels.External,
				Method = (int)method,
				ArtifactSha256 = request.ArtifactSha256,
				SignedOn = now,
				IpAddress = Clean(ipAddress, 64),
				UserAgent = Clean(userAgent, 1000),
				SignatureRequestId = request.SignatureRequestId,
				CapturedByUserId = capturedBy,
				CreatedOn = now
			}, cancellationToken, true);

			await _captures.InsertAsync(new SignatureCapture
			{
				DepartmentId = request.DepartmentId,
				SignatureRequestId = request.SignatureRequestId,
				SignatureRequestSignerId = signer.SignatureRequestSignerId,
				SignatureRecordId = record.SignatureRecordId,
				Method = (int)method,
				BoundRevisionSha256 = (boundSha256 ?? request.ArtifactSha256).ToLowerInvariant(),
				SignatureImageEncrypted = _encryption.Encrypt("data:image/png;base64," + Convert.ToBase64String(image)),
				SignatureImageSha256 = SealingPolicy.Sha256(image),
				PrintedNameEncrypted = _encryption.Encrypt(name),
				WitnessNameEncrypted = string.IsNullOrWhiteSpace(witnessName) ? null : _encryption.Encrypt(Clean(witnessName, 200)),
				WitnessUserId = Clean(witnessUserId, 128),
				CapturedByUserId = capturedBy,
				CapturedOn = capturedOn,
				ReceivedOn = now,
				DeviceInfo = Clean(deviceInfo, 256)
			}, cancellationToken, true);

			signer.Status = (int)SignatureSignerStatuses.Signed;
			signer.SignedOn = now;
			signer.SignatureRecordId = record.SignatureRecordId;
			signer.NextReminderOn = null;
			await _signers.UpdateAsync(signer, cancellationToken, true);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = request.DepartmentId,
				UserId = capturedBy,
				Type = AuditLogTypes.ExternalSignatureCaptured,
				After = JsonConvert.SerializeObject(new { record.SignatureRecordId, request.SignatureRequestId, request.SubjectKind, request.SubjectId, record.Method, record.ArtifactSha256 }),
				Successful = true,
				IpAddress = ipAddress,
				UserAgent = userAgent,
				ServerName = Environment.MachineName
			});
			return record;
		}

		/// <summary>A PNG data URL's bytes (at most <see cref="MaxSignatureImageBytes"/>), or null.</summary>
		public static byte[] SignatureImage(string dataUrl)
		{
			const string prefix = "data:image/png;base64,";
			if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || dataUrl.Length > MaxSignatureImageBytes * 2)
				return null;
			try
			{
				var bytes = Convert.FromBase64String(dataUrl.Substring(prefix.Length));
				var png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
				return png && bytes.Length <= MaxSignatureImageBytes ? bytes : null;
			}
			catch (FormatException)
			{
				return null;
			}
		}

		#endregion

		#region Completion

		/// <summary>Marks the request completed, seals the signature evidence (L3), applies the module write-back and publishes 221.</summary>
		private async Task CompleteAsync(SignatureRequestRecord request, string actingUserId, CancellationToken cancellationToken, byte[] providerCompletedPdf = null)
		{
			var signers = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
			request.Status = (int)SignatureRequestStatuses.Completed;
			request.CompletedOn = DateTime.UtcNow;
			request.UpdatedOn = DateTime.UtcNow;
			request.ReconcileAfter = null;
			await _requests.UpdateAsync(request, cancellationToken, true);

			try
			{
				var outcome = await SealEvidenceAsync(request, signers, providerCompletedPdf, cancellationToken);
				if (outcome?.Document != null)
				{
					request.SealedDocumentId = outcome.Document.SealedDocumentId;
					await _requests.UpdateAsync(request, cancellationToken, true);
				}
			}
			catch (Exception ex)
			{
				// The signature stands; the evidence document is simply not sealed (it can be sealed again from the request).
				Logging.LogException(ex, "The signature evidence could not be prepared for sealing.");
			}

			var source = SourceFor((SignatureSubjectKinds)request.SubjectKind);
			if (source != null)
			{
				foreach (var signer in signers.Where(s => s.Status == (int)SignatureSignerStatuses.Signed))
				{
					try { await source.OnSignedAsync(request, signer, actingUserId ?? request.CreatedByUserId, cancellationToken); }
					catch (Exception ex) { Logging.LogException(ex, "A module write-back after an external signature failed."); }
				}
			}

			Audit(request, actingUserId, AuditLogTypes.SignatureRequestCompleted, null, null);
			Publish(request, SignatureRequestChanges.Completed, signers, actingUserId);
			await NotifyClosedAsync(request, signers, actingUserId);
		}

		private async Task<SealOutcome> SealEvidenceAsync(SignatureRequestRecord request, List<SignatureRequestSigner> signers, byte[] providerCompletedPdf, CancellationToken cancellationToken)
		{
			var department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false);
			var info = await DescribeAsync(request.DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.CreatedByUserId);
			var records = (await _records.GetForSubjectAsync(request.DepartmentId, request.SubjectKind, request.SubjectId) ?? new List<SignatureRecord>())
				.Where(r => r.DepartmentId == request.DepartmentId && r.SignatureRequestId == request.SignatureRequestId).ToList();
			var captures = (await _captures.GetForRequestAsync(request.DepartmentId, request.SignatureRequestId) ?? new List<SignatureCapture>()).Where(c => c.DepartmentId == request.DepartmentId).ToList();

			var images = new Dictionary<int, string>();
			var witnesses = new Dictionary<int, string>();
			foreach (var capture in captures)
			{
				try
				{
					images[capture.SignatureRequestSignerId] = _encryption.Decrypt(capture.SignatureImageEncrypted);
					if (!string.IsNullOrEmpty(capture.WitnessNameEncrypted))
						witnesses[capture.SignatureRequestSignerId] = _encryption.Decrypt(capture.WitnessNameEncrypted);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A signature capture could not be decrypted for the evidence document.");
				}
			}

			// The document only when the subject is still at the signed revision; otherwise the evidence names the hash alone.
			var document = info != null && SignatureTokens.HashEquals(info.RevisionSha256, request.ArtifactSha256) ? info.DocumentHtml : null;
			var html = SignatureEvidenceDocument.Build(request, signers, records, images, witnesses, document,
				providerCompletedPdf == null ? null : SealingPolicy.Sha256(providerCompletedPdf), department?.Name);

			var use = ExternalSignatureUse.For((SignatureSubjectKinds)request.SubjectKind);
			return await _sealing.SealHtmlAsync(new SealHtmlRequest
			{
				DepartmentId = request.DepartmentId,
				SubjectKind = (SignatureSubjectKinds)request.SubjectKind,
				SubjectId = request.SubjectId,
				RevisionId = request.RevisionId,
				RevisionSha256 = request.ArtifactSha256,
				Title = request.Title,
				FileName = FileNameFor(request),
				Html = html,
				PageSize = "Letter",
				RetentionYears = info?.RetentionYears,
				LongTermSubject = use?.LongTerm ?? false,
				SignatureRequestId = request.SignatureRequestId,
				Role = SealedDocumentRoles.Sealed,
				DepartmentName = department?.Name,
				RequestedByUserId = request.CreatedByUserId,
				ReuseExisting = false,
				Signers = signers.Where(s => s.Status == (int)SignatureSignerStatuses.Signed).OrderBy(s => s.SignerOrder).ThenBy(s => s.SignedOn).Select(s =>
				{
					var record = records.FirstOrDefault(r => r.SignatureRecordId == s.SignatureRecordId);
					return new SealAuditSigner
					{
						// S3: a member step's name and level come from its SignatureRecord (L1/L2); an external signer is L4.
						Name = s.IsMember ? record?.SignerNameSnapshot ?? s.StepLabel : s.ExternalName,
						Role = ((SignatureSignerRoles)s.Role).ToString(),
						Level = record?.Level ?? (int)SignatureLevels.External,
						Method = record == null ? ((SignatureSignerAuthMethods)s.AuthMethod).ToString() : ((SignatureMethods)record.Method).ToString(),
						SignedOnUtc = s.SignedOn,
						ArtifactSha256 = record?.ArtifactSha256 ?? request.ArtifactSha256,
						Evidence = s.IsMember ? MemberEvidenceLine(record) : EvidenceLine(s, witnesses.TryGetValue(s.SignatureRequestSignerId, out var w) ? w : null)
					};
				}).ToList()
			}, cancellationToken);
		}

		/// <summary>The audit page's evidence for one signer: consent version and time, the one-time code channel, masked address and time.</summary>
		public static string EvidenceLine(SignatureRequestSigner signer, string witnessName)
		{
			var parts = new List<string>();
			if (signer.ConsentedOn.HasValue)
				parts.Add("consent " + (signer.ConsentTextVersion ?? "?") + " " + signer.ConsentedOn.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
			if (signer.OtpVerifiedOn.HasValue)
				parts.Add((signer.OtpChannel == (int)SignatureSignerAuthMethods.SmsOtp ? "SMS" : "e-mail") + " code to " + (signer.ContactMasked ?? "?") + " verified " +
					signer.OtpVerifiedOn.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
			else if (signer.AuthMethod == (int)SignatureSignerAuthMethods.EmailOtp || signer.AuthMethod == (int)SignatureSignerAuthMethods.SmsOtp)
				parts.Add("no Resgrid code verified");
			if (signer.AuthMethod == (int)SignatureSignerAuthMethods.InPerson)
				parts.Add("in person");
			if (!string.IsNullOrWhiteSpace(witnessName))
				parts.Add("witness " + witnessName);
			return string.Join("; ", parts);
		}

		/// <summary>The audit page's evidence for a member step (S3): signed in Resgrid, how the identity was re-proved.</summary>
		public static string MemberEvidenceLine(SignatureRecord record)
		{
			if (record == null)
				return "member";
			var evidence = "member; " + (SignatureMethods)record.Method;
			return string.IsNullOrWhiteSpace(record.StepUpEvidenceReference) ? evidence : evidence + " " + record.StepUpEvidenceReference;
		}

		#endregion

		#region Worker 90

		public async Task<SignatureRequestRecord> ReconcileAsync(int departmentId, int signatureRequestId, CancellationToken cancellationToken = default)
		{
			var request = await GetAsync(departmentId, signatureRequestId);
			if (request == null || request.Channel != (int)SignatureRequestChannels.Remote || string.IsNullOrWhiteSpace(request.ProviderEnvelopeId) || !request.IsOpen)
				return request;
			if (_provider == null || !_provider.IsAvailable)
				return request;

			var token = await _accounts.GetApiTokenAsync(departmentId);
			if (string.IsNullOrWhiteSpace(token))
				return request;

			// Only the re-fetched envelope is trusted (a webhook body never changes state).
			var envelope = await _provider.GetEnvelopeAsync(token, request.ProviderEnvelopeId, cancellationToken);
			var now = DateTime.UtcNow;
			request.LastReconciledOn = now;
			if (envelope == null)
			{
				request.ReconcileAfter = now.AddMinutes(Math.Max(5, ESignConfig.ReconcileIntervalMinutes));
				await _requests.UpdateAsync(request, cancellationToken, true);
				return request;
			}

			var signers = await GetSignersAsync(departmentId, signatureRequestId);
			var declined = false;
			var newlySigned = false;
			// S3: only external signers are on the envelope; member steps sign in Resgrid.
			foreach (var signer in signers.Where(s => !s.IsMember))
			{
				var recipient = envelope.Recipients.FirstOrDefault(r => !string.IsNullOrWhiteSpace(signer.ProviderRecipientId) && r.ProviderRecipientId == signer.ProviderRecipientId)
					?? envelope.Recipients.FirstOrDefault(r => r.SigningOrder == signer.SignerOrder);
				if (recipient == null)
					continue;

				if (recipient.Signed && signer.Status != (int)SignatureSignerStatuses.Signed)
				{
					var record = await _records.InsertAsync(new SignatureRecord
					{
						DepartmentId = request.DepartmentId,
						SubjectKind = request.SubjectKind,
						SubjectId = request.SubjectId,
						RevisionId = request.RevisionId,
						SignerUserId = null,
						ExternalSignerName = signer.ExternalName,
						ExternalSignerContact = signer.ContactMasked,
						SignerNameSnapshot = signer.ExternalName,
						SignerRoleSnapshot = ((SignatureSignerRoles)signer.Role).ToString(),
						Intent = (int)SignatureIntents.Attest,
						StatementVersion = request.StatementVersion,
						StatementText = request.StatementText,
						Level = (int)SignatureLevels.External,
						Method = (int)SignatureMethods.ExternalLink,
						ArtifactSha256 = request.ArtifactSha256,
						SignedOn = recipient.SignedAtUtc ?? now,
						SignatureRequestId = request.SignatureRequestId,
						ProviderEvidenceKey = Clean("documenso:" + request.ProviderEnvelopeId + ":" + recipient.ProviderRecipientId, 512),
						StepUpEvidenceReference = signer.OtpVerifiedOn.HasValue ? Clean("otp:" + ((SignatureSignerAuthMethods)(signer.OtpChannel ?? signer.AuthMethod)) + ":" + signer.OtpVerifiedOn.Value.ToString("o", CultureInfo.InvariantCulture), 256) : null,
						CreatedOn = now
					}, cancellationToken, true);

					signer.Status = (int)SignatureSignerStatuses.Signed;
					signer.SignedOn = recipient.SignedAtUtc ?? now;
					signer.SignatureRecordId = record.SignatureRecordId;
					signer.NextReminderOn = null;
					await _signers.UpdateAsync(signer, cancellationToken, true);
					newlySigned = true;
				}
				else if (recipient.Rejected && signer.Status != (int)SignatureSignerStatuses.Declined)
				{
					signer.Status = (int)SignatureSignerStatuses.Declined;
					signer.DeclinedOn = now;
					signer.DeclineReason = Clean(recipient.RejectionReason, 1000);
					signer.NextReminderOn = null;
					await _signers.UpdateAsync(signer, cancellationToken, true);
					declined = true;
					request.DeclineReason = Clean(recipient.RejectionReason, 1000);
				}
			}

			if (declined || envelope.Rejected)
			{
				request.Status = (int)SignatureRequestStatuses.Declined;
				request.DeclinedOn = now;
				request.ReconcileAfter = null;
				request.UpdatedOn = now;
				await _requests.UpdateAsync(request, cancellationToken, true);
				await CloseLinksAsync(request, cancellationToken);
				Audit(request, null, AuditLogTypes.SignatureRequestDeclined, null, null);
				Publish(request, SignatureRequestChanges.Declined, signers, null);
				await NotifyClosedAsync(request, signers, null);
				return request;
			}

			if (envelope.Completed && signers.All(s => s.Status == (int)SignatureSignerStatuses.Signed))
			{
				var completed = await StoreProviderDocumentsAsync(request, token, envelope, cancellationToken);
				await CloseLinksAsync(request, cancellationToken);
				await CompleteAsync(request, null, cancellationToken, completed);
				return request;
			}

			if (signers.Any(s => s.Status == (int)SignatureSignerStatuses.Signed))
				request.Status = (int)SignatureRequestStatuses.PartiallySigned;
			request.ReconcileAfter = now.AddMinutes(Math.Max(5, ESignConfig.ReconcileIntervalMinutes));
			request.UpdatedOn = now;
			await _requests.UpdateAsync(request, cancellationToken, true);

			// S3: an external signature in an ordered request opens the next group (member notifications, the next links).
			if (newlySigned)
				await StartTurnsAsync(request, signers, null, null, cancellationToken);
			return request;
		}

		/// <summary>Stores the provider's completed PDF and audit certificate as delivered; returns the completed PDF (null when unavailable).</summary>
		private async Task<byte[]> StoreProviderDocumentsAsync(SignatureRequestRecord request, string token, ESignEnvelope envelope, CancellationToken cancellationToken)
		{
			byte[] completed = null;
			try
			{
				completed = await _provider.DownloadCompletedPdfAsync(token, envelope, cancellationToken);
				if (completed != null)
					await _sealing.StoreAsIsAsync(request.DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.ArtifactSha256, request.SignatureRequestId,
						SealedDocumentRoles.ProviderCompleted, request.Title, FileNameFor(request), completed, cancellationToken);
				var certificate = await _provider.DownloadAuditCertificateAsync(token, envelope, cancellationToken);
				if (certificate != null)
					await _sealing.StoreAsIsAsync(request.DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.ArtifactSha256, request.SignatureRequestId,
						SealedDocumentRoles.ProviderAuditCertificate, request.Title, "audit-" + FileNameFor(request), certificate, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The completed e-sign documents could not be stored; they stay with the provider.");
			}

			return completed;
		}

		public async Task<int> ProcessWebhookEventsAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var processed = 0;
			foreach (var hint in await _webhookEvents.GetDueAsync(utcNow - ClaimLease, ESignWebhookPolicy.MaxAttempts, Math.Max(1, batchSize)) ?? new List<ESignWebhookEvent>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await _webhookEvents.ClaimAsync(hint.ESignWebhookEventId, utcNow - ClaimLease, utcNow, cancellationToken))
					continue;

				try
				{
					var request = await RequestForHintAsync(hint);
					if (request == null)
					{
						await _webhookEvents.MarkAsync(hint.ESignWebhookEventId, ESignWebhookEventStates.Ignored, utcNow, "no_request", cancellationToken);
						continue;
					}

					await ReconcileAsync(request.DepartmentId, request.SignatureRequestId, cancellationToken);
					await _webhookEvents.MarkAsync(hint.ESignWebhookEventId, ESignWebhookEventStates.Done, utcNow, null, cancellationToken);
					processed++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An e-sign webhook hint could not be processed; it is retried.");
					await _webhookEvents.MarkAsync(hint.ESignWebhookEventId, ESignWebhookEventStates.Pending, utcNow, ex.Message, CancellationToken.None);
				}
			}

			return processed;
		}

		/// <summary>
		/// The request a hint names: by the provider's envelope id, or by the external id Resgrid gave the envelope
		/// ("resgrid:{departmentId}:{requestId}") — then only when that request really holds an envelope of this provider. Either
		/// way nothing in the hint changes state; the request's own stored envelope is re-fetched.
		/// </summary>
		private async Task<SignatureRequestRecord> RequestForHintAsync(ESignWebhookEvent hint)
		{
			var key = hint?.EnvelopeId;
			if (string.IsNullOrWhiteSpace(key))
				return null;

			if (key.StartsWith("resgrid:", StringComparison.Ordinal))
			{
				var parts = key.Split(':');
				if (parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var departmentId)
					&& int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var requestId))
				{
					var request = await GetAsync(departmentId, requestId);
					return request != null && request.ProviderKind == hint.ProviderKind && !string.IsNullOrWhiteSpace(request.ProviderEnvelopeId) ? request : null;
				}
				return null;
			}

			return await _requests.GetByEnvelopeAsync(hint.ProviderKind, key);
		}

		public async Task<int> ReconcileDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var request in await _requests.GetDueReconcileAsync(utcNow, utcNow - ClaimLease, Math.Max(1, batchSize)) ?? new List<SignatureRequestRecord>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await _requests.ClaimAsync(request.SignatureRequestId, utcNow - ClaimLease, utcNow, cancellationToken))
					continue;
				try
				{
					await ReconcileAsync(request.DepartmentId, request.SignatureRequestId, cancellationToken);
					count++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An e-sign envelope could not be reconciled.");
				}
				finally
				{
					await _requests.ReleaseAsync(request.SignatureRequestId, CancellationToken.None);
				}
			}

			return count;
		}

		public async Task<int> ExpireDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			var expired = 0;
			foreach (var due in await _requests.GetDueExpiryAsync(utcNow, Math.Max(1, batchSize)) ?? new List<SignatureRequestRecord>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					// A signature that landed just before expiry still counts: re-fetch first.
					var request = due.Channel == (int)SignatureRequestChannels.Remote ? await ReconcileAsync(due.DepartmentId, due.SignatureRequestId, cancellationToken) ?? due : due;
					if (!request.IsOpen)
						continue;

					request.Status = (int)SignatureRequestStatuses.Expired;
					request.ExpiredOn = utcNow;
					request.ReconcileAfter = null;
					request.UpdatedOn = utcNow;
					await _requests.UpdateAsync(request, cancellationToken, true);
					await CloseLinksAsync(request, cancellationToken);
					await DeleteEnvelopeAsync(request);
					Audit(request, null, AuditLogTypes.SignatureRequestExpired, null, null);
					var signers = await GetSignersAsync(request.DepartmentId, request.SignatureRequestId);
					Publish(request, SignatureRequestChanges.Expired, signers, null);
					await NotifyClosedAsync(request, signers, null);
					expired++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A signature request could not be expired.");
				}
			}

			return expired;
		}

		#endregion

		#region Helpers

		private string IssueLink(SignatureRequestSigner signer, SignatureRequestRecord request)
		{
			var token = SignatureTokens.NewToken();
			signer.LinkTokenHash = SignatureTokens.Hash(token);
			signer.LinkTokenExpiresOn = request.ExpiresOn;
			signer.LinkTokenUsedOn = null;
			return token;
		}

		private async Task CloseLinksAsync(SignatureRequestRecord request, CancellationToken cancellationToken)
		{
			foreach (var signer in await GetSignersAsync(request.DepartmentId, request.SignatureRequestId))
			{
				if (signer.LinkTokenHash == null && signer.SessionHash == null && signer.OtpHash == null && signer.NextReminderOn == null)
					continue;
				signer.LinkTokenExpiresOn = signer.LinkTokenExpiresOn.HasValue && signer.LinkTokenExpiresOn.Value > DateTime.UtcNow ? DateTime.UtcNow : signer.LinkTokenExpiresOn;
				signer.SessionExpiresOn = signer.SessionExpiresOn.HasValue && signer.SessionExpiresOn.Value > DateTime.UtcNow ? DateTime.UtcNow : signer.SessionExpiresOn;
				signer.OtpHash = null;
				// S3: a closed request reminds nobody.
				signer.NextReminderOn = null;
				await _signers.UpdateAsync(signer, cancellationToken, true);
			}
		}

		private async Task DeleteEnvelopeAsync(SignatureRequestRecord request)
		{
			if (string.IsNullOrWhiteSpace(request.ProviderEnvelopeId) || _provider == null || !_provider.IsAvailable)
				return;
			try
			{
				var token = await _accounts.GetApiTokenAsync(request.DepartmentId);
				if (!string.IsNullOrWhiteSpace(token))
					await _provider.DeleteEnvelopeAsync(token, request.ProviderEnvelopeId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An e-sign envelope could not be withdrawn.");
			}
		}

		private async Task<bool> SendLinkAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string token, string culture)
		{
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false);
				var url = (SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/Sign/" + token;
				if (signer.AuthMethod == (int)SignatureSignerAuthMethods.SmsOtp)
				{
					var phone = Decrypt(signer.ExternalPhoneEncrypted);
					if (phone == null)
						return false;
					var number = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(request.DepartmentId);
					return await _textMessages.SendTextMessage(phone, SystemMessagesResources.Get("SigningLinkSmsBody", culture, department?.Name, request.Title, url),
						number, MobileCarriers.None, request.DepartmentId, false, false);
				}

				var email = Decrypt(signer.ExternalEmailEncrypted);
				if (email == null)
					return false;
				using var mail = new MailMessage();
				mail.To.Add(email);
				mail.From = new MailAddress(OutboundEmailServerConfig.FromMail, "Resgrid");
				mail.Subject = SystemMessagesResources.Get("SigningLinkEmailSubject", culture, department?.Name);
				mail.Body = SystemMessagesResources.Get("SigningLinkEmailBody", culture, signer.ExternalName, department?.Name, request.Title, url,
					request.ExpiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
				mail.IsBodyHtml = false;
				return await _emailSender.SendEmail(mail);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A signing link could not be sent.");
				return false;
			}
		}

		private string Decrypt(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			try { return _encryption.Decrypt(value); }
			catch (Exception ex) { Logging.LogException(ex, "An encrypted signer value could not be read."); return null; }
		}

		/// <summary>The sealed-input PDF the platform shows: the subject's document, then a signature page with the statement.</summary>
		private byte[] RenderSigningInput(SignatureSubjectInfo info, SignatureRequestRecord request, string culture, out int signaturePage)
		{
			var html = SignatureEvidenceDocument.SigningInput(info.DocumentHtml, request, culture);
			var pdf = _pdf.ConvertHtmlToPdf(html, "Letter");
			signaturePage = Math.Max(1, PdfPageCount(pdf));
			return pdf;
		}

		/// <summary>Pages in a PDF NReco produced (its page objects are not compressed into object streams).</summary>
		public static int PdfPageCount(byte[] pdf)
		{
			if (pdf == null || pdf.Length == 0)
				return 0;
			var text = Encoding.Latin1.GetString(pdf);
			return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?![s\w])", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2)).Count;
		}

		private static string Statement(SignatureSubjectKinds kind, SignatureSubjectInfo info, string culture) =>
			SignatureTexts.Get("Statement" + kind, culture, info.Title, SignaturePolicy.ShortCode(info.RevisionSha256));

		private static string FileNameFor(SignatureRequestRecord request) =>
			((SignatureSubjectKinds)request.SubjectKind).ToString().ToLowerInvariant() + "-" + request.SignatureRequestId.ToString(CultureInfo.InvariantCulture) + ".pdf";

		private static string LanguageOf(string culture)
		{
			if (string.IsNullOrWhiteSpace(culture))
				return "en";
			var separator = culture.IndexOfAny(new[] { '-', '_' });
			return (separator > 0 ? culture.Substring(0, separator) : culture).ToLowerInvariant();
		}

		public static string NormalizeEmail(string email)
		{
			var value = (email ?? string.Empty).Trim();
			if (value.Length == 0 || value.Length > 254)
				return null;
			try { return new MailAddress(value).Address == value ? value : null; }
			catch (FormatException) { return null; }
		}

		/// <summary>E.164: a plus sign and 8 to 15 digits (spaces, dashes and brackets removed).</summary>
		public static string NormalizePhone(string phone)
		{
			var value = new string((phone ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
			if (!value.StartsWith("+", StringComparison.Ordinal) || value.Count(c => c == '+') != 1)
				return null;
			var digits = value.Length - 1;
			return digits >= 8 && digits <= 15 ? value : null;
		}

		private static string Clean(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		private void Audit(SignatureRequestRecord request, string userId, AuditLogTypes type, string ipAddress, string userAgent)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = request.DepartmentId,
				UserId = userId,
				Type = type,
				After = JsonConvert.SerializeObject(new
				{
					request.SignatureRequestId,
					request.SubjectKind,
					request.SubjectId,
					request.ArtifactSha256,
					request.Channel,
					request.Status,
					request.ProviderKind,
					request.ProviderEnvelopeId,
					request.ExpiresOn,
					request.SealedDocumentId,
					request.VoidReason
				}),
				Successful = true,
				IpAddress = ipAddress,
				UserAgent = userAgent,
				ServerName = Environment.MachineName
			});
		}

		/// <summary>Workflow events 220-223. The payload names the subject and counts only: never a signer's name, e-mail or phone.</summary>
		private void Publish(SignatureRequestRecord request, SignatureRequestChanges change, List<SignatureRequestSigner> signers, string userId)
		{
			_eventAggregator?.SendMessage<SignatureRequestEvent>(BuildEvent(request, change, signers, userId));
		}

		public static SignatureRequestEvent BuildEvent(SignatureRequestRecord request, SignatureRequestChanges change, List<SignatureRequestSigner> signers, string userId)
		{
			return new SignatureRequestEvent
			{
				DepartmentId = request.DepartmentId,
				Change = change,
				SignatureRequestId = request.SignatureRequestId,
				SubjectKind = ((SignatureSubjectKinds)request.SubjectKind).ToString(),
				SubjectId = request.SubjectId,
				Title = request.Title,
				Channel = ((SignatureRequestChannels)request.Channel).ToString(),
				Status = ((SignatureRequestStatuses)request.Status).ToString(),
				SignerCount = signers?.Count ?? 0,
				SignedCount = signers?.Count(s => s.Status == (int)SignatureSignerStatuses.Signed) ?? 0,
				ContentCode = SignaturePolicy.ShortCode(request.ArtifactSha256),
				ExpiresOn = request.ExpiresOn,
				OccurredOn = DateTime.UtcNow,
				SealedDocumentId = request.SealedDocumentId,
				UserId = userId
			};
		}

		#endregion
	}
}
