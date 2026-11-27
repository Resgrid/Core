using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Services;
using Resgrid.Web.Controllers;
using File = System.IO.File;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P5 (round 6 A): public pages (eligibility guard over every field, revocation, the embed CSP), the template library (clone keeps
	/// attribution and creates drafts), the open format (version 2 round trip, version 1 files still import, the schema documents every field)
	/// and the analytics maths.
	/// </summary>
	[TestFixture]
	public class ProtocolSharingTests
	{
		private const int Dept = 10;

		#region Public pages: the eligibility guard

		private static DispatchProtocol Published(int id = 1, bool licensed = false) => new DispatchProtocol
		{
			DispatchProtocolId = id,
			DepartmentId = Dept,
			Name = "Mayday " + id,
			Code = "SOG-" + id,
			Kind = (int)ProtocolKinds.Sog,
			Status = (int)ProtocolStatuses.Published,
			CurrentVersionId = 100 + id,
			IsLicensedContent = licensed,
			OwnerUserId = "owner-secret",
			Tags = "rit",
			ExcludeFromAi = true
		};

		private static ProtocolVersion InForce(int protocolId = 1, string html = "<p>Call it early.</p>") => new ProtocolVersion
		{
			ProtocolVersionId = 100 + protocolId,
			DispatchProtocolId = protocolId,
			DepartmentId = Dept,
			Status = (int)ProtocolVersionStatuses.Published,
			Major = 2,
			Minor = 1,
			EffectiveOn = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
			ContentHtml = html,
			ContentHash = ProtocolContentHash.Compute(html),
			ApprovedByUserId = "approver-secret"
		};

		private static List<IContentAudienceRule> Everyone() => new List<IContentAudienceRule>();

		[Test]
		public void A_published_protocol_in_force_without_an_audience_is_eligible()
		{
			ProtocolPublicPages.Eligibility(Published(), InForce(), Everyone()).Should().Be(ProtocolPublicEligibility.Eligible);
		}

		[Test]
		public void Drafts_audiences_licensed_document_disabled_and_stale_versions_are_never_public()
		{
			var draft = Published();
			draft.Status = (int)ProtocolStatuses.Draft;
			ProtocolPublicPages.Eligibility(draft, InForce(), Everyone()).Should().Be(ProtocolPublicEligibility.NotPublished);

			ProtocolPublicPages.Eligibility(Published(licensed: true), InForce(), Everyone()).Should().Be(ProtocolPublicEligibility.LicensedContent);

			var restricted = new List<IContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Group, "4") };
			ProtocolPublicPages.Eligibility(Published(), InForce(), restricted).Should().Be(ProtocolPublicEligibility.AudienceRestricted);

			var document = InForce();
			document.ContentKind = (int)ProtocolVersionContentKinds.Document;
			ProtocolPublicPages.Eligibility(Published(), document, Everyone()).Should().Be(ProtocolPublicEligibility.DocumentBacked);

			var disabled = Published();
			disabled.IsDisabled = true;
			ProtocolPublicPages.Eligibility(disabled, InForce(), Everyone()).Should().Be(ProtocolPublicEligibility.Disabled);

			var deleted = Published();
			deleted.IsDeleted = true;
			ProtocolPublicPages.Eligibility(deleted, InForce(), Everyone()).Should().Be(ProtocolPublicEligibility.NotPublished);

			// A version that is not the one in force (a draft, a superseded one, another department's) never shows.
			foreach (var status in new[] { ProtocolVersionStatuses.Draft, ProtocolVersionStatuses.InReview, ProtocolVersionStatuses.Approved, ProtocolVersionStatuses.Superseded })
			{
				var version = InForce();
				version.Status = (int)status;
				ProtocolPublicPages.Eligibility(Published(), version, Everyone()).Should().Be(ProtocolPublicEligibility.NotPublished, status.ToString());
			}
			var other = InForce();
			other.DepartmentId = 99;
			ProtocolPublicPages.Eligibility(Published(), other, Everyone()).Should().Be(ProtocolPublicEligibility.NotPublished);
			var notCurrent = InForce();
			notCurrent.ProtocolVersionId = 555;
			ProtocolPublicPages.Eligibility(Published(), notCurrent, Everyone()).Should().Be(ProtocolPublicEligibility.NotPublished);

			ProtocolPublicPages.Project(Published(licensed: true), InForce(), Everyone(), null, "Sog", "<p>x</p>", "en", null).Should().BeNull();
			ProtocolPublicPages.Project(Published(), InForce(), restricted, null, "Sog", "<p>x</p>", "en", null).Should().BeNull();
		}

		[Test]
		public void The_public_projection_carries_only_the_allowed_fields()
		{
			// Guard over every field: adding a property to the public page types fails here until it is reviewed as public.
			typeof(PublicProtocolEntry).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
				"Title", "Number", "KindName", "CategoryName", "VersionLabel", "EffectiveOn", "BodyHtml", "Locale", "Attribution", "ContentShortCode");
			typeof(PublicProtocolAttribution).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("SourceName", "Author", "License", "LicenseUrl", "SourceVersion");
			typeof(PublicProtocolPage).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("DepartmentName", "Title", "IsSet", "Superseded", "GeneratedOn", "Entries");

			var entry = ProtocolPublicPages.Project(Published(), InForce(), Everyone(), "Fireground", "Sog", "<p>Call it early.</p>", "en",
				new ContentAttribution { SourceName = "Resgrid template library: Fire", License = "CC-BY-4.0", LicenseUrl = "javascript:alert(1)", SourceVersion = "1.0" });
			var json = JsonConvert.SerializeObject(entry);
			json.Should().NotContain("owner-secret").And.NotContain("approver-secret").And.NotContain("rit").And.NotContain("\"101\"");
			entry.VersionLabel.Should().Be("2.1");
			entry.Number.Should().Be("SOG-1");
			entry.ContentShortCode.Should().Be(SignaturePolicy.ShortCode(InForce().ContentHash));
			entry.Attribution.LicenseUrl.Should().BeNull("only http(s) licence links reach a public page");
		}

		#endregion Public pages: the eligibility guard

		#region Public pages: the service

		private sealed class PublicWorld
		{
			public readonly Mock<IProtocolPublicLinksRepository> Links = new Mock<IProtocolPublicLinksRepository>();
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<IProtocolLibraryService> Library = new Mock<IProtocolLibraryService>();
			public readonly Mock<IProtocolVersionsRepository> Versions = new Mock<IProtocolVersionsRepository>();
			public readonly Mock<IContentAttributionsRepository> Attributions = new Mock<IContentAttributionsRepository>();
			public readonly Mock<IProtocolTranslationService> Translations = new Mock<IProtocolTranslationService>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
			public readonly List<DispatchProtocol> All = new List<DispatchProtocol>();
			public readonly Dictionary<int, ProtocolVersion> ById = new Dictionary<int, ProtocolVersion>();
			public readonly List<ProtocolAudience> Audiences = new List<ProtocolAudience>();
			public readonly List<ProtocolPublicLink> Stored = new List<ProtocolPublicLink>();
			public readonly ProtocolPublicPageService Service;

			public PublicWorld()
			{
				Departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Anytown FD" });
				Protocols.Setup(x => x.GetProtocolHeaderByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => All.FirstOrDefault(p => p.DispatchProtocolId == id));
				Protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => All.ToList());
				Library.Setup(x => x.GetAudienceAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Audiences.Where(a => a.DispatchProtocolId == id).ToList());
				Library.Setup(x => x.GetDepartmentAudiencesAsync(Dept)).ReturnsAsync(() => Audiences.ToList());
				Library.Setup(x => x.GetVersionAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => ById.TryGetValue(id, out var v) ? v : null);
				Library.Setup(x => x.GetDepartmentVersionHeadersAsync(Dept)).ReturnsAsync(() => ById.Values.ToList());
				Library.Setup(x => x.GetCategoriesAsync(Dept)).ReturnsAsync(new List<ProtocolCategory>
				{
					new ProtocolCategory { ProtocolCategoryId = 7, DepartmentId = Dept, Name = "Fireground" },
					new ProtocolCategory { ProtocolCategoryId = 8, DepartmentId = Dept, Name = "RIT", ParentCategoryId = 7 }
				});
				Versions.Setup(x => x.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => ById.TryGetValue(id, out var v) ? v : null);
				Versions.Setup(x => x.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()))
					.ReturnsAsync((int d, IEnumerable<int> ids, bool _) => ids.Where(ById.ContainsKey).Select(i => ById[i]).ToList());
				Attributions.Setup(x => x.GetForSubjectsAsync(Dept, ContentAttributionSubjects.ProtocolVersion, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<ContentAttribution>());
				Links.Setup(x => x.GetByTokenAsync(It.IsAny<string>())).ReturnsAsync((string token) => Stored.FirstOrDefault(l => l.Token == token));
				Links.Setup(x => x.GetForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync((int d, bool revoked) => Stored.Where(l => revoked || !l.IsRevoked).ToList());
				Links.Setup(x => x.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Stored.FirstOrDefault(l => l.ProtocolPublicLinkId == id));
				Links.Setup(x => x.InsertAsync(It.IsAny<ProtocolPublicLink>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ProtocolPublicLink l, CancellationToken _, bool __) => { l.ProtocolPublicLinkId = Stored.Count + 1; Stored.Add(l); return l; });
				Links.Setup(x => x.TryRevokeAsync(Dept, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, int id, string user, DateTime on, CancellationToken _) =>
					{
						var link = Stored.FirstOrDefault(l => l.ProtocolPublicLinkId == id && !l.IsRevoked);
						if (link == null) return false;
						link.RevokedOn = on;
						link.RevokedByUserId = user;
						return true;
					});
				Service = new ProtocolPublicPageService(Links.Object, Protocols.Object, Library.Object, Versions.Object, Attributions.Object, Translations.Object, Departments.Object, Events.Object);
			}

			public DispatchProtocol Add(int id, int? categoryId = 7, bool licensed = false, bool draft = false)
			{
				var protocol = Published(id, licensed);
				protocol.CategoryId = categoryId;
				if (draft)
				{
					protocol.Status = (int)ProtocolStatuses.Draft;
					protocol.CurrentVersionId = null;
				}
				All.Add(protocol);
				var version = InForce(id, "<p>Text of " + id + "</p>");
				if (draft)
					version.Status = (int)ProtocolVersionStatuses.Draft;
				ById[version.ProtocolVersionId] = version;
				return protocol;
			}
		}

		private static ProtocolReader Approver() => new ProtocolReader { DepartmentId = Dept, UserId = "approver", IsCurrentMember = true, CanApprove = true };

		[Test]
		public async Task A_protocol_link_serves_the_version_in_force_and_stops_at_once_when_revoked()
		{
			var world = new PublicWorld();
			world.Add(1);
			var linkEvents = new List<ProtocolWorkflowEvent>();
			world.Events.Setup(x => x.SendMessage(It.IsAny<ProtocolWorkflowEvent>())).Callback((ProtocolWorkflowEvent e) => linkEvents.Add(e));

			var created = await world.Service.CreateProtocolLinkAsync(Approver(), 1, false, false, null);
			created.Succeeded.Should().BeTrue();
			ProtocolPublicPages.IsTokenShape(created.Link.Token).Should().BeTrue();

			var page = await world.Service.GetPageAsync(created.Link.Token, null);
			page.Entries.Should().ContainSingle().Which.Title.Should().Be("Mayday 1");
			page.DepartmentName.Should().Be("Anytown FD");

			(await world.Service.RevokeAsync(Approver(), created.Link.ProtocolPublicLinkId)).Succeeded.Should().BeTrue();
			(await world.Service.GetPageAsync(created.Link.Token, null)).Should().BeNull("a revoked link is gone at once");
			(await world.Service.GetEmbedPolicyAsync(created.Link.Token)).Enabled.Should().BeFalse();
			world.Events.Verify(x => x.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.ProtocolPublicLinkRevoked)), Times.Once);

			// Round 7C, workflow trigger 250: created then revoked, once each, never the token.
			(await world.Service.RevokeAsync(Approver(), created.Link.ProtocolPublicLinkId)).Succeeded.Should().BeTrue();
			linkEvents.Select(e => e.PublicLinkAction).Should().Equal("Created", "Revoked");
			linkEvents.Should().OnlyContain(e => e.Trigger == WorkflowTriggerEventType.ProtocolPublicLinkChanged && e.ProtocolId == 1 && e.PublicLinkKind == "Protocol" &&
				e.PublicLinkId == created.Link.ProtocolPublicLinkId && e.UserId == "approver");
			Newtonsoft.Json.JsonConvert.SerializeObject(linkEvents).Should().NotContain(created.Link.Token);
		}

		[Test]
		public async Task A_link_whose_protocol_later_gains_an_audience_or_a_licence_flag_shows_nothing()
		{
			var world = new PublicWorld();
			var protocol = world.Add(1);
			var link = (await world.Service.CreateProtocolLinkAsync(Approver(), 1, false, false, null)).Link;

			world.Audiences.Add(new ProtocolAudience { DepartmentId = Dept, DispatchProtocolId = 1, Kind = (int)ContentAudienceKinds.Role, RefId = "3" });
			(await world.Service.GetPageAsync(link.Token, null)).Should().BeNull();

			world.Audiences.Clear();
			protocol.IsLicensedContent = true;
			(await world.Service.GetPageAsync(link.Token, null)).Should().BeNull();

			protocol.IsLicensedContent = false;
			protocol.Status = (int)ProtocolStatuses.Retired;
			(await world.Service.GetPageAsync(link.Token, null)).Should().BeNull();
		}

		[Test]
		public async Task Making_licensed_audience_or_draft_protocols_public_is_refused_with_the_reason()
		{
			var world = new PublicWorld();
			world.Add(1, licensed: true);
			world.Add(2, draft: true);
			world.Add(3);
			world.Audiences.Add(new ProtocolAudience { DepartmentId = Dept, DispatchProtocolId = 3, Kind = (int)ContentAudienceKinds.Group, RefId = "5" });

			var licensed = await world.Service.CreateProtocolLinkAsync(Approver(), 1, false, false, null);
			licensed.Error.Should().Be(ProtocolPublicLinkErrors.NotEligible);
			licensed.Eligibility.Should().Be(ProtocolPublicEligibility.LicensedContent);
			(await world.Service.CreateProtocolLinkAsync(Approver(), 2, false, false, null)).Eligibility.Should().Be(ProtocolPublicEligibility.NotPublished);
			(await world.Service.CreateProtocolLinkAsync(Approver(), 3, false, false, null)).Eligibility.Should().Be(ProtocolPublicEligibility.AudienceRestricted);

			var member = new ProtocolReader { DepartmentId = Dept, UserId = "member", IsCurrentMember = true, CanAuthor = true };
			(await world.Service.CreateProtocolLinkAsync(member, 3, false, false, null)).Error.Should().Be(ProtocolPublicLinkErrors.NotAuthorized);
			world.Stored.Should().BeEmpty();
		}

		[Test]
		public async Task A_category_set_shows_only_its_eligible_protocols_including_sub_categories()
		{
			var world = new PublicWorld();
			world.Add(1, categoryId: 7);
			world.Add(2, categoryId: 8);
			world.Add(3, categoryId: 7, licensed: true);
			world.Add(4, categoryId: 7, draft: true);
			world.Add(5, categoryId: 7);
			world.Add(6, categoryId: null);
			world.Audiences.Add(new ProtocolAudience { DepartmentId = Dept, DispatchProtocolId = 5, Kind = (int)ContentAudienceKinds.Shift, RefId = "2" });

			var link = (await world.Service.CreateCategoryLinkAsync(Approver(), 7, true, "https://fire.example.org")).Link;
			var page = await world.Service.GetPageAsync(link.Token, null);

			page.IsSet.Should().BeTrue();
			page.Title.Should().Be("Fireground");
			page.Entries.Select(e => e.Title).Should().BeEquivalentTo("Mayday 1", "Mayday 2");
		}

		[Test]
		public async Task A_pinned_version_says_superseded_once_a_newer_version_is_in_force()
		{
			var world = new PublicWorld();
			var protocol = world.Add(1);
			var link = (await world.Service.CreateProtocolLinkAsync(Approver(), 1, true, false, null)).Link;
			link.ProtocolVersionId.Should().Be(101);

			var newer = InForce(1, "<p>Newer</p>");
			newer.ProtocolVersionId = 201;
			world.ById[201] = newer;
			protocol.CurrentVersionId = 201;

			var page = await world.Service.GetPageAsync(link.Token, null);
			page.Superseded.Should().BeTrue();
			page.Entries.Should().BeEmpty();
		}

		[Test]
		public async Task Unknown_and_malformed_tokens_find_nothing()
		{
			var world = new PublicWorld();
			(await world.Service.GetPageAsync("not-a-token", null)).Should().BeNull();
			(await world.Service.GetPageAsync(ProtocolPublicPages.NewToken(), null)).Should().BeNull();
			world.Links.Verify(x => x.GetByTokenAsync("not-a-token"), Times.Never);
		}

		#endregion Public pages: the service

		#region Public pages: CSP and the embed route

		[Test]
		public void Embed_origins_are_normalized_https_origins_only()
		{
			ProtocolPublicPages.NormalizeEmbedOrigins(" https://Fire.Example.org/ https://fire.example.org,https://ems.example.org:8443", out var valid)
				.Should().Be("https://fire.example.org https://ems.example.org:8443");
			valid.Should().BeTrue();
			ProtocolPublicPages.NormalizeEmbedOrigins("", out valid).Should().BeEmpty();
			valid.Should().BeTrue();
			foreach (var bad in new[] { "http://fire.example.org", "https://fire.example.org/page", "*", "https://a.org; script-src *", "javascript:alert(1)" })
			{
				ProtocolPublicPages.NormalizeEmbedOrigins(bad, out valid).Should().BeNull(bad);
				valid.Should().BeFalse(bad);
			}
		}

		[Test]
		public void The_page_is_never_framed_and_runs_no_script_while_the_embed_route_is_framed_only_by_its_sites()
		{
			ProtocolPublicPages.PageCsp.Should().Contain("frame-ancestors 'none'").And.Contain("default-src 'none'").And.NotContain("script-src");
			ProtocolPublicPages.EmbedPolicy(new ProtocolPublicLink { EmbedEnabled = false }).Enabled.Should().BeFalse();
			ProtocolPublicPages.EmbedPolicy(new ProtocolPublicLink { EmbedEnabled = true }).FrameAncestors.Should().Be("https:");
			ProtocolPublicPages.EmbedPolicy(new ProtocolPublicLink { EmbedEnabled = true, EmbedOrigins = "https://fire.example.org" }).FrameAncestors.Should().Be("https://fire.example.org");
			ProtocolPublicPages.EmbedPolicy(new ProtocolPublicLink { EmbedEnabled = true, RevokedOn = DateTime.UtcNow }).Enabled.Should().BeFalse();
			ProtocolPublicPages.EmbedCsp("https://fire.example.org").Should().Contain("frame-ancestors https://fire.example.org;").And.NotContain("script-src");
			ProtocolPublicPages.EmbedSnippet("https://resgrid.example/public/protocols/abc/embed", "<b>\"x\"</b>").Should().NotContain("<b>").And.Contain("sandbox=");
		}

		private static PublicProtocolsController Controller(Mock<IProtocolPublicPageService> pages)
		{
			var http = new DefaultHttpContext();
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113." + new Random().Next(1, 250));
			return new PublicProtocolsController(pages.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[Test]
		public async Task The_public_controller_sends_the_strict_headers_and_404s_unknown_or_unembeddable_links()
		{
			var token = ProtocolPublicPages.NewToken();
			var pages = new Mock<IProtocolPublicPageService>();
			pages.Setup(x => x.GetPageAsync(token, It.IsAny<string>())).ReturnsAsync(new PublicProtocolPage { DepartmentName = "Anytown FD" });
			pages.Setup(x => x.GetEmbedPolicyAsync(token)).ReturnsAsync(new PublicProtocolEmbedPolicy { Enabled = false, FrameAncestors = "'none'" });

			var controller = Controller(pages);
			(await controller.Index(token)).Should().BeOfType<ViewResult>();
			controller.Response.Headers["Content-Security-Policy"].ToString().Should().Be(ProtocolPublicPages.PageCsp);
			controller.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
			controller.Response.Headers["X-Robots-Tag"].ToString().Should().Contain("noindex");

			var unknown = Controller(pages);
			(await unknown.Index(ProtocolPublicPages.NewToken())).Should().BeOfType<NotFoundResult>();
			unknown.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");

			var embedOff = Controller(pages);
			(await embedOff.Embed(token)).Should().BeOfType<NotFoundResult>();
			embedOff.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("frame-ancestors 'none'");

			pages.Setup(x => x.GetEmbedPolicyAsync(token)).ReturnsAsync(new PublicProtocolEmbedPolicy { Enabled = true, FrameAncestors = "https://fire.example.org" });
			var embed = Controller(pages);
			(await embed.Embed(token)).Should().BeOfType<ViewResult>();
			embed.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("frame-ancestors https://fire.example.org;").And.NotContain("script-src");
			embed.Response.Headers.ContainsKey("X-Frame-Options").Should().BeFalse();
		}

		#endregion Public pages: CSP and the embed route

		#region Template library

		[Test]
		public void Every_template_set_is_a_valid_version_2_set_of_valid_protocols()
		{
			var sets = ProtocolTemplateLibrary.Sets;
			sets.Select(s => s.Set.Key).Should().BeEquivalentTo("fire-sog-starter", "sar-starter", "industrial-hazard-starter", "business-continuity-starter", "ems-clinical-structure",
				"law-enforcement-policy-starter", "security-post-orders-starter", "workplace-policies-starter");
			foreach (var set in sets)
			{
				set.Version.Should().Be(2, set.Set.Key);
				set.Format.Should().Be(ProtocolExchangeDocument.FormatName);
				ContentLicenses.IsContributable(set.Set.License).Should().BeTrue(set.Set.Key);
				set.Set.LicenseUrl.Should().Be(ContentLicenses.UrlFor(set.Set.License));
				set.Protocols.Select(p => p.Code).Should().OnlyHaveUniqueItems();
				foreach (var item in set.Protocols)
				{
					item.IsLicensedContent.Should().BeFalse();
					Enum.TryParse<ProtocolKinds>(item.Kind, out var kind).Should().BeTrue(item.Code);
					ProtocolKindRules.IsAuthorable((int)kind).Should().BeTrue(item.Code);
					ProtocolRules.Validate(new DispatchProtocol { Name = item.Name, Code = item.Code, Description = item.Description }).Should().BeEmpty(item.Code);
					var normalized = ProtocolBlocks.Normalize(item.Content);
					normalized.Blocks.Should().HaveCount(item.Content.Blocks.Count, item.Code + " has no block the normalizer drops");
					normalized.Render().Should().NotBeNullOrWhiteSpace();
				}
			}
		}

		[Test]
		public void The_EMS_set_is_structure_only_with_no_clinical_text_or_doses()
		{
			var ems = ProtocolTemplateLibrary.Get("ems-clinical-structure");
			ems.Set.License.Should().Be(ContentLicenses.Cc0);
			var json = ems.ToJson();
			json.Should().NotContain("\"medication\"");
			Regex.IsMatch(json, @"\d+(\.\d+)?\s?(mg|mcg|ml|mL|g|units|J)\b").Should().BeFalse("no doses or energies");
			foreach (var item in ems.Protocols)
				item.Content.Render().Should().Contain("[Medical director approved");
		}

		[Test]
		public async Task Cloning_a_set_creates_drafts_that_keep_the_attribution_through_later_drafts()
		{
			var world = new ProtocolLibraryWorld();
			var nextId = 900;
			world.ProtocolsService.Setup(x => x.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatchProtocol p, CancellationToken _) => { if (p.DispatchProtocolId == 0) p.DispatchProtocolId = nextId++; world.Protocols[p.DispatchProtocolId] = p; return p; });
			var events = new Mock<IEventAggregator>();
			var templates = new ProtocolTemplateLibraryService(world.Service, events.Object);

			var result = await templates.CloneAsync(ProtocolLibraryWorld.Manager(), "fire-sog-starter", new[] { "SOG-101", "sog-102" });

			result.Error.Should().BeNull();
			result.Created.Should().Be(2);
			result.CreatedProtocolIds.Should().HaveCount(2);
			foreach (var id in result.CreatedProtocolIds)
			{
				world.Protocols[id].Status.Should().Be((int)ProtocolStatuses.Draft, "nothing is published by a clone");
				world.Protocols[id].IsLicensedContent.Should().BeFalse();
				var version = world.Versions.Values.Single(v => v.DispatchProtocolId == id);
				version.Status.Should().Be((int)ProtocolVersionStatuses.Draft);
				ProtocolContentDocument.Parse(version.ContentJson).IsBlockModel.Should().BeTrue();
				var attribution = world.Attributions[((int)ContentAttributionSubjects.ProtocolVersion, version.ProtocolVersionId)];
				attribution.SourceKind.Should().Be((int)ContentAttributionSources.TemplateLibrary);
				attribution.SourceRef.Should().Be("fire-sog-starter");
				attribution.SourceName.Should().Contain("Resgrid template library");
				attribution.License.Should().Be(ContentLicenses.CcBy);
				attribution.SourceVersion.Should().Be("1.0");
			}
			events.Verify(x => x.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.ProtocolTemplateCloned)), Times.Once);

			// Import and modify: publish the clone, start a new draft, and the attribution is still there.
			var first = result.CreatedProtocolIds[0];
			var published = world.Versions.Values.Single(v => v.DispatchProtocolId == first);
			published.Status = (int)ProtocolVersionStatuses.Published;
			published.Major = 1;
			world.Protocols[first].Status = (int)ProtocolStatuses.Published;
			world.Protocols[first].CurrentVersionId = published.ProtocolVersionId;
			var draft = await world.Service.StartDraftAsync(ProtocolLibraryWorld.Manager(), first);
			draft.Succeeded.Should().BeTrue();
			world.Attributions[((int)ContentAttributionSubjects.ProtocolVersion, draft.Version.ProtocolVersionId)].SourceRef.Should().Be("fire-sog-starter");
		}

		[Test]
		public async Task Only_authors_clone_templates()
		{
			var world = new ProtocolLibraryWorld();
			var templates = new ProtocolTemplateLibraryService(world.Service, Mock.Of<IEventAggregator>());
			var member = new ProtocolReader { DepartmentId = ProtocolLibraryWorld.DepartmentId, UserId = "m", IsCurrentMember = true };
			(await templates.CloneAsync(member, "fire-sog-starter", null)).Error.Should().Be("not_authorized");
			(await templates.CloneAsync(ProtocolLibraryWorld.Manager(), "no-such-set", null)).Error.Should().Be("not_found");
			world.Versions.Should().BeEmpty();
		}

		#endregion Template library

		#region Open format

		/// <summary>A file written by P1's export (version 1): it must keep importing.</summary>
		private const string Version1File = @"{ ""format"": ""resgrid-protocols"", ""version"": 1, ""exportedOn"": ""2026-11-24T12:00:00Z"",
  ""protocols"": [ { ""code"": ""SOG-204"", ""name"": ""Mayday"", ""kind"": ""Sog"", ""number"": ""SOG-204"", ""category"": ""Fireground"",
    ""tags"": ""rit,mayday"", ""description"": ""Calling a Mayday"", ""minimumWeight"": 0, ""requiresAcknowledgment"": true, ""reviewIntervalDays"": 365,
    ""excludeFromAi"": false, ""isLicensedContent"": false, ""versionLabel"": ""3.1"",
    ""content"": { ""schema"": 1, ""blocks"": [ { ""type"": ""html"", ""key"": ""b1"", ""html"": ""<h2>Scene</h2><p>Call it early.</p>"" } ], ""anchors"": [] },
    ""triggers"": [], ""questions"": [ { ""question"": ""Trapped?"", ""answers"": [ { ""answer"": ""Yes"", ""weight"": 1 } ] } ], ""audience"": [] } ] }";

		private static ProtocolLibraryWorld ImportWorld()
		{
			var world = new ProtocolLibraryWorld();
			var nextId = 700;
			world.ProtocolsService.Setup(x => x.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatchProtocol p, CancellationToken _) => { if (p.DispatchProtocolId == 0) p.DispatchProtocolId = nextId++; world.Protocols[p.DispatchProtocolId] = p; return p; });
			world.ProtocolsService.Setup(x => x.GetProtocolSummariesForDepartmentAsync(ProtocolLibraryWorld.DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => world.Protocols.Values.ToList());
			world.VersionsRepository.Setup(x => x.GetHeadersForDepartmentAsync(ProtocolLibraryWorld.DepartmentId)).ReturnsAsync(() => world.Versions.Values.ToList());
			return world;
		}

		[Test]
		public async Task A_version_1_file_still_imports_as_a_draft_without_attribution()
		{
			var world = ImportWorld();
			var document = ProtocolExchangeDocument.Parse(Version1File);
			document.Version.Should().Be(1);

			var result = await world.Service.ImportAsync(ProtocolLibraryWorld.Manager(), document);

			result.Error.Should().BeNull();
			result.Created.Should().Be(1);
			var version = world.Versions.Values.Single();
			version.Status.Should().Be((int)ProtocolVersionStatuses.Draft);
			version.ContentHtml.Should().Contain("Call it early.");
			world.Attributions.Should().BeEmpty();
		}

		[Test]
		public async Task An_unknown_format_version_is_refused()
		{
			var world = ImportWorld();
			var document = ProtocolExchangeDocument.Parse(Version1File);
			document.Version = 3;
			(await world.Service.ImportAsync(ProtocolLibraryWorld.Manager(), document)).Error.Should().Be("unknown_format");
			document.Version = 0;
			(await world.Service.ImportAsync(ProtocolLibraryWorld.Manager(), document)).Error.Should().Be("unknown_format");
		}

		[Test]
		public async Task A_version_2_export_round_trips_blocks_graph_and_attribution()
		{
			var source = ImportWorld();
			var set = ProtocolTemplateLibrary.Get("sar-starter");
			var withAttribution = ProtocolExchangeDocument.Parse(set.ToJson());
			foreach (var item in withAttribution.Protocols)
				item.Attribution = new ProtocolExchangeAttribution { Source = "County EMS office", Author = "Medical director", License = "CC-BY-4.0", SourceVersion = "4" };
			var graphItem = withAttribution.Protocols[0];
			graphItem.Kind = "Dispatch";
			graphItem.Content.Graph = new ProtocolDecisionGraph
			{
				Nodes = new List<ProtocolDecisionNode>
				{
					new ProtocolDecisionNode { Key = "q1", IsRoot = true, Kind = (int)ProtocolDecisionNodeKinds.Question, QuestionType = (int)ProtocolQuestionTypes.YesNo, Text = "Is the person a child?", Answers = new List<ProtocolDecisionAnswer>
					{
						new ProtocolDecisionAnswer { Key = "y", Label = "Yes", Next = "o1" }, new ProtocolDecisionAnswer { Key = "n", Label = "No", Next = "o2" }
					} },
					new ProtocolDecisionNode { Key = "o1", Kind = (int)ProtocolDecisionNodeKinds.Outcome, Determinant = "SAR-1-E" },
					new ProtocolDecisionNode { Key = "o2", Kind = (int)ProtocolDecisionNodeKinds.Outcome, Determinant = "SAR-1-C" }
				}
			};

			(await source.Service.ImportAsync(ProtocolLibraryWorld.Manager(), withAttribution)).Created.Should().Be(4);
			var exported = await source.Service.ExportAsync(ProtocolLibraryWorld.Manager(), Enumerable.Empty<int>());
			exported.Version.Should().Be(2);
			exported.Schema.Should().Be(ProtocolExchangeDocument.SchemaId);
			exported.Protocols.Should().HaveCount(4);
			exported.Protocols.Should().OnlyContain(p => p.Attribution != null && p.Attribution.Source == "County EMS office" && p.ContentHash != null);

			// Through text, as a file travels, into a second department's library.
			var file = exported.ToJson();
			var target = ImportWorld();
			var reimport = await target.Service.ImportAsync(ProtocolLibraryWorld.Manager(), ProtocolExchangeDocument.Parse(file));
			reimport.Created.Should().Be(4);
			var again = await target.Service.ExportAsync(ProtocolLibraryWorld.Manager(), Enumerable.Empty<int>());

			string Blocks(ProtocolExchangeItem item) => JsonConvert.SerializeObject(new { item.Content.Blocks, item.Content.Graph });
			foreach (var original in exported.Protocols)
			{
				var copy = again.Protocols.Single(p => p.Code == original.Code);
				Blocks(copy).Should().Be(Blocks(original), original.Code);
				copy.ContentHash.Should().Be(original.ContentHash, "the same content gives the same hash");
				copy.Attribution.Should().BeEquivalentTo(original.Attribution);
				copy.Questions.Should().BeEquivalentTo(original.Questions);
			}
			again.Protocols.Single(p => p.Code == graphItem.Code).Content.HasGraph.Should().BeTrue();
		}

		[Test]
		public void The_documented_schema_names_every_field_of_the_format()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull();
			var schema = JObject.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "docs", "protocols", "resgrid-protocols-2.schema.json")));
			File.Exists(Path.Combine(directory.FullName, "docs", "protocols", "open-format.md")).Should().BeTrue();

			IEnumerable<string> JsonNames(Type type) => type.GetProperties()
				.Select(p => p.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName).Where(n => n != null);
			IEnumerable<string> Documented(JToken node) => ((JObject)node["properties"]).Properties().Select(p => p.Name);

			JsonNames(typeof(ProtocolExchangeDocument)).Should().BeSubsetOf(Documented(schema));
			var defs = schema["$defs"];
			JsonNames(typeof(ProtocolExchangeItem)).Should().BeSubsetOf(Documented(defs["protocol"]));
			JsonNames(typeof(ProtocolExchangeSet)).Should().BeSubsetOf(Documented(defs["set"]));
			JsonNames(typeof(ProtocolExchangeAttribution)).Should().BeSubsetOf(Documented(defs["attribution"]));
			JsonNames(typeof(ProtocolExchangeVersion)).Should().BeSubsetOf(Documented(defs["historyVersion"]));
			JsonNames(typeof(ProtocolExchangeTranslation)).Should().BeSubsetOf(Documented(defs["translation"]));
			JsonNames(typeof(ProtocolExchangeAttachment)).Should().BeSubsetOf(Documented(defs["attachment"]));
			JsonNames(typeof(ProtocolContentDocument)).Should().BeSubsetOf(Documented(defs["content"]));
			JsonNames(typeof(ProtocolContentBlock)).Should().BeSubsetOf(Documented(defs["block"]));
			JsonNames(typeof(ProtocolMedication)).Should().BeSubsetOf(Documented(defs["medication"]));
			((JArray)defs["block"]["properties"]["type"]["enum"]).Select(t => (string)t).Should().BeEquivalentTo(ProtocolBlockTypes.All);
			((JArray)schema["properties"]["version"]["enum"]).Select(t => (int)t).Should().Contain(ProtocolExchangeDocument.CurrentVersion);
		}

		#endregion Open format

		#region Analytics maths

		private static ProtocolCallUsageRow Row(int id, int call, int protocol, int? version, string determinant = null, string overrideReason = null, int? source = null,
			DateTime? removed = null, string stepLog = null) => new ProtocolCallUsageRow
			{
				CallProtocolId = id, CallId = call, DispatchProtocolId = protocol, ProtocolVersionId = version, Determinant = determinant, OverrideReason = overrideReason,
				Source = source, RemovedOn = removed, StepLogJson = stepLog, LoggedOn = new DateTime(2026, 11, 1)
			};

		private static string Log(int minutes, bool complete = true)
		{
			var start = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
			return new ProtocolStepLog
			{
				StartedOn = start,
				CompletedOn = complete ? start.AddMinutes(minutes) : (DateTime?)null,
				Entries = new List<ProtocolStepLogEntry> { new ProtocolStepLogEntry { StepId = "s1", At = start.AddMinutes(minutes) } }
			}.ToJson();
		}

		[Test]
		public void Usage_determinants_overrides_time_and_compliance_are_counted_exactly()
		{
			var now = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
			var protocols = new[]
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = Dept, Name = "Breathing", Code = "D-06", CurrentVersionId = 11 },
				new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = Dept, Name = "Unused", Code = "D-99" },
				new DispatchProtocol { DispatchProtocolId = 3, DepartmentId = 77, Name = "Other department", Code = "X" }
			};
			var versions = new[]
			{
				new ProtocolVersion { ProtocolVersionId = 10, DispatchProtocolId = 1, Major = 1, Minor = 0, Status = (int)ProtocolVersionStatuses.Superseded },
				new ProtocolVersion { ProtocolVersionId = 11, DispatchProtocolId = 1, Major = 1, Minor = 1, Status = (int)ProtocolVersionStatuses.Published }
			};
			var calls = new[]
			{
				Row(1, 100, 1, 10, "06-D-1", null, (int)CallProtocolSources.Trigger, stepLog: Log(4)),
				Row(2, 101, 1, 11, "06-D-1", "Caller unreliable", (int)CallProtocolSources.Manual, stepLog: Log(8)),
				Row(3, 102, 1, 11, "06-c-1", null, (int)CallProtocolSources.Ai, stepLog: Log(6, complete: false)),
				Row(4, 103, 1, 11, null, null, null, stepLog: Log(60 * 13)),
				Row(5, 104, 1, 11, "06-D-1", null, null, removed: now),
				Row(6, 100, 1, 10, null)
			};
			var acks = new[]
			{
				new ProtocolAcknowledgment { DepartmentId = Dept, DispatchProtocolId = 1, ProtocolVersionId = 11, Status = (int)ProtocolAcknowledgmentStatuses.Acknowledged },
				new ProtocolAcknowledgment { DepartmentId = Dept, DispatchProtocolId = 1, ProtocolVersionId = 11, Status = (int)ProtocolAcknowledgmentStatuses.Open, DueOn = now.AddDays(-1) },
				new ProtocolAcknowledgment { DepartmentId = Dept, DispatchProtocolId = 1, ProtocolVersionId = 11, Status = (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz, DueOn = now.AddDays(3) },
				new ProtocolAcknowledgment { DepartmentId = Dept, DispatchProtocolId = 1, ProtocolVersionId = 11, Status = (int)ProtocolAcknowledgmentStatuses.Withdrawn },
				new ProtocolAcknowledgment { DepartmentId = Dept, DispatchProtocolId = 1, ProtocolVersionId = 10, Status = (int)ProtocolAcknowledgmentStatuses.Acknowledged }
			};

			var report = ProtocolAnalytics.Build(Dept, now.AddDays(-30), now, protocols, versions, calls, acks, now);

			report.Rows.Select(r => r.DispatchProtocolId).Should().Equal(1, 2);
			var row = report.Rows[0];
			row.Calls.Should().Be(4, "distinct calls, the removed attachment excluded");
			row.Removed.Should().Be(1);
			row.ByTrigger.Should().Be(3, "rows without a source are trigger rows");
			row.ByDispatcher.Should().Be(1);
			row.ByAi.Should().Be(1);
			row.Versions.Should().BeEquivalentTo(new[]
			{
				new ProtocolVersionUsage { ProtocolVersionId = 11, Label = "1.1", Calls = 3 },
				new ProtocolVersionUsage { ProtocolVersionId = 10, Label = "1.0", Calls = 1 }
			}, o => o.WithStrictOrdering());
			row.WithDeterminant.Should().Be(3);
			row.Overridden.Should().Be(1);
			row.OverrideRate.Should().Be(0.3333m);
			row.Determinants.Should().BeEquivalentTo(new[]
			{
				new ProtocolDeterminantCount { Determinant = "06-D-1", Calls = 2, Overridden = 1 },
				new ProtocolDeterminantCount { Determinant = "06-C-1", Calls = 1, Overridden = 0 }
			}, o => o.WithStrictOrdering());
			row.TimedCalls.Should().Be(3, "a 13-hour log is a log left open");
			row.MedianMinutes.Should().Be(6m);
			row.MeanMinutes.Should().Be(6m);
			row.AckAssigned.Should().Be(3, "withdrawn rows and other versions owe nothing");
			row.AckSigned.Should().Be(1);
			row.AckOverdue.Should().Be(1);
			row.AckCompliance.Should().Be(0.3333m);

			report.Rows[1].Calls.Should().Be(0);
			report.Rows[1].OverrideRate.Should().BeNull();
			report.Rows[1].MedianMinutes.Should().BeNull();
			report.TotalCalls.Should().Be(4);
			report.OverrideRate.Should().Be(0.3333m);
			report.AckCompliance.Should().Be(0.3333m);
		}

		[Test]
		public void Median_mean_rates_and_queries_follow_their_definitions()
		{
			ProtocolAnalytics.Median(new List<double> { 3, 1, 2 }).Should().Be(2m);
			ProtocolAnalytics.Median(new List<double> { 4, 1, 3, 2 }).Should().Be(2.5m);
			ProtocolAnalytics.Median(new List<double>()).Should().BeNull();
			ProtocolAnalytics.Mean(new List<double> { 1, 2 }).Should().Be(1.5m);
			ProtocolAnalytics.Rate(1, 3).Should().Be(0.3333m);
			ProtocolAnalytics.Rate(0, 0).Should().BeNull();
			ProtocolAnalytics.NormalizeQuery("  Confined   SPACE ").Should().Be("confined space");
			ProtocolAnalytics.NormalizeQuery(" a ").Should().BeNull();
			ProtocolAnalytics.NormalizeQuery(new string('x', 500)).Should().HaveLength(ProtocolSearchMiss.QueryMaxLength);
			ProtocolAnalytics.MinutesOnProtocol(null).Should().BeNull();
			ProtocolAnalytics.MinutesOnProtocol("{ not json").Should().BeNull();
			ProtocolAnalytics.MinutesOnProtocol(Log(-5)).Should().BeNull();
		}

		[Test]
		public void The_csv_guards_formulas_and_uses_the_invariant_culture()
		{
			var report = new ProtocolAnalyticsReport();
			report.Rows.Add(new ProtocolAnalyticsRow { Name = "=HYPERLINK(\"x\")", Number = "D-06", Calls = 3, OverrideRate = 0.25m, MedianMinutes = 6.5m });
			var headers = Enumerable.Range(1, 19).Select(i => "H" + i).ToList();
			var previous = System.Globalization.CultureInfo.CurrentCulture;
			try
			{
				System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
				var csv = ProtocolAnalytics.ToCsv(report, headers);
				var line = csv.Split('\n')[1];
				line.Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"").And.Contain(",0.25,").And.Contain(",6.5,");
				line.Split(new[] { "\",\"", "," }, StringSplitOptions.None).Length.Should().BeGreaterThan(10);
			}
			finally
			{
				System.Globalization.CultureInfo.CurrentCulture = previous;
			}
			Resgrid.Web.Areas.User.Controllers.ProtocolSharingController.CsvHeaderKeys.Should().HaveCount(19, "one header per CSV column");
		}

		[Test]
		public void The_period_is_capped_at_a_year_and_never_runs_past_tomorrow()
		{
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			var (from, to) = ProtocolAnalyticsService.ClampPeriod(now.AddYears(-3), now.AddYears(1), now);
			to.Should().Be(now.Date.AddDays(1));
			(to - from).TotalDays.Should().Be(ProtocolAnalytics.MaxPeriodDays);
			var (f2, t2) = ProtocolAnalyticsService.ClampPeriod(now, now.AddDays(-5), now);
			t2.Should().Be(now.AddDays(-5));
			f2.Should().Be(t2.AddDays(-30));
		}

		[Test]
		public async Task Analytics_are_for_managers_and_a_search_miss_is_counted_by_query_never_by_person()
		{
			var misses = new Mock<IProtocolSearchMissesRepository>();
			var service = new ProtocolAnalyticsService(Mock.Of<IProtocolAnalyticsRepository>(), misses.Object, Mock.Of<IProtocolsService>(), Mock.Of<IProtocolLibraryService>(),
				Mock.Of<IProtocolAcknowledgmentsRepository>(), Mock.Of<IKnowledgeGapsService>(), Mock.Of<IEventAggregator>());

			(await service.GetReportAsync(new ProtocolReader { DepartmentId = Dept, UserId = "m", IsCurrentMember = true }, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow))
				.Should().BeNull();

			await service.RecordSearchMissAsync(Dept, ProtocolSearchSurfaces.Library, "  Trench   Rescue ");
			await service.RecordSearchMissAsync(Dept, ProtocolSearchSurfaces.Library, " ");
			misses.Verify(x => x.RecordAsync(Dept, ProtocolSearchSurfaces.Library, "trench rescue", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			misses.VerifyNoOtherCalls();

			misses.Setup(x => x.RecordAsync(It.IsAny<int>(), It.IsAny<ProtocolSearchSurfaces>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("down"));
			await service.Invoking(s => s.RecordSearchMissAsync(Dept, ProtocolSearchSurfaces.Library, "hazmat")).Should().NotThrowAsync("a miss never fails the search");
		}

		#endregion Analytics maths
	}
}
