using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Attributes;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	public class ProfileControllerPasswordResetSecurityTests
	{
		[Test]
		public void privileged_reset_actions_require_mfa_verified_within_five_minutes()
		{
			var actions = typeof(ProfileController)
				.GetMethods(BindingFlags.Instance | BindingFlags.Public)
				.Where(method => method.Name == nameof(ProfileController.ResetPasswordForUser))
				.Where(method => method.GetCustomAttribute<HttpGetAttribute>() != null ||
				                 method.GetCustomAttribute<HttpPostAttribute>() != null)
				.ToArray();

			actions.Should().HaveCount(2);
			foreach (var action in actions)
			{
				var attribute = action.GetCustomAttribute<RequiresRecentTwoFactorAttribute>();
				attribute.Should().NotBeNull();
				attribute.RequireForOperation.Should().BeTrue();
				attribute.VerificationWindowMinutes.Should().Be(5);
			}
		}

		[Test]
		public void privileged_reset_audit_payload_records_mfa_verification_timestamp()
		{
			var verifiedAtUtc = new DateTime(2026, 8, 20, 12, 34, 56, DateTimeKind.Utc);
			var builder = typeof(ProfileController).GetMethod("BuildPasswordResetAuditData",
				BindingFlags.Static | BindingFlags.NonPublic);

			builder.Should().NotBeNull();
			var json = builder.Invoke(null, new object[]
			{
				"target-user", "direct", verifiedAtUtc, true, true, true
			}) as string;
			json.Should().Contain($"\"mfa_verified_at\":\"{verifiedAtUtc:O}\"");
			var payload = JObject.Parse(json);

			payload.Value<string>("target_user_id").Should().Be("target-user");
			payload.Value<string>("reset_mode").Should().Be("direct");
			payload.Value<bool>("sessions_revoked").Should().BeTrue();
		}

		[Test]
		public void password_reset_by_administrator_has_a_human_readable_audit_type()
		{
			var service = new AuditService(null, null);

			service.GetAuditLogTypeString(AuditLogTypes.PasswordResetByAdministrator)
				.Should().Be("Password Reset by Administrator");
		}
	}
}
