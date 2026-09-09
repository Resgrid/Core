using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[TestCase(0), TestCase(49), TestCase(50), TestCase(51)]
		public async Task Definition_paging_counts_visible_rows_and_looks_ahead(int count)
		{
			_authorization.Setup(a => a.CanManageAsync(It.IsAny<ChecklistActor>())).ReturnsAsync(false);
			var version = new ChecklistDefinitionVersion { DepartmentId = 77, Content = JsonConvert.SerializeObject(Form()) };
			await _store.WriteAsync(version, true);
			for (var i = 0; i < 110; i++)
				await _store.WriteAsync(new ChecklistDefinition { DepartmentId = 77, Content = "hidden draft", CreatedOn = DateTime.UtcNow.AddDays(1) }, true);
			for (var i = 0; i < count; i++)
				await _store.WriteAsync(new ChecklistDefinition { DepartmentId = 77, CurrentVersionId = version.Id, Content = "draft", CreatedOn = DateTime.UtcNow.AddSeconds(i) }, true);
			var first = await _service.ListAsync(_actor, includeNext: true);
			first.Should().HaveCount(count);
			first.All(d => d.Definition.Content == null).Should().BeTrue();
			var second = await _service.ListAsync(_actor, 1, includeNext: true);
			second.Should().HaveCount(Math.Max(0, count - 50));
			second.Select(d => d.Definition.Id).Should().NotIntersectWith(first.Take(50).Select(d => d.Definition.Id));
		}

		[Test]
		public async Task History_paging_skips_unreadable_runs_before_counting_rows()
		{
			var definition = Guid.NewGuid().ToString();
			var occurrence = new ChecklistOccurrence { DepartmentId = 77, Content = JsonConvert.SerializeObject(new ChecklistTarget { Name = "Visible target" }) };
			await _store.WriteAsync(occurrence, true);
			for (var i = 0; i < 160; i++)
				await _store.WriteAsync(new ChecklistCompletion { DepartmentId = 77, ParentId = definition, OccurrenceId = occurrence.Id,
					CreatedBy = i < 109 ? "another member" : _actor.UserId, CreatedOn = DateTime.UtcNow.AddSeconds(-i), Content = "{}" }, true);
			var first = await _service.HistoryAsync(_actor, definition, includeNext: true);
			first.Should().HaveCount(51);
			var second = await _service.HistoryAsync(_actor, definition, 1, includeNext: true);
			second.Should().ContainSingle();
			second[0].Completion.Id.Should().Be(first[50].Completion.Id);
		}

		[Test]
		public async Task Member_definition_view_reveals_the_published_version_once()
		{
			var setup = await Start();
			_authorization.Setup(a => a.CanManageAsync(It.IsAny<ChecklistActor>())).ReturnsAsync(false);
			_read.Invocations.Clear();
			var view = await _service.GetDefinitionAsync(_actor, setup.Definition);
			view.Form.Name.Should().Be(view.PublishedForm.Name);
			_read.Invocations.Count(i => i.Method.Name == "ResolveRecordsEntitiesForReadAsync").Should().Be(1);
		}

		[TestCase("\"Signed independently\"", "Signed independently")]
		[TestCase("{}", null)]
		[TestCase("null", null)]
		public async Task Witness_attestation_is_projected_as_text_before_rendering(string value, string expected)
		{
			var setup = await Start();
			var row = await _store.GetAsync<ChecklistCompletion>(77, setup.Run);
			row.Content = "{\"WitnessAttestation\":" + value + "}";
			await _store.WriteAsync(row, false);
			(await _service.GetRunAsync(_actor, setup.Run)).WitnessAttestation.Should().Be(expected);
		}
	}

	[TestFixture, NonParallelizable]
	public class ChecklistPr503ControllerTests
	{
		[TestCase("null"), TestCase("/* empty */"), TestCase("{")]
		public async Task Invalid_json_is_rejected_before_either_save_service_is_called(string json)
		{
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			try
			{
				ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "author"), new Claim(ClaimTypes.PrimaryGroupSid, "77") }, "test"))
				} };
				var service = new Mock<IChecklistsService>(MockBehavior.Strict);
				var controller = new ChecklistsController(Mock.Of<IChecklistTemplateService>(), service.Object, Mock.Of<IReadinessAccessService>(), Mock.Of<IProtectedGrantContext>(), Mock.Of<IDepartmentDataProtectionService>(), null);
				Func<Task> definition = () => controller.SaveDefinition(null, 0, json);
				Func<Task> run = () => controller.SaveRun(Guid.NewGuid().ToString(), json, false);
				(await definition.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(400);
				(await run.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(400);
				service.VerifyNoOtherCalls();
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}

	public partial class ChecklistDatabaseTests
	{
		[Test]
		public async Task Batched_answers_and_child_reads_preserve_null_outcomes_ownership_and_metadata_only_files()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
			var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
			var parents = new List<string>();
			for (var i = 0; i < 2; i++)
			{
				var completion = Row<ChecklistCompletion>(definition.Id); completion.VersionId = version.Id; completion.TargetId = "77";
				var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.VersionId = version.Id; occurrence.TargetId = "77"; occurrence.CompletionId = completion.Id;
				await store.WriteAsync(occurrence, true); completion.OccurrenceId = occurrence.Id; await store.WriteAsync(completion, true); parents.Add(completion.Id);
				var answers = Enumerable.Range(0, 205).Select(n => { var answer = Row<ChecklistCompletionItem>(completion.Id); answer.ItemId = Guid.NewGuid().ToString(); answer.IsFailure = n % 2 == 0 ? null : false; answer.Content = "quoted ' content"; return answer; }).ToList();
				await store.ReplaceAnswersAsync(77, completion.Id, answers);
				var file = Row<ChecklistCompletionFile>(completion.Id); file.ItemId = answers[0].ItemId; file.ContentType = "image/png"; file.Size = 3; file.Data = new byte[] { 1, 2, 3 }; file.Sha256 = "hash";
				await store.WriteAsync(file, true);
			}
			uow.CommitChanges();
			var rows = await store.ListChildrenAsync<ChecklistCompletionItem>(77, parents, take: 500);
			rows.Should().HaveCount(410); rows.Count(r => r.IsFailure == null).Should().Be(206); rows.Should().OnlyContain(r => r.Content == "quoted ' content");
			(await store.ListChildrenAsync<ChecklistCompletionItem>(88, parents)).Should().BeEmpty();
			(await store.ListChildrenAsync<ChecklistCompletionItem>(77, new[] { Guid.NewGuid().ToString() })).Should().BeEmpty();
			(await store.ListChildrenAsync<ChecklistCompletionFile>(77, parents)).Should().HaveCount(2).And.OnlyContain(f => f.Data == null && f.Size == 3);
			(await store.ListChildrenAsync<ChecklistCompletionItem>(77, parents, 400)).Should().HaveCount(10);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var invalid = Row<ChecklistCompletionItem>(parents[1]); invalid.ItemId = Guid.NewGuid().ToString();
			Func<Task> replace = () => store.ReplaceAnswersAsync(77, parents[0], new[] { invalid });
			await replace.Should().ThrowAsync<InvalidOperationException>();
			(await store.ListChildrenAsync<ChecklistCompletionItem>(77, new[] { parents[0] }, take: 500)).Should().HaveCount(205);
			uow.DiscardChanges();
		}

		[Test]
		public async Task Negative_list_offset_reports_skip()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			Func<Task> list = () => store.ListAsync<ChecklistDefinition>(77, skip: -1);
			(await list.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which.ParamName.Should().Be("skip");
		}
	}
}
