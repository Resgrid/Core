using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// K1 guard (§16.3): a member never retrieves a chunk they cannot open. Checked over every source kind, so a kind added later without a
	/// rule fails here instead of leaking.
	/// </summary>
	[TestFixture]
	public class KnowledgeAccessFilterTests
	{
		private const int DepartmentId = 7;
		private const string Member = "member-1";

		private static KnowledgeReaderScope Scope(Action<KnowledgeReaderScope> configure = null)
		{
			var scope = new KnowledgeReaderScope
			{
				DepartmentId = DepartmentId,
				UserId = Member,
				IsCurrentMember = true,
				Facts = new ContentAudienceMemberFacts { UserId = Member, IsActive = true, GroupIds = new HashSet<int> { 3 }, RoleIds = new HashSet<int> { 4 } }
			};
			configure?.Invoke(scope);
			return scope;
		}

		private static KnowledgeChunk Chunk(KnowledgeSourceKinds kind, string itemId = "10", int departmentId = DepartmentId, KnowledgeAccessFlags flags = KnowledgeAccessFlags.None,
			IEnumerable<ContentAudienceRule> audience = null) => new KnowledgeChunk
		{
			DepartmentId = kind == KnowledgeSourceKinds.Academy ? 0 : departmentId,
			SourceKind = (int)kind,
			ItemId = itemId,
			Text = "text",
			AccessFlags = (int)flags,
			AudienceJson = KnowledgeAccessFilter.SerializeAudience(audience ?? Enumerable.Empty<ContentAudienceRule>())
		};

		public static IEnumerable<KnowledgeSourceKinds> DepartmentKinds => Enum.GetValues(typeof(KnowledgeSourceKinds)).Cast<KnowledgeSourceKinds>().Where(k => k != KnowledgeSourceKinds.Academy);

		[TestCaseSource(nameof(DepartmentKinds))]
		public void A_department_chunk_is_read_only_when_its_item_is_in_the_readers_live_readable_set(KnowledgeSourceKinds kind)
		{
			KnowledgeAccessFilter.CanRead(Scope(), Chunk(kind)).Should().BeFalse("nothing of {0} was allowed", kind);
			KnowledgeAccessFilter.CanRead(Scope(s => s.Allow(kind, new[] { "11" })), Chunk(kind)).Should().BeFalse("another item of {0} was allowed", kind);
			KnowledgeAccessFilter.CanRead(Scope(s => s.Allow(kind, new[] { "10" })), Chunk(kind)).Should().BeTrue("the item of {0} is readable", kind);
		}

		[TestCaseSource(nameof(DepartmentKinds))]
		public void Another_department_a_non_member_and_admin_only_content_are_never_read(KnowledgeSourceKinds kind)
		{
			var allowed = Scope(s => s.Allow(kind, new[] { "10" }));
			KnowledgeAccessFilter.CanRead(allowed, Chunk(kind, departmentId: 8)).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(allowed, Chunk(kind, departmentId: 0)).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(Scope(s => { s.Allow(kind, new[] { "10" }); s.IsCurrentMember = false; }), Chunk(kind)).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(allowed, Chunk(kind, flags: KnowledgeAccessFlags.AdminOnly)).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(Scope(s => { s.Allow(kind, new[] { "10" }); s.IsDepartmentAdmin = true; }), Chunk(kind, flags: KnowledgeAccessFlags.AdminOnly)).Should().BeTrue();
		}

		[TestCaseSource(nameof(DepartmentKinds))]
		public void A_stored_audience_the_member_is_not_in_fails_closed_even_when_the_live_set_allows(KnowledgeSourceKinds kind)
		{
			var scope = Scope(s => s.Allow(kind, new[] { "10" }));
			KnowledgeAccessFilter.CanRead(scope, Chunk(kind, audience: new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "99") })).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(scope, Chunk(kind, audience: new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "3") })).Should().BeTrue();
			KnowledgeAccessFilter.CanRead(scope, new KnowledgeChunk { DepartmentId = DepartmentId, SourceKind = (int)kind, ItemId = "10", AudienceJson = "{not json" }).Should().BeFalse();
		}

		[Test]
		public void The_Academy_is_read_by_every_member_but_only_from_the_system_corpus()
		{
			KnowledgeAccessFilter.CanRead(Scope(), Chunk(KnowledgeSourceKinds.Academy)).Should().BeTrue();
			KnowledgeAccessFilter.CanRead(Scope(), new KnowledgeChunk { DepartmentId = DepartmentId, SourceKind = (int)KnowledgeSourceKinds.Academy, ItemId = "05-calls" }).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(Scope(s => s.IsCurrentMember = false), Chunk(KnowledgeSourceKinds.Academy)).Should().BeFalse();
		}

		[Test]
		public void Protocol_managers_and_dispatchers_read_as_the_library_does_and_unknown_kinds_are_never_read()
		{
			var restricted = Chunk(KnowledgeSourceKinds.Protocol, audience: new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "77") });
			var dispatch = Chunk(KnowledgeSourceKinds.Protocol, flags: KnowledgeAccessFlags.DispatchersRead, audience: new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "77") });

			KnowledgeAccessFilter.CanRead(Scope(s => s.Allow(KnowledgeSourceKinds.Protocol, new[] { "10" })), restricted).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(Scope(s => { s.Allow(KnowledgeSourceKinds.Protocol, new[] { "10" }); s.IsProtocolManager = true; }), restricted).Should().BeTrue();
			KnowledgeAccessFilter.CanRead(Scope(s => { s.Allow(KnowledgeSourceKinds.Protocol, new[] { "10" }); s.IsDispatcher = true; }), dispatch).Should().BeTrue();
			KnowledgeAccessFilter.CanRead(Scope(s => { s.Allow(KnowledgeSourceKinds.Protocol, new[] { "10" }); s.IsDispatcher = true; }), restricted).Should().BeFalse();

			KnowledgeAccessFilter.CanRead(Scope(), new KnowledgeChunk { DepartmentId = DepartmentId, SourceKind = 42, ItemId = "10" }).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(null, restricted).Should().BeFalse();
		}

		[Test]
		public void A_training_not_open_to_all_is_read_by_whoever_the_live_rule_allows()
		{
			var assigned = Chunk(KnowledgeSourceKinds.Training, flags: KnowledgeAccessFlags.AssigneesRead);
			KnowledgeAccessFilter.CanRead(Scope(), assigned).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(Scope(s => s.Allow(KnowledgeSourceKinds.Training, new[] { "10" })), assigned).Should().BeTrue();
		}

		[Test]
		public void An_audience_too_large_to_store_is_stored_as_nobodys()
		{
			var many = Enumerable.Range(1, 2000).Select(i => new ContentAudienceRule(ContentAudienceKinds.User, Guid.NewGuid().ToString()));
			var json = KnowledgeAccessFilter.SerializeAudience(many);
			json.Length.Should().BeLessThanOrEqualTo(KnowledgeChunk.AudienceMaxLength);
			KnowledgeAccessFilter.ParseAudience(json).Should().ContainSingle().Which.RefId.Should().Be("!overflow");
			KnowledgeAccessFilter.SerializeAudience(new[] { new ContentAudienceRule(ContentAudienceKinds.Everyone) }).Should().Contain("\"Kind\":0");
			KnowledgeAccessFilter.SerializeAudience(null).Should().BeNull();
		}
	}
}
