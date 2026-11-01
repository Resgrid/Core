using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Features.Indexed;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard
{
	/// <summary>
	/// Board resolution and persistence (home-dashboard-redesign-plan.md §5.3–5.4): saved board → role/group template →
	/// department default template → built-in default; optimistic concurrency; reset; the classic switch; templates.
	/// </summary>
	[TestFixture]
	public class DashboardServiceTests
	{
		private const int DepartmentId = 7;
		private const string UserId = "user-1";

		private Mock<IUserDashboardsRepository> _boards;
		private Mock<IDepartmentDashboardTemplatesRepository> _templates;
		private Mock<ICacheProvider> _cache;
		private Mock<IIndex<string, IDashboardOptionSource>> _sources;
		private List<UserDashboard> _rows;
		private List<DepartmentDashboardTemplate> _templateRows;
		private bool _cacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_cacheEnabled = global::Resgrid.Config.SystemBehaviorConfig.CacheEnabled;
			global::Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;

			_rows = new List<UserDashboard>();
			_templateRows = new List<DepartmentDashboardTemplate>();
			_boards = new Mock<IUserDashboardsRepository>();
			_templates = new Mock<IDepartmentDashboardTemplatesRepository>();
			_cache = new Mock<ICacheProvider>();
			_sources = new Mock<IIndex<string, IDashboardOptionSource>>();

			_boards.Setup(r => r.GetForUserAsync(DepartmentId, UserId)).ReturnsAsync(() => _rows.ToList());
			_boards.Setup(r => r.GetByIdForUserAsync(It.IsAny<string>(), DepartmentId, UserId)).ReturnsAsync((string id, int d, string u) => _rows.FirstOrDefault(r => r.UserDashboardId == id));
			_boards.Setup(r => r.InsertAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UserDashboard b, CancellationToken c, bool f) => { _rows.Add(b); return b; });
			_boards.Setup(r => r.DeleteAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((UserDashboard b, CancellationToken c) => _rows.Remove(_rows.First(r => r.UserDashboardId == b.UserDashboardId)));
			_boards.Setup(r => r.UpdateWithRowVersionAsync(It.IsAny<UserDashboard>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((UserDashboard b, int expected, CancellationToken c) =>
				{
					var stored = _rows.FirstOrDefault(r => r.UserDashboardId == b.UserDashboardId);
					if (stored == null || stored.RowVersion != expected)
						return false;

					stored.LayoutJson = b.LayoutJson;
					stored.RowVersion = expected + 1;
					b.RowVersion = expected + 1;
					return true;
				});

			_templates.Setup(r => r.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _templateRows.ToList());
		}

		[TearDown]
		public void TearDown() => global::Resgrid.Config.SystemBehaviorConfig.CacheEnabled = _cacheEnabled;

		private DashboardService Service() => new DashboardService(_boards.Object, _templates.Object, _cache.Object, _sources.Object);

		private static DashboardViewer Viewer(DashboardAudiences audience = DashboardAudiences.Member, int[] roles = null, int[] groups = null) => new DashboardViewer
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			IsDepartmentAdmin = audience == DashboardAudiences.Administrator,
			GrantedPolicies = audience == DashboardAudiences.Dispatcher
				? new HashSet<string>(new[] { DashboardViewer.DispatcherPolicy }, StringComparer.Ordinal)
				: new HashSet<string>(StringComparer.Ordinal),
			PersonnelRoleIds = roles ?? Array.Empty<int>(),
			GroupIds = groups ?? Array.Empty<int>()
		};

		private static string Layout(params string[] keys) => DashboardLayoutValidator.Serialize(new DashboardLayout
		{
			Widgets = keys.Select((k, i) => new DashboardLayoutWidget { Id = $"t{i}", Key = k, Size = "m" }).ToList()
		});

		private DepartmentDashboardTemplate AddTemplate(string id, DashboardTemplateAudiences audience, int? target, int priority, bool isDefault, string key)
		{
			var template = new DepartmentDashboardTemplate
			{
				DepartmentDashboardTemplateId = id, DepartmentId = DepartmentId, Name = id, Audience = (int)audience, TargetId = target,
				Priority = priority, IsDefault = isDefault, LayoutJson = Layout(key)
			};
			_templateRows.Add(template);
			return template;
		}

		[Test]
		public async Task A_member_who_never_saved_gets_the_built_in_default_for_their_audience()
		{
			var boards = await Service().GetBoardsAsync(DepartmentId, UserId, Viewer(DashboardAudiences.Dispatcher));

			boards.Boards.Should().ContainSingle();
			boards.Boards[0].Source.Should().Be(DashboardLayoutSources.Default);
			boards.Boards[0].BoardId.Should().BeNull();
			boards.Boards[0].Layout.Widgets.Select(w => w.Id).Should().Equal(DashboardBuiltInLayouts.For(DashboardAudiences.Dispatcher).Widgets.Select(w => w.Id));
		}

		[Test]
		public async Task An_uncompleted_viewer_resolves_the_same_default_the_bootstrap_showed()
		{
			// WidgetData and SaveLayout resolve the board before the department facts are completed: the audience must come
			// from the identity facts alone, or an admin's unsaved home board resolves as the member layout and every
			// instance id misses.
			var admin = new DashboardViewer { DepartmentId = DepartmentId, UserId = UserId, IsDepartmentAdmin = true };
			var groupAdmin = new DashboardViewer { DepartmentId = DepartmentId, UserId = UserId, IsGroupAdmin = true };

			admin.Audience.Should().Be(DashboardAudiences.Administrator);
			groupAdmin.Audience.Should().Be(DashboardAudiences.Dispatcher);
			new DashboardViewer().Audience.Should().Be(DashboardAudiences.Member);

			var board = await Service().GetBoardAsync(DepartmentId, UserId, admin);
			board.Layout.Widgets.Select(w => w.Id).Should().Equal(DashboardBuiltInLayouts.For(DashboardAudiences.Administrator).Widgets.Select(w => w.Id));
		}

		[Test]
		public async Task A_role_template_beats_the_department_default_and_priority_orders_matches()
		{
			AddTemplate("default", DashboardTemplateAudiences.Everyone, null, 0, true, "retired.default");
			AddTemplate("role-low", DashboardTemplateAudiences.PersonnelRole, 5, 10, false, "retired.rolelow");
			AddTemplate("role-high", DashboardTemplateAudiences.PersonnelRole, 5, 1, false, "retired.rolehigh");
			AddTemplate("other-role", DashboardTemplateAudiences.PersonnelRole, 9, 0, false, "retired.other");

			var board = await Service().GetBoardAsync(DepartmentId, UserId, Viewer(roles: new[] { 5 }));

			board.Source.Should().Be(DashboardLayoutSources.Template);
			board.TemplateId.Should().Be("role-high");

			var nobody = await Service().GetBoardAsync(DepartmentId, UserId, Viewer(groups: new[] { 99 }));
			nobody.TemplateId.Should().Be("default");
		}

		[Test]
		public async Task A_group_template_applies_to_members_of_the_group()
		{
			AddTemplate("station-2", DashboardTemplateAudiences.Group, 22, 0, false, "retired.station");

			var board = await Service().GetBoardAsync(DepartmentId, UserId, Viewer(groups: new[] { 22 }));

			board.TemplateId.Should().Be("station-2");
		}

		[Test]
		public async Task First_save_creates_the_home_row_and_later_saves_need_the_current_row_version()
		{
			var service = Service();
			var layout = new DashboardLayout { Widgets = { new DashboardLayoutWidget { Id = "a", Key = "department.info", Size = "s" } } };

			var first = await service.SaveBoardAsync(DepartmentId, UserId, Viewer(), null, 0, layout, null, CancellationToken.None);
			first.Saved.Should().BeTrue();
			first.Board.Source.Should().Be(DashboardLayoutSources.User);
			first.Board.RowVersion.Should().Be(1);
			_rows.Should().ContainSingle(r => r.SortOrder == 0);

			var stale = await service.SaveBoardAsync(DepartmentId, UserId, Viewer(), null, 0, layout, null, CancellationToken.None);
			stale.Error.Should().Be(DashboardSaveErrors.Conflict);
			stale.Current.Should().NotBeNull();

			var second = await service.SaveBoardAsync(DepartmentId, UserId, Viewer(), first.Board.BoardId, 1, layout, null, CancellationToken.None);
			second.Saved.Should().BeTrue();
			second.Board.RowVersion.Should().Be(2);
		}

		[Test]
		public async Task Save_rejects_invalid_layouts()
		{
			var result = await Service().SaveBoardAsync(DepartmentId, UserId, Viewer(), null, 0,
				new DashboardLayout { SchemaVersion = DashboardLayout.CurrentSchemaVersion + 1 }, null, CancellationToken.None);

			result.Error.Should().Be(DashboardSaveErrors.Invalid);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task Reset_removes_the_home_row_so_the_member_follows_the_default_again()
		{
			var service = Service();
			await service.SaveBoardAsync(DepartmentId, UserId, Viewer(), null, 0,
				new DashboardLayout { Widgets = { new DashboardLayoutWidget { Id = "a", Key = "department.info" } } }, null, CancellationToken.None);

			var reset = await service.ResetBoardAsync(DepartmentId, UserId, Viewer(), null, CancellationToken.None);

			reset.Source.Should().Be(DashboardLayoutSources.Default);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task The_classic_switch_lives_on_a_layoutless_row_that_still_follows_the_default()
		{
			var service = Service();

			await service.SetUseClassicAsync(DepartmentId, UserId, true, CancellationToken.None);

			(await service.GetUseClassicAsync(DepartmentId, UserId)).Should().BeTrue();
			var boards = await service.GetBoardsAsync(DepartmentId, UserId, Viewer(DashboardAudiences.Administrator));
			boards.UseClassic.Should().BeTrue();
			boards.Boards.Should().ContainSingle();
			boards.Boards[0].BoardId.Should().NotBeNull("the switch's row is the home board");
			boards.Boards[0].Layout.Widgets.Select(w => w.Id).Should().Equal(DashboardBuiltInLayouts.For(DashboardAudiences.Administrator).Widgets.Select(w => w.Id));

			// Resetting a classic row keeps the switch but clears the layout.
			await service.SaveBoardAsync(DepartmentId, UserId, Viewer(), boards.Boards[0].BoardId, boards.Boards[0].RowVersion,
				new DashboardLayout { Widgets = { new DashboardLayoutWidget { Id = "a", Key = "department.info" } } }, null, CancellationToken.None);
			await service.ResetBoardAsync(DepartmentId, UserId, Viewer(), null, CancellationToken.None);
			_rows.Should().ContainSingle().Which.LayoutJson.Should().BeNull();
		}

		[Test]
		public async Task Extra_boards_are_capped_and_the_home_board_cannot_be_deleted()
		{
			var service = Service();
			for (var i = 1; i < DashboardLimits.MaxBoards; i++)
				(await service.AddBoardAsync(DepartmentId, UserId, Viewer(), $"Board {i}", CancellationToken.None)).Saved.Should().BeTrue();

			(await service.AddBoardAsync(DepartmentId, UserId, Viewer(), "one too many", CancellationToken.None)).Error.Should().Be(DashboardSaveErrors.LimitReached);

			var home = await service.SaveBoardAsync(DepartmentId, UserId, Viewer(), null, 0, new DashboardLayout(), null, CancellationToken.None);
			(await service.DeleteBoardAsync(DepartmentId, UserId, home.Board.BoardId, CancellationToken.None)).Should().BeFalse();
		}

		[Test]
		public async Task Saving_a_default_template_clears_the_previous_default_and_role_templates_need_a_target()
		{
			_templates.Setup(r => r.InsertAsync(It.IsAny<DepartmentDashboardTemplate>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentDashboardTemplate t, CancellationToken c, bool f) => t);

			var service = Service();
			var saved = await service.SaveTemplateAsync(DepartmentId, UserId, new DashboardTemplateSaveRequest
			{
				Name = "Engine company", IsDefault = true, Layout = new DashboardLayout()
			}, CancellationToken.None);

			saved.Template.Should().NotBeNull();
			_templates.Verify(r => r.ClearDefaultAsync(DepartmentId, saved.Template.DepartmentDashboardTemplateId, It.IsAny<CancellationToken>()), Times.Once);

			var noTarget = await service.SaveTemplateAsync(DepartmentId, UserId, new DashboardTemplateSaveRequest
			{
				Name = "Officers", Audience = DashboardTemplateAudiences.PersonnelRole, Layout = new DashboardLayout()
			}, CancellationToken.None);
			noTarget.Error.Should().Be(DashboardSaveErrors.Invalid);
		}

		[Test]
		public async Task Unknown_option_sources_come_back_empty()
		{
			IDashboardOptionSource source = null;
			_sources.Setup(s => s.TryGetValue("nope", out source)).Returns(false);

			var options = await Service().GetSettingOptionsAsync(DepartmentId, UserId, new[] { "nope" });

			options["nope"].Should().BeEmpty();
		}
	}
}
