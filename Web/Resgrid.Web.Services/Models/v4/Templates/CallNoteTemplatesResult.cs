using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Templates
{
	/// <summary>
	/// Multiple call note template result
	/// </summary>
	public class CallNoteTemplatesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallNoteTemplateResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallNoteTemplatesResult()
		{
			Data = new List<CallNoteTemplateResultData>();
		}
	}

	/// <summary>
	/// A call note template
	/// </summary>
	public class CallNoteTemplateResultData
	{
		/// <summary>
		/// Id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Type of the note
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// Sort order
		/// </summary>
		public int Sort { get; set; }

		/// <summary>
		/// Name of the template
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Data for the note template
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// When this note template was added on (UTC)
		/// </summary>
		public string AddedOn { get; set; }

		/// <summary>
		/// Who added this note template
		/// </summary>
		public string AddedByUserId { get; set; }
	}
}
