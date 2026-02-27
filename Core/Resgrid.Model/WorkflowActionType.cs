namespace Resgrid.Model
{
	public enum WorkflowActionType
	{
		SendEmail = 0,
		SendSms = 1,
		CallApiGet = 2,
		CallApiPost = 3,
		CallApiPut = 4,
		CallApiDelete = 5,
		UploadFileFtp = 6,
		UploadFileSftp = 7,
		UploadFileS3 = 8,
		SendTeamsMessage = 9,
		SendSlackMessage = 10,
		SendDiscordMessage = 11,
		UploadFileAzureBlob = 12,
		UploadFileBox = 13,
		UploadFileDropbox = 14
	}
}

