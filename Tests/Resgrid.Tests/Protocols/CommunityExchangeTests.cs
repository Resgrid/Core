using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// The community exchange (P5 / T5, decision 7c): the review gate (nothing is listed, shown to others or imported before a Resgrid reviewer
	/// lists it), licensed-content refusal, department-data stripping with phone numbers flagged, attribution kept on import, ratings, comments
	/// and the LMS-type rule.
	/// </summary>
	[TestFixture]
	public class CommunityExchangeTests
	{
		private const int Contributor = 10;
		private const int Importer = 20;

		#region Scrubber

		[Test]
		public void Department_data_is_stripped_and_phone_numbers_are_flagged_but_kept()
		{
			var scrubber = new ExchangeScrubber("Anytown Fire Department");
			var text = scrubber.Html("<p>Call the Anytown Fire Department duty officer at 555-201-4477 or chief@anytown.gov. Give 2.5 mg, repeat in 5 min (v3.1, 2026-11-25).</p>" +
				"<p><a href=\"mailto:chief@anytown.gov\">mail</a></p>");

			text.Should().NotContain("Anytown Fire Department").And.Contain(ExchangeScrubber.DepartmentPlaceholder);
			text.Should().NotContain("chief@anytown.gov").And.NotContain("mailto:");
			text.Should().Contain("555-201-4477", "phone numbers stay for the reviewer to judge");
			scrubber.Flags.Select(f => f.Kind).Should().BeEquivalentTo(ExchangeReviewFlagKinds.DepartmentNameRemoved, ExchangeReviewFlagKinds.EmailRemoved,
				ExchangeReviewFlagKinds.PhoneNumber);
			scrubber.Flags.Single(f => f.Kind == ExchangeReviewFlagKinds.PhoneNumber).Text.Should().Be("555-201-4477");
			scrubber.Flags.Should().NotContain(f => f.Text == "2.5" || f.Text == "2026-11-25", "doses and dates are not phone numbers");
		}

		[Test]
		public void Block_documents_lose_internal_links_files_inventory_links_contacts_and_scopes()
		{
			var document = ProtocolBlocks.Normalize(new ProtocolContentDocument
			{
				Blocks = new List<ProtocolContentBlock>
				{
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "p1", Html = "<p>Station 4 line +1 (555) 201-4477</p>", Scopes = new List<ProtocolScopeTag> { new ProtocolScopeTag { Id = 3 } } },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Link, Key = "l1", LinkKind = ProtocolLinkKinds.Protocol, RefId = "44", Label = "Our RIT SOG" },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Link, Key = "l2", LinkKind = ProtocolLinkKinds.Url, Url = "https://www.nfpa.org/", Label = "NFPA" },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Attachment, Key = "a1", AttachmentId = 9, Label = "Map" },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Image, Key = "i1", AttachmentId = 10, Alt = "Floor plan" },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Gate, Key = "g1", Html = "<p>Call medical control</p>", Contact = "Dr Smith 555 201 9999" },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Medication, Key = "m1", Medication = new ProtocolMedication { Drug = "Epinephrine", InventoryItemId = "inv-7", InventoryItemName = "Epi kit" } }
				}
			});

			var scrubber = new ExchangeScrubber("Anytown FD");
			var clean = scrubber.Document(document);

			clean.Blocks.Select(b => b.Key).Should().Equal("p1", "l2", "g1", "m1");
			clean.Blocks.Should().OnlyContain(b => b.Scopes == null || b.Scopes.Count == 0);
			clean.Blocks.Single(b => b.Key == "g1").Contact.Should().BeNull();
			clean.Blocks.Single(b => b.Key == "m1").Medication.InventoryItemId.Should().BeNull();
			clean.Blocks.Single(b => b.Key == "m1").Medication.Drug.Should().Be("Epinephrine");
			scrubber.Flags.Select(f => f.Kind).Should().Contain(new[]
			{
				ExchangeReviewFlagKinds.InternalLinkRemoved, ExchangeReviewFlagKinds.AttachmentRemoved, ExchangeReviewFlagKinds.ContactRemoved,
				ExchangeReviewFlagKinds.InventoryLinkRemoved, ExchangeReviewFlagKinds.PhoneNumber
			});
			document.Blocks.Should().HaveCount(7, "the source document is never changed");
		}

		#endregion Scrubber

		#region Sources

		[Test]
		public void Licensed_draft_and_document_protocols_can_never_be_submitted()
		{
			var current = new ProtocolVersion { ProtocolVersionId = 5, Status = (int)ProtocolVersionStatuses.Published };
			ProtocolExchangeItemSource.Blocked(new DispatchProtocol { Status = (int)ProtocolStatuses.Published, CurrentVersionId = 5 }, current).Should().Be(ExchangeErrors.None);
			ProtocolExchangeItemSource.Blocked(new DispatchProtocol { Status = (int)ProtocolStatuses.Published, CurrentVersionId = 5, IsLicensedContent = true }, current)
				.Should().Be(ExchangeErrors.LicensedContent);
			ProtocolExchangeItemSource.Blocked(new DispatchProtocol { Status = (int)ProtocolStatuses.Draft }, null).Should().Be(ExchangeErrors.NotPublished);
			ProtocolExchangeItemSource.Blocked(new DispatchProtocol { Status = (int)ProtocolStatuses.Published, CurrentVersionId = 5 },
				new ProtocolVersion { ProtocolVersionId = 5, Status = (int)ProtocolVersionStatuses.Published, ContentKind = (int)ProtocolVersionContentKinds.Document })
				.Should().Be(ExchangeErrors.DocumentBacked);
			ProtocolExchangeItemSource.Blocked(new DispatchProtocol { Status = (int)ProtocolStatuses.Published, Kind = (int)ProtocolKinds.Bulletin, CurrentVersionId = 5 }, current)
				.Should().Be(ExchangeErrors.NotFound);
			TrainingExchangeItemSource.Blocked(new Training { ExcludeFromAi = true }).Should().Be(ExchangeErrors.LicensedContent);
		}

		private static ExchangeActor Approver(int department = Contributor) => new ExchangeActor
		{
			DepartmentId = department, UserId = "approver-" + department, DepartmentName = "Anytown Fire Department", IsCurrentMember = true,
			CanApproveProtocols = true, CanAuthorProtocols = true, CanCreateTrainings = true
		};

		[Test]
		public async Task A_protocol_submission_carries_no_department_configuration_and_a_licensed_one_is_refused()
		{
			var protocols = new Mock<IProtocolsService>();
			var library = new Mock<IProtocolLibraryService>();
			var protocol = new DispatchProtocol
			{
				DispatchProtocolId = 3, DepartmentId = Contributor, Name = "Mayday", Code = "SOG-101", Number = "AFD-12", Kind = (int)ProtocolKinds.Sog,
				Status = (int)ProtocolStatuses.Published, CurrentVersionId = 30, Description = "<p>For Anytown Fire Department members</p>",
				Triggers = new List<DispatchProtocolTrigger> { new DispatchProtocolTrigger { Type = 1, Priority = 2 } },
				Questions = new List<DispatchProtocolQuestion> { new DispatchProtocolQuestion { Question = "Trapped?", Answers = new List<DispatchProtocolQuestionAnswer>() } }
			};
			protocols.Setup(x => x.GetProtocolByIdAsync(3)).ReturnsAsync(protocol);
			library.Setup(x => x.GetVersionAsync(Contributor, 30)).ReturnsAsync(new ProtocolVersion
			{
				ProtocolVersionId = 30, DepartmentId = Contributor, DispatchProtocolId = 3, Status = (int)ProtocolVersionStatuses.Published, Major = 3, Minor = 1,
				ContentHtml = "<p>Call it early. Duty officer 555-201-4477.</p>", ContentJson = ProtocolContentDocument.FromHtml("<p>Call it early. Duty officer 555-201-4477.</p>").ToJson()
			});
			library.Setup(x => x.GetAudienceAsync(Contributor, 3)).ReturnsAsync(new List<ProtocolAudience> { new ProtocolAudience { Kind = (int)ContentAudienceKinds.Group, RefId = "4" } });
			var source = new ProtocolExchangeItemSource(protocols.Object, library.Object, Mock.Of<IProtocolAccessService>());

			var prepared = await source.PrepareAsync(Approver(), "3");

			prepared.Error.Should().Be(ExchangeErrors.None);
			var item = ProtocolExchangeDocument.Parse(prepared.PayloadJson).Protocols.Single();
			item.Number.Should().BeNull();
			item.Triggers.Should().BeEmpty();
			item.Audience.Should().BeEmpty();
			item.Category.Should().BeNull();
			item.IsLicensedContent.Should().BeFalse();
			item.Description.Should().Contain(ExchangeScrubber.DepartmentPlaceholder).And.NotContain("Anytown");
			item.Questions.Should().ContainSingle();
			prepared.VersionLabel.Should().Be("3.1");
			prepared.Flags.Select(f => f.Kind).Should().Contain(new[] { ExchangeReviewFlagKinds.ConfigurationRemoved, ExchangeReviewFlagKinds.PhoneNumber, ExchangeReviewFlagKinds.DepartmentNameRemoved });

			protocol.IsLicensedContent = true;
			(await source.PrepareAsync(Approver(), "3")).Error.Should().Be(ExchangeErrors.LicensedContent);

			var other = Approver(Importer);
			(await source.PrepareAsync(other, "3")).Error.Should().Be(ExchangeErrors.NotFound, "another department's protocol is never submittable");
		}

		[Test]
		public async Task A_training_submission_has_no_assignees_and_its_preview_hides_the_answers()
		{
			var trainings = new Mock<ITrainingService>();
			trainings.Setup(x => x.GetTrainingByIdAsync(8)).ReturnsAsync(new Training
			{
				TrainingId = 8, DepartmentId = Contributor, Name = "Ladder drill", Description = "<p>Annual</p>", TrainingText = "<p>Raise a 35 ft ladder.</p>",
				UsersToAdd = "u1,u2", MinimumScore = 80,
				Questions = new List<TrainingQuestion>
				{
					new TrainingQuestion { TrainingQuestionId = 1, Question = "Climbing angle?", Answers = new List<TrainingQuestionAnswer>
					{
						new TrainingQuestionAnswer { Answer = "75 degrees", Correct = true }, new TrainingQuestionAnswer { Answer = "45 degrees", Correct = false }
					} }
				}
			});
			var source = new TrainingExchangeItemSource(trainings.Object, Mock.Of<IContentAttributionsRepository>());

			var prepared = await source.PrepareAsync(Approver(), "8");

			prepared.Error.Should().Be(ExchangeErrors.None);
			prepared.PayloadJson.Should().NotContain("u1").And.Contain("75 degrees");
			prepared.Flags.Select(f => f.Kind).Should().Contain(ExchangeReviewFlagKinds.ConfigurationRemoved);
			var preview = source.PreviewHtml(prepared.PayloadJson);
			preview.Should().Contain("Climbing angle?").And.NotContain("75 degrees");
		}

		[Test]
		public async Task An_LMS_lesson_plan_leaves_its_competency_links_behind_and_importing_it_needs_the_add_on()
		{
			var plans = new Mock<ILmsLessonPlanService>();
			var gate = new Mock<ILmsGateService>();
			var manager = new Resgrid.Model.Lms.LmsActor
			{
				DepartmentId = Contributor, UserId = "approver-10", IsCurrentMember = true, CanManageCourses = true,
				Gate = new Resgrid.Model.Lms.LmsDepartmentGate { DepartmentId = Contributor, ModuleOn = true, FlagOn = true, AddonActive = false }
			};
			gate.Setup(x => x.GetActorAsync(Contributor, It.IsAny<string>())).ReturnsAsync(manager);
			plans.Setup(x => x.GetPlanAsync(Contributor, 4)).ReturnsAsync(new Resgrid.Model.Lms.LmsLessonPlan { LmsLessonPlanId = 4, DepartmentId = Contributor, Title = "Ladder raise", CurrentVersionId = 40 });
			plans.Setup(x => x.GetVersionAsync(Contributor, 40)).ReturnsAsync(new Resgrid.Model.Lms.LmsLessonPlanVersion
			{
				LmsLessonPlanVersionId = 40, DepartmentId = Contributor, LmsLessonPlanId = 4, Major = 1, Minor = 2, Title = "Ladder raise",
				ContentJson = JsonConvert.SerializeObject(new Resgrid.Model.Lms.LmsLessonPlanContent
				{
					Objectives = new List<string> { "Raise a 35 ft ladder", "Call Anytown Fire Department dispatch on 555-201-4477" },
					CompetencyItemIds = new List<int> { 7, 8 }
				})
			});
			var source = new LmsLessonPlanExchangeSource(plans.Object, gate.Object, Mock.Of<IContentAttributionsRepository>());

			var prepared = await source.PrepareAsync(Approver(), "4");

			prepared.Error.Should().Be(ExchangeErrors.None);
			prepared.VersionLabel.Should().Be("1.2");
			prepared.PayloadJson.Should().NotContain("Anytown").And.Contain("555-201-4477");
			LmsLessonPlanExchangePayload.Parse(prepared.PayloadJson).Content.CompetencyItemIds.Should().BeEmpty();
			prepared.Flags.Select(f => f.Kind).Should().Contain(new[] { ExchangeReviewFlagKinds.ConfigurationRemoved, ExchangeReviewFlagKinds.PhoneNumber });
			source.NeedsLms.Should().BeTrue();
			(await source.CanImportAsync(Approver())).Should().BeFalse("the add-on is not active");

			manager.Gate.AddonActive = true;
			(await source.CanImportAsync(Approver())).Should().BeTrue();
			manager.CanManageCourses = false;
			(await source.PrepareAsync(Approver(), "4")).Error.Should().Be(ExchangeErrors.NotAuthorized);
		}

		#endregion Sources

		#region The exchange service

		private sealed class ExchangeWorld
		{
			public readonly Dictionary<int, ExchangeListing> Listings = new Dictionary<int, ExchangeListing>();
			public readonly List<ExchangeRating> Ratings = new List<ExchangeRating>();
			public readonly List<ExchangeComment> Comments = new List<ExchangeComment>();
			public readonly List<ExchangeImport> Imports = new List<ExchangeImport>();
			public readonly Mock<IExchangeItemSource> Source = new Mock<IExchangeItemSource>();
			public readonly Mock<IExchangeItemSource> LmsSource = new Mock<IExchangeItemSource>();
			public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
			public ContentAttribution ImportedWith;
			public readonly CommunityExchangeService Service;

			public ExchangeWorld()
			{
				Source.SetupGet(x => x.Kind).Returns(ExchangeItemKinds.Protocol);
				Source.Setup(x => x.CanSubmit(It.IsAny<ExchangeActor>())).Returns((ExchangeActor a) => a.CanApproveProtocols);
				Source.Setup(x => x.CanImportAsync(It.IsAny<ExchangeActor>())).ReturnsAsync((ExchangeActor a) => a.CanAuthorProtocols);
				Source.Setup(x => x.PrepareAsync(It.IsAny<ExchangeActor>(), "1")).ReturnsAsync(new ExchangePreparedItem
				{
					ItemKind = 0, SourceRef = "1", Title = "Mayday", Summary = "Calling a Mayday", VersionLabel = "3.1", PayloadJson = "{\"format\":\"resgrid-protocols\",\"version\":2,\"protocols\":[]}",
					Flags = new List<ExchangeReviewFlag> { new ExchangeReviewFlag { Kind = ExchangeReviewFlagKinds.PhoneNumber, Text = "555-201-4477" } }
				});
				Source.Setup(x => x.PrepareAsync(It.IsAny<ExchangeActor>(), "2")).ReturnsAsync(new ExchangePreparedItem { Error = ExchangeErrors.LicensedContent });
				Source.Setup(x => x.PreviewHtml(It.IsAny<string>())).Returns("<p>preview</p>");
				Source.Setup(x => x.ImportAsync(It.IsAny<ExchangeActor>(), It.IsAny<ExchangeListing>(), It.IsAny<ContentAttribution>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ExchangeActor a, ExchangeListing l, ContentAttribution attribution, CancellationToken _) => { ImportedWith = attribution; return 4321; });

				LmsSource.SetupGet(x => x.Kind).Returns(ExchangeItemKinds.SkillSheet);
				LmsSource.SetupGet(x => x.NeedsLms).Returns(true);
				LmsSource.Setup(x => x.CanImportAsync(It.IsAny<ExchangeActor>())).ReturnsAsync(false);

				var listings = new Mock<IExchangeListingsRepository>();
				listings.Setup(x => x.InsertAsync(It.IsAny<ExchangeListing>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ExchangeListing l, CancellationToken _, bool __) => { l.ExchangeListingId = Listings.Count + 1; Listings[l.ExchangeListingId] = Clone(l); return l; });
				listings.Setup(x => x.GetHeaderAsync(It.IsAny<int>())).ReturnsAsync((int id) => Listings.TryGetValue(id, out var l) ? Strip(Clone(l)) : null);
				listings.Setup(x => x.GetWithPayloadAsync(It.IsAny<int>())).ReturnsAsync((int id) => Listings.TryGetValue(id, out var l) ? Clone(l) : null);
				listings.Setup(x => x.TryUpdateStatusAsync(It.IsAny<ExchangeListing>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ExchangeListing l, int expected, CancellationToken _) =>
					{
						if (!Listings.TryGetValue(l.ExchangeListingId, out var stored) || stored.Status != expected) return false;
						stored.Status = l.Status; stored.ReviewedOn = l.ReviewedOn; stored.ReviewedByUserId = l.ReviewedByUserId; stored.ReviewNote = l.ReviewNote;
						stored.ListedOn = l.ListedOn; stored.WithdrawnOn = l.WithdrawnOn;
						return true;
					});
				listings.Setup(x => x.RefreshRatingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns((int id, CancellationToken _) =>
				{
					Listings[id].RatingCount = Ratings.Count(r => r.ExchangeListingId == id);
					Listings[id].RatingTotal = Ratings.Where(r => r.ExchangeListingId == id).Sum(r => r.Stars);
					return Task.CompletedTask;
				});
				listings.Setup(x => x.IncrementImportCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns((int id, CancellationToken _) => { Listings[id].ImportCount++; return Task.CompletedTask; });
				listings.Setup(x => x.GetPendingReviewAsync(It.IsAny<int>())).ReturnsAsync(() => Listings.Values.Where(l => l.Status == 0).ToList());

				var ratings = new Mock<IExchangeRatingsRepository>();
				ratings.Setup(x => x.GetForListingAndUserAsync(It.IsAny<int>(), It.IsAny<string>()))
					.ReturnsAsync((int id, string user) => Ratings.FirstOrDefault(r => r.ExchangeListingId == id && string.Equals(r.UserId, user, StringComparison.OrdinalIgnoreCase)));
				ratings.Setup(x => x.InsertAsync(It.IsAny<ExchangeRating>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ExchangeRating r, CancellationToken _, bool __) => { Ratings.Add(r); return r; });
				ratings.Setup(x => x.UpdateAsync(It.IsAny<ExchangeRating>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ExchangeRating r, CancellationToken _, bool __) => r);

				var comments = new Mock<IExchangeCommentsRepository>();
				comments.Setup(x => x.InsertAsync(It.IsAny<ExchangeComment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ExchangeComment c, CancellationToken _, bool __) => { c.ExchangeCommentId = Comments.Count + 1; Comments.Add(c); return c; });
				comments.Setup(x => x.GetForListingAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync((int id, bool hidden) => Comments.Where(c => c.ExchangeListingId == id && (hidden || !c.HiddenOn.HasValue)).ToList());
				comments.Setup(x => x.GetCommentAsync(It.IsAny<int>())).ReturnsAsync((int id) => Comments.FirstOrDefault(c => c.ExchangeCommentId == id));
				comments.Setup(x => x.TryHideAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int id, string user, DateTime on, CancellationToken _) =>
					{
						var c = Comments.FirstOrDefault(x => x.ExchangeCommentId == id && !x.HiddenOn.HasValue);
						if (c == null) return false;
						c.HiddenOn = on;
						return true;
					});

				var imports = new Mock<IExchangeImportsRepository>();
				imports.Setup(x => x.InsertAsync(It.IsAny<ExchangeImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ExchangeImport i, CancellationToken _, bool __) => { Imports.Add(i); return i; });
				imports.Setup(x => x.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int d) => Imports.Where(i => i.DepartmentId == d).ToList());

				Service = new CommunityExchangeService(new[] { Source.Object, LmsSource.Object }, listings.Object, ratings.Object, comments.Object, imports.Object, Events.Object);
			}

			private static ExchangeListing Clone(ExchangeListing l) => JsonConvert.DeserializeObject<ExchangeListing>(JsonConvert.SerializeObject(l));

			private static ExchangeListing Strip(ExchangeListing l)
			{
				l.PayloadJson = null;
				return l;
			}
		}

		private static ExchangeActor Reviewer() => new ExchangeActor { DepartmentId = 1, UserId = "resgrid-reviewer", IsCurrentMember = true, IsReviewer = true };

		private static ExchangeSubmitRequest Request(string sourceRef = "1") => new ExchangeSubmitRequest
		{
			ItemKind = (int)ExchangeItemKinds.Protocol, SourceRef = sourceRef, License = ContentLicenses.CcBy, Attested = true
		};

		[Test]
		public async Task Submissions_need_the_attestation_a_licence_and_unlicensed_content()
		{
			var world = new ExchangeWorld();
			var noAttestation = Request();
			noAttestation.Attested = false;
			(await world.Service.SubmitAsync(Approver(), noAttestation)).Error.Should().Be(ExchangeErrors.AttestationRequired);
			var noLicence = Request();
			noLicence.License = "All rights reserved";
			(await world.Service.SubmitAsync(Approver(), noLicence)).Error.Should().Be(ExchangeErrors.LicenseRequired);
			(await world.Service.SubmitAsync(Approver(), Request("2"))).Error.Should().Be(ExchangeErrors.LicensedContent);
			var author = Approver();
			author.CanApproveProtocols = false;
			(await world.Service.SubmitAsync(author, Request())).Error.Should().Be(ExchangeErrors.NotAuthorized);
			world.Listings.Should().BeEmpty();
		}

		[Test]
		public async Task Nothing_is_visible_rateable_or_importable_until_a_reviewer_lists_it()
		{
			var world = new ExchangeWorld();
			var submitted = await world.Service.SubmitAsync(Approver(), Request());
			submitted.Succeeded.Should().BeTrue();
			var id = submitted.Listing.ExchangeListingId;
			world.Listings[id].Status.Should().Be((int)ExchangeListingStatuses.PendingReview);
			world.Listings[id].PayloadHash.Should().Be(ExchangeRules.Hash(world.Listings[id].PayloadJson));
			world.Listings[id].AttestationVersion.Should().Be(ExchangeRules.AttestationVersion);
			world.Listings[id].AttributionName.Should().Be("Anytown Fire Department");
			world.Listings[id].ReviewFlags.Should().ContainSingle(f => f.Kind == ExchangeReviewFlagKinds.PhoneNumber);

			var importer = Approver(Importer);
			(await world.Service.GetListingAsync(importer, id)).Should().BeNull("another department never sees an unreviewed submission");
			(await world.Service.ImportAsync(importer, id)).Error.Should().Be(ExchangeErrors.WrongStatus);
			(await world.Service.RateAsync(importer, id, 5)).Error.Should().Be(ExchangeErrors.WrongStatus);
			(await world.Service.CommentAsync(importer, id, "Nice")).Error.Should().Be(ExchangeErrors.WrongStatus);
			(await world.Service.GetListingAsync(Approver(), id)).Should().NotBeNull("the contributor sees its own submission");

			(await world.Service.DecideAsync(Approver(), id, true, null)).Error.Should().Be(ExchangeErrors.NotAuthorized, "only Resgrid reviewers list");
			(await world.Service.GetReviewQueueAsync(Approver())).Should().BeEmpty();
			(await world.Service.GetReviewQueueAsync(Reviewer())).Should().ContainSingle();
			(await world.Service.DecideAsync(Reviewer(), id, false, " ")).Error.Should().Be(ExchangeErrors.InvalidInput, "declining needs a note");
			(await world.Service.DecideAsync(Reviewer(), id, true, null)).Succeeded.Should().BeTrue();
			world.Listings[id].Status.Should().Be((int)ExchangeListingStatuses.Listed);
			world.Listings[id].ListedOn.Should().NotBeNull();
			(await world.Service.DecideAsync(Reviewer(), id, true, null)).Error.Should().Be(ExchangeErrors.WrongStatus, "decided once");

			var view = await world.Service.GetListingAsync(importer, id);
			view.Should().NotBeNull();
			view.CanImport.Should().BeTrue();
			view.Listing.PayloadJson.Should().BeNull("the payload never leaves the server in a view");
		}

		[Test]
		public async Task A_reviewer_decision_raises_one_reviewed_event_for_the_submitting_department_and_a_refused_one_raises_nothing()
		{
			// Round 7C, workflow trigger 257.
			var world = new ExchangeWorld();
			var raised = new List<ExchangeListingWorkflowEvent>();
			world.Events.Setup(x => x.SendMessage(It.IsAny<ExchangeListingWorkflowEvent>())).Callback((ExchangeListingWorkflowEvent e) => raised.Add(e));
			var id = (await world.Service.SubmitAsync(Approver(), Request())).Listing.ExchangeListingId;

			(await world.Service.DecideAsync(Approver(), id, true, null)).Succeeded.Should().BeFalse();
			(await world.Service.DecideAsync(Reviewer(), id, false, " ")).Succeeded.Should().BeFalse();
			raised.Should().BeEmpty();

			(await world.Service.DecideAsync(Reviewer(), id, false, "Contains a crew member's mobile number")).Succeeded.Should().BeTrue();
			(await world.Service.DecideAsync(Reviewer(), id, true, null)).Succeeded.Should().BeFalse("decided once");

			raised.Should().ContainSingle();
			var reviewed = raised.Single();
			reviewed.Trigger.Should().Be(WorkflowTriggerEventType.ExchangeListingReviewed);
			reviewed.DepartmentId.Should().Be(Contributor);
			reviewed.ListingId.Should().Be(id);
			reviewed.Decision.Should().Be("Rejected");
			reviewed.ItemKind.Should().Be("Protocol");
			Newtonsoft.Json.JsonConvert.SerializeObject(reviewed).Should().NotContain("mobile number", "the review note stays with the listing");
		}

		[Test]
		public async Task An_import_keeps_the_listing_attribution_and_is_counted()
		{
			var world = new ExchangeWorld();
			var id = (await world.Service.SubmitAsync(Approver(), Request())).Listing.ExchangeListingId;
			await world.Service.DecideAsync(Reviewer(), id, true, null);

			var result = await world.Service.ImportAsync(Approver(Importer), id);

			result.Succeeded.Should().BeTrue();
			result.CreatedItemId.Should().Be(4321);
			world.ImportedWith.SourceKind.Should().Be((int)ContentAttributionSources.CommunityExchange);
			world.ImportedWith.SourceRef.Should().Be(id.ToString());
			world.ImportedWith.Author.Should().Be("Anytown Fire Department");
			world.ImportedWith.License.Should().Be(ContentLicenses.CcBy);
			world.ImportedWith.LicenseUrl.Should().Be(ContentLicenses.UrlFor(ContentLicenses.CcBy));
			world.ImportedWith.SourceVersion.Should().Be("3.1");
			world.ImportedWith.DepartmentId.Should().Be(Importer);
			world.Imports.Should().ContainSingle(i => i.DepartmentId == Importer && i.CreatedItemId == 4321);
			world.Listings[id].ImportCount.Should().Be(1);
			world.Events.Verify(x => x.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.ExchangeListingImported && a.DepartmentId == Importer)), Times.Once);

			var noAuthor = Approver(30);
			noAuthor.CanAuthorProtocols = false;
			(await world.Service.ImportAsync(noAuthor, id)).Error.Should().Be(ExchangeErrors.NotAuthorized);
		}

		[Test]
		public async Task A_payload_changed_after_review_is_never_imported()
		{
			var world = new ExchangeWorld();
			var id = (await world.Service.SubmitAsync(Approver(), Request())).Listing.ExchangeListingId;
			await world.Service.DecideAsync(Reviewer(), id, true, null);
			world.Listings[id].PayloadJson = "{\"format\":\"resgrid-protocols\",\"version\":2,\"protocols\":[{\"name\":\"tampered\"}]}";

			(await world.Service.ImportAsync(Approver(Importer), id)).Error.Should().Be(ExchangeErrors.PayloadChanged);
			world.Imports.Should().BeEmpty();
		}

		[Test]
		public async Task LMS_types_need_the_add_on_to_import()
		{
			var world = new ExchangeWorld();
			world.Listings[50] = new ExchangeListing
			{
				ExchangeListingId = 50, DepartmentId = Contributor, ItemKind = (int)ExchangeItemKinds.SkillSheet, Status = (int)ExchangeListingStatuses.Listed,
				PayloadJson = "{}", PayloadHash = ExchangeRules.Hash("{}"), License = ContentLicenses.CcBy, Title = "Ladder raise"
			};
			(await world.Service.ImportAsync(Approver(Importer), 50)).Error.Should().Be(ExchangeErrors.LmsRequired);
			(await world.Service.GetListingAsync(Approver(Importer), 50)).ImportNeedsLms.Should().BeTrue();
		}

		[Test]
		public async Task Ratings_are_one_per_member_never_for_the_own_department_and_comments_are_hidden_not_deleted()
		{
			var world = new ExchangeWorld();
			var id = (await world.Service.SubmitAsync(Approver(), Request())).Listing.ExchangeListingId;
			await world.Service.DecideAsync(Reviewer(), id, true, null);

			(await world.Service.RateAsync(Approver(), id, 5)).Error.Should().Be(ExchangeErrors.OwnListing);
			(await world.Service.RateAsync(Approver(Importer), id, 6)).Error.Should().Be(ExchangeErrors.InvalidInput);
			(await world.Service.RateAsync(Approver(Importer), id, 2)).Succeeded.Should().BeTrue();
			(await world.Service.RateAsync(Approver(Importer), id, 4)).Succeeded.Should().BeTrue();
			(await world.Service.RateAsync(Approver(30), id, 5)).Succeeded.Should().BeTrue();
			world.Ratings.Should().HaveCount(2);
			world.Listings[id].AverageRating.Should().Be(4.5m);

			(await world.Service.CommentAsync(Approver(Importer), id, "<script>alert(1)</script> Works for us")).Succeeded.Should().BeTrue();
			world.Comments.Single().AuthorName.Should().Be("Anytown Fire Department");
			(await world.Service.HideCommentAsync(Approver(30), 1)).Error.Should().Be(ExchangeErrors.NotAuthorized);
			(await world.Service.HideCommentAsync(Reviewer(), 1)).Succeeded.Should().BeTrue();
			world.Comments.Should().ContainSingle(c => c.HiddenOn.HasValue, "hidden, never deleted");
			(await world.Service.GetListingAsync(Approver(Importer), id)).Comments.Should().BeEmpty();
			(await world.Service.GetListingAsync(Reviewer(), id)).Comments.Should().ContainSingle();
		}

		[Test]
		public async Task The_contributor_withdraws_and_a_reviewer_takes_down_with_a_note()
		{
			var world = new ExchangeWorld();
			var id = (await world.Service.SubmitAsync(Approver(), Request())).Listing.ExchangeListingId;
			(await world.Service.WithdrawAsync(Approver(Importer), id)).Error.Should().Be(ExchangeErrors.NotFound);
			(await world.Service.WithdrawAsync(Approver(), id)).Succeeded.Should().BeTrue();
			world.Listings[id].Status.Should().Be((int)ExchangeListingStatuses.Withdrawn);

			var second = (await world.Service.SubmitAsync(Approver(), Request())).Listing.ExchangeListingId;
			await world.Service.DecideAsync(Reviewer(), second, true, null);
			(await world.Service.RemoveAsync(Reviewer(), second, "")).Error.Should().Be(ExchangeErrors.InvalidInput);
			(await world.Service.RemoveAsync(Reviewer(), second, "Contains a licensed card set")).Succeeded.Should().BeTrue();
			world.Listings[second].Status.Should().Be((int)ExchangeListingStatuses.Removed);
			(await world.Service.ImportAsync(Approver(Importer), second)).Error.Should().Be(ExchangeErrors.WrongStatus);
		}

		#endregion The exchange service
	}
}
