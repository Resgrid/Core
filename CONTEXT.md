# CONTEXT

**Current Task:** Implementing Phase 3 of the Resgrid chatbot (branch `chatbot`) — write/dispatch handlers, outbound delivery, localization, and new platform adapters. Build green; 71 chatbot unit tests passing. Remaining work is recorded in the plan: `…/Resgrid/Dev/Chatbot/chatbot-phase3-plan.md` → "[v1.2] Implementation Status".

**Key Decisions:**
- Destructive-action confirmation is real: ingress (`ChatbotIngressService` step 5a) re-dispatches a `__confirmed` intent to the owning handler on YES (replaced the engine's faked confirm).
- Outbound = chat as a first-class `CommunicationService` channel: `IChatbotOutboundService` in `Resgrid.Model`, `NullChatbotOutboundService` default (`PreserveExistingDefaults`), real impl + `IChatbotAdapterRegistry` in `Providers.Chatbot`. Same Null+PreserveExistingDefaults pattern for `IChatbotWebChatNotifier`.
- Localization uses culture-explicit `ChatbotResources` (not `IStringLocalizer`), keyed off `ChatbotSession.Culture` (from `UserProfile.Language`); English values kept identical to old literals so tests are unchanged. 21/22 handlers localized in 9 languages (Help left as English command reference).

**Next Steps:**
- Wire `CommunicationService.SendNotificationAsync` + `SendCalendarAsync` to `IChatbotOutboundService` (same one-line pattern as SendMessage/SendCall); do P3.17 (`AddManualIdentity` endpoint, `IChatbotIdentityResolver`, migration `M0071` per-identity outbound prefs).
- Real platform work needing external SDKs/infra: Teams (`Microsoft.Bot.Builder`), Signal (`signald`), Discord/Slack/Telegram rich rendering (real clients), WebChat web-layer SignalR `IChatbotWebChatNotifier` impl + `ChatbotMessageReceived` hub event.
- P3.20 UI (Profile "Linked Chat Accounts" + Admin chatbot config, existing stack); P3.13 Scriban templates; P3.14 multi-turn polish (SetStatus/SetStaffing still prompt-faked); native-speaker review of non-English translations.
