using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Services.Wellness;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// The wellness roles (decided 2026-11-26): each is an assigned personnel role, and the department may link a certification type of its
	/// own whose requirements and validity it decides. Role membership decides who acts; the linked certification is shown as status; with
	/// the department's switch on, only members holding a current certification act. Also the templates and the peer support role.
	/// </summary>
	[TestFixture]
	public class WellnessRoleCertificationTests
	{
		private static readonly DateTime Today = new DateTime(2026, 11, 26);
		private bool _doNotBroadcast;

		// Notices are sent only while broadcasting is allowed; other fixtures switch it off globally.
		[SetUp]
		public void Allow_broadcast()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
		}

		[TearDown]
		public void Restore_broadcast() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		private static PersonnelCertification Cert(string userId, int typeId, DateTime? expiresOn, PersonnelCertificationStatuses status = PersonnelCertificationStatuses.Active) =>
			new PersonnelCertification
			{
				PersonnelCertificationId = Math.Abs((userId + typeId + expiresOn).GetHashCode()),
				DepartmentId = WellnessTestWorld.DepartmentId,
				UserId = userId,
				DepartmentCertificationTypeId = typeId,
				ExpiresOn = expiresOn,
				Status = (int)status
			};

		private static DepartmentCertificationType Type(int id = 9, bool neverExpires = false) =>
			new DepartmentCertificationType { DepartmentCertificationTypeId = id, DepartmentId = 7, Code = "W", Type = "Wellness Officer", NeverExpires = neverExpires };

		#region Pure rules

		[Test]
		public void A_role_falls_back_like_the_designated_officer_and_so_does_its_certification()
		{
			var settings = new WellnessSettings { WellnessOfficerRoleId = 41, WellnessOfficerCertificationTypeId = 78, PeerTeamCertificationTypeId = 77 };

			WellnessRoleRules.RoleIdFor(settings, WellnessRoleKinds.WellnessOfficer).Should().Be(41);
			WellnessRoleRules.RoleIdFor(settings, WellnessRoleKinds.DesignatedOfficer).Should().Be(41, "with no designated officer role the wellness officer role serves");
			WellnessRoleRules.CertificationTypeIdFor(settings, WellnessRoleKinds.DesignatedOfficer).Should().Be(78, "and so does its certification");
			WellnessRoleRules.RoleIdFor(settings, WellnessRoleKinds.PeerSupportTeam).Should().Be(0);
			WellnessRoleRules.PeerTeamByCertificationOnly(settings).Should().BeTrue();
			WellnessRoleRules.PeerTeamConfigured(settings).Should().BeTrue();

			settings.DesignatedOfficerRoleId = 42;
			WellnessRoleRules.RoleIdFor(settings, WellnessRoleKinds.DesignatedOfficer).Should().Be(42);
			WellnessRoleRules.CertificationTypeIdFor(settings, WellnessRoleKinds.DesignatedOfficer).Should().Be(0, "a named designated role has its own certification or none");
			settings.DesignatedOfficerCertificationTypeId = 80;
			WellnessRoleRules.CertificationTypeIdFor(settings, WellnessRoleKinds.DesignatedOfficer).Should().Be(80);

			settings.PeerTeamRoleId = 43;
			WellnessRoleRules.PeerTeamByCertificationOnly(settings).Should().BeFalse();
			WellnessRoleRules.CertificationTypeIdFor(settings, WellnessRoleKinds.PeerSupportTeam).Should().Be(77);
			WellnessRoleRules.PeerTeamConfigured(new WellnessSettings()).Should().BeFalse();
			WellnessRoleRules.PeerTeamConfigured(new WellnessSettings { PeerTeamRoleId = 43 }).Should().BeTrue();
		}

		[Test]
		public void Status_is_current_expiring_expired_or_missing_on_the_department_date()
		{
			var type = Type();
			WellnessRoleRules.Status(null, null, Today).State.Should().Be(WellnessCertificationStates.NotLinked);
			WellnessRoleRules.Status(new PersonnelCertification[0], type, Today).State.Should().Be(WellnessCertificationStates.Missing);

			var current = WellnessRoleRules.Status(new[] { Cert("a", 9, Today.AddMonths(6)) }, type, Today);
			current.State.Should().Be(WellnessCertificationStates.Current);
			current.ExpiresOn.Should().Be(Today.AddMonths(6));
			current.IsCurrent.Should().BeTrue();

			var expiring = WellnessRoleRules.Status(new[] { Cert("a", 9, Today.AddDays(WellnessRoleRules.ExpiringSoonDays)) }, type, Today);
			expiring.State.Should().Be(WellnessCertificationStates.Expiring);
			expiring.IsCurrent.Should().BeTrue("an expiring certification still counts");

			var expired = WellnessRoleRules.Status(new[] { Cert("a", 9, Today.AddDays(-1)) }, type, Today);
			expired.State.Should().Be(WellnessCertificationStates.Expired);
			expired.ExpiresOn.Should().Be(Today.AddDays(-1));
			expired.IsCurrent.Should().BeFalse();

			WellnessRoleRules.Status(new[] { Cert("a", 9, Today.AddYears(1), PersonnelCertificationStatuses.Suspended) }, type, Today).State
				.Should().Be(WellnessCertificationStates.Expired, "a suspended record is not current");
			WellnessRoleRules.Status(new[] { Cert("a", 9, Today.AddYears(1), PersonnelCertificationStatuses.PendingVerification) }, type, Today).IsCurrent
				.Should().BeFalse("a record awaiting verification does not count");
			WellnessRoleRules.Status(new[] { Cert("a", 10, Today.AddYears(1)) }, type, Today).State.Should().Be(WellnessCertificationStates.Missing, "another type is not this one");

			var never = WellnessRoleRules.Status(new[] { Cert("a", 9, Today.AddDays(3)) }, Type(neverExpires: true), Today);
			never.State.Should().Be(WellnessCertificationStates.Current, "a type that never expires is never expiring");
			never.ExpiresOn.Should().BeNull();
		}

		[Test]
		public void Only_the_switch_with_a_linked_type_takes_a_role_away_from_a_member_without_a_current_certification()
		{
			var missing = new WellnessCertificationStatus { State = WellnessCertificationStates.Missing };
			var expiring = new WellnessCertificationStatus { State = WellnessCertificationStates.Expiring };
			var notLinked = new WellnessCertificationStatus { State = WellnessCertificationStates.NotLinked };
			var off = new WellnessSettings { WellnessOfficerRoleId = 41, WellnessOfficerCertificationTypeId = 78 };
			var on = new WellnessSettings { WellnessOfficerRoleId = 41, WellnessOfficerCertificationTypeId = 78, RequireCurrentCertification = true };
			var onWithoutType = new WellnessSettings { WellnessOfficerRoleId = 41, RequireCurrentCertification = true };

			WellnessRoleRules.Acts(off, WellnessRoleKinds.WellnessOfficer, missing).Should().BeTrue("off: the role decides and the certification is status");
			WellnessRoleRules.Acts(on, WellnessRoleKinds.WellnessOfficer, missing).Should().BeFalse();
			WellnessRoleRules.Acts(on, WellnessRoleKinds.WellnessOfficer, expiring).Should().BeTrue();
			WellnessRoleRules.Acts(on, WellnessRoleKinds.WellnessOfficer, notLinked).Should().BeTrue("a linked type that no longer exists never takes the role away");
			WellnessRoleRules.Acts(onWithoutType, WellnessRoleKinds.WellnessOfficer, missing).Should().BeTrue("a role with no linked type is not affected");
			WellnessRoleRules.RequiresCertification(on, WellnessRoleKinds.PeerSupportTeam).Should().BeFalse("the peer team links no type here");
		}

		[Test]
		public void The_new_settings_survive_normalization_and_the_protobuf_round_trip()
		{
			var settings = new WellnessSettings
			{
				WellnessOfficerRoleId = 41,
				WellnessOfficerCertificationTypeId = 78,
				DesignatedOfficerCertificationTypeId = -3,
				PeerTeamRoleId = 43,
				RequireCurrentCertification = true
			}.Normalized();
			settings.DesignatedOfficerCertificationTypeId.Should().Be(0);

			var back = ObjectSerialization.Deserialize<WellnessSettings>(ObjectSerialization.Serialize(settings));
			back.WellnessOfficerCertificationTypeId.Should().Be(78);
			back.PeerTeamRoleId.Should().Be(43);
			back.RequireCurrentCertification.Should().BeTrue();
			back.RetentionYears.Should().Be(MemberWellnessRules.MinimumRetentionYears);

			ObjectSerialization.Deserialize<WellnessSettings>(ObjectSerialization.Serialize(new WellnessSettings())).RequireCurrentCertification
				.Should().BeFalse("the switch is off unless a department turns it on");
		}

		[Test]
		public void Every_wellness_role_is_renamed_by_its_own_term()
		{
			WellnessRoleRules.TermKeyFor(WellnessRoleKinds.WellnessOfficer).Should().Be(DepartmentTerms.WellnessOfficers);
			WellnessRoleRules.TermKeyFor(WellnessRoleKinds.DesignatedOfficer).Should().Be(DepartmentTerms.DesignatedOfficers);
			WellnessRoleRules.TermKeyFor(WellnessRoleKinds.PeerSupportTeam).Should().Be(DepartmentTerms.PeerSupportTeam);
			WellnessRoleRules.Kinds.Select(WellnessRoleRules.TermKeyFor).Should().OnlyContain(k => DepartmentTerms.IsKnown(k));
		}

		#endregion Pure rules

		#region Templates

		[Test]
		public void Neutral_wellness_role_templates_exist_for_each_role_and_are_translated()
		{
			foreach (var code in new[] { "WELLNESS-OFFICER", "PEER-SUPPORT", "DESIGNATED-OFFICER" })
			{
				var template = CertificationTypeTemplateCatalog.GetById(code);
				template.Should().NotBeNull(code);
				template.AppliesTo.Should().Be(CertificationAppliesTo.Person);
				template.NeverExpires.Should().BeFalse("the department decides the validity; the template proposes one");
				template.Name.Should().NotContain("Fire").And.NotContain("EMS", "the wellness templates are not limited to first responders");

				var german = CertificationTypeTemplateCatalog.Localized(template, "de");
				german.Code.Should().Be(code);
				german.Name.Should().NotBe(template.Name, code + " ships a German name");
			}

			CertificationTypeTemplateCatalog.Search("peer support").Select(t => t.Code).Should().Contain("PEER-SUPPORT");
			CertificationTypeTemplateCatalog.Search("wellness").Select(t => t.Code).Should().Contain("WELLNESS-OFFICER");
			CertificationTypeTemplateCatalog.ToDepartmentType(CertificationTypeTemplateCatalog.GetById("PEER-SUPPORT"), 7).DefaultValidityMonths.Should().Be(12);
		}

		#endregion Templates

		#region Wellness officers (WL0)

		[Test]
		public async Task With_the_switch_off_the_role_decides_and_the_certification_is_only_status()
		{
			var world = new WellnessTestWorld();
			world.Settings.WellnessOfficerCertificationTypeId = WellnessTestWorld.OfficerCertificationTypeId;
			var service = world.Service();

			(await service.IsWellnessOfficerAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Should().BeTrue("no certification is on record, but the switch is off");

			var roster = await service.GetRosterAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin);
			var officers = roster.Single(r => r.Kind == WellnessRoleKinds.WellnessOfficer);
			officers.CertificationTypeName.Should().Be("Wellness Officer");
			officers.RequiresCertification.Should().BeFalse();
			officers.Members.Should().ContainSingle(m => m.UserId == WellnessTestWorld.Officer && m.Acts && m.Certification.State == WellnessCertificationStates.Missing);
		}

		[Test]
		public async Task With_the_switch_on_an_officer_without_a_current_certification_stops_acting()
		{
			var world = new WellnessTestWorld();
			world.Settings.WellnessOfficerCertificationTypeId = WellnessTestWorld.OfficerCertificationTypeId;
			world.Settings.RequireCurrentCertification = true;
			var exposure = world.AddExposure(WellnessTestWorld.Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, DateTime.UtcNow.AddDays(-2));
			var service = world.Service();

			(await service.IsWellnessOfficerAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Should().BeFalse();
			Func<Task> read = () => service.GetOverviewAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer, WellnessTestWorld.Member);
			await read.Should().ThrowAsync<UnauthorizedAccessException>("an officer whose certification lapsed no longer opens members' logs");
			(await service.GetActingMemberIdsAsync(WellnessTestWorld.DepartmentId, WellnessRoleKinds.WellnessOfficer)).Should().BeEmpty();

			world.Certifications_.Add(Cert(WellnessTestWorld.Officer, WellnessTestWorld.OfficerCertificationTypeId, DateTime.UtcNow.AddDays(-10)));
			(await service.IsWellnessOfficerAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Should().BeFalse("an expired certification is not current");

			world.Certifications_.Add(Cert(WellnessTestWorld.Officer, WellnessTestWorld.OfficerCertificationTypeId, DateTime.UtcNow.AddDays(10)));
			(await service.IsWellnessOfficerAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Should().BeTrue("an expiring certification still counts");
			(await service.GetEntryAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer, exposure.RmsResponderExposureId)).Should().NotBeNull();
			(await service.GetActingMemberIdsAsync(WellnessTestWorld.DepartmentId, WellnessRoleKinds.WellnessOfficer)).Should().Equal(WellnessTestWorld.Officer);

			var roster = (await service.GetRosterAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Single(r => r.Kind == WellnessRoleKinds.WellnessOfficer);
			roster.RequiresCertification.Should().BeTrue();
			roster.Members.Single().Certification.State.Should().Be(WellnessCertificationStates.Expiring);
		}

		[Test]
		public async Task Holding_the_certification_without_the_role_never_makes_a_wellness_officer()
		{
			var world = new WellnessTestWorld();
			world.Settings.WellnessOfficerCertificationTypeId = WellnessTestWorld.OfficerCertificationTypeId;
			world.Settings.RequireCurrentCertification = true;
			world.Certifications_.Add(Cert(WellnessTestWorld.Member, WellnessTestWorld.OfficerCertificationTypeId, DateTime.UtcNow.AddYears(1)));

			(await world.Service().IsWellnessOfficerAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Member)).Should().BeFalse("role membership decides who acts");
		}

		[Test]
		public async Task A_linked_type_that_no_longer_exists_leaves_the_role_alone_to_decide_and_is_reported()
		{
			var world = new WellnessTestWorld();
			world.Settings.WellnessOfficerCertificationTypeId = 999;
			world.Settings.RequireCurrentCertification = true;
			var service = world.Service();

			(await service.IsWellnessOfficerAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Should().BeTrue();
			var roster = (await service.GetRosterAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin)).Single(r => r.Kind == WellnessRoleKinds.WellnessOfficer);
			roster.CertificationTypeMissing.Should().BeTrue();
			roster.Members.Single().Acts.Should().BeTrue();
		}

		[Test]
		public async Task Only_administrators_and_wellness_officers_read_the_roster()
		{
			var world = new WellnessTestWorld();
			var service = world.Service();

			Func<Task> member = () => service.GetRosterAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Member);
			await member.Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.GetRosterAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Officer)).Select(r => r.Kind).Should().Equal(WellnessRoleRules.Kinds);
		}

		#endregion Wellness officers (WL0)

		#region Settings

		[Test]
		public async Task Turning_the_switch_on_is_refused_while_no_officer_holds_a_current_certification()
		{
			var world = new WellnessTestWorld();
			var service = world.Service();
			var wanted = new WellnessSettings
			{
				WellnessOfficerRoleId = WellnessTestWorld.OfficerRoleId,
				WellnessOfficerCertificationTypeId = WellnessTestWorld.OfficerCertificationTypeId,
				RequireCurrentCertification = true
			};

			Func<Task> refused = () => service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin, wanted);
			(await refused.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("certification_switch_leaves_no_officer");

			world.Certifications_.Add(Cert(WellnessTestWorld.Officer, WellnessTestWorld.OfficerCertificationTypeId, DateTime.UtcNow.AddYears(1)));
			var stored = await service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin, wanted);
			stored.RequireCurrentCertification.Should().BeTrue();

			// Once on, later saves are not blocked by a lapse (the roster warns instead).
			world.Certifications_.Clear();
			wanted.RetentionYears = 40;
			(await service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin, wanted)).RetentionYears.Should().Be(40);
		}

		[Test]
		public async Task A_wellness_role_certification_must_be_a_person_type_of_the_department_and_the_peer_role_a_department_role()
		{
			var world = new WellnessTestWorld();
			var service = world.Service();

			Func<Task> unitType = () => service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin,
				new WellnessSettings { WellnessOfficerCertificationTypeId = WellnessTestWorld.UnitTypeId });
			(await unitType.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("certification_type_not_found");

			Func<Task> unknown = () => service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin,
				new WellnessSettings { DesignatedOfficerCertificationTypeId = 12345 });
			(await unknown.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("certification_type_not_found");

			Func<Task> foreignRole = () => service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin, new WellnessSettings { PeerTeamRoleId = 555 });
			(await foreignRole.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("role_not_found");

			var stored = await service.SaveSettingsAsync(WellnessTestWorld.DepartmentId, WellnessTestWorld.Admin,
				new WellnessSettings { PeerTeamRoleId = WellnessTestWorld.PeerRoleId, WellnessOfficerCertificationTypeId = WellnessTestWorld.OfficerCertificationTypeId });
			stored.PeerTeamRoleId.Should().Be(WellnessTestWorld.PeerRoleId);
			world.Audits.Should().ContainSingle(a => a.Type == AuditLogTypes.WellnessSettingsChanged && a.After.Contains("\"PeerTeamRoleId\":43"));
		}

		#endregion Settings

		#region Peer support team

		[Test]
		public async Task Without_a_peer_role_the_certification_holders_are_the_team_as_before()
		{
			var world = new WellnessTestWorld();
			(await world.Service().GetPeerTeamUserIdsAsync(WellnessTestWorld.DepartmentId)).Should().Equal(WellnessTestWorld.Peer);
		}

		[Test]
		public async Task With_a_peer_role_its_members_are_the_team_and_the_switch_keeps_only_current_holders()
		{
			var world = new WellnessTestWorld();
			world.Settings.PeerTeamRoleId = WellnessTestWorld.PeerRoleId;
			world.RoleMembers[WellnessTestWorld.PeerRoleId] = new List<string> { WellnessTestWorld.Member, WellnessTestWorld.Hidden };
			var service = world.Service();

			(await service.GetPeerTeamUserIdsAsync(WellnessTestWorld.DepartmentId)).Should().Equal(new[] { WellnessTestWorld.Member },
				"the role's active members are the team; the certification holder outside the role is not, and a hidden member never is");

			world.Settings.RequireCurrentCertification = true;
			(await service.GetPeerTeamUserIdsAsync(WellnessTestWorld.DepartmentId)).Should().BeEmpty("the switch needs a current peer support certification");

			world.Certifications_.Add(Cert(WellnessTestWorld.Member, WellnessTestWorld.PeerCertificationTypeId, DateTime.UtcNow.AddMonths(6)));
			(await service.GetPeerTeamUserIdsAsync(WellnessTestWorld.DepartmentId)).Should().Equal(WellnessTestWorld.Member);
		}

		[Test]
		public async Task Anniversary_notices_carry_the_departments_word_for_its_peer_support_team_but_never_rewrite_a_name()
		{
			var world = new WellnessTestWorld();
			world.Terminology.Set(DepartmentTerms.PeerSupportTeam, "CISM team");
			var today = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
			world.Exposures.Add(new RmsResponderExposure
			{
				RmsResponderExposureId = Guid.NewGuid().ToString(),
				DepartmentId = WellnessTestWorld.DepartmentId,
				UserId = WellnessTestWorld.Member,
				Category = (int)ResponderExposureCategories.CriticalIncident,
				Status = (int)ResponderExposureStatuses.Confirmed,
				OccurredOn = new DateTime(2024, 11, 30, 20, 0, 0, DateTimeKind.Utc),
				RemindMember = true,
				RemindPeerTeam = true,
				NextAnniversaryOn = new DateTime(2026, 11, 30)
			});
			world.Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(WellnessTestWorld.DepartmentId))
				.ReturnsAsync(new List<PersonName> { new PersonName { UserId = WellnessTestWorld.Member, FirstName = "Peer support team", LastName = "Fan" } });

			await world.Service().SendAnniversaryRemindersAsync(today);

			world.Sent.Single(s => s.UserId == WellnessTestWorld.Member).Message.Should().Contain("Your CISM team and wellness resources");
			var peer = world.Sent.Single(s => s.UserId == WellnessTestWorld.Peer).Message;
			peer.Should().Contain("asked for the CISM team to know");
			peer.Should().StartWith("Peer support team Fan", "the member's name goes in after the terms and is never rewritten");
		}

		#endregion Peer support team

		#region Designated officers (WL1)

		[Test]
		public async Task With_the_switch_on_a_designated_officer_without_a_current_certification_neither_acts_nor_is_told()
		{
			const int designatedTypeId = 72;
			var world = new MemberHealthTestWorld();
			world.Settings.DesignatedOfficerCertificationTypeId = designatedTypeId;
			world.Settings.RequireCurrentCertification = true;
			world.CertificationService.Setup(c => c.GetCertificationTypeByIdAsync(designatedTypeId))
				.ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = designatedTypeId, DepartmentId = MemberHealthTestWorld.DepartmentId, Code = "DESIGNATED-OFFICER", Type = "Designated" });
			world.CertificationService.Setup(c => c.GetCertificationsForDepartmentAsync(MemberHealthTestWorld.DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int _, IEnumerable<string> ids) => world.Certifications.Where(c => ids.Contains(c.UserId)).ToList());
			var service = world.Service();

			(await service.IsDesignatedOfficerAsync(MemberHealthTestWorld.DepartmentId, MemberHealthTestWorld.Designated)).Should().BeFalse();
			(await service.GetAccessAsync(MemberHealthTestWorld.DepartmentId, MemberHealthTestWorld.WellnessOfficer)).IsWellnessOfficer
				.Should().BeTrue("the wellness officer role links no certification here, so the switch leaves it alone");

			await service.ReportExposureAsync(MemberHealthTestWorld.DepartmentId, MemberHealthTestWorld.Member, new ExposureIncidentReport
			{
				Route = (int)ExposureIncidentRoutes.Percutaneous,
				OccurredOn = DateTime.UtcNow.AddDays(-1)
			});
			world.Sent.Should().BeEmpty("no designated officer acts, so nobody is told (the settings page warns)");

			world.Certifications.Add(new PersonnelCertification
			{
				PersonnelCertificationId = 5001, DepartmentId = MemberHealthTestWorld.DepartmentId, UserId = MemberHealthTestWorld.Designated,
				DepartmentCertificationTypeId = designatedTypeId, ExpiresOn = DateTime.UtcNow.AddYears(1), Status = (int)PersonnelCertificationStatuses.Active
			});
			(await service.IsDesignatedOfficerAsync(MemberHealthTestWorld.DepartmentId, MemberHealthTestWorld.Designated)).Should().BeTrue();
			await service.ReportExposureAsync(MemberHealthTestWorld.DepartmentId, MemberHealthTestWorld.Member, new ExposureIncidentReport
			{
				Route = (int)ExposureIncidentRoutes.Percutaneous,
				OccurredOn = DateTime.UtcNow.AddDays(-1)
			});
			world.Sent.Select(s => s.UserId).Should().Equal(MemberHealthTestWorld.Designated);
		}

		#endregion Designated officers (WL1)

		#region Notice terms

		[Test]
		public void Notice_terms_follow_the_readers_language_and_change_nothing_when_nothing_is_renamed()
		{
			var config = new DepartmentTerminologyConfig();
			WellnessTerms.Apply("Your peer support team is here.", config, CultureInfo.GetCultureInfo("en")).Should().Be("Your peer support team is here.");

			config.Set(DepartmentTerms.PeerSupportTeam, "Member assistance team");
			config.Set(DepartmentTerms.WellnessOfficers, "Safety officers", "Safety officer");
			WellnessTerms.Apply("Your peer support team and the wellness officers are here. Ask a wellness officer.", config, CultureInfo.GetCultureInfo("en"))
				.Should().Be("Your member assistance team and the safety officers are here. Ask a safety officer.");

			var german = WellnessTerms.Shipped(WellnessTerms.ResourceNames[DepartmentTerms.PeerSupportTeam].Name, CultureInfo.GetCultureInfo("de"));
			german.Should().Be("Peer-Support-Team");
			WellnessTerms.Apply("Ihr " + german + " ist für Sie da.", config, CultureInfo.GetCultureInfo("de")).Should().Be("Ihr Member assistance team ist für Sie da.");
			WellnessTerms.Apply("Your peer support team", config, CultureInfo.GetCultureInfo("de"))
				.Should().Be("Your peer support team", "a German reader's text is matched on the German words only");
		}

		[Test]
		public void Every_wellness_term_has_shipped_words_in_every_language()
		{
			foreach (var culture in new[] { "ar", "de", "el", "en", "es", "fr", "it", "pl", "sv", "uk" }.Select(CultureInfo.GetCultureInfo))
			{
				foreach (var (key, names) in WellnessTerms.ResourceNames)
				{
					WellnessTerms.Shipped(names.Name, culture).Should().NotBeNullOrWhiteSpace(culture.Name + " " + key);
					if (names.Singular != null)
						WellnessTerms.Shipped(names.Singular, culture).Should().NotBeNullOrWhiteSpace(culture.Name + " " + key);
					if (culture.Name != "en")
						WellnessTerms.Shipped(names.Name, culture).Should().NotBe(WellnessTerms.Shipped(names.Name, CultureInfo.GetCultureInfo("en")), culture.Name + " " + key);
				}
			}

			WellnessTerms.ResourceNames.Keys.Should().BeEquivalentTo(new[] { DepartmentTerms.Wellness, DepartmentTerms.WellnessOfficers, DepartmentTerms.DesignatedOfficers, DepartmentTerms.PeerSupportTeam });
			WellnessTerms.ResourceNames.Where(p => p.Value.Singular != null).Select(p => p.Key)
				.Should().BeEquivalentTo(DepartmentTerms.All.Where(t => t.Key.StartsWith(DepartmentTerms.RolePrefix) && t.HasSingular).Select(t => t.Key));
		}

		#endregion Notice terms
	}
}
