using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// Gets the messages for a user
/// </summary>
public class GetRecipientsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<RecipientsResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetRecipientsResult()
	{
		Data = new List<RecipientsResultData>();
	}
}

/// <summary>
/// Holds Resgrid message data
/// </summary>
public class RecipientsResultData
{
	/// <summary>
	/// Identifier of the recipient
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// Type of the recipient
	/// </summary>
	public string Type { get; set; }

	/// <summary>
	/// The name of the recipient
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Is the recipient selected or not
	/// </summary>
	public bool Selected { get; set; }
}
