using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The one signing layer (S0). The level is decided here from server-side proof only: a step-up recorded for the session
	/// within <see cref="Config.TwoFactorConfig.StepUpVerificationWindowMinutes"/>, or a passkey assertion verified within
	/// <see cref="PasskeyWindow"/> whose challenge embedded exactly the artifact hash being signed. Anything less is L1.
	/// </summary>
	public class SignatureService : ISignatureService
	{
		/// <summary>A signing passkey assertion counts for this long after it was verified (one signing flow, not a session).</summary>
		public static readonly TimeSpan PasskeyWindow = TimeSpan.FromMinutes(5);

		private readonly ISignatureRecordsRepository _signatures;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IPersonnelCertificationRepository _certifications;
		private readonly IUserProfileService _profiles;
		private readonly IPersonnelRolesService _roles;
		private readonly IEventAggregator _eventAggregator;
		private readonly IProtocolVersionFilesRepository _versionFiles;

		public SignatureService(ISignatureRecordsRepository signatures, IProtocolVersionsRepository versions, IPersonnelCertificationRepository certifications,
			IUserProfileService profiles, IPersonnelRolesService roles, IEventAggregator eventAggregator, IProtocolVersionFilesRepository versionFiles)
		{
			_versionFiles = versionFiles;
			_signatures = signatures;
			_versions = versions;
			_certifications = certifications;
			_profiles = profiles;
			_roles = roles;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The level and method the proof establishes for this artifact (pure, for tests and the panel).</summary>
		public static (SignatureLevels Level, SignatureMethods Method, string Evidence, SignatureOutcomes? Refusal) Assess(SignatureRequest request, SignatureProof proof, DateTime utcNow)
		{
			if (proof == null)
				return (SignatureLevels.Attest, SignatureMethods.Session, null, null);

			if (!string.IsNullOrWhiteSpace(proof.PasskeyId))
			{
				if (!string.Equals(proof.PasskeyBoundSha256, request?.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
					return (SignatureLevels.Attest, SignatureMethods.Session, null, SignatureOutcomes.ArtifactMismatch);

				if (proof.PasskeyVerifiedOnUtc.HasValue && Fresh(proof.PasskeyVerifiedOnUtc.Value, PasskeyWindow, utcNow))
					return (SignatureLevels.StepUp, SignatureMethods.Passkey, "passkey-challenge:" + proof.PasskeyChallengeId, null);
			}

			var window = TimeSpan.FromMinutes(Math.Max(1, Config.TwoFactorConfig.StepUpVerificationWindowMinutes));
			if (proof.StepUpVerifiedOnUtc.HasValue && Fresh(proof.StepUpVerifiedOnUtc.Value, window, utcNow))
				return (SignatureLevels.StepUp, SignatureMethods.StepUp,
					"mfa:" + (proof.StepUpMethod?.ToString(CultureInfo.InvariantCulture) ?? "?") + ":" + DateTime.SpecifyKind(proof.StepUpVerifiedOnUtc.Value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture), null);

			return (SignatureLevels.Attest, SignatureMethods.Session, null, null);
		}

		private static bool Fresh(DateTime verifiedOnUtc, TimeSpan window, DateTime utcNow)
		{
			var verified = DateTime.SpecifyKind(verifiedOnUtc, DateTimeKind.Utc);
			// A few seconds ahead (another node's clock) is now; anything further in the future is not proof.
			return verified <= utcNow.AddSeconds(30) && utcNow - verified <= window;
		}

		public async Task<SignatureResult> SignAsync(SignatureRequest request, SignatureProof proof, CancellationToken cancellationToken = default)
		{
			if (request?.Use == null || request.DepartmentId <= 0 || proof == null || string.IsNullOrWhiteSpace(proof.UserId) ||
				!SignaturePolicy.IsSha256(request.ArtifactSha256) || string.IsNullOrWhiteSpace(request.StatementText) || string.IsNullOrWhiteSpace(request.SubjectId))
				return SignatureResult.Of(SignatureOutcomes.InvalidRequest);

			var now = DateTime.UtcNow;
			var required = SignaturePolicy.RequiredLevel(request.Use, request.DepartmentLevel);
			var (level, method, evidence, refusal) = Assess(request, proof, now);

			if (refusal.HasValue)
				return SignatureResult.Of(refusal.Value);

			if (!SignaturePolicy.Satisfies(required, request.RequirePasskey, level, method))
			{
				if (request.RequirePasskey && method != SignatureMethods.Passkey)
					return SignatureResult.Of(SignatureOutcomes.PasskeyRequired);
				return SignatureResult.Of(SignatureOutcomes.StepUpRequired);
			}

			var record = new SignatureRecord
			{
				DepartmentId = request.DepartmentId,
				SubjectKind = (int)request.Use.SubjectKind,
				SubjectId = Truncate(request.SubjectId, SignatureRecord.SubjectIdMaxLength),
				RevisionId = Truncate(request.RevisionId, SignatureRecord.SubjectIdMaxLength),
				SignerUserId = proof.UserId,
				SignerNameSnapshot = Truncate(await NameAsync(proof.UserId), 256),
				SignerRoleSnapshot = Truncate(await RolesAsync(proof.UserId, request.DepartmentId), 1000),
				Intent = (int)request.Use.Intent,
				StatementVersion = request.Use.StatementVersion,
				StatementText = Truncate(request.StatementText.Trim(), SignatureRecord.StatementMaxLength),
				Level = (int)level,
				Method = (int)method,
				ArtifactSha256 = request.ArtifactSha256.ToLowerInvariant(),
				SignedOn = now,
				IpAddress = Truncate(proof.IpAddress, 64),
				UserAgent = Truncate(proof.UserAgent, 1000),
				PasskeyCredentialId = method == SignatureMethods.Passkey ? Truncate(proof.PasskeyId, 128) : null,
				StepUpEvidenceReference = Truncate(evidence, 256),
				// S3: a member's step of a multi-signer request is linked to it (the request's evidence lists it).
				SignatureRequestId = request.SignatureRequestId > 0 ? request.SignatureRequestId : null,
				CreatedOn = now
			};

			var saved = await _signatures.InsertAsync(record, cancellationToken, true);
			Audit(saved.DepartmentId, saved.SignerUserId, AuditLogTypes.SignatureRecorded, null, saved, proof);

			return new SignatureResult { Outcome = SignatureOutcomes.Signed, Record = saved };
		}

		public SignatureVerification Verify(SignatureRecord record, string currentArtifactSha256)
		{
			if (record == null)
				return null;

			var verification = new SignatureVerification { Record = record, CurrentSha256 = currentArtifactSha256 };
			if (record.VoidedOn.HasValue)
				verification.State = SignatureVerificationStates.Voided;
			else if (string.IsNullOrWhiteSpace(currentArtifactSha256))
				verification.State = SignatureVerificationStates.SubjectMissing;
			else if (string.Equals(record.ArtifactSha256, currentArtifactSha256, StringComparison.OrdinalIgnoreCase))
				verification.State = SignatureVerificationStates.Valid;
			else
				verification.State = SignatureVerificationStates.ArtifactChanged;

			return verification;
		}

		public async Task<SignatureVerification> VerifyAsync(int departmentId, int signatureRecordId)
		{
			var record = await GetByIdAsync(departmentId, signatureRecordId);
			if (record == null)
				return null;

			return Verify(record, await CurrentHashAsync(record));
		}

		/// <summary>The artifact's hash recomputed from the subject as stored now; null when the subject is gone.</summary>
		private async Task<string> CurrentHashAsync(SignatureRecord record)
		{
			if (!int.TryParse(record.SubjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
				return null;

			switch ((SignatureSubjectKinds)record.SubjectKind)
			{
				case SignatureSubjectKinds.ProtocolVersion:
				case SignatureSubjectKinds.ProtocolReview:
				case SignatureSubjectKinds.ProtocolAcknowledgment:
					var version = await _versions.GetByIdForDepartmentAsync(record.DepartmentId, id);
					// Recomputed from the stored HTML (or a document-backed version's file bytes), never read from the stored hash
					// column: a changed text or a swapped file must fail.
					return version == null ? null : await ProtocolVersionHashing.CurrentHashAsync(version, _versionFiles);
				case SignatureSubjectKinds.CertificationVerification:
					var certification = await _certifications.GetByIdAsync(id);
					return certification == null || certification.DepartmentId != record.DepartmentId ? null : CertificationArtifactSha256(certification);
				default:
					return null;
			}
		}

		public string CertificationArtifactSha256(PersonnelCertification certification)
		{
			if (certification == null)
				return null;

			// Protected text (number, issuer) and the document bytes can be re-sealed without the record changing, so the artifact
			// is what an officer verifies that never moves: whose record, of which type, issued and expiring when.
			var artifact = JsonConvert.SerializeObject(new
			{
				kind = "certification-verification/1",
				id = certification.PersonnelCertificationId,
				department = certification.DepartmentId,
				holder = (certification.UserId ?? string.Empty).ToLowerInvariant(),
				type = certification.DepartmentCertificationTypeId,
				received = certification.RecievedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				expires = certification.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			});
			return SignaturePolicy.HashArtifact(artifact);
		}

		public async Task<SignatureRecord> VoidAsync(int departmentId, int signatureRecordId, string userId, string reason, CancellationToken cancellationToken = default)
		{
			var record = await GetByIdAsync(departmentId, signatureRecordId);
			if (record == null || string.IsNullOrWhiteSpace(reason))
				return null;

			if (record.VoidedOn.HasValue)
				return record;

			var before = JsonConvert.SerializeObject(new { record.SignatureRecordId, record.SubjectKind, record.SubjectId, record.VoidedOn });
			record.VoidedOn = DateTime.UtcNow;
			record.VoidedByUserId = userId;
			record.VoidReason = Truncate(reason.Trim(), 1000);

			var saved = await _signatures.SaveOrUpdateAsync(record, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.SignatureVoided, before, saved, null);
			return saved;
		}

		public async Task<SignatureRecord> GetByIdAsync(int departmentId, int signatureRecordId)
		{
			if (departmentId <= 0 || signatureRecordId <= 0)
				return null;

			var record = await _signatures.GetByIdForDepartmentAsync(departmentId, signatureRecordId);
			return record != null && record.DepartmentId == departmentId ? record : null;
		}

		public async Task<List<SignatureRecord>> GetForSubjectAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return new List<SignatureRecord>();

			return (await _signatures.GetForSubjectAsync(departmentId, (int)subjectKind, subjectId) ?? new List<SignatureRecord>())
				.Where(x => x.DepartmentId == departmentId)
				.ToList();
		}

		public async Task<List<SignatureVerification>> GetVerifiedForSubjectAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId)
		{
			var records = await GetForSubjectAsync(departmentId, subjectKind, subjectId);
			if (records.Count == 0)
				return new List<SignatureVerification>();

			// One subject: its current hash is computed once for every signature on it.
			var current = await CurrentHashAsync(records[0]);
			return records.Select(r => Verify(r, current)).ToList();
		}

		private async Task<string> NameAsync(string userId)
		{
			try
			{
				var profile = await _profiles.GetProfileByUserIdAsync(userId);
				var name = profile?.FullName?.AsFirstNameLastName?.Trim();
				return string.IsNullOrWhiteSpace(name) ? null : name;
			}
			catch (Exception ex)
			{
				// The signature stands on the user id; the name is a convenience snapshot.
				Logging.LogException(ex, "Signer name snapshot could not be read.");
				return null;
			}
		}

		private async Task<string> RolesAsync(string userId, int departmentId)
		{
			try
			{
				var roles = await _roles.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();
				var names = roles.Where(r => r != null && r.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Name.Trim()).Distinct().OrderBy(x => x).ToList();
				return names.Count == 0 ? null : string.Join(", ", names);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Signer role snapshot could not be read.");
				return null;
			}
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, SignatureRecord after, SignatureProof proof)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after == null ? null : JsonConvert.SerializeObject(new
				{
					after.SignatureRecordId,
					after.SubjectKind,
					after.SubjectId,
					after.RevisionId,
					after.Intent,
					after.Level,
					after.Method,
					after.ArtifactSha256,
					after.StatementVersion,
					after.VoidedOn,
					after.VoidReason
				}),
				Successful = true,
				IpAddress = proof?.IpAddress ?? after?.IpAddress,
				ServerName = Environment.MachineName,
				UserAgent = proof?.UserAgent ?? after?.UserAgent
			});
		}

		private static string Truncate(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
				return value;
			return value.Length > max ? value.Substring(0, max) : value;
		}
	}
}
