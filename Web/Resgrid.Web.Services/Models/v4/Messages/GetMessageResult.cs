using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// Gets a single message
/// </summary>
public class GetMessageResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public MessageResultData Data { get; set; }
}
