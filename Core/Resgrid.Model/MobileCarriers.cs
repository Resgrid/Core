using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Resgrid.Model
{
	//http://en.wikipedia.org/wiki/List_of_SMS_gateways
	//http://www.theblog.ca/free-sms-canada
	//http://www.ukrainecalling.com/email-to-text.aspx
	//http://martinfitzpatrick.name/list-of-email-to-sms-gateways/
	public enum MobileCarriers
	{
		[Description("None")]
		None = 0,

		[Description("Alltel")]
		Alltel = 1, // number@sms.alltelwireless.co

		[Description("Verizon")]
		Verizon = 2, // number@vtext.com

		[Description("AT&T")]
		Att = 3, //domestic-number@txt.att.net

		[Description("Bell South")]
		BellSouth = 4, //number@sms.bellsouth.com

		[Description("Boost Mobile")]
		BoostMobile = 5, //number@myboostmobile.com

		[Description("Cingular")]
		CingularPlan = 6, //number@mobile.mycingular.com

		[Description("Cingular GoPhone")]
		CingularGoPhone = 7, //number@cingulartext.com

		[Description("Cricket")]
		Cricket = 8, //number@sms.mycricket.com

		[Description("MetroPCS")]
		MetroPcs = 9, //number@mymetropcs.com

		[Description("Quest Wireless")]
		QuestWireless = 10, //number@qwestmp.com

		[Description("Sprint")]
		Sprint = 11, //number@messaging.sprintpcs.com

		[Description("Nextel")]
		Nextel = 12, //number@page.nextel.com

		[Description("Straight Talk")]
		StraightTalk = 13, //number@vtext.com

		[Description("T-Mobile")]
		TMobile = 14, //number@tmomail.net

		[Description("US Cellular")]
		USCellular = 15, //number@email.uscc.net

		[Description("Virgin Mobile")]
		VirginMobile = 16, //number@vmobl.com

		[Description("TracFone")]
		TracFone = 17, //number@mmst5.tracfone.com

		[Description("Koodo Mobile")]
		Koodo = 18, //SMS Direct Send //number@msg.koodomobile.com

		[Description("Bell Mobility")]
		BellMobility = 19, //number@txt.bell.ca 

		[Description("Telus Mobility")]
		TelusMobility = 20, //number@msg.telus.com // Now Direct Send

		[Description("Rogers Wireless")]
		RogersWireless = 21, //number@pcs.rogers.com

		[Description("Virgin Mobile UK")]
		VirginMobileUk = 22, //0number@vxtras.com

		[Description("O2")]
		O2 = 23, //44number@mmail.co.uk

		[Description("Orange")]
		Orange = 24, //0number@orange.net

		[Description("T-Mobile UK")]
		TMobileUk = 25, //0number@t-mobile.uk.net 

		[Description("Vodafone")]
		Vodafone = 26, //0number@vodafone.net

		[Description("Three (3) UK")]
		Three = 27, //number@smtp-mbb.three.co.uk

		[Description("Giff Gaff")]
		GiffGaff = 28, //number@giffgaff.com

		[Description("Appalachian Wireless")]
		AppalachianWireless = 29, //number@awsms.com

		[Description("Optus (T-Mobile)")]
		Optus = 30, //number@optusmobile.com.au

		[Description("SaskTel Mobility")]
		SaskTel = 31, //number@sms.sasktel.com, SMS Direct Send now

		[Description("Globalstar")]
		Globalstar = 32, //number@msg.globalstarusa.com http://www.ocens.com/Globalstar-SMS.aspx

		[Description("Iridium")]
		Iridium = 33, //number@msg.iridium.com

		[Description("Telstra")]
		Telstra = 34, // SMS Direct Send

		[Description("BTC (Bahamas)")]
		Btc = 35, // SMS Direct Send

		[Description("EE (UK)")]
		EE = 36, // SMS Direct Send

		[Description("MTS Mobility")]
		MTSMobility = 37, // number@text.mtsmobility.com

		[Description("STC")]
		STC = 38, // SMS Direct Send

		[Description("Mobily")]
		Mobily = 39, // SMS Direct Send

		[Description("Zain")]
		Zain = 40, // SMS Direct Send

		[Description("Fido")]
		Fido = 41, // SMS Direct Send

		[Description("Spark")]
		Spark = 42, // SMS Direct Send

		[Description("Mint")]
		Mint = 43, // SMS Direct Send

		[Description("Chatr")]
		Chatr = 44, // SMS Direct Send

		[Description("Eastlink")]
		Eastlink = 45, // SMS Direct Send

		[Description("Freedom Mobile")]
		FreedomMobile = 46, // SMS Direct Send

		[Description("FSM Telecom")]
		FSMTC = 47, // SMS Direct Send

		[Description("MTC Namibia")]
		MTCNamibia = 48, // SMS Direct Send

		[Description("Google Fi")]
		GoogleFi = 49, // SMS Direct Send
		
		[Description("Cellcom")]
		Cellcom = 50, // SMS Direct Send

		[Description("Vodacom")]
		Vodacom = 51, // SMS Direct Send

		[Description("MTN")]
		MTN = 52, // SMS Direct Send

		[Description("Telkom Mobile")]
		TelkomMobile = 53, // SMS Direct Send

		[Description("Cell C")]
		CellC = 54, // SMS Direct Send
	}

	public static class Carriers
	{
		public static Dictionary<MobileCarriers, string> CarriersMap = new Dictionary<MobileCarriers, string>()
																																{
																																	{MobileCarriers.Alltel, "{0}@sms.alltelwireless.com"},
																																	{MobileCarriers.Att, "{0}@txt.att.net"},
																																	{MobileCarriers.BellSouth, "{0}@sms.bellsouth.com"},
																																	{MobileCarriers.BoostMobile, "{0}@myboostmobile.com"},
																																	{MobileCarriers.CingularGoPhone,"{0}@cingulartext.com"},
																																	{MobileCarriers.CingularPlan,"{0}@mobile.mycingular.com"},
																																	{MobileCarriers.Cricket,"{0}@sms.mycricket.com"},
																																	{MobileCarriers.MetroPcs, "{0}@mymetropcs.com"},
																																	{MobileCarriers.Nextel, "{0}@messaging.nextel.com"},
																																	{MobileCarriers.QuestWireless, "{0}@qwestmp.com"},
																																	{MobileCarriers.Sprint, "{0}@messaging.sprintpcs.com"},
																																	{MobileCarriers.StraightTalk, "{0}@vtext.com"},
																																	{MobileCarriers.TMobile, "{0}@tmomail.net"},
																																	//{MobileCarriers.TracFone, "{0}@mmst5.tracfone.com"},
																																	{MobileCarriers.TracFone, "Direct"},
																																	{MobileCarriers.USCellular, "{0}@email.uscc.net"},
																																	{MobileCarriers.Verizon, "{0}@vtext.com"},
																																	//{MobileCarriers.Verizon, "Direct"},
																																	{MobileCarriers.VirginMobile, "Direct"},
																																	{MobileCarriers.Koodo, "{0}@msg.koodomobile.com"},
																																	{MobileCarriers.BellMobility, "Direct"},
																																	{MobileCarriers.TelusMobility, "Direct"},
																																	{MobileCarriers.RogersWireless, "{0}@pcs.rogers.com"},
																																	{MobileCarriers.VirginMobileUk, "{0}@vxtras.com"},
																																	{MobileCarriers.O2, "{0}@mmail.co.uk"},
																																	{MobileCarriers.Orange, "{0}@orange.net"},
																																	{MobileCarriers.TMobileUk, "{0}@t-mobile.uk.net"},
																																	{MobileCarriers.Vodafone, "{0}@vodafone.net"},
																																	{MobileCarriers.Three, "{0}@smtp-mbb.three.co.uk"},
																																	{MobileCarriers.GiffGaff, "{0}@giffgaff.com"},
																																	{MobileCarriers.AppalachianWireless, "{0}@awsms.com"},
																																	{MobileCarriers.Optus, "{0}@optusmobile.com.au"},
																																	//{MobileCarriers.SaskTel, "{0}@sms.sasktel.com"},
																																	{MobileCarriers.SaskTel, "Direct"},
																																	{MobileCarriers.Globalstar, "{0}@msg.globalstarusa.com"},
																																	{MobileCarriers.Iridium, "{0}@msg.iridium.com"},
																																	{MobileCarriers.Telstra, "Direct"},
																																	{MobileCarriers.Btc, "Direct"},
																																	{MobileCarriers.EE, "Direct"},
																																	{MobileCarriers.MTSMobility, "{0}@text.mtsmobility.com"},
																																	{MobileCarriers.Fido, "Direct"},
																																	{MobileCarriers.Spark, "Direct"},
																																	{MobileCarriers.Mint, "{0}@tmomail.net"},
																																	{MobileCarriers.Chatr, "Direct"},
																																	{MobileCarriers.Eastlink, "Direct"},
																																	{MobileCarriers.FreedomMobile, "Direct"},
																																	{MobileCarriers.FSMTC, "Direct"},
																																	{MobileCarriers.MTCNamibia, "Direct"},
																																	{MobileCarriers.GoogleFi, "Direct"},
																																	{MobileCarriers.Cellcom, "Direct"},
																																	{MobileCarriers.Vodacom, "Direct"},
																																	{MobileCarriers.MTN, "Direct"},
																																	{MobileCarriers.TelkomMobile, "Direct"},
																																	{MobileCarriers.CellC, "Direct"},
																																};

		public static Dictionary<MobileCarriers, Tuple<int, string>> CarriersNumberLength = new Dictionary<MobileCarriers, Tuple<int, string>>()
																																{
																																	{MobileCarriers.Alltel, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.Att, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.BellSouth, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.BoostMobile, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.CingularGoPhone, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.CingularPlan, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.Cricket, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.MetroPcs, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.Nextel, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.QuestWireless, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.Sprint, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.StraightTalk, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.TMobile, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.TracFone, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.USCellular, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.Verizon, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.VirginMobile, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.Koodo, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.BellMobility, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.TelusMobility, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.RogersWireless, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	{MobileCarriers.VirginMobileUk, Tuple.Create(12, "Format is 44##########, Must start with 44 country code.")},
																																	{MobileCarriers.O2, Tuple.Create(12, "Format is 44##########, Must start with 44 country code.")},
																																	{MobileCarriers.Orange, Tuple.Create(12, "Format is 44##########, Must start with 44 country code.")},
																																	{MobileCarriers.TMobileUk, Tuple.Create(12, "Format is 44##########, Must start with 44 country code.")},
																																	{MobileCarriers.Vodafone, Tuple.Create(12, "Format is 44##########, Must start with 44 country code.")},
																																	//{MobileCarriers.Three, "{0}@smtp-mbb.three.co.uk"},
																																	//{MobileCarriers.GiffGaff, "{0}@giffgaff.com"},
																																	{MobileCarriers.AppalachianWireless, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																	//{MobileCarriers.Optus, "{0}@optusmobile.com.au"},
																																	{MobileCarriers.SaskTel, Tuple.Create(10, "Format is ###-###-####, No country code.")},
																																};

		public static HashSet<MobileCarriers> DirectSendCarriers = new HashSet<MobileCarriers>()
		{
			MobileCarriers.Telstra,
			MobileCarriers.Att,
			MobileCarriers.Cricket,
			MobileCarriers.Sprint,
			MobileCarriers.Btc,
			MobileCarriers.EE,
			MobileCarriers.Verizon,
			MobileCarriers.TelusMobility,
			MobileCarriers.TracFone,
			MobileCarriers.STC,
			MobileCarriers.Mobily,
			MobileCarriers.Zain,
			MobileCarriers.VirginMobile,
			MobileCarriers.Fido,
			MobileCarriers.BellMobility,
			MobileCarriers.SaskTel,
			MobileCarriers.Spark,
			MobileCarriers.Koodo,
			MobileCarriers.Mint,
			MobileCarriers.Chatr,
			MobileCarriers.Eastlink,
			MobileCarriers.FreedomMobile,
			MobileCarriers.FSMTC,
			MobileCarriers.MTCNamibia,
			MobileCarriers.GoogleFi,
			MobileCarriers.Cellcom,
			MobileCarriers.Vodacom,
			MobileCarriers.MTN,
			MobileCarriers.TelkomMobile,
			MobileCarriers.CellC
		};

		public static HashSet<MobileCarriers> OnPremSmsGatewayCarriers = new HashSet<MobileCarriers>()
		{
			//MobileCarriers.Cricket,
			//MobileCarriers.TracFone
		};


	}
}
