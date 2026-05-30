using System;
using System.Collections.Generic;

namespace Resgrid.Chatbot.Localization
{
	/// <summary>
	/// Culture-explicit string resources for chatbot responses. Unlike the web app's
	/// <c>IStringLocalizer</c> (which keys off the request thread culture), the chatbot must render in the
	/// *target user's* preferred language (from <c>UserProfile.Language</c>, carried on
	/// <c>ChatbotSession.Culture</c>), so lookups take an explicit culture and fall back to English.
	///
	/// English values intentionally match the handlers' original literals so behavior (and tests) are
	/// unchanged for English. Add new keys here and translate them across the supported cultures; a
	/// missing culture for a key falls back to English, and a missing key surfaces the key name.
	/// </summary>
	public static class ChatbotResources
	{
		private static readonly HashSet<string> _supported =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "es", "sv", "de", "fr", "it", "pl", "uk", "ar" };

		public static string NormalizeCulture(string language)
		{
			if (string.IsNullOrWhiteSpace(language))
				return "en";

			var lang = language.Trim();
			var sep = lang.IndexOfAny(new[] { '-', '_' });
			if (sep > 0)
				lang = lang.Substring(0, sep);
			lang = lang.ToLowerInvariant();

			return _supported.Contains(lang) ? lang : "en";
		}

		public static string Get(string key, string culture, params object[] args)
		{
			var c = NormalizeCulture(culture);

			string value = null;
			if (_table.TryGetValue(key, out var byCulture))
			{
				if (!byCulture.TryGetValue(c, out value))
					byCulture.TryGetValue("en", out value);
			}

			value ??= key;

			if (args != null && args.Length > 0)
			{
				try { value = string.Format(value, args); }
				catch { /* malformed format string — return the unformatted text rather than throw */ }
			}

			return value;
		}

