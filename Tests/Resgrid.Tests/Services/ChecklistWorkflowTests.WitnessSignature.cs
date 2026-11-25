using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Signatures;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// e-Signature S3 checklist witness signature (free with Checklists): a form that requires it is never witnessed with text
	/// alone; the witness signs at L2 inside the witness transaction, bound to the completion's operational content hash, and a
	/// refused signature leaves the run awaiting its witness. The hash carries no ADP-private member (notes, attestations, free text).
	/// </summary>
	public partial class ChecklistWorkflowTests
	{
		private (ChecklistWitnessSignatureService Witness, List<SignatureRecord> Records) WitnessSigning()
		{
			var records = new List<SignatureRecord>();
			var repository = new Mock<ISignatureRecordsRepository>();
			repository.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => { r.SignatureRecordId = records.Count + 1; records.Add(r); return r; });
			repository.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => records.Where(r => r.DepartmentId == d && r.SubjectKind == k && r.SubjectId == s).ToList());
			var signatures = new SignatureService(repository.Object, Mock.Of<IProtocolVersionsRepository>(), Mock.Of<IPersonnelCertificationRepository>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IEventAggregator>(), Mock.Of<IProtocolVersionFilesRepository>());
			return (new ChecklistWitnessSignatureService(_service, signatures, Mock.Of<IEventAggregator>()), records);
		}

		private async Task<(string Run, ChecklistActor Witness, string SubmissionHash)> AwaitingSignedWitnessAsync()
		{
			var form = Form(true);
			form.RequireWitnessSignature = true;
			var definition = await _service.SaveDefinitionAsync(_actor, null, 0, form);
			await _service.PublishAsync(_actor, definition, 1);
			var run = await _service.StartAsync(_actor, definition, "77", Guid.NewGuid().ToString());
			await _service.SaveRunAsync(_actor, run, Answers(form), true);
			var witness = new ChecklistActor { DepartmentId = 77, UserId = "witness" };
			_authorization.Setup(a => a.CanReadAsync(witness, It.IsAny<ChecklistCompletion>())).ReturnsAsync(true);
			var current = await _service.GetRunAsync(_actor, run);
			current.Completion.State.Should().Be((int)ChecklistRunState.AwaitingWitness);
			return (run, witness, current.Completion.SubmissionHash);
		}

		[Test]
		public async Task A_form_requiring_a_witness_signature_is_never_witnessed_with_text_alone()
		{
			var (run, witness, hash) = await AwaitingSignedWitnessAsync();
			Func<Task> textOnly = () => _service.WitnessAsync(witness, run, hash, "I watched the count.");
			(await textOnly.Should().ThrowAsync<ChecklistException>()).Which.Message.Should().Be("WitnessSignatureRequired");
			(await _service.GetRunAsync(_actor, run)).Completion.State.Should().Be((int)ChecklistRunState.AwaitingWitness);
		}

		[Test]
		public async Task The_witness_signs_at_L2_bound_to_the_operational_hash_and_a_refused_signature_completes_nothing()
		{
			var (run, witness, hash) = await AwaitingSignedWitnessAsync();
			var (signing, records) = WitnessSigning();

			// The session alone is L1: refused, and the run still awaits its witness.
			var refused = await signing.SignAsync(witness, run, hash, "I watched the count.", SignatureProof.SessionOnly("witness"), "en");
			refused.Should().Be(SignatureOutcomes.StepUpRequired);
			records.Should().BeEmpty();
			(await _service.GetRunAsync(_actor, run)).Completion.State.Should().Be((int)ChecklistRunState.AwaitingWitness);

			// Someone else's proof never signs for the witness.
			(await signing.SignAsync(witness, run, hash, "x", SignatureProof.SessionOnly("author"), "en")).Should().Be(SignatureOutcomes.InvalidRequest);

			var proof = SignatureProof.SessionOnly("witness");
			proof.StepUpVerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1);
			(await signing.SignAsync(witness, run, hash, "I watched the count.", proof, "en")).Should().Be(SignatureOutcomes.Signed);

			var completed = await _service.GetRunAsync(_actor, run);
			completed.Completion.State.Should().Be((int)ChecklistRunState.Submitted);
			completed.Completion.WitnessUserId.Should().Be("witness");
			var record = records.Single();
			record.SubjectKind.Should().Be((int)SignatureSubjectKinds.ChecklistWitness);
			record.SubjectId.Should().Be(completed.Completion.Id);
			record.Level.Should().Be((int)SignatureLevels.StepUp);
			record.Intent.Should().Be((int)SignatureIntents.Witness);
			record.ArtifactSha256.Should().Be(await _service.WitnessArtifactHashAsync(_actor, run));
			record.StatementText.Should().Contain(SignaturePolicy.ShortCode(record.ArtifactSha256));

			var view = await signing.GetAsync(_actor, run, "en");
			view.Required.Should().BeTrue();
			view.AwaitingWitness.Should().BeFalse();
			view.Signatures.Single().State.Should().Be(SignatureVerificationStates.Valid);
			view.CanSign.Should().BeFalse();
		}

		[Test]
		public void The_witness_hash_binds_to_operational_content_and_never_to_private_text()
		{
			var typed = Guid.NewGuid().ToString("D");
			var freeText = Guid.NewGuid().ToString("D");
			var form = new ChecklistForm
			{
				Sections = { new ChecklistSection { Items = { new ChecklistItem { Id = typed, Type = ChecklistItemType.NumericReading }, new ChecklistItem { Id = freeText, Type = ChecklistItemType.FreeText } } } }
			};
			ChecklistCompletion Completion(decimal score) => new ChecklistCompletion
			{
				Id = "C0A80001-0000-4000-8000-000000000001", DepartmentId = 77, ParentId = "def", VersionId = "ver", TargetType = 1, TargetId = "12", CreatedBy = "Author",
				SubmittedOn = new DateTime(2026, 11, 25, 8, 30, 0), Score = score, Passed = true, Content = "{\"Note\":\"Patient Jane Doe was on scene\",\"WitnessAttestation\":\"Seen by Bob\"}"
			};
			List<ChecklistAnswer> Answered(string reading, string text, string note) => new List<ChecklistAnswer>
			{
				new ChecklistAnswer { ItemId = typed, Status = ChecklistAnswerStatus.Answered, Value = reading, Note = note },
				new ChecklistAnswer { ItemId = freeText, Status = ChecklistAnswerStatus.Answered, Value = text, NotApplicableReason = "n/a " + note }
			};
			var items = new[] { new ChecklistCompletionItem { ItemId = typed, IsFailure = false }, new ChecklistCompletionItem { ItemId = freeText, IsFailure = false } };
			var files = new[] { new ChecklistCompletionFile { Id = "f1", ItemId = typed, Sha256 = new string('a', 64), Content = "{\"FileName\":\"jane-doe.jpg\"}" } };
			var input = new ChecklistRunInput { Note = "Patient Jane Doe was on scene", LocationDescription = "Station 2 bay", Latitude = 33.1m, Longitude = -111.9m };

			var baseline = ChecklistWitnessArtifact.Hash(Completion(100), form, Answered("42", "Jane Doe's oxygen tank", "John Roe checked it"), items, files, input);
			var canonical = ChecklistWitnessArtifact.Canonical(Completion(100), form, Answered("42", "Jane Doe's oxygen tank", "John Roe checked it"), items, files, input);

			// Private members (the note, the attestation, a free-text value, item notes, not-applicable reasons, file names) never enter it.
			canonical.Should().NotContain("Jane").And.NotContain("John").And.NotContain("Bob").And.NotContain("jane-doe").And.NotContain("n/a");
			canonical.Should().Contain("\"value\":\"42\"").And.Contain("Station 2 bay");

			// So changing them leaves the hash alone: the hash is the same for every reader and needs no protected-data grant.
			var otherInput = new ChecklistRunInput { Note = "Different note", LocationDescription = "Station 2 bay", Latitude = 33.1m, Longitude = -111.9m };
			ChecklistWitnessArtifact.Hash(Completion(100), form, Answered("42", "Something else", "another note"), items, files, otherInput).Should().Be(baseline);

			// Operational content binds: a typed reading, the score, an evidence file's bytes.
			ChecklistWitnessArtifact.Hash(Completion(100), form, Answered("43", "Jane Doe's oxygen tank", "John Roe checked it"), items, files, input).Should().NotBe(baseline);
			ChecklistWitnessArtifact.Hash(Completion(90), form, Answered("42", "Jane Doe's oxygen tank", "John Roe checked it"), items, files, input).Should().NotBe(baseline);
			ChecklistWitnessArtifact.Hash(Completion(100), form, Answered("42", "Jane Doe's oxygen tank", "John Roe checked it"), items,
				new[] { new ChecklistCompletionFile { Id = "f1", ItemId = typed, Sha256 = new string('b', 64) } }, input).Should().NotBe(baseline);
			SignaturePolicy.IsSha256(baseline).Should().BeTrue();
		}
	}
}
