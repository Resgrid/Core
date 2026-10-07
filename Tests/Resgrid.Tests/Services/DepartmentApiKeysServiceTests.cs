using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Attributes;
using Resgrid.Web.Services.Middleware;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentApiKeysServiceTests
	{
		private List<DepartmentApiKey> _rows;
		private Mock<IDepartmentApiKeysRepository> _repository;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<ICacheProvider> _cacheProvider;
		private Mock<IEventAggregator> _eventAggregator;
		private DepartmentApiKeysService _service;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<DepartmentApiKey>();
			_repository = new Mock<IDepartmentApiKeysRepository>();
			_repository.Setup(x => x.InsertAsync(It.IsAny<DepartmentApiKey>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentApiKey k, CancellationToken _, bool __) => { _rows.Add(k); return k; });
			_repository.Setup(x => x.UpdateAsync(It.IsAny<DepartmentApiKey>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentApiKey k, CancellationToken _, bool __) => k);
			_repository.Setup(x => x.GetBySecretHashAsync(It.IsAny<string>()))
				.ReturnsAsync((string hash) => _rows.FirstOrDefault(x => x.SecretHash == hash));
			_repository.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((object id) => _rows.FirstOrDefault(x => x.DepartmentApiKeyId == (string)id));
			_repository.Setup(x => x.GetAllForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int departmentId) => _rows.Where(x => x.DepartmentId == departmentId).ToList());

			_departmentsService = new Mock<IDepartmentsService>();
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, Name = "Dept " + id, ManagingUserId = "owner-" + id, TimeZone = "Eastern Standard Time" });

			_cacheProvider = new Mock<ICacheProvider>();
			_cacheProvider.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentApiKey>>>(), It.IsAny<TimeSpan>()))
				.Returns((string _, Func<Task<DepartmentApiKey>> fallback, TimeSpan __) => fallback());

			_eventAggregator = new Mock<IEventAggregator>();

			_service = new DepartmentApiKeysService(_repository.Object, _departmentsService.Object, _cacheProvider.Object, _eventAggregator.Object);
		}

		private Task<DepartmentApiKeyCreateResult> Create(int departmentId = 1, string ranges = null, params string[] scopes) =>
			_service.CreateKeyAsync(departmentId, "Follow-up system", scopes.Length == 0 ? new[] { DepartmentApiKeyScopes.CallsCreate } : scopes,
				DateTime.UtcNow.AddDays(90), ranges, "admin-1");

		[Test]
		public async Task CreateKey_ReturnsTheKeyOnce_AndStoresOnlyAKeyedHash()
		{
			var result = await Create(1, null, DepartmentApiKeyScopes.CallsCreate, DepartmentApiKeyScopes.CallsRead, "bogus.scope");

			result.Success.Should().BeTrue();
			result.Key.Should().StartWith("rgk_" + result.ApiKey.KeyPrefix + "_");
			result.Key.Length.Should().Be(56);

			var stored = _rows.Single();
			stored.SecretHash.Should().HaveLength(64).And.NotContain(result.Key);
			stored.Scopes.Should().Be("calls.read calls.create");
			stored.ExpiresOn.Should().BeAfter(DateTime.UtcNow.AddDays(89));
			typeof(DepartmentApiKey).GetProperties().Select(p => p.GetValue(stored)?.ToString())
				.Should().NotContain(result.Key, "the key itself is never persisted");

			_eventAggregator.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.DepartmentApiKeyCreated && e.DepartmentId == 1
				&& !e.After.Contains(result.Key) && !e.After.Contains(stored.SecretHash))), Times.Once);
		}

		[Test]
		public async Task CreateKey_RefusesMissingScopesBadExpiryBadRangesAndTooManyKeys()
		{
			(await _service.CreateKeyAsync(1, "x", new[] { "nope" }, DateTime.UtcNow.AddDays(1), null, "a")).Success.Should().BeFalse();
			(await _service.CreateKeyAsync(1, " ", new[] { DepartmentApiKeyScopes.CallsRead }, DateTime.UtcNow.AddDays(1), null, "a")).Success.Should().BeFalse();
			(await _service.CreateKeyAsync(1, "x", new[] { DepartmentApiKeyScopes.CallsRead }, DateTime.UtcNow.AddMinutes(-1), null, "a")).Success.Should().BeFalse();
			(await _service.CreateKeyAsync(1, "x", new[] { DepartmentApiKeyScopes.CallsRead }, DateTime.UtcNow.AddDays(Resgrid.Config.SecurityConfig.DepartmentApiKeyMaxLifetimeDays + 2), null, "a")).Success.Should().BeFalse();
			(await _service.CreateKeyAsync(1, "x", new[] { DepartmentApiKeyScopes.CallsRead }, DateTime.UtcNow.AddDays(1), "10.0.0.0/8\nnot-an-ip", "a")).Error.Should().Contain("not-an-ip");

			for (var i = 0; i < Resgrid.Config.SecurityConfig.DepartmentApiKeyMaxActivePerDepartment; i++)
				(await Create(2)).Success.Should().BeTrue();

			(await Create(2)).Success.Should().BeFalse();
			(await Create(3)).Success.Should().BeTrue("the limit is per department");
		}

		[Test]
		public async Task Authenticate_AcceptsTheIssuedKey_AndRefusesAnyOther()
		{
			var created = await Create(7, null, DepartmentApiKeyScopes.CallsCreate, DepartmentApiKeyScopes.ReferenceRead);

			var ok = await _service.AuthenticateAsync(created.Key, "198.51.100.4");
			ok.Status.Should().Be(DepartmentApiKeyAuthenticationStatus.Success);
			ok.Department.DepartmentId.Should().Be(7);
			ok.Scopes.Should().Equal(DepartmentApiKeyScopes.CallsCreate, DepartmentApiKeyScopes.ReferenceRead);
			_repository.Verify(x => x.MarkUsedAsync(created.ApiKey.DepartmentApiKeyId, It.IsAny<DateTime>(), "198.51.100.4", It.IsAny<CancellationToken>()), Times.Once);

			var tampered = created.Key.Substring(0, created.Key.Length - 1) + (created.Key.EndsWith("A") ? "B" : "A");
			(await _service.AuthenticateAsync(tampered, "198.51.100.4")).Status.Should().Be(DepartmentApiKeyAuthenticationStatus.Invalid);
			(await _service.AuthenticateAsync("not a key", "198.51.100.4")).Status.Should().Be(DepartmentApiKeyAuthenticationStatus.Invalid);
			(await _service.AuthenticateAsync(null, "198.51.100.4")).Status.Should().Be(DepartmentApiKeyAuthenticationStatus.Invalid);
		}

		[Test]
		public async Task Authenticate_RefusesExpiredAndRevokedKeys()
		{
			var expired = await Create();
			_rows.Single(x => x.DepartmentApiKeyId == expired.ApiKey.DepartmentApiKeyId).ExpiresOn = DateTime.UtcNow.AddSeconds(-1);
			(await _service.AuthenticateAsync(expired.Key, null)).Status.Should().Be(DepartmentApiKeyAuthenticationStatus.Expired);

			var revoked = await Create();
			(await _service.RevokeKeyAsync(1, revoked.ApiKey.DepartmentApiKeyId, "admin-2")).Should().BeTrue();
			(await _service.AuthenticateAsync(revoked.Key, null)).Status.Should().Be(DepartmentApiKeyAuthenticationStatus.Revoked);
			(await _service.RevokeKeyAsync(1, revoked.ApiKey.DepartmentApiKeyId, "admin-2")).Should().BeFalse("already revoked");
			_cacheProvider.Verify(x => x.RemoveAsync(It.Is<string>(k => k.Contains(revoked.ApiKey.SecretHash))), Times.Once);
			_eventAggregator.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.DepartmentApiKeyRevoked && e.UserId == "admin-2")), Times.Once);
		}

		[Test]
		public async Task Revoke_CannotReachAnotherDepartmentsKey()
		{
			var other = await Create(5);

			(await _service.RevokeKeyAsync(1, other.ApiKey.DepartmentApiKeyId, "admin-1")).Should().BeFalse();
			(await _service.AuthenticateAsync(other.Key, null)).Success.Should().BeTrue();
		}

		[TestCase("203.0.113.10", "203.0.113.10", true)]
		[TestCase("203.0.113.0/24", "203.0.113.200", true)]
		[TestCase("203.0.113.0/24", "::ffff:203.0.113.200", true)]
		[TestCase("203.0.113.0/24", "198.51.100.1", false)]
		[TestCase("203.0.113.0/24", null, false)]
		[TestCase("2001:db8::/32", "2001:db8::1", true)]
		public async Task Authenticate_HonorsTheAllowedAddresses(string ranges, string remote, bool allowed)
		{
			var created = await Create(1, ranges);

			var result = await _service.AuthenticateAsync(created.Key, remote);

			result.Status.Should().Be(allowed ? DepartmentApiKeyAuthenticationStatus.Success : DepartmentApiKeyAuthenticationStatus.AddressNotAllowed);
		}

		[Test]
		public void Identity_IsDepartmentScoped_CarriesOnlyItsScopes_AndIsNeverAServiceAccount()
		{
			var identity = DepartmentApiKeyAuthHandler.BuildIdentity(new DepartmentApiKeyAuthenticationResult
			{
				Status = DepartmentApiKeyAuthenticationStatus.Success,
				ApiKey = new DepartmentApiKey { DepartmentApiKeyId = "key-1", Name = "Bridge" },
				Department = new Department { DepartmentId = 9, Name = "Nine", ManagingUserId = "owner-9", TimeZone = "UTC" },
				Scopes = new List<string> { DepartmentApiKeyScopes.CallsCreate }
			});

			identity.AuthenticationType.Should().Be(DepartmentApiKeyAuthHandler.AuthenticationType);
			identity.FindFirst(ClaimTypes.PrimarySid).Value.Should().Be("owner-9");
			identity.FindFirst(ClaimTypes.PrimaryGroupSid).Value.Should().Be("9");
			identity.HasClaim(DepartmentApiKeyScopes.ScopeClaimType, DepartmentApiKeyScopes.CallsCreate).Should().BeTrue();
			identity.HasClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			identity.HasClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View).Should().BeFalse();
			identity.HasClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete).Should().BeFalse();
			identity.HasClaim(c => c.Type == ResgridClaimTypes.Data.ServiceAccount).Should().BeFalse("a service account would unlock cross-department lookups");
			identity.HasClaim(c => c.Type == ResgridClaimTypes.Resources.Department).Should().BeFalse("never a department admin");
		}

		/// <summary>
		/// Pins the endpoints a department API key can reach. Adding one is a deliberate decision: change this list with it.
		/// </summary>
		[Test]
		public void OnlyTheAllowlistedEndpointsAcceptAKey_EachWithItsOwnPolicy()
		{
			var expected = new Dictionary<string, string>
			{
				["Calls.GetActiveCalls"] = DepartmentApiKeyScopes.CallsRead,
				["Calls.GetCall"] = DepartmentApiKeyScopes.CallsRead,
				["Calls.GetCalls"] = DepartmentApiKeyScopes.CallsRead,
				["Calls.GetPendingCalls"] = DepartmentApiKeyScopes.CallsRead,
				["Calls.GetAllPendingScheduledCalls"] = DepartmentApiKeyScopes.CallsRead,
				["Calls.GetNewCallFieldPolicy"] = DepartmentApiKeyScopes.CallsRead,
				["Calls.SaveCall"] = DepartmentApiKeyScopes.CallsCreate,
				["Calls.EditCall"] = DepartmentApiKeyScopes.CallsUpdate,
				["Calls.UpdateScheduledDispatchTime"] = DepartmentApiKeyScopes.CallsUpdate,
				["Calls.DispatchCallNow"] = DepartmentApiKeyScopes.CallsUpdate,
				["Calls.CloseCall"] = DepartmentApiKeyScopes.CallsClose,
				["CallTypes.GetAllCallTypes"] = DepartmentApiKeyScopes.ReferenceRead,
				["CallPriorities.GetAllCallPriorites"] = DepartmentApiKeyScopes.ReferenceRead,
				["Groups.GetAllGroups"] = DepartmentApiKeyScopes.ReferenceRead,
				["Dispatch.GetRolesForCallGrid"] = DepartmentApiKeyScopes.ReferenceRead,
				["Dispatch.GetCallTemplates"] = DepartmentApiKeyScopes.ReferenceRead,
				["Units.GetAllUnits"] = DepartmentApiKeyScopes.UnitsRead,
				["UnitStatus.GetAllUnitStatuses"] = DepartmentApiKeyScopes.UnitsRead,
			};

			var actual = new Dictionary<string, string>();
			var controllers = typeof(DepartmentApiKeyAuthHandler).Assembly.GetTypes()
				.Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

			foreach (var controller in controllers)
			{
				foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
				{
					var scope = method.GetCustomAttribute<DepartmentApiKeyScopeAttribute>();
					if (scope == null)
						continue;

					actual[controller.Name.Replace("Controller", "") + "." + method.Name] = scope.Scope;

					method.GetCustomAttributes<AuthorizeAttribute>().Any(a => a is not DepartmentApiKeyScopeAttribute && !string.IsNullOrEmpty(a.Policy))
						.Should().BeTrue($"{controller.Name}.{method.Name} must keep its own resource policy");
				}
			}

			actual.Should().BeEquivalentTo(expected);
		}
	}
}
