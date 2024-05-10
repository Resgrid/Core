using System;

namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// The standard response base object for the v4 api. A Data property will be adding on top of this.
	/// </summary>
	public class BillingApiResponseBase
	{
		/// <summary>
		/// Number of records returned
		/// </summary>
		public int PageSize { get; set; }

		/// <summary>
		/// The requested page size
		/// </summary>
		public int Page { get; set; }

		/// <summary>
		/// Timestamp in UTC of the operation
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// API Version that produced the response
		/// </summary>
		public string Version { get; set; }

		/// <summary>
		/// Name of the node the processed the operation
		/// </summary>
		public string Node { get; set; }

		/// <summary>
		/// Name of the environment that the api is running under
		/// </summary>
		public string Environment { get; set; }

		/// <summary>
		/// Trace or Request Id for the operation
		/// </summary>
		public string RequestId { get; set; }

		/// <summary>
		/// Status of the Response
		/// </summary>
		public string Status { get; set; }

		/// <summary>
		/// If pagination values were supplied what is the previous page url (if empty this is the first page)
		/// </summary>
		public string PreviousPageUrl { get; set; }

		/// <summary>
		/// If pagination values were supplied what is the next page url (if empty there is no more records)
		/// </summary>
		public string NextPageUrl { get; set; }
	}
}
