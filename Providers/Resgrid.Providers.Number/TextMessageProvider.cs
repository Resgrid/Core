using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using CsvHelper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.NumberProvider.Models;
using RestSharp;
using RestSharp.Authenticators;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Resgrid.Providers.NumberProvider
{
	public class TextMessageProvider : ITextMessageProvider
	{
		private static int _minZone = 1;
		private static int _maxZone = 4;
		private static IEnumerable<AreaCodeCity> _areaCodes;

		public async Task<bool> SendTextMessage(string number, string message, string departmentNumber, MobileCarriers carrier, int departmentId, bool forceGateway = false, bool isCall = false)
		{
			var wasTwillioSuccessful = await SendTextMessageViaTwillio(number, message, departmentNumber);

			if (!wasTwillioSuccessful && isCall)
			{
				return await SendTextMessageViaSignalWire(number, message, departmentNumber);
			}

			return wasTwillioSuccessful;

			/* I'm leaving the abomination below as a comment just in case I need to quickly reference the code block for any missing
			 * edge cases at 1AM Dealing with a production issue. The below is the result of trying to give as much functionality
			 * to the free and low paying tiers as possible by lowering Resgrid's costs. But the Greedflation of the 2020's has
			 * killed my ability to stem the tide anymore. Signalwire will still be used but now only as a backup for calls only.
			 * Free tiers don't do SMS anymore at all and Standard and Permium tiers no longer can use Message based SMS. -SJ 6-20-2023.
			 */

			/*
			if (carrier == MobileCarriers.Telstra)
			{
				return await SendTextMessageViaNexmo(number, message, departmentNumber);
			}
			else if (Carriers.OnPremSmsGatewayCarriers.Contains(carrier) || forceGateway)
			{
				if (!Config.SystemBehaviorConfig.DepartmentsToForceBackupSmsProvider.Contains(departmentId))
				{
					if (!await SendTextMessageViaDiafaan(number, message))
					{
						return await SendTextMessageViaSignalWire(number, message, departmentNumber);
					}
					else
					{
						return true;
					}
				}
				else
				{
					return await SendTextMessageViaSignalWire(number, message, departmentNumber);
				}
			}
			else if (Config.SystemBehaviorConfig.SmsProviderType == Config.SmsProviderTypes.SignalWire)
			{
				if (!Config.SystemBehaviorConfig.DepartmentsToForceBackupSmsProvider.Contains(departmentId))
				{
					if (!await SendTextMessageViaSignalWire(number, message, departmentNumber))
					{
						return await SendTextMessageViaTwillio(number, message, departmentNumber);
					}
					else
					{
						return true;
					}
				}
				else
				{
					if (isCall)
					{
						await SendTextMessageViaTwillio(number, message, departmentNumber);

						if (Config.SystemBehaviorConfig.AlsoSendToPrimarySmsProvider)
							return await SendTextMessageViaSignalWire(number, message, departmentNumber);
						else
							return true;
					}
					else
					{
						return await SendTextMessageViaSignalWire(number, message, departmentNumber);
						//SendTextMessageViaTwillio(number, message, departmentNumber);
					}
				}
			}
			else
			{
				if (!Config.SystemBehaviorConfig.DepartmentsToForceBackupSmsProvider.Contains(departmentId))
				{
					if (!await SendTextMessageViaTwillio(number, message, departmentNumber))
					{
						return await SendTextMessageViaSignalWire(number, message, departmentNumber);
					}
					else
					{
						return true;
					}
				}
				else
				{
					return await SendTextMessageViaSignalWire(number, message, departmentNumber);
				}
			}
			*/
		}

		private async Task<bool> SendTextMessageViaNexmo(string number, string message, string departmentNumber)
		{
			var client = new RestClient(Config.NumberProviderConfig.BaseNexmoUrl);
			var request = new RestRequest(GenerateSendTextMessageUrl(number, message, departmentNumber), Method.Get);

			var response = await client.ExecuteAsync(request);

			if (response.ResponseStatus == ResponseStatus.Completed)
			{
				if (response.Content.Contains("rejected"))
				{
					// Error
					return false;
				}
			}

			return true;
		}

		public async Task<bool> SendTextMessageViaTwillio(string number, string message, string departmentNumber)
		{
			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);
			MessageResource messageResource;
			try
			{
				messageResource = await MessageResource.CreateAsync(
					from: new PhoneNumber(Config.NumberProviderConfig.TwilioResgridNumber),
					to: new PhoneNumber(number),
					body: message);

				if (messageResource != null)
					return true;
				else
					return false;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}

		}

		public async Task<bool> SendTextMessageViaSignalWire(string number, string message, string departmentNumber)
		{
			try
			{
				var options = new RestClientOptions(Config.NumberProviderConfig.SignalWireApiUrl)
				{
					Authenticator = new HttpBasicAuthenticator(Config.NumberProviderConfig.SignalWireAccountSid, Config.NumberProviderConfig.SignalWireApiKey)
				};
				var client = new RestClient(options);
				var request = new RestRequest(GenerateSendTextMessageUrlForSignalWire(), Method.Post);

				if (!number.StartsWith("+"))
				{
					if (number.Length >= 11)
						number = $"+{number}";
					else
					{
						if (number.Length == 10)
						{
							number = $"+1{number}";
						}
					}

				}

				var sendingPhoneNumber = GetSendingPhoneNumber(number);

				request.AddJsonBody(new
				{
					From = sendingPhoneNumber,
					To = number,
					Body = message
				});

				var response = await client.ExecuteAsync<SignalWireMessageResponse>(request);

				if (response.ResponseStatus == ResponseStatus.Completed)
				{
					if (response.StatusCode == HttpStatusCode.Created && response.Data.error_code == null)
						return true;
					else
						return false;
				}
				else
				{
					return false;
				}
			}
			catch (Exception ex)
			{
				return false;
			}
		}

		public async Task<bool> SendTextMessageViaDiafaan(string number, string message)
		{
			var client = new RestClient(Config.NumberProviderConfig.DiafaanSmsGatewayUrl);
			var request = new RestRequest(GenerateSendTextMessageUrlForDiafaan(number, message), Method.Get);

			var response = await client.ExecuteAsync(request);

			if (response.ResponseStatus == ResponseStatus.Completed)
			{
				if (response.StatusCode == HttpStatusCode.OK)
					return true;
				else
					return false;
			}
			else
			{
				return false;
			}
		}

		public string GetSendingPhoneNumber(string number)
		{
			var sendingPhoneNumber = Config.NumberProviderConfig.SignalWireResgridNumber;

			/* 
			 * Logic below was to work around some issues sending messages to numbers via
			 * Signalwire, it looks like they would 'spam' block or rate limit if all sent
			 * from one number. So added regions and message would be sent to users from a
			 * Region their number is located in, also helped them get stuff from a local'ish
			 * number. But it makes sense to just migrate this to toll-free numbers now. -SJ
			 * 
			 */
			//try
			//{
			//	var zone = GetZoneForAreaCode(number);
			//	var possibleNumbers = Config.NumberProviderConfig.SignalWireZones[zone];
			//	var sendingNumber = possibleNumbers.Random();

			//	if (!String.IsNullOrWhiteSpace(sendingNumber))
			//		sendingPhoneNumber = sendingNumber;
			//}
			//catch (Exception ex)
			//{
			//	Logging.LogException(ex);
			//}

			return sendingPhoneNumber;
		}

		public int GetZoneForAreaCode(string number)
		{
			try
			{
				LoadAreaCodeData();

				if (_areaCodes != null && _areaCodes.Any())
				{
					string areaCode = NumberHelper.TryGetAreaCode(number);

					if (!String.IsNullOrWhiteSpace(areaCode))
					{
						var record = _areaCodes.Where(x => x.AreaCode == int.Parse(areaCode)).FirstOrDefault();

						if (record != null && !String.IsNullOrWhiteSpace(record.State))
						{
							switch (record.State.ToUpper())
							{
								case "ALABAMA":
									return 4;
								case "ALASKA":
									return 1;
								case "AMERICAN SAMOA":
									return 1;
								case "ARIZONA":
									return 2;
								case "ARKANSAS":
									return 4;
								case "CALIFORNIA":
									return 1;
								case "COLORADO":
									return 2;
								case "CONNECTICUT":
									return 5;
								case "DELAWARE":
									return 5;
								case "DISTRICT OF COLUMBIA":
									return 6;
								case "FEDERATED STATES OF MICRONESIA":
									return 1;
								case "FLORIDA":
									return 4;
								case "GEORGIA":
									return 4;
								case "GUAM":
									return 1;
								case "HAWAII":
									return 1;
								case "IDAHO":
									return 1;
								case "ILLINOIS":
									return 3;
								case "INDIANA":
									return 6;
								case "IOWA":
									return 3;
								case "KANSAS":
									return 3;
								case "KENTUCKY":
									return 4;
								case "LOUISIANA":
									return 4;
								case "MAINE":
									return 5;
								case "MARSHALL ISLANDS":
									return 1;
								case "MARYLAND":
									return 5;
								case "MASSACHUSETTS":
									return 5;
								case "MICHIGAN":
									return 6;
								case "MINNESOTA":
									return 3;
								case "MISSISSIPPI":
									return 4;
								case "MISSOURI":
									return 3;
								case "MONTANA":
									return 2;
								case "NEBRASKA":
									return 3;
								case "NEVADA":
									return 1;
								case "NEW HAMPSHIRE":
									return 5;
								case "NEW JERSEY":
									return 5;
								case "NEW MEXICO":
									return 2;
								case "NEW YORK":
									return 5;
								case "NORTH CAROLINA":
									return 6;
								case "NORTH DAKOTA":
									return 3;
								case "NORTHERN MARIANA ISLANDS":
									return 1;
								case "OHIO":
									return 6;
								case "OKLAHOMA":
									return 4;
								case "OREGON":
									return 1;
								case "PALAU":
									return 1;
								case "PENNSYLVANIA":
									return 5;
								case "PUERTO RICO":
									return 1;
								case "RHODE ISLAND":
									return 5;
								case "SOUTH CAROLINA":
									return 6;
								case "SOUTH DAKOTA":
									return 3;
								case "TENNESSEE":
									return 4;
								case "TEXAS":
									return 4;
								case "UTAH":
									return 2;
								case "VERMONT":
									return 5;
								case "VIRGIN ISLANDS":
									return 1;
								case "VIRGINIA":
									return 6;
								case "WASHINGTON":
									return 1;
								case "WEST VIRGINIA":
									return 6;
								case "WISCONSIN":
									return 3;
								case "WYOMING":
									return 2;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{

			}

			var rnd = new Random(DateTime.Now.Millisecond);
			int zone = rnd.Next(_minZone, _maxZone);
			return zone;
		}

		private void LoadAreaCodeData()
		{
			if (_areaCodes == null || !_areaCodes.Any())
			{
				using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Resgrid.Providers.Number.Data.area-code-cities.csv"))
				{
					using (var reader = new StreamReader(stream))
					{
						var config = new CsvHelper.Configuration.CsvConfiguration(new CultureInfo("en-US"));
						config.HasHeaderRecord = false;

						var csvReader = new CsvReader(reader, config);
						_areaCodes = csvReader.GetRecords<AreaCodeCity>().ToList();
					}
				}

				var minKey = Config.NumberProviderConfig.SignalWireZones.First();
				var maxKey = Config.NumberProviderConfig.SignalWireZones.Last();
				_minZone = minKey.Key;
				_maxZone = maxKey.Key;
			}
		}

		private static string GenerateSendTextMessageUrl(string number, string message, string departmentNumber)
		{
			if (!string.IsNullOrWhiteSpace(departmentNumber))
				return string.Format("sms/json?api_key={0}&api_secret={1}&from={2}&to={3}&text={4}", Config.NumberProviderConfig.NexmoApiKey, Config.NumberProviderConfig.NexmoApiSecret, departmentNumber, number, HttpUtility.UrlEncode(message));
			else
				return string.Format("sms/json?api_key={0}&api_secret={1}&from={2}&to={3}&text={4}", Config.NumberProviderConfig.NexmoApiKey, Config.NumberProviderConfig.NexmoApiSecret, "12132633666", number, HttpUtility.UrlEncode(message));
		}

		private static string GenerateSendTextMessageUrlForDiafaan(string number, string message)
		{
			return $"http/send-message/?username={HttpUtility.UrlEncode(Config.NumberProviderConfig.DiafaanSmsGatewayUserName)}&password={HttpUtility.UrlEncode(Config.NumberProviderConfig.DiafaanSmsGatewayPassword)}&to={HttpUtility.UrlEncode(number)}&message={HttpUtility.UrlEncode(message)}";
		}

		private static string GenerateSendTextMessageUrlForSignalWire()
		{
			return $"/api/laml/2010-04-01/Accounts/{Resgrid.Config.NumberProviderConfig.SignalWireAccountSid}/Messages.json";
		}
	}
}
