using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class SettingsEvidenceTests
	{
		private const int DepartmentId = 7;
		private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

		private static async Task<IReadOnlyList<ConfigurationEvidence>> Read(params DepartmentSetting[] rows)
		{
			var settings = new Mock<IDepartmentSettingsRepository>();
			settings.Setup(s => s.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(rows);
			return await new SettingsEvidenceSource(settings.Object).ReadAsync(new(DepartmentId, "admin"), Now, CancellationToken.None);
		}

		private static DepartmentSetting Policy(string value) =>
			new() { DepartmentId = DepartmentId, SettingType = (int)DepartmentSettingTypes.NewCallFieldPolicy, Setting = value };

		private static DepartmentSetting Policy(params NewCallFieldRule[] rules) =>
			Policy(ObjectSerialization.Serialize(new NewCallFieldPolicy { Rules = rules.ToList() }));

		private static ConfigurationEvidence Hidden(IReadOnlyList<ConfigurationEvidence> evidence) =>
			evidence.Single(e => e.Id == SettingsEvidenceSource.NewCallDispatchListsHiddenId);

		private static NewCallFieldRule HiddenRule(string key) => new() { Key = key, Visible = false };

		[Test]
		public async Task Every_declared_evidence_id_is_read_once()
		{
			var source = new SettingsEvidenceSource(Mock.Of<IDepartmentSettingsRepository>(s =>
				s.GetAllByDepartmentIdAsync(DepartmentId) == Task.FromResult<IEnumerable<DepartmentSetting>>(Array.Empty<DepartmentSetting>())));
			var evidence = await source.ReadAsync(new(DepartmentId, "admin"), Now, CancellationToken.None);
			Assert.That(evidence.Select(e => e.Id), Is.EquivalentTo(source.EvidenceIds));
		}

		[Test]
		public async Task No_new_call_field_policy_means_every_dispatch_list_is_shown()
		{
			var fact = Hidden(await Read());
			Assert.That(fact.State, Is.EqualTo(EvidenceState.Known));
			Assert.That(fact.Boolean, Is.False);
		}

		[Test]
		public async Task Hiding_all_four_dispatch_lists_is_reported()
		{
			var fact = Hidden(await Read(Policy(NewCallFieldKeys.DispatchTargets.Select(HiddenRule).ToArray())));
			Assert.That(fact.State, Is.EqualTo(EvidenceState.Known));
			Assert.That(fact.Boolean, Is.True);
		}

		[Test]
		public async Task A_legacy_hidden_dispatch_section_hides_every_list()
		{
			var fact = Hidden(await Read(Policy(HiddenRule(NewCallFieldKeys.DispatchList))));
			Assert.That(fact.Boolean, Is.True);
		}

		[Test]
		public async Task One_shown_list_keeps_the_form_dispatchable()
		{
			var rules = NewCallFieldKeys.DispatchTargets.Where(k => k != NewCallFieldKeys.DispatchUnits).Select(HiddenRule).ToArray();
			var fact = Hidden(await Read(Policy(rules)));
			Assert.That(fact.State, Is.EqualTo(EvidenceState.Known));
			Assert.That(fact.Boolean, Is.False);
		}

		[Test]
		public async Task Hiding_other_fields_does_not_report_hidden_dispatch_lists()
		{
			var fact = Hidden(await Read(Policy(HiddenRule(NewCallFieldKeys.Note), HiddenRule(NewCallFieldKeys.ContactInfo))));
			Assert.That(fact.Boolean, Is.False);
		}

		[Test]
		public async Task An_unreadable_policy_is_unknown_not_shown()
		{
			var fact = Hidden(await Read(Policy("not a serialized policy")));
			Assert.That(fact.State, Is.EqualTo(EvidenceState.Unknown));
			Assert.That(fact.Boolean, Is.Null);
			Assert.That(fact.ReasonCode, Is.EqualTo("InvalidStoredValue"));
		}

		[Test]
		public async Task Only_the_derived_answer_leaves_the_source()
		{
			var evidence = await Read(Policy(HiddenRule(NewCallFieldKeys.DispatchPersonnel), new NewCallFieldRule { Key = NewCallFieldKeys.IncidentId, Required = true }));
			var fact = Hidden(evidence);
			Assert.That(fact.Code, Is.Null);
			Assert.That(fact.Number, Is.Null);
			Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(evidence), Does.Not.Contain(NewCallFieldKeys.IncidentId));
		}
	}
}
