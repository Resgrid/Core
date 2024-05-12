using System;
using System.Collections.Generic;
using System.Text;

namespace Resgrid.Providers.Voip.LiveKit.Model
{
	internal class LiveKitAccessTokenOptions
	{
		/**
		   * amount of time before expiration
		   * expressed in seconds or a string describing a time span zeit/ms.
		   * eg: '2 days', '10h', or seconds as numeric value
		   */
		public string ttl { get; set; }

		/**
		 * display name for the participant, available as `Participant.name`
		 */
		public string name { get; set; }

		/**
		 * identity of the user, required for room join tokens
		 */
		public string identity { get; set; }

		/**
		 * custom metadata to be passed to participants
		 */
		public string metadata { get; set; }
	}
}