		private static Dictionary<string, string> L(string en, string es, string sv, string de, string fr, string it, string pl, string uk, string ar)
			=> new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["en"] = en, ["es"] = es, ["sv"] = sv, ["de"] = de, ["fr"] = fr, ["it"] = it, ["pl"] = pl, ["uk"] = uk, ["ar"] = ar
			};

		private static Dictionary<string, string> EnOnly(string en)
			=> new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = en };

		// Command tokens inside examples (e.g. '#42', 'reply yes to #42', 'send message to [name]: [message]')
		// stay in English because the classifier only recognizes English commands today.
		private static readonly Dictionary<string, Dictionary<string, string>> _table =
			new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
		{
			["Msg_NoUnread"] = L(
				"You have no unread messages.",
				"No tienes mensajes sin leer.",
				"Du har inga olästa meddelanden.",
				"Sie haben keine ungelesenen Nachrichten.",
				"Vous n'avez aucun message non lu.",
				"Non hai messaggi non letti.",
				"Nie masz nieprzeczytanych wiadomości.",
				"У вас немає непрочитаних повідомлень.",
				"ليس لديك رسائل غير مقروءة."),

			["Msg_UnreadCount"] = L(
				"You have {0} unread message(s):",
				"Tienes {0} mensaje(s) sin leer:",
				"Du har {0} oläst(a) meddelande(n):",
				"Sie haben {0} ungelesene Nachricht(en):",
				"Vous avez {0} message(s) non lu(s) :",
				"Hai {0} messaggio/i non letto/i:",
				"Masz {0} nieprzeczytaną(-e) wiadomość(-ci):",
				"У вас {0} непрочитаних повідомлень:",
				"لديك {0} رسالة (رسائل) غير مقروءة:"),

			["Msg_AndMore"] = L(
				"...and {0} more.",
				"...y {0} más.",
				"...och {0} till.",
				"...und {0} weitere.",
				"...et {0} de plus.",
				"...e altri {0}.",
				"...i {0} więcej.",
				"...та ще {0}.",
				"...و{0} أخرى."),

			["Msg_ListReadHint"] = L(
				"Reply '#<number>' to read a message (e.g., '#42').",
				"Responde '#<number>' para leer un mensaje (p. ej., '#42').",
				"Svara '#<number>' för att läsa ett meddelande (t.ex. '#42').",
				"Antworten Sie mit '#<number>', um eine Nachricht zu lesen (z. B. '#42').",
				"Répondez '#<number>' pour lire un message (p. ex. '#42').",
				"Rispondi '#<number>' per leggere un messaggio (es. '#42').",
				"Odpowiedz '#<number>', aby przeczytać wiadomość (np. '#42').",
				"Відповідайте '#<number>', щоб прочитати повідомлення (напр., '#42').",
				"أرسل '#<number>' لقراءة رسالة (مثال: '#42')."),

			["Msg_ListItem"] = EnOnly("#{0} - {1} ({2})"),

			["Msg_NoSubject"] = L(
				"(no subject)",
				"(sin asunto)",
				"(inget ämne)",
				"(kein Betreff)",
				"(sans objet)",
				"(nessun oggetto)",
				"(brak tematu)",
				"(без теми)",
				"(بدون موضوع)"),

			["Msg_ErrorRetrieving"] = L(
				"Error retrieving messages.",
				"Error al recuperar los mensajes.",
				"Det gick inte att hämta meddelanden.",
				"Fehler beim Abrufen der Nachrichten.",
				"Erreur lors de la récupération des messages.",
				"Errore durante il recupero dei messaggi.",
				"Błąd podczas pobierania wiadomości.",
				"Помилка отримання повідомлень.",
				"حدث خطأ أثناء جلب الرسائل."),

			["Msg_SpecifyNumberRead"] = L(
				"Please specify a message number, e.g., '#42'.",
				"Indica un número de mensaje, p. ej., '#42'.",
				"Ange ett meddelandenummer, t.ex. '#42'.",
				"Bitte geben Sie eine Nachrichtennummer an, z. B. '#42'.",
				"Veuillez indiquer un numéro de message, p. ex. '#42'.",
				"Specifica un numero di messaggio, es. '#42'.",
				"Podaj numer wiadomości, np. '#42'.",
				"Вкажіть номер повідомлення, напр., '#42'.",
				"يرجى تحديد رقم الرسالة، مثال: '#42'."),

			["Msg_NotFound"] = L(
				"Message #{0} not found.",
				"Mensaje #{0} no encontrado.",
				"Meddelande #{0} hittades inte.",
				"Nachricht #{0} nicht gefunden.",
				"Message #{0} introuvable.",
				"Messaggio #{0} non trovato.",
				"Nie znaleziono wiadomości #{0}.",
				"Повідомлення #{0} не знайдено.",
				"لم يتم العثور على الرسالة #{0}."),

			["Msg_ReadHeader"] = L(
				"Message #{0}: {1}",
				"Mensaje #{0}: {1}",
				"Meddelande #{0}: {1}",
				"Nachricht #{0}: {1}",
				"Message #{0} : {1}",
				"Messaggio #{0}: {1}",
				"Wiadomość #{0}: {1}",
				"Повідомлення #{0}: {1}",
				"رسالة #{0}: {1}"),

			["Msg_Sent"] = L(
				"Sent: {0}",
				"Enviado: {0}",
				"Skickat: {0}",
				"Gesendet: {0}",
				"Envoyé : {0}",
				"Inviato: {0}",
				"Wysłano: {0}",
				"Надіслано: {0}",
				"أُرسلت: {0}"),

			["Msg_ReadFooter"] = L(
				"Reply 'reply yes to #{0}' to respond, or 'delete #{0}' to remove it.",
				"Responde 'reply yes to #{0}' para responder, o 'delete #{0}' para eliminarla.",
				"Svara 'reply yes to #{0}' för att svara, eller 'delete #{0}' för att ta bort den.",
				"Antworten Sie mit 'reply yes to #{0}', um zu antworten, oder 'delete #{0}', um sie zu entfernen.",
				"Répondez 'reply yes to #{0}' pour répondre, ou 'delete #{0}' pour la supprimer.",
				"Rispondi 'reply yes to #{0}' per rispondere, oppure 'delete #{0}' per eliminarla.",
				"Odpowiedz 'reply yes to #{0}', aby odpowiedzieć, lub 'delete #{0}', aby ją usunąć.",
				"Відповідайте 'reply yes to #{0}', щоб відповісти, або 'delete #{0}', щоб видалити.",
				"أرسل 'reply yes to #{0}' للرد، أو 'delete #{0}' لحذفها."),

			["Msg_ErrorRetrievingOne"] = L(
				"Error retrieving the message.",
				"Error al recuperar el mensaje.",
				"Det gick inte att hämta meddelandet.",
				"Fehler beim Abrufen der Nachricht.",
				"Erreur lors de la récupération du message.",
				"Errore durante il recupero del messaggio.",
				"Błąd podczas pobierania wiadomości.",
				"Помилка отримання повідомлення.",
				"حدث خطأ أثناء جلب الرسالة."),

			["Msg_SpecifyNumberDelete"] = L(
				"Please specify a message number, e.g., 'delete #42'.",
				"Indica un número de mensaje, p. ej., 'delete #42'.",
				"Ange ett meddelandenummer, t.ex. 'delete #42'.",
				"Bitte geben Sie eine Nachrichtennummer an, z. B. 'delete #42'.",
				"Veuillez indiquer un numéro de message, p. ex. 'delete #42'.",
				"Specifica un numero di messaggio, es. 'delete #42'.",
				"Podaj numer wiadomości, np. 'delete #42'.",
				"Вкажіть номер повідомлення, напр., 'delete #42'.",
				"يرجى تحديد رقم الرسالة، مثال: 'delete #42'."),

			["Msg_Deleted"] = L(
				"Message #{0} deleted.",
				"Mensaje #{0} eliminado.",
				"Meddelande #{0} borttaget.",
				"Nachricht #{0} gelöscht.",
				"Message #{0} supprimé.",
				"Messaggio #{0} eliminato.",
				"Wiadomość #{0} usunięta.",
				"Повідомлення #{0} видалено.",
				"تم حذف الرسالة #{0}."),

			["Msg_ErrorDeleting"] = L(
				"Error deleting the message.",
				"Error al eliminar el mensaje.",
				"Det gick inte att ta bort meddelandet.",
				"Fehler beim Löschen der Nachricht.",
				"Erreur lors de la suppression du message.",
				"Errore durante l'eliminazione del messaggio.",
				"Błąd podczas usuwania wiadomości.",
				"Помилка видалення повідомлення.",
				"حدث خطأ أثناء حذف الرسالة."),

			["Msg_RespondUsage"] = L(
				"Usage: 'reply yes to #42'.",
				"Uso: 'reply yes to #42'.",
				"Användning: 'reply yes to #42'.",
				"Verwendung: 'reply yes to #42'.",
				"Utilisation : 'reply yes to #42'.",
				"Uso: 'reply yes to #42'.",
				"Użycie: 'reply yes to #42'.",
				"Використання: 'reply yes to #42'.",
				"الاستخدام: 'reply yes to #42'."),

			["Msg_ResponseRecorded"] = L(
				"Response '{0}' recorded for message #{1}.",
				"Respuesta '{0}' registrada para el mensaje #{1}.",
				"Svaret '{0}' har registrerats för meddelande #{1}.",
				"Antwort '{0}' für Nachricht #{1} gespeichert.",
				"Réponse '{0}' enregistrée pour le message #{1}.",
				"Risposta '{0}' registrata per il messaggio #{1}.",
				"Odpowiedź '{0}' zapisana dla wiadomości #{1}.",
				"Відповідь '{0}' збережено для повідомлення #{1}.",
				"تم تسجيل الرد '{0}' للرسالة #{1}."),

			["Msg_ErrorResponding"] = L(
				"Error responding to the message.",
				"Error al responder al mensaje.",
				"Det gick inte att svara på meddelandet.",
				"Fehler beim Beantworten der Nachricht.",
				"Erreur lors de la réponse au message.",
				"Errore durante la risposta al messaggio.",
				"Błąd podczas odpowiadania na wiadomość.",
				"Помилка відповіді на повідомлення.",
				"حدث خطأ أثناء الرد على الرسالة."),

			["Msg_SendUsage"] = L(
				"Usage: 'send message to [name]: [message]' (e.g., 'send message to John Smith: running late').",
				"Uso: 'send message to [name]: [message]' (p. ej., 'send message to John Smith: running late').",
				"Användning: 'send message to [name]: [message]' (t.ex. 'send message to John Smith: running late').",
				"Verwendung: 'send message to [name]: [message]' (z. B. 'send message to John Smith: running late').",
				"Utilisation : 'send message to [name]: [message]' (p. ex. 'send message to John Smith: running late').",
				"Uso: 'send message to [name]: [message]' (es. 'send message to John Smith: running late').",
				"Użycie: 'send message to [name]: [message]' (np. 'send message to John Smith: running late').",
				"Використання: 'send message to [name]: [message]' (напр., 'send message to John Smith: running late').",
				"الاستخدام: 'send message to [name]: [message]' (مثال: 'send message to John Smith: running late')."),

			["Msg_NoSingleMatch"] = L(
				"I couldn't find a single match for '{0}'. Try their full name.",
				"No encontré una coincidencia única para '{0}'. Prueba con su nombre completo.",
				"Jag hittade ingen unik träff för '{0}'. Försök med deras fullständiga namn.",
				"Ich konnte keine eindeutige Übereinstimmung für '{0}' finden. Versuchen Sie den vollständigen Namen.",
				"Je n'ai pas trouvé de correspondance unique pour '{0}'. Essayez avec leur nom complet.",
				"Non ho trovato una corrispondenza univoca per '{0}'. Prova con il nome completo.",
				"Nie znalazłem jednoznacznego dopasowania dla '{0}'. Spróbuj podać pełne imię i nazwisko.",
				"Не вдалося знайти єдиний збіг для '{0}'. Спробуйте вказати повне ім'я.",
				"لم أتمكن من العثور على تطابق واحد لـ '{0}'. حاول استخدام الاسم الكامل."),

			["Msg_MessageSent"] = L(
				"Message sent to {0}.",
				"Mensaje enviado a {0}.",
				"Meddelandet skickades till {0}.",
				"Nachricht an {0} gesendet.",
				"Message envoyé à {0}.",
				"Messaggio inviato a {0}.",
				"Wiadomość wysłana do {0}.",
				"Повідомлення надіслано до {0}.",
				"تم إرسال الرسالة إلى {0}."),

			["Msg_ErrorSending"] = L(
				"Error sending the message.",
				"Error al enviar el mensaje.",
				"Det gick inte att skicka meddelandet.",
				"Fehler beim Senden der Nachricht.",
				"Erreur lors de l'envoi du message.",
				"Errore durante l'invio del messaggio.",
				"Błąd podczas wysyłania wiadomości.",
				"Помилка надсилання повідомлення.",
				"حدث خطأ أثناء إرسال الرسالة."),

			// === Call / dispatch handlers (YES/NO reply tokens and command examples stay English) ===
			["Call_NotFound"] = L(
				"Call #{0} not found.",
				"Llamada #{0} no encontrada.",
				"Larm #{0} hittades inte.",
				"Einsatz #{0} nicht gefunden.",
				"Appel #{0} introuvable.",
				"Chiamata #{0} non trovata.",
				"Nie znaleziono zgłoszenia #{0}.",
				"Виклик #{0} не знайдено.",
				"لم يتم العثور على البلاغ #{0}."),

			["Call_CloseWhich"] = L(
				"Which call would you like to close? Reply with the call number (e.g., C1445).",
				"¿Qué llamada quieres cerrar? Responde con el número de llamada (p. ej., C1445).",
				"Vilket larm vill du stänga? Svara med larmnumret (t.ex. C1445).",
				"Welchen Einsatz möchten Sie schließen? Antworten Sie mit der Einsatznummer (z. B. C1445).",
				"Quel appel souhaitez-vous clôturer ? Répondez avec le numéro d'appel (p. ex. C1445).",
				"Quale chiamata vuoi chiudere? Rispondi con il numero della chiamata (es. C1445).",
				"Które zgłoszenie chcesz zamknąć? Odpowiedz numerem zgłoszenia (np. C1445).",
				"Який виклик ви хочете закрити? Відповідайте номером виклику (напр., C1445).",
				"أي بلاغ تريد إغلاقه؟ أرسل رقم البلاغ (مثال: C1445)."),

			["Call_NoClosePermission"] = L(
				"You don't have permission to close this call.",
				"No tienes permiso para cerrar esta llamada.",
				"Du har inte behörighet att stänga detta larm.",
				"Sie haben keine Berechtigung, diesen Einsatz zu schließen.",
				"Vous n'avez pas la permission de clôturer cet appel.",
				"Non hai il permesso di chiudere questa chiamata.",
				"Nie masz uprawnień do zamknięcia tego zgłoszenia.",
				"У вас немає дозволу закрити цей виклик.",
				"ليس لديك إذن لإغلاق هذا البلاغ."),

			["Call_AlreadyClosed"] = L(
				"Call #{0} is already closed.",
				"La llamada #{0} ya está cerrada.",
				"Larm #{0} är redan stängt.",
				"Einsatz #{0} ist bereits geschlossen.",
				"L'appel #{0} est déjà clôturé.",
				"La chiamata #{0} è già chiusa.",
				"Zgłoszenie #{0} jest już zamknięte.",
				"Виклик #{0} вже закрито.",
				"البلاغ #{0} مغلق بالفعل."),

			["Call_CloseConfirm"] = L(
				"Close Call #{0} — {1}? Reply YES to confirm or NO to cancel.",
				"¿Cerrar la llamada #{0} — {1}? Responde YES para confirmar o NO para cancelar.",
				"Stänga larm #{0} — {1}? Svara YES för att bekräfta eller NO för att avbryta.",
				"Einsatz #{0} — {1} schließen? Antworten Sie YES zum Bestätigen oder NO zum Abbrechen.",
				"Clôturer l'appel #{0} — {1} ? Répondez YES pour confirmer ou NO pour annuler.",
				"Chiudere la chiamata #{0} — {1}? Rispondi YES per confermare o NO per annullare.",
				"Zamknąć zgłoszenie #{0} — {1}? Odpowiedz YES, aby potwierdzić, lub NO, aby anulować.",
				"Закрити виклик #{0} — {1}? Відповідайте YES для підтвердження або NO для скасування.",
				"إغلاق البلاغ #{0} — {1}؟ أرسل YES للتأكيد أو NO للإلغاء."),

			["Call_Closed"] = L(
				"Call #{0} — {1} has been closed.",
				"La llamada #{0} — {1} ha sido cerrada.",
				"Larm #{0} — {1} har stängts.",
				"Einsatz #{0} — {1} wurde geschlossen.",
				"L'appel #{0} — {1} a été clôturé.",
				"La chiamata #{0} — {1} è stata chiusa.",
				"Zgłoszenie #{0} — {1} zostało zamknięte.",
				"Виклик #{0} — {1} закрито.",
				"تم إغلاق البلاغ #{0} — {1}."),

			["Call_ErrorClosing"] = L(
				"Error closing the call.",
				"Error al cerrar la llamada.",
				"Det gick inte att stänga larmet.",
				"Fehler beim Schließen des Einsatzes.",
				"Erreur lors de la clôture de l'appel.",
				"Errore durante la chiusura della chiamata.",
				"Błąd podczas zamykania zgłoszenia.",
				"Помилка закриття виклику.",
				"حدث خطأ أثناء إغلاق البلاغ."),

			["Call_NoCreatePermission"] = L(
				"You don't have permission to create calls.",
				"No tienes permiso para crear llamadas.",
				"Du har inte behörighet att skapa larm.",
				"Sie haben keine Berechtigung, Einsätze zu erstellen.",
				"Vous n'avez pas la permission de créer des appels.",
				"Non hai il permesso di creare chiamate.",
				"Nie masz uprawnień do tworzenia zgłoszeń.",
				"У вас немає дозволу створювати виклики.",
				"ليس لديك إذن لإنشاء بلاغات."),

			["Call_DescribePrompt"] = L(
				"Please describe the call (e.g., 'Structure fire at 123 Main St').",
				"Describe la llamada (p. ej., 'Structure fire at 123 Main St').",
				"Beskriv larmet (t.ex. 'Structure fire at 123 Main St').",
				"Bitte beschreiben Sie den Einsatz (z. B. 'Structure fire at 123 Main St').",
				"Veuillez décrire l'appel (p. ex. 'Structure fire at 123 Main St').",
				"Descrivi la chiamata (es. 'Structure fire at 123 Main St').",
				"Opisz zgłoszenie (np. 'Structure fire at 123 Main St').",
				"Опишіть виклик (напр., 'Structure fire at 123 Main St').",
				"يرجى وصف البلاغ (مثال: 'Structure fire at 123 Main St')."),

			["Call_DispatchConfirm"] = L(
				"Create a new call: \"{0}\"? Reply YES to confirm or NO to cancel.",
				"¿Crear una nueva llamada: \"{0}\"? Responde YES para confirmar o NO para cancelar.",
				"Skapa ett nytt larm: \"{0}\"? Svara YES för att bekräfta eller NO för att avbryta.",
				"Neuen Einsatz erstellen: \"{0}\"? Antworten Sie YES zum Bestätigen oder NO zum Abbrechen.",
				"Créer un nouvel appel : \"{0}\" ? Répondez YES pour confirmer ou NO pour annuler.",
				"Creare una nuova chiamata: \"{0}\"? Rispondi YES per confermare o NO per annullare.",
				"Utworzyć nowe zgłoszenie: \"{0}\"? Odpowiedz YES, aby potwierdzić, lub NO, aby anulować.",
				"Створити новий виклик: \"{0}\"? Відповідайте YES для підтвердження або NO для скасування.",
				"إنشاء بلاغ جديد: \"{0}\"؟ أرسل YES للتأكيد أو NO للإلغاء."),

			["Call_Created"] = L(
				"Call #{0} created: {1}",
				"Llamada #{0} creada: {1}",
				"Larm #{0} skapat: {1}",
				"Einsatz #{0} erstellt: {1}",
				"Appel #{0} créé : {1}",
				"Chiamata #{0} creata: {1}",
				"Utworzono zgłoszenie #{0}: {1}",
				"Виклик #{0} створено: {1}",
				"تم إنشاء البلاغ #{0}: {1}"),

			["Call_ErrorCreating"] = L(
				"Error creating the call.",
				"Error al crear la llamada.",
				"Det gick inte att skapa larmet.",
				"Fehler beim Erstellen des Einsatzes.",
				"Erreur lors de la création de l'appel.",
				"Errore durante la creazione della chiamata.",
				"Błąd podczas tworzenia zgłoszenia.",
				"Помилка створення виклику.",
				"حدث خطأ أثناء إنشاء البلاغ."),

			["Call_RespondWhich"] = L(
				"Which call are you responding to? Reply with the call number (e.g., C1445).",
				"¿A qué llamada estás respondiendo? Responde con el número de llamada (p. ej., C1445).",
				"Vilket larm svarar du på? Svara med larmnumret (t.ex. C1445).",
				"Auf welchen Einsatz reagieren Sie? Antworten Sie mit der Einsatznummer (z. B. C1445).",
				"À quel appel répondez-vous ? Répondez avec le numéro d'appel (p. ex. C1445).",
				"A quale chiamata stai rispondendo? Rispondi con il numero della chiamata (es. C1445).",
				"Na które zgłoszenie odpowiadasz? Odpowiedz numerem zgłoszenia (np. C1445).",
				"На який виклик ви реагуєте? Відповідайте номером виклику (напр., C1445).",
				"على أي بلاغ تستجيب؟ أرسل رقم البلاغ (مثال: C1445)."),

			["Call_Responding"] = L(
				"You're now responding to Call #{0} — {1}.",
				"Ahora estás respondiendo a la llamada #{0} — {1}.",
				"Du svarar nu på larm #{0} — {1}.",
				"Sie reagieren jetzt auf Einsatz #{0} — {1}.",
				"Vous répondez maintenant à l'appel #{0} — {1}.",
				"Ora stai rispondendo alla chiamata #{0} — {1}.",
				"Teraz odpowiadasz na zgłoszenie #{0} — {1}.",
				"Тепер ви реагуєте на виклик #{0} — {1}.",
				"أنت الآن تستجيب للبلاغ #{0} — {1}."),

			["Call_ErrorResponding"] = L(
				"Error setting your response status.",
				"Error al establecer tu estado de respuesta.",
				"Det gick inte att ange din svarsstatus.",
				"Fehler beim Festlegen Ihres Antwortstatus.",
				"Erreur lors de la définition de votre statut de réponse.",
				"Errore durante l'impostazione del tuo stato di risposta.",
				"Błąd podczas ustawiania statusu odpowiedzi.",
				"Помилка встановлення статусу відповіді.",
				"حدث خطأ أثناء تعيين حالة استجابتك."),

			// === Calendar RSVP handler ===
			["Cal_RsvpUsage"] = L(
				"Usage: 'RSVP yes to [event name or number]'.",
				"Uso: 'RSVP yes to [event name or number]'.",
				"Användning: 'RSVP yes to [event name or number]'.",
				"Verwendung: 'RSVP yes to [event name or number]'.",
				"Utilisation : 'RSVP yes to [event name or number]'.",
				"Uso: 'RSVP yes to [event name or number]'.",
				"Użycie: 'RSVP yes to [event name or number]'.",
				"Використання: 'RSVP yes to [event name or number]'.",
				"الاستخدام: 'RSVP yes to [event name or number]'."),

			["Cal_MultipleMatch"] = L(
				"Multiple events match '{0}'. Reply with the event number instead.",
				"Varios eventos coinciden con '{0}'. Responde con el número del evento.",
				"Flera händelser matchar '{0}'. Svara med händelsens nummer i stället.",
				"Mehrere Veranstaltungen passen zu '{0}'. Antworten Sie stattdessen mit der Veranstaltungsnummer.",
				"Plusieurs événements correspondent à '{0}'. Répondez plutôt avec le numéro de l'événement.",
				"Più eventi corrispondono a '{0}'. Rispondi invece con il numero dell'evento.",
				"Wiele wydarzeń pasuje do '{0}'. Odpowiedz numerem wydarzenia.",
				"Кілька подій відповідають '{0}'. Відповідайте номером події.",
				"هناك عدة أحداث تطابق '{0}'. أرسل رقم الحدث بدلاً من ذلك."),

			["Cal_EventNotFound"] = L(
				"Event '{0}' not found.",
				"Evento '{0}' no encontrado.",
				"Händelsen '{0}' hittades inte.",
				"Veranstaltung '{0}' nicht gefunden.",
				"Événement '{0}' introuvable.",
				"Evento '{0}' non trovato.",
				"Nie znaleziono wydarzenia '{0}'.",
				"Подію '{0}' не знайдено.",
				"لم يتم العثور على الحدث '{0}'."),

			["Cal_NotAuthorized"] = L(
				"You're not able to RSVP to this event.",
				"No puedes confirmar asistencia a este evento.",
				"Du kan inte svara på denna händelse.",
				"Sie können für diese Veranstaltung nicht zusagen.",
				"Vous ne pouvez pas répondre à cet événement.",
				"Non puoi confermare la presenza a questo evento.",
				"Nie możesz potwierdzić udziału w tym wydarzeniu.",
				"Ви не можете відповісти на цю подію.",
				"لا يمكنك تأكيد الحضور لهذا الحدث."),

			["Cal_RsvpDone"] = L(
				"You've RSVP'd '{0}' to {1}.",
				"Has confirmado '{0}' para {1}.",
				"Du har svarat '{0}' på {1}.",
				"Sie haben '{0}' für {1} bestätigt.",
				"Vous avez répondu '{0}' à {1}.",
				"Hai risposto '{0}' a {1}.",
				"Odpowiedziałeś '{0}' na {1}.",
				"Ви відповіли '{0}' на {1}.",
				"لقد رددت '{0}' على {1}."),

			["Cal_ErrorRsvp"] = L(
				"Error processing your RSVP.",
				"Error al procesar tu confirmación.",
				"Det gick inte att behandla ditt svar.",
				"Fehler bei der Verarbeitung Ihrer Zusage.",
				"Erreur lors du traitement de votre réponse.",
				"Errore durante l'elaborazione della tua risposta.",
				"Błąd podczas przetwarzania odpowiedzi.",
				"Помилка обробки вашої відповіді.",
				"حدث خطأ أثناء معالجة ردك."),

			// === Shift signup / drop handlers ('shifts'/command examples stay English) ===
			["Shift_SpecifySignup"] = L(
				"Please specify a shift day by number. Use 'shifts' to see available shifts.",
				"Indica un día de turno por número. Usa 'shifts' para ver los turnos disponibles.",
				"Ange en skiftdag med nummer. Använd 'shifts' för att se tillgängliga skift.",
				"Bitte geben Sie einen Schichttag per Nummer an. Verwenden Sie 'shifts', um verfügbare Schichten zu sehen.",
				"Veuillez indiquer un jour de quart par numéro. Utilisez 'shifts' pour voir les quarts disponibles.",
				"Specifica un giorno di turno per numero. Usa 'shifts' per vedere i turni disponibili.",
				"Podaj dzień zmiany numerem. Użyj 'shifts', aby zobaczyć dostępne zmiany.",
				"Вкажіть день зміни за номером. Введіть 'shifts', щоб переглянути доступні зміни.",
				"حدد يوم المناوبة بالرقم. استخدم 'shifts' لعرض المناوبات المتاحة."),

			["Shift_NotFound"] = L(
				"Shift #{0} not found.",
				"Turno #{0} no encontrado.",
				"Skift #{0} hittades inte.",
				"Schicht #{0} nicht gefunden.",
				"Quart #{0} introuvable.",
				"Turno #{0} non trovato.",
				"Nie znaleziono zmiany #{0}.",
				"Зміну #{0} не знайдено.",
				"لم يتم العثور على المناوبة #{0}."),

			["Shift_AlreadySignedUp"] = L(
				"You're already signed up for this shift.",
				"Ya estás inscrito en este turno.",
				"Du är redan anmäld till detta skift.",
				"Sie sind bereits für diese Schicht eingetragen.",
				"Vous êtes déjà inscrit à ce quart.",
				"Sei già iscritto a questo turno.",
				"Jesteś już zapisany na tę zmianę.",
				"Ви вже записані на цю зміну.",
				"أنت مسجّل بالفعل في هذه المناوبة."),

			["Shift_Full"] = L(
				"This shift is already full.",
				"Este turno ya está completo.",
				"Detta skift är redan fullt.",
				"Diese Schicht ist bereits voll.",
				"Ce quart est déjà complet.",
				"Questo turno è già al completo.",
				"Ta zmiana jest już pełna.",
				"Ця зміна вже заповнена.",
				"هذه المناوبة ممتلئة بالفعل."),

			["Shift_SignedUp"] = L(
				"Signed up for the shift on {0}.",
				"Te has inscrito en el turno del {0}.",
				"Du har anmält dig till skiftet den {0}.",
				"Für die Schicht am {0} eingetragen.",
				"Inscrit au quart du {0}.",
				"Iscritto al turno del {0}.",
				"Zapisano na zmianę w dniu {0}.",
				"Записано на зміну {0}.",
				"تم التسجيل في مناوبة يوم {0}."),

			["Shift_ErrorSignup"] = L(
				"Error signing up for the shift.",
				"Error al inscribirse en el turno.",
				"Det gick inte att anmäla sig till skiftet.",
				"Fehler beim Eintragen für die Schicht.",
				"Erreur lors de l'inscription au quart.",
				"Errore durante l'iscrizione al turno.",
				"Błąd podczas zapisywania na zmianę.",
				"Помилка запису на зміну.",
				"حدث خطأ أثناء التسجيل في المناوبة."),

			["Shift_SpecifyDrop"] = L(
				"Please specify the shift day number to drop (e.g., 'drop shift 5').",
				"Indica el número del día de turno que quieres dejar (p. ej., 'drop shift 5').",
				"Ange numret på skiftdagen du vill lämna (t.ex. 'drop shift 5').",
				"Bitte geben Sie die Nummer des Schichttags an, den Sie abgeben möchten (z. B. 'drop shift 5').",
				"Veuillez indiquer le numéro du jour de quart à abandonner (p. ex. 'drop shift 5').",
				"Specifica il numero del giorno di turno da lasciare (es. 'drop shift 5').",
				"Podaj numer dnia zmiany do rezygnacji (np. 'drop shift 5').",
				"Вкажіть номер дня зміни, від якої відмовляєтесь (напр., 'drop shift 5').",
				"حدد رقم يوم المناوبة التي تريد التخلي عنها (مثال: 'drop shift 5')."),

			["Shift_NotSignedUp"] = L(
				"You're not signed up for this shift.",
				"No estás inscrito en este turno.",
				"Du är inte anmäld till detta skift.",
				"Sie sind nicht für diese Schicht eingetragen.",
				"Vous n'êtes pas inscrit à ce quart.",
				"Non sei iscritto a questo turno.",
				"Nie jesteś zapisany na tę zmianę.",
				"Ви не записані на цю зміну.",
				"أنت غير مسجّل في هذه المناوبة."),

			["Shift_NoDropPermission"] = L(
				"You don't have permission to drop this shift.",
				"No tienes permiso para dejar este turno.",
				"Du har inte behörighet att lämna detta skift.",
				"Sie haben keine Berechtigung, diese Schicht abzugeben.",
				"Vous n'avez pas la permission d'abandonner ce quart.",
				"Non hai il permesso di lasciare questo turno.",
				"Nie masz uprawnień do rezygnacji z tej zmiany.",
				"У вас немає дозволу відмовитися від цієї зміни.",
				"ليس لديك إذن للتخلي عن هذه المناوبة."),

			["Shift_Dropped"] = L(
				"Dropped your shift on {0}.",
				"Has dejado tu turno del {0}.",
				"Du har lämnat ditt skift den {0}.",
				"Ihre Schicht am {0} wurde abgegeben.",
				"Vous avez abandonné votre quart du {0}.",
				"Hai lasciato il tuo turno del {0}.",
				"Zrezygnowano ze zmiany w dniu {0}.",
				"Ви відмовилися від зміни {0}.",
				"تم التخلي عن مناوبتك يوم {0}."),

			["Shift_ErrorDrop"] = L(
				"Error dropping the shift.",
				"Error al dejar el turno.",
				"Det gick inte att lämna skiftet.",
				"Fehler beim Abgeben der Schicht.",
				"Erreur lors de l'abandon du quart.",
				"Errore durante l'abbandono del turno.",
				"Błąd podczas rezygnacji ze zmiany.",
				"Помилка відмови від зміни.",
				"حدث خطأ أثناء التخلي عن المناوبة."),

			// === Set unit status handler ('units'/YES/NO/command examples stay English) ===
			["Unit_SetUsage"] = L(
				"Usage: 'set unit [name] to [status]'.",
				"Uso: 'set unit [name] to [status]'.",
				"Användning: 'set unit [name] to [status]'.",
				"Verwendung: 'set unit [name] to [status]'.",
				"Utilisation : 'set unit [name] to [status]'.",
				"Uso: 'set unit [name] to [status]'.",
				"Użycie: 'set unit [name] to [status]'.",
				"Використання: 'set unit [name] to [status]'.",
				"الاستخدام: 'set unit [name] to [status]'."),

			["Unit_NotFound"] = L(
				"Unit '{0}' not found. Use 'units' to see current unit statuses.",
				"Unidad '{0}' no encontrada. Usa 'units' para ver los estados actuales de las unidades.",
				"Enheten '{0}' hittades inte. Använd 'units' för att se aktuella enhetsstatusar.",
				"Einheit '{0}' nicht gefunden. Verwenden Sie 'units', um die aktuellen Einheitenstatus zu sehen.",
				"Unité '{0}' introuvable. Utilisez 'units' pour voir les statuts actuels des unités.",
				"Unità '{0}' non trovata. Usa 'units' per vedere gli stati attuali delle unità.",
				"Nie znaleziono jednostki '{0}'. Użyj 'units', aby zobaczyć aktualne statusy jednostek.",
				"Підрозділ '{0}' не знайдено. Введіть 'units', щоб переглянути поточні статуси підрозділів.",
				"لم يتم العثور على الوحدة '{0}'. استخدم 'units' لعرض حالات الوحدات الحالية."),

			["Unit_NoPermission"] = L(
				"You don't have permission to modify this unit.",
				"No tienes permiso para modificar esta unidad.",
				"Du har inte behörighet att ändra denna enhet.",
				"Sie haben keine Berechtigung, diese Einheit zu ändern.",
				"Vous n'avez pas la permission de modifier cette unité.",
				"Non hai il permesso di modificare questa unità.",
				"Nie masz uprawnień do modyfikacji tej jednostki.",
				"У вас немає дозволу змінювати цей підрозділ.",
				"ليس لديك إذن لتعديل هذه الوحدة."),

			["Unit_UnknownStatus"] = L(
				"Unknown unit status '{0}'. Use 'units' to see current unit statuses.",
				"Estado de unidad desconocido '{0}'. Usa 'units' para ver los estados actuales de las unidades.",
				"Okänd enhetsstatus '{0}'. Använd 'units' för att se aktuella enhetsstatusar.",
				"Unbekannter Einheitenstatus '{0}'. Verwenden Sie 'units', um die aktuellen Einheitenstatus zu sehen.",
				"Statut d'unité inconnu '{0}'. Utilisez 'units' pour voir les statuts actuels des unités.",
				"Stato dell'unità sconosciuto '{0}'. Usa 'units' per vedere gli stati attuali delle unità.",
				"Nieznany status jednostki '{0}'. Użyj 'units', aby zobaczyć aktualne statusy jednostek.",
				"Невідомий статус підрозділу '{0}'. Введіть 'units', щоб переглянути поточні статуси.",
				"حالة وحدة غير معروفة '{0}'. استخدم 'units' لعرض حالات الوحدات الحالية."),

			["Unit_SetConfirm"] = L(
				"Set {0} to {1}? Reply YES to confirm or NO to cancel.",
				"¿Establecer {0} en {1}? Responde YES para confirmar o NO para cancelar.",
				"Ställa in {0} till {1}? Svara YES för att bekräfta eller NO för att avbryta.",
				"{0} auf {1} setzen? Antworten Sie YES zum Bestätigen oder NO zum Abbrechen.",
				"Définir {0} sur {1} ? Répondez YES pour confirmer ou NO pour annuler.",
				"Impostare {0} su {1}? Rispondi YES per confermare o NO per annullare.",
				"Ustawić {0} na {1}? Odpowiedz YES, aby potwierdzić, lub NO, aby anulować.",
				"Встановити {0} на {1}? Відповідайте YES для підтвердження або NO для скасування.",
				"تعيين {0} إلى {1}؟ أرسل YES للتأكيد أو NO للإلغاء."),

			["Unit_SetDone"] = L(
				"{0} set to {1}.",
				"{0} establecido en {1}.",
				"{0} inställd på {1}.",
				"{0} auf {1} gesetzt.",
				"{0} défini sur {1}.",
				"{0} impostato su {1}.",
				"Ustawiono {0} na {1}.",
				"{0} встановлено на {1}.",
				"تم تعيين {0} إلى {1}."),

			["Unit_ErrorSet"] = L(
				"Error setting the unit status.",
				"Error al establecer el estado de la unidad.",
				"Det gick inte att ställa in enhetsstatusen.",
				"Fehler beim Festlegen des Einheitenstatus.",
				"Erreur lors de la définition du statut de l'unité.",
				"Errore durante l'impostazione dello stato dell'unità.",
				"Błąd podczas ustawiania statusu jednostki.",
				"Помилка встановлення статусу підрозділу.",
				"حدث خطأ أثناء تعيين حالة الوحدة."),

			// === Weather alert handler ===
			["Weather_NoAlerts"] = L(
				"No active weather alerts for your area.",
				"No hay alertas meteorológicas activas para tu zona.",
				"Inga aktiva vädervarningar för ditt område.",
				"Keine aktiven Wetterwarnungen für Ihr Gebiet.",
				"Aucune alerte météo active pour votre région.",
				"Nessuna allerta meteo attiva per la tua zona.",
				"Brak aktywnych ostrzeżeń pogodowych dla Twojego obszaru.",
				"Немає активних погодних попереджень для вашого регіону.",
				"لا توجد تنبيهات طقس نشطة لمنطقتك."),

			["Weather_Header"] = L(
				"Active Weather Alerts:",
				"Alertas meteorológicas activas:",
				"Aktiva vädervarningar:",
				"Aktive Wetterwarnungen:",
				"Alertes météo actives :",
				"Allerte meteo attive:",
				"Aktywne ostrzeżenia pogodowe:",
				"Активні погодні попередження:",
				"تنبيهات الطقس النشطة:"),

			["Weather_AlertItem"] = EnOnly("- {0}"),

			["Weather_Effective"] = L(
				"  Effective: {0} UTC",
				"  Vigente: {0} UTC",
				"  Gäller: {0} UTC",
				"  Gültig ab: {0} UTC",
				"  En vigueur : {0} UTC",
				"  In vigore: {0} UTC",
				"  Obowiązuje: {0} UTC",
				"  Чинне: {0} UTC",
				"  ساري: {0} UTC"),

			["Weather_Expires"] = L(
				"  Expires: {0} UTC",
				"  Caduca: {0} UTC",
				"  Upphör: {0} UTC",
				"  Läuft ab: {0} UTC",
				"  Expire : {0} UTC",
				"  Scade: {0} UTC",
				"  Wygasa: {0} UTC",
				"  Закінчується: {0} UTC",
				"  ينتهي: {0} UTC"),

			["Weather_Error"] = L(
				"Error retrieving weather alerts.",
				"Error al recuperar las alertas meteorológicas.",
				"Det gick inte att hämta vädervarningar.",
				"Fehler beim Abrufen der Wetterwarnungen.",
				"Erreur lors de la récupération des alertes météo.",
				"Errore durante il recupero delle allerte meteo.",
				"Błąd podczas pobierania ostrzeżeń pogodowych.",
				"Помилка отримання погодних попереджень.",
				"حدث خطأ أثناء جلب تنبيهات الطقس."),

			// === Personnel handler ===
			["Personnel_NoPermission"] = L(
				"You don't have permission to view personnel for your department.",
				"No tienes permiso para ver el personal de tu departamento.",
				"Du har inte behörighet att se personal för din avdelning.",
				"Sie haben keine Berechtigung, das Personal Ihrer Abteilung anzuzeigen.",
				"Vous n'avez pas la permission de voir le personnel de votre département.",
				"Non hai il permesso di visualizzare il personale del tuo dipartimento.",
				"Nie masz uprawnień do wyświetlania personelu swojego działu.",
				"У вас немає дозволу переглядати персонал вашого підрозділу.",
				"ليس لديك إذن لعرض أفراد قسمك."),

			["Personnel_None"] = L(
				"No personnel found for your department.",
				"No se encontró personal para tu departamento.",
				"Ingen personal hittades för din avdelning.",
				"Kein Personal für Ihre Abteilung gefunden.",
				"Aucun personnel trouvé pour votre département.",
				"Nessun personale trovato per il tuo dipartimento.",
				"Nie znaleziono personelu dla Twojego działu.",
				"Персонал для вашого підрозділу не знайдено.",
				"لم يتم العثور على أفراد لقسمك."),

			["Personnel_NoMatch"] = L(
				"No personnel found matching '{0}'.",
				"No se encontró personal que coincida con '{0}'.",
				"Ingen personal matchade '{0}'.",
				"Kein Personal gefunden, das zu '{0}' passt.",
				"Aucun personnel correspondant à '{0}'.",
				"Nessun personale corrispondente a '{0}'.",
				"Nie znaleziono personelu pasującego do '{0}'.",
				"Не знайдено персоналу за запитом '{0}'.",
				"لم يتم العثور على أفراد يطابقون '{0}'."),

			["Personnel_HeaderQuery"] = L(
				"Personnel matching '{0}':",
				"Personal que coincide con '{0}':",
				"Personal som matchar '{0}':",
				"Personal, das zu '{0}' passt:",
				"Personnel correspondant à '{0}' :",
				"Personale corrispondente a '{0}':",
				"Personel pasujący do '{0}':",
				"Персонал за запитом '{0}':",
				"الأفراد المطابقون لـ '{0}':"),

			["Personnel_Header"] = L(
				"Personnel Status:",
				"Estado del personal:",
				"Personalstatus:",
				"Personalstatus:",
				"Statut du personnel :",
				"Stato del personale:",
				"Status personelu:",
				"Статус персоналу:",
				"حالة الأفراد:"),

			["Personnel_Line"] = EnOnly("{0}, {1}: {2} / {3}"),

			["Personnel_Unknown"] = L(
				"Unknown",
				"Desconocido",
				"Okänd",
				"Unbekannt",
				"Inconnu",
				"Sconosciuto",
				"Nieznany",
				"Невідомо",
				"غير معروف"),

			["Personnel_NA"] = EnOnly("N/A"),

			["Personnel_Error"] = L(
				"Error retrieving personnel.",
				"Error al recuperar el personal.",
				"Det gick inte att hämta personal.",
				"Fehler beim Abrufen des Personals.",
				"Erreur lors de la récupération du personnel.",
				"Errore durante il recupero del personale.",
				"Błąd podczas pobierania personelu.",
				"Помилка отримання персоналу.",
				"حدث خطأ أثناء جلب الأفراد."),

			// === Status / Staffing / MyStatus handlers (HELP token stays English) ===
			["Status_CouldNotDetermine"] = L(
				"Could not determine status to set. Text HELP for available commands.",
				"No se pudo determinar el estado a establecer. Escribe HELP para ver los comandos disponibles.",
				"Det gick inte att avgöra vilken status som ska anges. Skriv HELP för tillgängliga kommandon.",
				"Der zu setzende Status konnte nicht ermittelt werden. Senden Sie HELP für verfügbare Befehle.",
				"Impossible de déterminer le statut à définir. Tapez HELP pour les commandes disponibles.",
				"Impossibile determinare lo stato da impostare. Scrivi HELP per i comandi disponibili.",
				"Nie można określić statusu do ustawienia. Wpisz HELP, aby zobaczyć dostępne polecenia.",
				"Не вдалося визначити статус. Введіть HELP для списку команд.",
				"تعذّر تحديد الحالة المراد تعيينها. أرسل HELP لعرض الأوامر المتاحة."),

			["Status_Updated"] = L(
				"Status updated to: {0}",
				"Estado actualizado a: {0}",
				"Status uppdaterad till: {0}",
				"Status aktualisiert auf: {0}",
				"Statut mis à jour : {0}",
				"Stato aggiornato a: {0}",
				"Status zaktualizowano na: {0}",
				"Статус оновлено на: {0}",
				"تم تحديث الحالة إلى: {0}"),

			["Status_Error"] = L(
				"Error updating status. Please try again.",
				"Error al actualizar el estado. Inténtalo de nuevo.",
				"Det gick inte att uppdatera statusen. Försök igen.",
				"Fehler beim Aktualisieren des Status. Bitte versuchen Sie es erneut.",
				"Erreur lors de la mise à jour du statut. Veuillez réessayer.",
				"Errore durante l'aggiornamento dello stato. Riprova.",
				"Błąd podczas aktualizacji statusu. Spróbuj ponownie.",
				"Помилка оновлення статусу. Спробуйте ще раз.",
				"حدث خطأ أثناء تحديث الحالة. يرجى المحاولة مرة أخرى."),

			["Staffing_CouldNotDetermine"] = L(
				"Could not determine staffing level to set. Text HELP for available commands.",
				"No se pudo determinar el nivel de personal a establecer. Escribe HELP para ver los comandos disponibles.",
				"Det gick inte att avgöra bemanningsnivån. Skriv HELP för tillgängliga kommandon.",
				"Der zu setzende Personalstatus konnte nicht ermittelt werden. Senden Sie HELP für verfügbare Befehle.",
				"Impossible de déterminer le niveau d'effectif à définir. Tapez HELP pour les commandes disponibles.",
				"Impossibile determinare il livello di organico da impostare. Scrivi HELP per i comandi disponibili.",
				"Nie można określić poziomu obsady. Wpisz HELP, aby zobaczyć dostępne polecenia.",
				"Не вдалося визначити рівень укомплектування. Введіть HELP для списку команд.",
				"تعذّر تحديد مستوى التوظيف. أرسل HELP لعرض الأوامر المتاحة."),

			["Staffing_Updated"] = L(
				"Staffing level updated to: {0}",
				"Nivel de personal actualizado a: {0}",
				"Bemanningsnivå uppdaterad till: {0}",
				"Personalstatus aktualisiert auf: {0}",
				"Niveau d'effectif mis à jour : {0}",
				"Livello di organico aggiornato a: {0}",
				"Poziom obsady zaktualizowano na: {0}",
				"Рівень укомплектування оновлено на: {0}",
				"تم تحديث مستوى التوظيف إلى: {0}"),

			["Staffing_Error"] = L(
				"Error updating staffing. Please try again.",
				"Error al actualizar el personal. Inténtalo de nuevo.",
				"Det gick inte att uppdatera bemanningen. Försök igen.",
				"Fehler beim Aktualisieren des Personalstatus. Bitte versuchen Sie es erneut.",
				"Erreur lors de la mise à jour de l'effectif. Veuillez réessayer.",
				"Errore durante l'aggiornamento dell'organico. Riprova.",
				"Błąd podczas aktualizacji obsady. Spróbuj ponownie.",
				"Помилка оновлення укомплектування. Спробуйте ще раз.",
				"حدث خطأ أثناء تحديث التوظيف. يرجى المحاولة مرة أخرى."),

			["MyStatus_Summary"] = L(
				"Hello {0} at {1}.\nStatus: {2}\nStaffing: {3}",
				"Hola {0}, {1}.\nEstado: {2}\nPersonal: {3}",
				"Hej {0}, {1}.\nStatus: {2}\nBemanning: {3}",
				"Hallo {0}, {1}.\nStatus: {2}\nPersonal: {3}",
				"Bonjour {0}, {1}.\nStatut : {2}\nEffectif : {3}",
				"Ciao {0}, {1}.\nStato: {2}\nOrganico: {3}",
				"Cześć {0}, {1}.\nStatus: {2}\nObsada: {3}",
				"Привіт, {0}, {1}.\nСтатус: {2}\nУкомплектування: {3}",
				"مرحبًا {0}، {1}.\nالحالة: {2}\nالتوظيف: {3}"),

			["MyStatus_Error"] = L(
				"Error retrieving your status.",
				"Error al recuperar tu estado.",
				"Det gick inte att hämta din status.",
				"Fehler beim Abrufen Ihres Status.",
				"Erreur lors de la récupération de votre statut.",
				"Errore durante il recupero del tuo stato.",
				"Błąd podczas pobierania Twojego statusu.",
				"Помилка отримання вашого статусу.",
				"حدث خطأ أثناء جلب حالتك."),

			// === Shared ===
			["Common_YourDepartment"] = L(
				"your department",
				"tu departamento",
				"din avdelning",
				"Ihre Abteilung",
				"votre département",
				"il tuo dipartimento",
				"Twój dział",
				"ваш підрозділ",
				"قسمك"),

			// === Calls list handler ===
			["Calls_NoActive"] = L(
				"No active calls for {0}.",
				"No hay llamadas activas para {0}.",
				"Inga aktiva larm för {0}.",
				"Keine aktiven Einsätze für {0}.",
				"Aucun appel actif pour {0}.",
				"Nessuna chiamata attiva per {0}.",
				"Brak aktywnych zgłoszeń dla {0}.",
				"Немає активних викликів для {0}.",
				"لا توجد بلاغات نشطة لـ {0}."),

			["Calls_Header"] = L(
				"Active Calls for {0}:",
				"Llamadas activas para {0}:",
				"Aktiva larm för {0}:",
				"Aktive Einsätze für {0}:",
				"Appels actifs pour {0} :",
				"Chiamate attive per {0}:",
				"Aktywne zgłoszenia dla {0}:",
				"Активні виклики для {0}:",
				"البلاغات النشطة لـ {0}:"),

			["Calls_Line"] = EnOnly("Call#{0} {1} - {2}"),

			["Calls_Error"] = L(
				"Error retrieving calls. Please try again.",
				"Error al recuperar las llamadas. Inténtalo de nuevo.",
				"Det gick inte att hämta larm. Försök igen.",
				"Fehler beim Abrufen der Einsätze. Bitte versuchen Sie es erneut.",
				"Erreur lors de la récupération des appels. Veuillez réessayer.",
				"Errore durante il recupero delle chiamate. Riprova.",
				"Błąd podczas pobierania zgłoszeń. Spróbuj ponownie.",
				"Помилка отримання викликів. Спробуйте ще раз.",
				"حدث خطأ أثناء جلب البلاغات. يرجى المحاولة مرة أخرى."),

			// === Units list handler ===
			["Units_None"] = L(
				"No units found for your department.",
				"No se encontraron unidades para tu departamento.",
				"Inga enheter hittades för din avdelning.",
				"Keine Einheiten für Ihre Abteilung gefunden.",
				"Aucune unité trouvée pour votre département.",
				"Nessuna unità trovata per il tuo dipartimento.",
				"Nie znaleziono jednostek dla Twojego działu.",
				"Підрозділів для вашого відділу не знайдено.",
				"لم يتم العثور على وحدات لقسمك."),

			["Units_Header"] = L(
				"Unit Statuses for {0}:",
				"Estados de unidades para {0}:",
				"Enhetsstatusar för {0}:",
				"Einheitenstatus für {0}:",
				"Statuts des unités pour {0} :",
				"Stati delle unità per {0}:",
				"Statusy jednostek dla {0}:",
				"Статуси підрозділів для {0}:",
				"حالات الوحدات لـ {0}:"),

			["Units_Line"] = EnOnly("{0}: {1}"),

			["Units_Error"] = L(
				"Error retrieving unit statuses.",
				"Error al recuperar los estados de las unidades.",
				"Det gick inte att hämta enhetsstatusar.",
				"Fehler beim Abrufen der Einheitenstatus.",
				"Erreur lors de la récupération des statuts des unités.",
				"Errore durante il recupero degli stati delle unità.",
				"Błąd podczas pobierania statusów jednostek.",
				"Помилка отримання статусів підрозділів.",
				"حدث خطأ أثناء جلب حالات الوحدات."),

			// === Call detail handler (C1445 example stays English) ===
			["CallDetail_Specify"] = L(
				"Please specify a call number, e.g., C1445",
				"Indica un número de llamada, p. ej., C1445",
				"Ange ett larmnummer, t.ex. C1445",
				"Bitte geben Sie eine Einsatznummer an, z. B. C1445",
				"Veuillez indiquer un numéro d'appel, p. ex. C1445",
				"Specifica un numero di chiamata, es. C1445",
				"Podaj numer zgłoszenia, np. C1445",
				"Вкажіть номер виклику, напр., C1445",
				"يرجى تحديد رقم البلاغ، مثال: C1445"),

			["CallDetail_NoPermission"] = L(
				"You don't have permission to view this call.",
				"No tienes permiso para ver esta llamada.",
				"Du har inte behörighet att se detta larm.",
				"Sie haben keine Berechtigung, diesen Einsatz anzuzeigen.",
				"Vous n'avez pas la permission de voir cet appel.",
				"Non hai il permesso di visualizzare questa chiamata.",
				"Nie masz uprawnień do wyświetlenia tego zgłoszenia.",
				"У вас немає дозволу переглядати цей виклик.",
				"ليس لديك إذن لعرض هذا البلاغ."),

			["CallDetail_Header"] = L(
				"Call #{0}: {1}",
				"Llamada #{0}: {1}",
				"Larm #{0}: {1}",
				"Einsatz #{0}: {1}",
				"Appel #{0} : {1}",
				"Chiamata #{0}: {1}",
				"Zgłoszenie #{0}: {1}",
				"Виклик #{0}: {1}",
				"بلاغ #{0}: {1}"),

			["CallDetail_Priority"] = L(
				"Priority: {0}",
				"Prioridad: {0}",
				"Prioritet: {0}",
				"Priorität: {0}",
				"Priorité : {0}",
				"Priorità: {0}",
				"Priorytet: {0}",
				"Пріоритет: {0}",
				"الأولوية: {0}"),

			["CallDetail_Nature"] = L(
				"Nature: {0}",
				"Naturaleza: {0}",
				"Typ: {0}",
				"Art: {0}",
				"Nature : {0}",
				"Natura: {0}",
				"Rodzaj: {0}",
				"Характер: {0}",
				"الطبيعة: {0}"),

			["CallDetail_Address"] = L(
				"Address: {0}",
				"Dirección: {0}",
				"Adress: {0}",
				"Adresse: {0}",
				"Adresse : {0}",
				"Indirizzo: {0}",
				"Adres: {0}",
				"Адреса: {0}",
				"العنوان: {0}"),

			["CallDetail_Logged"] = L(
				"Logged: {0}",
				"Registrado: {0}",
				"Loggat: {0}",
				"Erfasst: {0}",
				"Enregistré : {0}",
				"Registrato: {0}",
				"Zarejestrowano: {0}",
				"Зареєстровано: {0}",
				"سُجِّل: {0}"),

			["CallDetail_Error"] = L(
				"Error retrieving call details.",
				"Error al recuperar los detalles de la llamada.",
				"Det gick inte att hämta larmdetaljer.",
				"Fehler beim Abrufen der Einsatzdetails.",
				"Erreur lors de la récupération des détails de l'appel.",
				"Errore durante il recupero dei dettagli della chiamata.",
				"Błąd podczas pobierania szczegółów zgłoszenia.",
				"Помилка отримання деталей виклику.",
				"حدث خطأ أثناء جلب تفاصيل البلاغ."),

			// === Department handler ('departments'/'switch to department [...]' commands stay English) ===
			["Dept_UnknownCommand"] = L(
				"Unknown department command.",
				"Comando de departamento desconocido.",
				"Okänt avdelningskommando.",
				"Unbekannter Abteilungsbefehl.",
				"Commande de département inconnue.",
				"Comando di dipartimento sconosciuto.",
				"Nieznane polecenie działu.",
				"Невідома команда підрозділу.",
				"أمر قسم غير معروف."),

			["Dept_NoMembership"] = L(
				"You are not a member of any department.",
				"No eres miembro de ningún departamento.",
				"Du är inte medlem i någon avdelning.",
				"Sie sind kein Mitglied einer Abteilung.",
				"Vous n'êtes membre d'aucun département.",
				"Non sei membro di alcun dipartimento.",
				"Nie należysz do żadnego działu.",
				"Ви не є членом жодного підрозділу.",
				"أنت لست عضوًا في أي قسم."),

			["Dept_NoActiveMemberships"] = L(
				"You have no active department memberships.",
				"No tienes membresías de departamento activas.",
				"Du har inga aktiva avdelningsmedlemskap.",
				"Sie haben keine aktiven Abteilungsmitgliedschaften.",
				"Vous n'avez aucune adhésion active à un département.",
				"Non hai appartenenze a dipartimenti attive.",
				"Nie masz aktywnych członkostw w działach.",
				"У вас немає активних членств у підрозділах.",
				"ليس لديك عضويات أقسام نشطة."),

			["Dept_YourDepartments"] = L(
				"Your departments:",
				"Tus departamentos:",
				"Dina avdelningar:",
				"Ihre Abteilungen:",
				"Vos départements :",
				"I tuoi dipartimenti:",
				"Twoje działy:",
				"Ваші підрозділи:",
				"أقسامك:"),

			["Dept_ListItem"] = EnOnly("{0}. {1}{2}"),

			["Dept_ActiveMarker"] = L(
				" [ACTIVE]",
				" [ACTIVO]",
				" [AKTIV]",
				" [AKTIV]",
				" [ACTIF]",
				" [ATTIVO]",
				" [AKTYWNY]",
				" [АКТИВНИЙ]",
				" [نشط]"),

			["Dept_DepartmentNum"] = L(
				"Department #{0}",
				"Departamento #{0}",
				"Avdelning #{0}",
				"Abteilung #{0}",
				"Département #{0}",
				"Dipartimento #{0}",
				"Dział #{0}",
				"Підрозділ #{0}",
				"القسم #{0}"),

			["Dept_SwitchHint"] = L(
				"To switch departments, type: switch to department [name or number]",
				"Para cambiar de departamento, escribe: switch to department [name or number]",
				"För att byta avdelning, skriv: switch to department [name or number]",
				"Um die Abteilung zu wechseln, geben Sie ein: switch to department [name or number]",
				"Pour changer de département, tapez : switch to department [name or number]",
				"Per cambiare dipartimento, scrivi: switch to department [name or number]",
				"Aby zmienić dział, wpisz: switch to department [name or number]",
				"Щоб змінити підрозділ, введіть: switch to department [name or number]",
				"لتبديل القسم، اكتب: switch to department [name or number]"),

			["Dept_ErrorList"] = L(
				"Error retrieving your departments.",
				"Error al recuperar tus departamentos.",
				"Det gick inte att hämta dina avdelningar.",
				"Fehler beim Abrufen Ihrer Abteilungen.",
				"Erreur lors de la récupération de vos départements.",
				"Errore durante il recupero dei tuoi dipartimenti.",
				"Błąd podczas pobierania Twoich działów.",
				"Помилка отримання ваших підрозділів.",
				"حدث خطأ أثناء جلب أقسامك."),

			["Dept_NoActiveSet"] = L(
				"You don't have an active department set. Type 'departments' to see your options.",
				"No tienes un departamento activo. Escribe 'departments' para ver tus opciones.",
				"Du har ingen aktiv avdelning. Skriv 'departments' för att se dina alternativ.",
				"Sie haben keine aktive Abteilung. Geben Sie 'departments' ein, um Ihre Optionen zu sehen.",
				"Vous n'avez pas de département actif. Tapez 'departments' pour voir vos options.",
				"Non hai un dipartimento attivo. Scrivi 'departments' per vedere le opzioni.",
				"Nie masz aktywnego działu. Wpisz 'departments', aby zobaczyć opcje.",
				"У вас немає активного підрозділу. Введіть 'departments', щоб переглянути варіанти.",
				"ليس لديك قسم نشط. اكتب 'departments' لعرض خياراتك."),

			["Dept_ActiveIs"] = L(
				"Your {0} department is: {1}{2}.",
				"Tu departamento {0} es: {1}{2}.",
				"Din {0} avdelning är: {1}{2}.",
				"Ihre {0} Abteilung ist: {1}{2}.",
				"Votre département {0} est : {1}{2}.",
				"Il tuo dipartimento {0} è: {1}{2}.",
				"Twój {0} dział to: {1}{2}.",
				"Ваш {0} підрозділ: {1}{2}.",
				"قسمك {0} هو: {1}{2}."),

			["Dept_Active"] = L(
				"active", "activo", "aktiva", "aktive", "actif", "attivo", "aktywny", "активний", "النشط"),

			["Dept_NotActive"] = L(
				"not active", "no activo", "inte aktiva", "nicht aktive", "non actif", "non attivo", "nieaktywny", "неактивний", "غير النشط"),

			["Dept_DefaultMarker"] = L(
				" (your default)",
				" (tu predeterminado)",
				" (din standard)",
				" (Ihre Standardabteilung)",
				" (votre département par défaut)",
				" (il tuo predefinito)",
				" (Twój domyślny)",
				" (ваш за замовчуванням)",
				" (الافتراضي لديك)"),

			["Dept_ErrorActive"] = L(
				"Error retrieving your active department.",
				"Error al recuperar tu departamento activo.",
				"Det gick inte att hämta din aktiva avdelning.",
				"Fehler beim Abrufen Ihrer aktiven Abteilung.",
				"Erreur lors de la récupération de votre département actif.",
				"Errore durante il recupero del tuo dipartimento attivo.",
				"Błąd podczas pobierania aktywnego działu.",
				"Помилка отримання активного підрозділу.",
				"حدث خطأ أثناء جلب قسمك النشط."),

			["Dept_SwitchSpecify"] = L(
				"Please specify the department name or number to switch to.\nType 'departments' to see your list.",
				"Indica el nombre o número del departamento al que cambiar.\nEscribe 'departments' para ver tu lista.",
				"Ange namnet eller numret på avdelningen att byta till.\nSkriv 'departments' för att se din lista.",
				"Bitte geben Sie den Namen oder die Nummer der Abteilung an.\nGeben Sie 'departments' ein, um Ihre Liste zu sehen.",
				"Veuillez indiquer le nom ou le numéro du département.\nTapez 'departments' pour voir votre liste.",
				"Specifica il nome o il numero del dipartimento.\nScrivi 'departments' per vedere l'elenco.",
				"Podaj nazwę lub numer działu.\nWpisz 'departments', aby zobaczyć listę.",
				"Вкажіть назву або номер підрозділу.\nВведіть 'departments', щоб переглянути список.",
				"حدد اسم أو رقم القسم للتبديل إليه.\nاكتب 'departments' لعرض قائمتك."),

			["Dept_SwitchNotFound"] = L(
				"Couldn't find a department matching \"{0}\".\nType 'departments' to see your list.",
				"No se encontró un departamento que coincida con \"{0}\".\nEscribe 'departments' para ver tu lista.",
				"Hittade ingen avdelning som matchar \"{0}\".\nSkriv 'departments' för att se din lista.",
				"Keine Abteilung gefunden, die zu \"{0}\" passt.\nGeben Sie 'departments' ein, um Ihre Liste zu sehen.",
				"Aucun département correspondant à \"{0}\".\nTapez 'departments' pour voir votre liste.",
				"Nessun dipartimento corrispondente a \"{0}\".\nScrivi 'departments' per vedere l'elenco.",
				"Nie znaleziono działu pasującego do \"{0}\".\nWpisz 'departments', aby zobaczyć listę.",
				"Не знайдено підрозділу за запитом \"{0}\".\nВведіть 'departments', щоб переглянути список.",
				"لم يتم العثور على قسم يطابق \"{0}\".\nاكتب 'departments' لعرض قائمتك."),

			["Dept_AlreadyActive"] = L(
				"Your active department is already {0}.",
				"Tu departamento activo ya es {0}.",
				"Din aktiva avdelning är redan {0}.",
				"Ihre aktive Abteilung ist bereits {0}.",
				"Votre département actif est déjà {0}.",
				"Il tuo dipartimento attivo è già {0}.",
				"Twój aktywny dział to już {0}.",
				"Ваш активний підрозділ вже {0}.",
				"قسمك النشط هو بالفعل {0}."),

			["Dept_Switched"] = L(
				"Switched to {0}.",
				"Cambiado a {0}.",
				"Bytte till {0}.",
				"Zu {0} gewechselt.",
				"Basculé vers {0}.",
				"Passato a {0}.",
				"Przełączono na {0}.",
				"Перемкнено на {0}.",
				"تم التبديل إلى {0}."),

			["Dept_SwitchFailed"] = L(
				"Failed to switch departments. Please try again or contact your administrator.",
				"No se pudo cambiar de departamento. Inténtalo de nuevo o contacta a tu administrador.",
				"Det gick inte att byta avdelning. Försök igen eller kontakta din administratör.",
				"Abteilungswechsel fehlgeschlagen. Bitte versuchen Sie es erneut oder wenden Sie sich an Ihren Administrator.",
				"Échec du changement de département. Veuillez réessayer ou contacter votre administrateur.",
				"Impossibile cambiare dipartimento. Riprova o contatta l'amministratore.",
				"Nie udało się zmienić działu. Spróbuj ponownie lub skontaktuj się z administratorem.",
				"Не вдалося змінити підрозділ. Спробуйте ще раз або зверніться до адміністратора.",
				"تعذّر تبديل القسم. حاول مرة أخرى أو اتصل بالمسؤول."),

			["Dept_ErrorSwitch"] = L(
				"Error switching departments.",
				"Error al cambiar de departamento.",
				"Det gick inte att byta avdelning.",
				"Fehler beim Wechseln der Abteilung.",
				"Erreur lors du changement de département.",
				"Errore durante il cambio di dipartimento.",
				"Błąd podczas zmiany działu.",
				"Помилка зміни підрозділу.",
				"حدث خطأ أثناء تبديل القسم."),
		};
	}
}
