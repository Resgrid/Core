using System;
using Sentry;

namespace Resgrid.Framework
{
	/// <summary>
	/// Removes expected internet-scanner noise from Sentry while retaining application 404s.
	/// </summary>
	public static class SentryTransactionFilter
	{
		private static readonly string[] ScannerPathPrefixes =
		{
			"/.git",
			"/.hg",
			"/.svn",
			"/actuator",
			"/boaform",
			"/cgi-bin",
			"/phpmyadmin",
			"/pma",
			"/server-status",
			"/vendor/phpunit",
			"/wordpress",
			"/wp-admin",
			"/wp-content",
			"/wp-includes",
			"/wp-json"
		};

		private static readonly string[] ScannerFileNames =
		{
			".env",
			"appsettings.json",
			"composer.json",
			"composer.lock",
			"web.config"
		};

		private static readonly string[] ScannerFileExtensions =
		{
			".asp",
			".aspx",
			".cfm",
			".cgi",
			".jsp",
			".jspx",
			".phar",
			".php",
			".php3",
			".php4",
			".php5",
			".php7",
			".php8",
			".phtml",
			".pl"
		};

		/// <summary>
		/// Returns <see langword="null"/> only for a confirmed 404 whose request path matches a
		/// technology or sensitive-file probe that cannot be served by Resgrid.
		/// </summary>
		public static SentryTransaction Filter(SentryTransaction transaction)
		{
			if (transaction == null)
				return null;

			if (transaction.Request != null)
				transaction.Request.Url = RedactCapabilityPath(transaction.Request.Url);

			((IEventLike)transaction).TransactionName = RedactCapabilityPath(transaction.Name);

			if (transaction.Status != SpanStatus.NotFound)
				return transaction;

			var requestTarget = transaction.Request?.Url;
			if (string.IsNullOrWhiteSpace(requestTarget))
				requestTarget = GetRequestTargetFromTransactionName(transaction.Name);

			return ShouldDrop(transaction.Status, requestTarget) ? null : transaction;
		}

		public static string RedactCapabilityPath(string value)
		{
			const string prefix = "/api/v4/unit-trackers/c/";
			if (string.IsNullOrWhiteSpace(value))
				return value;

			var start = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
			if (start < 0)
				return value;

			var tokenStart = start + prefix.Length;
			var tokenEnd = value.IndexOfAny(new[] { '?', '#', '/' }, tokenStart);
			if (tokenEnd < 0)
				tokenEnd = value.Length;

			return value.Substring(0, tokenStart) +
			       "[REDACTED]" +
			       value.Substring(tokenEnd);
		}

		public static bool ShouldDrop(SpanStatus? status, string requestTarget)
		{
			return status == SpanStatus.NotFound && IsKnownScannerPath(requestTarget);
		}

		public static bool IsKnownScannerPath(string requestTarget)
		{
			var path = GetPath(requestTarget);
			if (string.IsNullOrWhiteSpace(path))
				return false;

			foreach (var prefix in ScannerPathPrefixes)
			{
				if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
					path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			foreach (var segment in segments)
			{
				foreach (var fileName in ScannerFileNames)
				{
					if (segment.Equals(fileName, StringComparison.OrdinalIgnoreCase))
						return true;
				}

				foreach (var extension in ScannerFileExtensions)
				{
					if (segment.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
						return true;
				}
			}

			return false;
		}

		private static string GetRequestTargetFromTransactionName(string transactionName)
		{
			if (string.IsNullOrWhiteSpace(transactionName))
				return null;

			var separatorIndex = transactionName.IndexOf(' ');
			return separatorIndex >= 0 && separatorIndex < transactionName.Length - 1
				? transactionName.Substring(separatorIndex + 1)
				: transactionName;
		}

		private static string GetPath(string requestTarget)
		{
			if (string.IsNullOrWhiteSpace(requestTarget))
				return null;

			var value = requestTarget.Trim();
			if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
				(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
				 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
			{
				value = uri.AbsolutePath;
			}
			else
			{
				var suffixIndex = value.IndexOfAny(new[] { '?', '#' });
				if (suffixIndex >= 0)
					value = value.Substring(0, suffixIndex);
			}

			try
			{
				value = Uri.UnescapeDataString(value);
			}
			catch (UriFormatException)
			{
				// Keep the original path when a scanner sends malformed escaping.
			}

			value = value.Replace('\\', '/');
			return value.StartsWith('/') ? value : "/" + value;
		}
	}
}
