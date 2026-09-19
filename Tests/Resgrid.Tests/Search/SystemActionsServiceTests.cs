using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>System functionality search: claim, admin, module and flag gates are applied before scoring; scoring handles slash commands, prefixes and one-edit typos.</summary>
	[TestFixture]
	public class SystemActionsServiceTests
	{
		private Mock<IFeatureToggleService> _flags;
		private SystemActionsService _service;
		private HashSet<string> _enabledFlags;
		private HashSet<string> _claims;
		private HashSet<string> _disabledModules;

		[SetUp]
		public void SetUp()
		{
			_enabledFlags = new HashSet<string>();
			_claims = new HashSet<string>();
			_disabledModules = new HashSet<string>();
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
				.ReturnsAsync((string key, int dept, bool def, IDictionary<string, string> ctx) => _enabledFlags.Contains(key));
			_service = new SystemActionsService(_flags.Object);
		}

		private SearchPrincipal Principal(bool admin = false) => new SearchPrincipal
		{
			UserId = "u1",
			DepartmentId = 7,
			IsDepartmentAdmin = admin,
			HasClaim = (resource, action) => _claims.Contains(resource + ":" + action),
			IsModuleEnabled = module => !_disabledModules.Contains(module)
		};

		[Test]
		public async Task New_call_needs_the_create_claim()
		{
			_claims.Add("Call:View");
			var without = await _service.SearchAsync("new call", Principal());
			without.Select(h => h.Key).Should().NotContain("new-call").And.Contain("calls");

			_claims.Add("Call:Create");
			var with = await _service.SearchAsync("new call", Principal());
			with.First().Key.Should().Be("new-call");
			with.First().Url.Should().EndWith("/User/Dispatch/NewCall");
		}

		[Test]
		public async Task Slash_commands_prefixes_and_typos_resolve()
		{
			_claims.Add("Call:View");
			_claims.Add("Personnel:View");
			(await _service.SearchAsync("/calls", Principal())).First().Key.Should().Be("calls");
			(await _service.SearchAsync("pers", Principal())).First().Key.Should().Be("personnel");
			(await _service.SearchAsync("personel", Principal())).Select(h => h.Key).Should().Contain("personnel");
			(await _service.SearchAsync("xyzzy", Principal())).Should().BeEmpty();
		}

		[Test]
		public async Task Feature_flag_and_module_gates_apply()
		{
			_claims.Add("Messages:View");
			(await _service.SearchAsync("chat", Principal())).Should().BeEmpty("Chat.System is off");
			_enabledFlags.Add(FeatureFlagKeys.ChatSystem);
			(await _service.SearchAsync("chat", Principal())).Select(h => h.Key).Should().Contain("chat");

			(await _service.SearchAsync("inbox", Principal())).Select(h => h.Key).Should().Contain("inbox");
			_disabledModules.Add(SystemActionModules.Messaging);
			(await _service.SearchAsync("inbox", Principal())).Should().BeEmpty("the messaging module is disabled for the department");
		}

		[Test]
		public async Task Logs_disappear_when_records_is_on_and_admin_entries_need_admin()
		{
			_claims.Add("Log:View");
			(await _service.SearchAsync("logs", Principal())).Select(h => h.Key).Should().Contain("logs");
			_enabledFlags.Add(FeatureFlagKeys.RecordsSystem);
			(await _service.SearchAsync("logs", Principal())).Select(h => h.Key).Should().NotContain("logs");

			(await _service.SearchAsync("department settings", Principal())).Should().BeEmpty();
			(await _service.SearchAsync("department settings", Principal(admin: true))).First().Key.Should().Be("department-settings");
		}

		[Test]
		public async Task Admins_see_every_claim_gated_entry_and_list_returns_them_unscored()
		{
			var list = await _service.ListAsync(Principal(admin: true));
			list.Select(l => l.Key).Should().Contain(new[] { "calls", "new-call", "personnel", "units", "contacts" });
			list.Should().OnlyContain(l => l.Score == 0f);
			list.Should().OnlyContain(l => l.Url.Contains("/User/"));
		}

		[Test]
		public void Within_one_edit_is_symmetric_and_bounded()
		{
			SystemActionsService.WithinOneEdit("personel", "personnel").Should().BeTrue();
			SystemActionsService.WithinOneEdit("personnel", "personel").Should().BeTrue();
			SystemActionsService.WithinOneEdit("unit", "units").Should().BeTrue();
			SystemActionsService.WithinOneEdit("calls", "chats").Should().BeFalse();
		}
	}
}
