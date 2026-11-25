using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// The checklist witness signature (S3, free with Checklists, decision §25.4a item 13): when a form requires it, the independent
	/// witness signs at L2 — a fresh step-up or a passkey bound to the content — over the completion's operational content hash
	/// (<see cref="ChecklistWitnessArtifact"/>: no private note, attestation or free text). The signature is recorded inside the
	/// checklist's witness transaction, so the run completes only with it and a refused signature leaves the run awaiting its witness.
	/// </summary>
	public class ChecklistWitnessSignatureService : IChecklistWitnessSignatureService
	{
		private readonly IChecklistsService _checklists;
		private readonly ISignatureService _signatures;
		private readonly IEventAggregator _eventAggregator;

		public ChecklistWitnessSignatureService(IChecklistsService checklists, ISignatureService signatures, IEventAggregator eventAggregator)
		{
			_checklists = checklists;
			_signatures = signatures;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The statement the witness signs (stored with the signature).</summary>
		public static string Statement(ChecklistRunView run, string artifactSha256, string culture) =>
			SignatureTexts.Get("StatementChecklistWitness", culture, run?.Form?.Name ?? string.Empty, run?.Target?.Name ?? run?.Completion?.TargetId ?? string.Empty,
				SignaturePolicy.ShortCode(artifactSha256));

		public async Task<ChecklistWitnessSignatureView> GetAsync(ChecklistActor actor, string completionId, string culture)
		{
			var run = await _checklists.GetRunAsync(actor, completionId);
			var view = new ChecklistWitnessSignatureView
			{
				Required = run.Form.RequiresIndependentWitness && run.Form.RequireWitnessSignature,
				AwaitingWitness = run.Completion.State == (int)ChecklistRunState.AwaitingWitness,
				SubmissionHash = run.Completion.SubmissionHash
			};
			if (run.Completion.State == (int)ChecklistRunState.InProgress)
				return view;

			view.ArtifactSha256 = await _checklists.WitnessArtifactHashAsync(actor, completionId);
			view.Statement = Statement(run, view.ArtifactSha256, culture);
			view.CanSign = view.Required && view.AwaitingWitness && !string.Equals(run.Completion.CreatedBy, actor.UserId, StringComparison.OrdinalIgnoreCase);
			var records = await _signatures.GetForSubjectAsync(actor.DepartmentId, SignatureSubjectKinds.ChecklistWitness, run.Completion.Id);
			view.Signatures = records.Select(r => _signatures.Verify(r, view.ArtifactSha256)).ToList();
			return view;
		}

		public async Task<SignatureOutcomes> SignAsync(ChecklistActor actor, string completionId, string submissionHash, string attestation, SignatureProof proof, string culture,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || proof == null || !string.Equals(proof.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				return SignatureOutcomes.InvalidRequest;

			var outcome = SignatureOutcomes.InvalidRequest;
			SignatureRecord signed = null;
			try
			{
				await _checklists.WitnessWithSignatureAsync(actor, completionId, submissionHash, attestation, async (run, artifactSha256) =>
				{
					var result = await _signatures.SignAsync(new SignatureRequest
					{
						DepartmentId = actor.DepartmentId,
						Use = SignatureUse.ChecklistWitness,
						SubjectId = run.Completion.Id,
						RevisionId = run.Completion.SubmissionHash,
						ArtifactSha256 = artifactSha256,
						StatementText = Statement(run, artifactSha256, culture)
					}, proof, cancellationToken);
					outcome = result?.Outcome ?? SignatureOutcomes.InvalidRequest;
					signed = result?.Record;
					return result?.Succeeded == true;
				});
			}
			catch (ChecklistException ex) when (ex.Message == "WitnessSignatureRefused")
			{
				// The witness transaction rolled back (the signature row with it); the outcome says why.
				return outcome == SignatureOutcomes.Signed ? SignatureOutcomes.InvalidRequest : outcome;
			}

			if (signed != null)
				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					Type = AuditLogTypes.ChecklistWitnessSigned,
					After = JsonConvert.SerializeObject(new { CompletionId = completionId, signed.SignatureRecordId, signed.Level, signed.Method, signed.ArtifactSha256 }),
					Successful = true,
					IpAddress = proof.IpAddress,
					UserAgent = proof.UserAgent,
					ServerName = Environment.MachineName
				});

			// An idempotent replay (already witnessed by this member with the same attestation) completes without a new signature.
			return SignatureOutcomes.Signed;
		}
	}
}
