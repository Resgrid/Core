using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;
using Resgrid.Model.Security;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The web helpers around server-side evidence: which session evidence belongs to, the password-time hand-off across the
	/// two-step sign-in, and the expiring staged authenticator key.
	/// </summary>
	[TestFixture]
	public class MfaEvidenceSessionTests
	{
		private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

		private sealed class MemorySession : ISession
		{
			private readonly Dictionary<string, byte[]> _values = new();
			public bool IsAvailable => true;
			public string Id { get; } = Guid.NewGuid().ToString();
			public IEnumerable<string> Keys => _values.Keys;
			public void Clear() => _values.Clear();
			public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
			public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
			public void Remove(string key) => _values.Remove(key);
			public void Set(string key, byte[] value) => _values[key] = value;
			public bool TryGetValue(string key, [NotNullWhen(true)] out byte[] value) => _values.TryGetValue(key, out value);
		}

		private sealed class SessionFeature : ISessionFeature
		{
			public ISession Session { get; set; }
		}

		private static DefaultHttpContext WithSession()
		{
			var context = new DefaultHttpContext();
			context.Features.Set<ISessionFeature>(new SessionFeature { Session = new MemorySession() });
			return context;
		}

		private static ClaimsPrincipal Principal(string sessionId = null)
		{
			var identity = new ClaimsIdentity("test");
			if (sessionId != null)
				identity.AddClaim(new Claim(SessionClaimTypes.SessionId, sessionId));
			return new ClaimsPrincipal(identity);
		}

		[Test]
		public void A_tracked_session_is_keyed_by_its_session_id()
		{
			var context = WithSession();

			MfaEvidenceSession.KeyForNewSignIn("abc", context).Should().Be("sid:abc");
			MfaEvidenceSession.KeyFor(Principal("abc"), context).Should().Be("sid:abc");
		}

		[Test]
		public void An_untracked_session_keeps_one_key_until_the_next_sign_in()
		{
			var context = WithSession();

			var signIn = MfaEvidenceSession.KeyForNewSignIn(null, context);
			signIn.Should().StartWith("web:");
			MfaEvidenceSession.KeyFor(Principal(), context).Should().Be(signIn);
			MfaEvidenceSession.KeyFor(Principal(), context).Should().Be(signIn);

			// Sign-out does not clear the ASP.NET session, so the next sign-in must not inherit this key's evidence.
			MfaEvidenceSession.KeyForNewSignIn(null, context).Should().NotBe(signIn);
		}

		[Test]
		public void Two_browsers_never_share_an_untracked_key()
		{
			MfaEvidenceSession.KeyForNewSignIn(null, WithSession()).Should().NotBe(MfaEvidenceSession.KeyForNewSignIn(null, WithSession()));
		}

		[Test]
		public void Without_a_session_there_is_no_key()
		{
			MfaEvidenceSession.KeyFor(Principal(), new DefaultHttpContext()).Should().BeNull();
			MfaEvidenceSession.KeyForNewSignIn(null, null).Should().BeNull();
		}

		[Test]
		public void The_password_time_hands_off_once_and_only_to_the_same_user()
		{
			var context = WithSession();
			MfaEvidenceSession.StashFirstFactor(context, "user-1", Now);

			MfaEvidenceSession.TakeFirstFactor(context, "USER-1").Should().Be(Now);
			MfaEvidenceSession.TakeFirstFactor(context, "user-1").Should().BeNull("the hand-off is single use");

			MfaEvidenceSession.StashFirstFactor(context, "user-1", Now);
			MfaEvidenceSession.TakeFirstFactor(context, "user-2").Should().BeNull();
			MfaEvidenceSession.TakeFirstFactor(context, "user-1").Should().BeNull("a mismatched read still clears the hand-off");
		}

		[Test]
		public void The_password_time_hand_off_without_a_session_is_harmless()
		{
			var context = new DefaultHttpContext();

			MfaEvidenceSession.StashFirstFactor(context, "user-1", Now);
			MfaEvidenceSession.TakeFirstFactor(context, "user-1").Should().BeNull();
		}

		[Test]
		public void A_staged_key_is_usable_only_within_its_lifetime()
		{
			var stored = StagedAuthenticatorKey.Serialize("JBSWY3DPEHPK3PXP", Now);
			var lifetime = TimeSpan.FromMinutes(10);

			StagedAuthenticatorKey.ReadUsableKey(stored, Now, lifetime).Should().Be("JBSWY3DPEHPK3PXP");
			StagedAuthenticatorKey.ReadUsableKey(stored, Now.AddMinutes(10), lifetime).Should().Be("JBSWY3DPEHPK3PXP");
			StagedAuthenticatorKey.ReadUsableKey(stored, Now.AddMinutes(10).AddSeconds(1), lifetime).Should().BeNull();
			StagedAuthenticatorKey.ReadUsableKey(stored, Now.AddSeconds(-31), lifetime).Should().BeNull("a key staged in the future is not trusted");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("JBSWY3DPEHPK3PXP")]
		[TestCase("{not json")]
		[TestCase("{\"Key\":\"\",\"StagedOnUtc\":\"2026-09-28T12:00:00Z\"}")]
		public void A_legacy_bare_or_malformed_staged_key_is_never_used(string stored)
		{
			// A bare key is what the code before this slice stored, with no time: it cannot prove it is recent.
			StagedAuthenticatorKey.ReadUsableKey(stored, Now, TimeSpan.FromMinutes(10)).Should().BeNull();
		}
	}
}
