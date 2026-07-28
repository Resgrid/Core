using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.TrackerGateway.Hosting
{
	public sealed class TrackingGatewayConfigurationException : InvalidOperationException
	{
		public TrackingGatewayConfigurationException(IEnumerable<string> errors)
			: base(BuildMessage(errors))
		{
			Errors = (errors ?? Array.Empty<string>()).ToList().AsReadOnly();
		}

		public IReadOnlyCollection<string> Errors { get; }

		private static string BuildMessage(IEnumerable<string> errors)
		{
			var errorList = (errors ?? Array.Empty<string>()).ToList();
			if (errorList.Count == 0)
				return "Tracker gateway configuration is invalid.";

			return $"Tracker gateway configuration is invalid: {string.Join(" ", errorList)}";
		}
	}
}
