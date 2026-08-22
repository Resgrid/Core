using CommonServiceLocator;
using IdentityModel.Client;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Dynamic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Resgrid.WebCore.Helpers
{
	// Kept as a source-compatibility marker. Browser password-grant token bridging was
	// removed; Web API calls now use the same-origin server-side facade.
	internal static class ApiAuthHelper
	{
	}
}
