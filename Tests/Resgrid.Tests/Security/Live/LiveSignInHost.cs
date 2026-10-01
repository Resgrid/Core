using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Resgrid.Tests.Security.Live
{
	/// <summary>
	/// Serves <see cref="LiveSignInServer"/> for the apps' live sign-in tests (their <c>*.live.test.ts</c>, which skip unless
	/// <c>RESGRID_LIVE_API</c> names this server). Run on purpose only:
	/// <c>RESGRID_LIVE_PORT=5098 RESGRID_LIVE_STOP=/path/stop dotnet test --filter FullyQualifiedName~LiveSignInHost</c>.
	/// It serves until the stop file exists or <c>RESGRID_LIVE_MINUTES</c> (default 30) pass, and writes
	/// <c>{ "baseUrl": … }</c> to <c>RESGRID_LIVE_MANIFEST</c> once it is listening. The control endpoints under
	/// <c>/__live</c> seed members, switch the deployment gates, play the identity provider and the device's passkey, and
	/// play the member's Responder on another phone.
	/// </summary>
	[TestFixture, Explicit("Serves the live sign-in server for the apps' live tests; run it on purpose")]
	public class LiveSignInHost
	{
		[Test]
		public async Task Serve()
		{
			var port = Environment.GetEnvironmentVariable("RESGRID_LIVE_PORT") ?? "5098";
			var minutes = int.TryParse(Environment.GetEnvironmentVariable("RESGRID_LIVE_MINUTES"), out var m) ? m : 30;
			var stopFile = Environment.GetEnvironmentVariable("RESGRID_LIVE_STOP");
			var manifest = Environment.GetEnvironmentVariable("RESGRID_LIVE_MANIFEST");

			await using var server = await LiveSignInServer.StartAsync($"http://127.0.0.1:{port}");
			if (!string.IsNullOrEmpty(manifest))
				await File.WriteAllTextAsync(manifest, new JObject { ["baseUrl"] = server.BaseUrl }.ToString());
			TestContext.Progress.WriteLine("Live sign-in server listening on " + server.BaseUrl);

			var until = DateTime.UtcNow.AddMinutes(minutes);
			while (DateTime.UtcNow < until && (string.IsNullOrEmpty(stopFile) || !File.Exists(stopFile)))
				await Task.Delay(250);
		}
	}
}
