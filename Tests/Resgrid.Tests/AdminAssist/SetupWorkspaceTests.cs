using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class SetupWorkspaceTests
	{
		private readonly DateTime _now = new(2026,9,24,12,0,0,DateTimeKind.Utc);
		private ConfigurationCatalog _catalog;
		private Mock<IAdminAssistRepository> _repository;
		private Mock<IConfigurationSnapshotProvider> _snapshots;
		private AdminAssistService _service;
		private SetupProgressCommand _saved;
		private static readonly AdminAssistActor Actor = new(7,"admin");
		[SetUp]
		public void SetUp()
		{
			_catalog = new(); _repository = new(); _snapshots = new(); _saved = null;
			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(Actor,true,It.IsAny<CancellationToken>())).ReturnsAsync(true);
			access.Setup(a => a.GetCapabilitiesAsync(Actor,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CapabilityAccess>());
			var workspace = new SetupWorkspace(7,0,SetupMode.Fresh,new Dictionary<string,SetupAreaChoice>{{"security",SetupAreaChoice.UseNow}},Array.Empty<string>(),Array.Empty<string>(),_catalog.Version,null);
			_repository.Setup(r => r.GetWorkspaceAsync(7,"admin",_catalog.Version,It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
			_repository.Setup(r => r.UpdateWorkspaceAsync(Actor,It.IsAny<SetupProgressCommand>(),It.IsAny<CancellationToken>()))
				.Callback<AdminAssistActor,SetupProgressCommand,CancellationToken>((actor,command,token) => _saved=command).ReturnsAsync(workspace);
			_snapshots.Setup(s => s.ReadAsync(Actor,It.IsAny<CancellationToken>())).ReturnsAsync(new ConfigurationSnapshot(7,"admin","12",_now,true,new Dictionary<string,ConfigurationEvidence>()));
			_service = new(access.Object,_catalog,_snapshots.Object,_repository.Object,new FixedTime(_now));
		}
		[Test]
		public void Legacy_area_choices_upgrade_without_resetting_department_scope()
		{
			var state = SetupWorkspaceMetadata.Read("{\"home\":0,\"security\":0,\"people\":1}");
			Assert.That(state.Areas["people"], Is.EqualTo(SetupAreaChoice.LearnLater));
			state.Areas["inventory"] = SetupAreaChoice.NotApplicable; state.AreaReasons["inventory"] = SetupAreaReason.OtherSystem;
			state.ReviewEvidence = new("version","12",_now,4,1,1,2); state.RevisitOnUtc = _now.AddDays(30);
			var restored = SetupWorkspaceMetadata.Read(state.Serialize());
			Assert.That(restored.Areas, Is.EquivalentTo(state.Areas)); Assert.That(restored.AreaReasons, Is.EquivalentTo(state.AreaReasons));
			Assert.That(restored.ReviewEvidence, Is.EqualTo(state.ReviewEvidence)); Assert.That(restored.RevisitOnUtc, Is.EqualTo(state.RevisitOnUtc));
		}
		[TestCase(null)] [TestCase("a private free-text reason")]
		public void Not_applicable_requires_an_allowlisted_reason(string reason)
		{
			Assert.ThrowsAsync<ArgumentException>(async () => await _service.UpdateSetupAsync(Actor,new(0,"area","inventory","NotApplicable",_catalog.Version,reason)));
			Assert.That(_saved, Is.Null);
		}
		[Test]
		public async Task Scope_reason_is_recorded_but_cannot_defer_baseline_security()
		{
			await _service.UpdateSetupAsync(Actor,new(0,"area","inventory","NotApplicable",_catalog.Version,"OtherSystem"));
			Assert.That(_saved.ReasonCode, Is.EqualTo("OtherSystem"));
			Assert.ThrowsAsync<ArgumentException>(async () => await _service.UpdateSetupAsync(Actor,new(0,"area","security","NotApplicable",_catalog.Version,"OtherSystem")));
		}
		[Test]
		public async Task Review_records_server_evaluated_evidence_and_preserves_unresolved_unknowns()
		{
			await _service.UpdateSetupAsync(Actor,new(0,"review",CatalogVersion:_catalog.Version,EvidenceRevision:"12")
				{ ReviewEvidence=new("forged","0",_now,1,1,0,0) });
			Assert.That(_saved.ReviewEvidence.SnapshotRevision, Is.EqualTo("12")); Assert.That(_saved.ReviewEvidence.CatalogVersion, Is.EqualTo(_catalog.Version));
			Assert.That(_saved.ReviewEvidence.Unknown, Is.GreaterThan(0)); Assert.That(_saved.ReviewEvidence.Verified, Is.EqualTo(0));
		}
		[Test]
		public void A_changed_configuration_must_be_reloaded_before_recording_a_review()
		{
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await _service.UpdateSetupAsync(Actor,new(0,"review",CatalogVersion:_catalog.Version,EvidenceRevision:"11")));
			Assert.That(_saved, Is.Null);
		}
		[Test]
		public async Task Revisit_date_is_metadata_only_and_can_be_cleared()
		{
			await _service.UpdateSetupAsync(Actor,new(0,"revisit",CatalogVersion:_catalog.Version,RevisitOnUtc:_now.AddDays(30)));
			Assert.That(_saved.RevisitOnUtc, Is.EqualTo(_now.AddDays(30)));
			await _service.UpdateSetupAsync(Actor,new(0,"revisit",CatalogVersion:_catalog.Version)); Assert.That(_saved.RevisitOnUtc, Is.Null);
			Assert.ThrowsAsync<ArgumentException>(async () => await _service.UpdateSetupAsync(Actor,new(0,"revisit",CatalogVersion:_catalog.Version,RevisitOnUtc:_now.AddDays(-1))));
			Assert.ThrowsAsync<ArgumentException>(async () => await _service.UpdateSetupAsync(Actor,new(0,"revisit",CatalogVersion:_catalog.Version,RevisitOnUtc:_now.AddDays(366))));
		}
		[Test]
		public void Workspace_revision_cannot_overflow()
		{
			Assert.ThrowsAsync<ArgumentException>(async () => await _service.UpdateSetupAsync(Actor,new(long.MaxValue,"mode",Choice:"Fresh",CatalogVersion:_catalog.Version)));
			Assert.That(_saved, Is.Null);
		}
		[Test]
		public void Public_serializers_cannot_accept_a_supplied_review_result()
		{
			var json = "{\"ExpectedRevision\":0,\"Operation\":\"review\",\"ReviewEvidence\":{\"CatalogVersion\":\"forged\",\"Verified\":999}}";
			Assert.That(System.Text.Json.JsonSerializer.Deserialize<SetupProgressCommand>(json).ReviewEvidence, Is.Null);
			Assert.That(Newtonsoft.Json.JsonConvert.DeserializeObject<SetupProgressCommand>(json).ReviewEvidence, Is.Null);
		}
		private sealed class FixedTime(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
	}
}
