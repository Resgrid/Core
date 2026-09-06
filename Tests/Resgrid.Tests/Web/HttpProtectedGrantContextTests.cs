using System.Collections.Generic;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The request-bound grant context behind the Records edit pages (RMS plan section 5.9.3): a grant arrives as the
	/// X-Resgrid-Protected-Grant header on AJAX calls and as the __ResgridProtectedGrant field on full-page form posts.
	/// </summary>
	[TestFixture]
	public class HttpProtectedGrantContextTests
	{
		private static HttpProtectedGrantContext Build(DefaultHttpContext http) => new HttpProtectedGrantContext(new HttpContextAccessor { HttpContext = http });

		private static DefaultHttpContext Authenticated(string userId = "user-1")
		{
			var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, userId) }, "test");
			return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
		}

		private static void PostForm(DefaultHttpContext http, string field, string value)
		{
			http.Request.Method = "POST";
			http.Request.ContentType = "application/x-www-form-urlencoded";
			http.Request.Form = new FormCollection(new Dictionary<string, StringValues> { [field] = value });
		}

		[Test]
		public void header_carries_the_grant()
		{
			var http = Authenticated();
			http.Request.Headers[HttpProtectedGrantContext.HeaderName] = " grant-from-header ";

			var context = Build(http);

			context.GrantToken.Should().Be("grant-from-header");
			context.IsWorkloadCaller.Should().BeFalse();
			context.UserId.Should().Be("user-1");
		}

		[Test]
		public void form_field_carries_the_grant_on_a_form_post()
		{
			var http = Authenticated();
			PostForm(http, HttpProtectedGrantContext.FormFieldName, "grant-from-form");

			Build(http).GrantToken.Should().Be("grant-from-form");
		}

		[Test]
		public void header_wins_over_the_form_field()
		{
			var http = Authenticated();
			http.Request.Headers[HttpProtectedGrantContext.HeaderName] = "grant-from-header";
			PostForm(http, HttpProtectedGrantContext.FormFieldName, "grant-from-form");

			Build(http).GrantToken.Should().Be("grant-from-header");
		}

		[Test]
		public void form_field_is_ignored_without_a_form_content_type()
		{
			var http = Authenticated();
			http.Request.Method = "POST";
			http.Request.ContentType = "application/json";

			Build(http).GrantToken.Should().BeNull();
		}

		[Test]
		public void blank_values_read_as_no_grant()
		{
			var http = Authenticated();
			PostForm(http, HttpProtectedGrantContext.FormFieldName, "   ");

			Build(http).GrantToken.Should().BeNull();
		}

		[Test]
		public void an_unauthenticated_request_is_a_workload_caller()
		{
			var context = Build(new DefaultHttpContext());

			context.IsWorkloadCaller.Should().BeTrue();
			context.UserId.Should().BeNull();
			context.GrantToken.Should().BeNull();
		}

		[Test]
		public void user_id_falls_back_to_name_identifier()
		{
			var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "api-user") }, "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

			Build(http).UserId.Should().Be("api-user");
		}

		[Test]
		public void expiry_field_is_read_only_from_a_form_post_and_parses_as_utc()
		{
			var http = Authenticated();
			PostForm(http, HttpProtectedGrantContext.ExpiresOnFormFieldName, "2026-09-05T18:30:00.0000000Z");

			var expiry = HttpProtectedGrantContext.ReadExpiry(http.Request);
			expiry.Should().Be(new System.DateTime(2026, 9, 5, 18, 30, 0, System.DateTimeKind.Utc));
			expiry.Value.Kind.Should().Be(System.DateTimeKind.Utc);

			var json = Authenticated();
			json.Request.Method = "POST";
			json.Request.ContentType = "application/json";
			HttpProtectedGrantContext.ReadExpiry(json.Request).Should().BeNull();
			HttpProtectedGrantContext.ReadExpiry(null).Should().BeNull();
		}

		[Test]
		public void expiry_values_that_do_not_parse_are_ignored()
		{
			HttpProtectedGrantContext.ParseExpiry(null).Should().BeNull();
			HttpProtectedGrantContext.ParseExpiry("   ").Should().BeNull();
			HttpProtectedGrantContext.ParseExpiry("not-a-date").Should().BeNull();
			HttpProtectedGrantContext.ParseExpiry("2026-09-05T18:30:00+02:00").Should().Be(new System.DateTime(2026, 9, 5, 16, 30, 0, System.DateTimeKind.Utc));
		}

		[Test]
		public void edit_views_mirror_the_read_result()
		{
			var view = new RecordEditView();

			view.ApplyProtection(new ProtectedReadResult { IsProtected = true, RedactedFields = new List<string> { "rmsoperationalrecorddetails.narrative" }, ProtectedReason = "step_up_required" });
			view.ProtectionEnforced.Should().BeTrue();
			view.ProtectionRedacted.Should().BeTrue();
			view.ProtectionReason.Should().Be("step_up_required");

			view.ApplyProtection(new ProtectedReadResult { IsProtected = true });
			view.ProtectionRedacted.Should().BeFalse();
			view.ProtectionReason.Should().BeNull();

			view.ApplyProtection(null);
			view.ProtectionEnforced.Should().BeFalse();
			view.ProtectedGrant.Should().BeNull("the grant is never derived from a read result");
		}
	}
}
