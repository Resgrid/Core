using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// A ProtocolAcknowledgmentService over in-memory rows: acknowledgments (with the unique version/member rule and the conditional
	/// status update), versions, protocols, audiences and member facts, with the real signing service and the real audience matcher.
	/// Department 1: members Alice and Bob in group 5 and Carol in group 6, all active; Dave is in group 5 but not an active member.
	/// </summary>
	public sealed class ProtocolAcknowledgmentHarness
	{
		public const int DepartmentId = 1;
		public const int GroupFive = 5;
		public const int GroupSix = 6;
		public const string Alice = "alice";
		public const string Bob = "bob";
		public const string Carol = "carol";
		public const string Dave = "dave";
		public const string Officer = "officer";

		public readonly List<ProtocolAcknowledgment> Acks = new List<ProtocolAcknowledgment>();
		public readonly Dictionary<int, ProtocolVersion> Versions = new Dictionary<int, ProtocolVersion>();
		public readonly Dictionary<int, DispatchProtocol> Protocols = new Dictionary<int, DispatchProtocol>();
		public readonly List<ProtocolAudience> Audiences = new List<ProtocolAudience>();
		public readonly Dictionary<string, HashSet<int>> MemberGroups = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase)
		{
			[Alice] = new HashSet<int> { GroupFive },
			[Bob] = new HashSet<int> { GroupFive },
			[Carol] = new HashSet<int> { GroupSix },
			[Dave] = new HashSet<int> { GroupFive }
		};
		public readonly HashSet<string> Inactive = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Dave };
		public readonly List<SignatureRecord> Signatures = new List<SignatureRecord>();
		public readonly List<TrainingUser> TrainingUsers = new List<TrainingUser>();
		public readonly List<(LearningRecordSourceKinds Kind, string SourceRefId, List<LearningRecordDraft> Drafts)> LearningWrites =
			new List<(LearningRecordSourceKinds, string, List<LearningRecordDraft>)>();
		public ProtocolLibrarySettings Settings = new ProtocolLibrarySettings();
		public bool LmsActive;

		public readonly Mock<IProtocolNotificationService> Notifications = new Mock<IProtocolNotificationService>();
		public readonly Mock<ILmsAccessService> Lms = new Mock<ILmsAccessService>();
		public readonly Mock<IProtocolLearningExtensions> Learning = new Mock<IProtocolLearningExtensions>();
		public readonly Mock<IProtocolLibraryService> Library = new Mock<IProtocolLibraryService>();
		public readonly Mock<ITrainingService> Trainings = new Mock<ITrainingService>();
		public readonly Mock<ITrainingAssignmentService> TrainingAssignments = new Mock<ITrainingAssignmentService>();
		public readonly Mock<IDepartmentGroupsService> Groups = new Mock<IDepartmentGroupsService>();
		public readonly Mock<IProtocolVersionApprovalsRepository> Approvals = new Mock<IProtocolVersionApprovalsRepository>();
		public readonly Mock<IDispatchProtocolRepository> ProtocolRows = new Mock<IDispatchProtocolRepository>();
		public readonly ProtocolAcknowledgmentService Service;
		private int _nextAck = 1;
		private int _nextSignature = 1;

		public ProtocolAcknowledgmentHarness()
		{
			var acks = new Mock<IProtocolAcknowledgmentsRepository>();
			acks.Setup(x => x.InsertAsync(It.IsAny<ProtocolAcknowledgment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ProtocolAcknowledgment a, CancellationToken _, bool __) =>
				{
					// The unique (version, member) index.
					if (Acks.Any(r => r.ProtocolVersionId == a.ProtocolVersionId && string.Equals(r.UserId, a.UserId, StringComparison.OrdinalIgnoreCase)))
						throw new InvalidOperationException("unique violation");
					a.ProtocolAcknowledgmentId = _nextAck++;
					Acks.Add(Copy(a));
					return a;
				});
			acks.Setup(x => x.GetForVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int v) => Acks.Where(a => a.DepartmentId == d && a.ProtocolVersionId == v).Select(Copy).ToList());
			acks.Setup(x => x.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Acks.Where(a => a.DepartmentId == d && string.Equals(a.UserId, u, StringComparison.OrdinalIgnoreCase)).Select(Copy).ToList());
			acks.Setup(x => x.GetForVersionAndUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int v, string u) => Acks.Where(a => a.DepartmentId == d && a.ProtocolVersionId == v && string.Equals(a.UserId, u, StringComparison.OrdinalIgnoreCase)).Select(Copy).FirstOrDefault());
			acks.Setup(x => x.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int d) => Acks.Where(a => a.DepartmentId == d).Select(Copy).ToList());
			acks.Setup(x => x.GetForProtocolAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int p) => Acks.Where(a => a.DepartmentId == d && a.DispatchProtocolId == p).Select(Copy).ToList());
			acks.Setup(x => x.TryUpdateStatusAsync(It.IsAny<ProtocolAcknowledgment>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolAcknowledgment a, int expected, CancellationToken _) =>
				{
					var index = Acks.FindIndex(r => r.ProtocolAcknowledgmentId == a.ProtocolAcknowledgmentId);
					if (index < 0 || Acks[index].Status != expected)
						return false;
					Acks[index] = Copy(a);
					return true;
				});
			acks.Setup(x => x.SupersedeOpenAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int p, int except, DateTime now, CancellationToken _) =>
				{
					var rows = Acks.Where(a => a.DepartmentId == d && a.DispatchProtocolId == p && a.ProtocolVersionId != except && a.Status == (int)ProtocolAcknowledgmentStatuses.Open).ToList();
					rows.ForEach(r => { r.Status = (int)ProtocolAcknowledgmentStatuses.Superseded; r.WithdrawnOn = now; });
					return rows.Count;
				});
			acks.Setup(x => x.WithdrawOpenForProtocolAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int p, DateTime now, CancellationToken _) =>
				{
					var rows = Acks.Where(a => a.DepartmentId == d && a.DispatchProtocolId == p && a.Status == (int)ProtocolAcknowledgmentStatuses.Open).ToList();
					rows.ForEach(r => { r.Status = (int)ProtocolAcknowledgmentStatuses.Withdrawn; r.WithdrawnOn = now; });
					return rows.Count;
				});
			acks.Setup(x => x.GetRemindersDueAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime now, int take) => Acks.Where(a => a.Status == (int)ProtocolAcknowledgmentStatuses.Open && a.DueOn.HasValue && a.DueOn <= now &&
						a.ReminderStage < (int)ProtocolAcknowledgmentRules.DueStage(a.DueOn, now))
					.Select(a => new ProtocolAcknowledgmentDue
					{
						ProtocolAcknowledgmentId = a.ProtocolAcknowledgmentId, DepartmentId = a.DepartmentId, DispatchProtocolId = a.DispatchProtocolId,
						ProtocolVersionId = a.ProtocolVersionId, UserId = a.UserId, DueOn = a.DueOn, ReminderStage = a.ReminderStage
					}).Take(take).ToList());
			acks.Setup(x => x.MarkReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int id, int stage, DateTime now, CancellationToken _) =>
				{
					var row = Acks.FirstOrDefault(a => a.ProtocolAcknowledgmentId == id && a.Status == (int)ProtocolAcknowledgmentStatuses.Open && a.ReminderStage < stage);
					if (row == null)
						return false;
					row.ReminderStage = stage;
					row.LastReminderOn = now;
					if (stage >= (int)ProtocolAcknowledgmentReminderStages.Escalated)
						row.EscalatedOn = now;
					return true;
				});
			acks.Setup(x => x.GetAwaitingQuizAsync(It.IsAny<int>()))
				.ReturnsAsync((int take) => Acks.Where(a => a.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz).Select(Copy).Take(take).ToList());
			acks.Setup(x => x.GetOpenBulletinsForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Acks.Where(a => a.DepartmentId == d && string.Equals(a.UserId, u, StringComparison.OrdinalIgnoreCase) &&
					a.Status == (int)ProtocolAcknowledgmentStatuses.Open && Protocols.TryGetValue(a.DispatchProtocolId, out var p) && p.IsBulletin &&
					p.Status == (int)ProtocolStatuses.Published && p.CurrentVersionId == a.ProtocolVersionId).Select(Copy).ToList());

			var versions = new Mock<IProtocolVersionsRepository>();
			versions.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int id) => Versions.TryGetValue(id, out var v) && v.DepartmentId == d ? Copy(v) : null);
			versions.Setup(x => x.GetByIdsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, IEnumerable<int> ids, bool _) => ids.Where(Versions.ContainsKey).Select(id => Copy(Versions[id])).Where(v => v.DepartmentId == d).ToList());
			versions.Setup(x => x.GetForProtocolAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, int p, bool _) => Versions.Values.Where(v => v.DepartmentId == d && v.DispatchProtocolId == p).OrderByDescending(v => v.ProtocolVersionId).Select(Copy).ToList());
			versions.Setup(x => x.GetHeadersForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Versions.Values.Where(v => v.DepartmentId == d).Select(Copy).ToList());
			versions.Setup(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolVersion v, long expected, CancellationToken _) =>
				{
					if (!Versions.TryGetValue(v.ProtocolVersionId, out var stored) || stored.RowVersion != expected)
						return false;
					v.RowVersion = expected + 1;
					Versions[v.ProtocolVersionId] = Copy(v);
					return true;
				});

			var audiences = new Mock<IProtocolAudiencesRepository>();
			audiences.Setup(x => x.GetForProtocolAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int p) => Audiences.Where(a => a.DepartmentId == d && a.DispatchProtocolId == p).ToList());
			audiences.Setup(x => x.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int d) => Audiences.Where(a => a.DepartmentId == d).ToList());

			var protocols = new Mock<IProtocolsService>();
			protocols.Setup(x => x.GetProtocolHeaderByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Protocols.TryGetValue(id, out var p) ? Copy(p) : null);
			protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, bool _) => Protocols.Values.Where(p => p.DepartmentId == d).Select(Copy).ToList());
			ProtocolRows.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DispatchProtocol p, CancellationToken _, bool __) => { Protocols[p.DispatchProtocolId] = Copy(p); return p; });

			var audienceService = new Mock<IContentAudienceService>();
			audienceService.Setup(x => x.GetMemberFactsAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, string u) => Facts(u));
			audienceService.Setup(x => x.ResolveAsync(It.IsAny<int>(), It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync((int d, IEnumerable<IContentAudienceRule> rules) =>
				{
					var list = rules.ToList();
					var members = MemberGroups.Keys.Where(u => ContentAudienceMatcher.Matches(Facts(u), list));
					return new ContentAudienceResolution { MemberUserIds = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase) };
				});

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetActiveMemberUserIdsAsync(It.IsAny<int>()))
				.ReturnsAsync(() => new HashSet<string>(MemberGroups.Keys.Where(u => !Inactive.Contains(u)).Concat(new[] { Officer }), StringComparer.OrdinalIgnoreCase));
			departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(() => MemberGroups.Keys.Select(u => new PersonName { UserId = u, FirstName = u, LastName = "Member" }).ToList());
			departments.Setup(x => x.GetActiveAdminsForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<Model.Identity.IdentityUser> { new Model.Identity.IdentityUser { Id = Officer } });
			departments.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });

			Groups.Setup(x => x.GetGroupIdsForAllUsersInDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(() => MemberGroups.ToDictionary(m => m.Key, m => m.Value.First(), StringComparer.OrdinalIgnoreCase));
			Groups.Setup(x => x.GetGroupForUserAsync(It.IsAny<string>(), It.IsAny<int>()))
				.ReturnsAsync((string u, int d) => MemberGroups.TryGetValue(u, out var g) ? new DepartmentGroup { DepartmentGroupId = g.First(), DepartmentId = d } : null);
			Groups.Setup(x => x.GetAllAdminsForGroupAndAncestorsAsync(GroupFive))
				.ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = Officer, DepartmentGroupId = GroupFive, IsAdmin = true } });
			Groups.Setup(x => x.GetAllGroupsForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = GroupFive, Name = "Station 5" }, new DepartmentGroup { DepartmentGroupId = GroupSix, Name = "Station 6" } });

			var signatureRows = new Mock<ISignatureRecordsRepository>();
			signatureRows.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => { r.SignatureRecordId = _nextSignature++; Signatures.Add(r); return r; });
			signatureRows.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int id) => Signatures.FirstOrDefault(s => s.SignatureRecordId == id && s.DepartmentId == d));
			signatureRows.Setup(x => x.SaveOrUpdateAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => r);
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(new UserProfile { FirstName = "Member", LastName = "One" });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<PersonnelRole>());
			var signatures = new SignatureService(signatureRows.Object, versions.Object, Mock.Of<IPersonnelCertificationRepository>(), profiles.Object, roles.Object,
				Mock.Of<IEventAggregator>(), Mock.Of<IProtocolVersionFilesRepository>());

			Lms.Setup(x => x.CanAuthorAndManageAsync(It.IsAny<int>())).ReturnsAsync(() => LmsActive);
			Library.Setup(x => x.GetSettingsAsync(It.IsAny<int>())).ReturnsAsync(() => Settings);
			Trainings.Setup(x => x.GetTrainingUsersForUserAsync(It.IsAny<string>()))
				.ReturnsAsync((string u) => TrainingUsers.Where(t => string.Equals(t.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
			Trainings.Setup(x => x.GetTrainingByIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => new Training { TrainingId = id, DepartmentId = DepartmentId, Name = "Mayday quiz", QuestionCount = 3 });

			var writer = new Mock<ILearningRecordWriter>();
			writer.Setup(x => x.WriteSourceAsync(It.IsAny<int>(), It.IsAny<LearningRecordSourceKinds>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((int d, LearningRecordSourceKinds k, string s, IReadOnlyCollection<LearningRecordDraft> drafts, string u, CancellationToken _) =>
					LearningWrites.Add((k, s, drafts.ToList())))
				.ReturnsAsync(new LearningRecordWriteResult());

			Notifications.Setup(x => x.NotifyAssignedAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, IReadOnlyCollection<string> users, DispatchProtocol p, ProtocolVersion v, CancellationToken _) => users.Count);
			Notifications.Setup(x => x.NotifyReminderAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(),
				It.IsAny<ProtocolAcknowledgmentReminderStages>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
			Notifications.Setup(x => x.NotifyEscalationAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<DispatchProtocol>(),
				It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, IReadOnlyCollection<string> users, string n, DispatchProtocol p, ProtocolVersion v, CancellationToken _) => users.Count);
			Notifications.Setup(x => x.NotifyBundleAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

			Service = new ProtocolAcknowledgmentService(acks.Object, versions.Object, Mock.Of<IProtocolVersionFilesRepository>(), audiences.Object, Approvals.Object,
				ProtocolRows.Object, protocols.Object, audienceService.Object, departments.Object, Groups.Object, signatures, Lms.Object, Notifications.Object,
				Mock.Of<IEventAggregator>(), new Lazy<IProtocolLibraryService>(() => Library.Object), new Lazy<ILearningRecordWriter>(() => writer.Object),
				new Lazy<ITrainingService>(() => Trainings.Object), new Lazy<ITrainingAssignmentService>(() => TrainingAssignments.Object),
				new Lazy<ISearchProjectionService>(() => Mock.Of<ISearchProjectionService>()), new Lazy<IProtocolLearningExtensions>(() => Learning.Object));
		}

		public ContentAudienceMemberFacts Facts(string userId) => new ContentAudienceMemberFacts
		{
			UserId = userId,
			IsActive = MemberGroups.ContainsKey(userId) && !Inactive.Contains(userId),
			GroupIds = MemberGroups.TryGetValue(userId, out var groups) ? new HashSet<int>(groups) : new HashSet<int>()
		};

		/// <summary>A published protocol for group 5 (no audience rows when <paramref name="group"/> is null) with version 1.0 in force.</summary>
		public (DispatchProtocol Protocol, ProtocolVersion Version) Published(int protocolId, int versionId, int? group = GroupFive, int major = 1, int minor = 0,
			bool requiresAcknowledgment = true, int? dueDays = null, ProtocolKinds kind = ProtocolKinds.Sog)
		{
			var html = "<p>Protocol " + protocolId + " v" + major + "." + minor + "</p>";
			var version = new ProtocolVersion
			{
				ProtocolVersionId = versionId, DepartmentId = DepartmentId, DispatchProtocolId = protocolId, Major = major, Minor = minor,
				Status = (int)ProtocolVersionStatuses.Published, ContentHtml = html, ContentHash = ProtocolContentHash.Compute(html),
				RequiresAcknowledgment = requiresAcknowledgment, EffectiveOn = DateTime.UtcNow.AddDays(-1), PublishedOn = DateTime.UtcNow.AddDays(-1)
			};
			Versions[versionId] = version;
			var protocol = new DispatchProtocol
			{
				DispatchProtocolId = protocolId, DepartmentId = DepartmentId, Name = "Protocol " + protocolId, Code = "P" + protocolId, Kind = (int)kind,
				Status = (int)ProtocolStatuses.Published, CurrentVersionId = versionId, AckDueDays = dueDays
			};
			Protocols[protocolId] = protocol;
			if (group.HasValue && !Audiences.Any(a => a.DispatchProtocolId == protocolId))
				Audiences.Add(new ProtocolAudience { DepartmentId = DepartmentId, DispatchProtocolId = protocolId, Kind = (int)ContentAudienceKinds.Group, RefId = group.Value.ToString() });
			return (Copy(protocol), Copy(version));
		}

		/// <summary>Publishes the next version of a protocol: the one in force becomes superseded.</summary>
		public (DispatchProtocol Protocol, ProtocolVersion Version) Next(int protocolId, int versionId, int major, int minor)
		{
			var protocol = Protocols[protocolId];
			var current = Versions[protocol.CurrentVersionId.Value];
			current.Status = (int)ProtocolVersionStatuses.Superseded;
			current.SupersededOn = DateTime.UtcNow;
			var html = "<p>Protocol " + protocolId + " v" + major + "." + minor + "</p>";
			Versions[versionId] = new ProtocolVersion
			{
				ProtocolVersionId = versionId, DepartmentId = DepartmentId, DispatchProtocolId = protocolId, Major = major, Minor = minor,
				Status = (int)ProtocolVersionStatuses.Published, ContentHtml = html, ContentHash = ProtocolContentHash.Compute(html),
				RequiresAcknowledgment = true, EffectiveOn = DateTime.UtcNow, PublishedOn = DateTime.UtcNow
			};
			protocol.CurrentVersionId = versionId;
			return (Copy(protocol), Copy(Versions[versionId]));
		}

		public ProtocolReader Reader(string userId) => new ProtocolReader
		{
			DepartmentId = DepartmentId,
			UserId = userId,
			IsCurrentMember = true,
			IsDepartmentAdmin = userId == Officer,
			Facts = Facts(userId)
		};

		public ProtocolAcknowledgment Row(int versionId, string userId) =>
			Acks.FirstOrDefault(a => a.ProtocolVersionId == versionId && string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase));

		public static T Copy<T>(T value) where T : class =>
			value == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<T>(Newtonsoft.Json.JsonConvert.SerializeObject(value));
	}
}
