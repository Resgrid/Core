using System;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Small, dependency-free parser for actionable session labels. Explicit app metadata wins;
	/// browser User-Agent parsing is a bounded fallback and is never used for authorization.
	/// </summary>
	public class ClientSessionMetadataParser : IClientSessionMetadataParser
	{
		public ClientSessionMetadata Parse(string userAgent, string deviceName = null, string deviceType = null,
			string operatingSystem = null, string browser = null, string applicationVersion = null)
		{
			var agent = userAgent ?? string.Empty;
			var parsedOs = First(operatingSystem, ParseOperatingSystem(agent));
			var parsedType = First(deviceType, ParseDeviceType(agent));
			return new ClientSessionMetadata
			{
				DeviceName = First(deviceName, DefaultDeviceName(parsedType, parsedOs)),
				DeviceType = parsedType,
				OperatingSystem = parsedOs,
				Browser = First(browser, ParseBrowser(agent)),
				ApplicationVersion = applicationVersion
			};
		}

		private static string ParseBrowser(string agent)
		{
			if (agent.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "Edg/", "Edge");
			if (agent.Contains("OPR/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "OPR/", "Opera");
			if (agent.Contains("CriOS/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "CriOS/", "Chrome");
			if (agent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "Chrome/", "Chrome");
			if (agent.Contains("FxiOS/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "FxiOS/", "Firefox");
			if (agent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "Firefox/", "Firefox");
			if (agent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) &&
				agent.Contains("Version/", StringComparison.OrdinalIgnoreCase)) return Token(agent, "Version/", "Safari");
			return string.IsNullOrWhiteSpace(agent) ? null : "Other client";
		}

		private static string ParseOperatingSystem(string agent)
		{
			if (agent.Contains("Windows NT 10.0", StringComparison.OrdinalIgnoreCase)) return "Windows 10/11";
			if (agent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
			if (agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "iOS";
			if (agent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iPadOS";
			if (agent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
			if (agent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)) return "macOS";
			if (agent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
			return null;
		}

		private static string ParseDeviceType(string agent)
		{
			if (agent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
				(agent.Contains("Android", StringComparison.OrdinalIgnoreCase) &&
				 !agent.Contains("Mobile", StringComparison.OrdinalIgnoreCase))) return "Tablet";
			if (agent.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
				agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "Phone";
			return string.IsNullOrWhiteSpace(agent) ? null : "Computer";
		}

		private static string DefaultDeviceName(string type, string operatingSystem)
		{
			if (string.IsNullOrWhiteSpace(type)) return null;
			return string.IsNullOrWhiteSpace(operatingSystem) ? type : $"{operatingSystem} {type}";
		}

		private static string Token(string agent, string marker, string name)
		{
			var start = agent.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (start < 0) return name;
			start += marker.Length;
			var end = agent.IndexOfAny(new[] {' ', ';', ')'}, start);
			var version = agent.Substring(start, (end < 0 ? agent.Length : end) - start);
			return string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}";
		}

		private static string First(string supplied, string fallback) =>
			string.IsNullOrWhiteSpace(supplied) ? fallback : supplied;
	}
}
