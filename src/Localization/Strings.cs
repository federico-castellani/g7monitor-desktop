using System;
using System.Globalization;
using G7MonitorDesktop.Services;

namespace G7MonitorDesktop.Localization
{
    /// <summary>
    /// User interface texts. One instance per language; <see cref="Current"/> is the one in use.
    /// Adding a language = one more instance and one more entry in <see cref="Apply"/>.
    /// </summary>
    public sealed class Strings
    {
        public const string Auto = "auto";

        public string Code;
        public CultureInfo Culture;

        // Reading
        public string InRange, Low, High, OldReading;
        public string JustNow, MinutesAgo, HoursMinutesAgo, DateFormat;
        public string NotConnectedToServer;

        // Graph and statistics
        public string Last3Hours, OpenSettingsToConnect, NoReadings3h, WaitingForData;
        public string StatInRange, StatAverage, StatMinMax;

        // Connection status
        public string StatusNotConnected, StatusConnecting, StatusLive, StatusPolling, StatusUnlinked;
        public string StatusOffline, StatusServerError, RetryIn, WidgetSetup;

        // Settings page
        public string Settings, ServerCardTitle, Username, Password, TotpCode;
        public string Connect, Connecting, Linked, LinkedAs, ReadOnlyDevice, Unlink;
        public string LowTarget, HighTarget, LaunchAtStartup, WidgetBackground, ShowOverFullscreen;
        public string Language, LanguageSystem, ExitApp, Save;

        // Linking errors
        public string InvalidServer, EnterUserPassword, TotpSixDigits;
        public string NotG7Server, Unreachable, UnexpectedResponse, Redirected, WrongCredentials;
        public string TotpRequired, WrongTotp, TooManyAttempts, PasswordChangeRequired, TooManyDevices, InvalidData;
        public string ErrorCode, ConnectionFailed;

        // Tray menu and fatal error
        public string MenuOpen, MenuExit, FatalError;

        public static readonly Strings English = new Strings
        {
            Code = "en",
            Culture = CultureInfo.GetCultureInfo("en-GB"),
            InRange = "In range", Low = "Low", High = "High", OldReading = "Old reading",
            JustNow = "Just now", MinutesAgo = "{0} min ago", HoursMinutesAgo = "{0} h {1} min ago", DateFormat = "d MMM HH:mm",
            NotConnectedToServer = "Not connected to a server",
            Last3Hours = "Last 3 hours",
            OpenSettingsToConnect = "Open settings to connect to your G7 Monitor server",
            NoReadings3h = "No readings in the last 3 hours", WaitingForData = "Waiting for data…",
            StatInRange = "In range", StatAverage = "Average", StatMinMax = "Min – Max",
            StatusNotConnected = "Not connected", StatusConnecting = "Connecting…", StatusLive = "Live",
            StatusPolling = "Connected (polling)", StatusUnlinked = "Device unlinked · sign in again",
            StatusOffline = "Offline", StatusServerError = "Server error", RetryIn = "{0} · retry in {1} s",
            WidgetSetup = "G7 · Setup",
            Settings = "Settings", ServerCardTitle = "G7 Monitor server",
            Username = "Username", Password = "Password", TotpCode = "2FA code",
            Connect = "Connect", Connecting = "Connecting…", Linked = "Linked", LinkedAs = "Linked as {0}",
            ReadOnlyDevice = "Read-only device · live updates", Unlink = "Unlink",
            LowTarget = "Low (mg/dL)", HighTarget = "High (mg/dL)",
            LaunchAtStartup = "Launch at startup", WidgetBackground = "Widget background",
            ShowOverFullscreen = "Show over fullscreen apps",
            Language = "Language", LanguageSystem = "System", ExitApp = "Exit app", Save = "Save",
            InvalidServer = "Invalid server address", EnterUserPassword = "Enter username and password",
            TotpSixDigits = "2FA code: 6 digits",
            NotG7Server = "Not a G7 Monitor server", Unreachable = "Server not reachable",
            UnexpectedResponse = "Unexpected server response", Redirected = "Redirect refused: check the address",
            WrongCredentials = "Wrong username or password", TotpRequired = "Enter the 2FA code",
            WrongTotp = "Wrong 2FA code", TooManyAttempts = "Too many attempts, try later",
            PasswordChangeRequired = "Change your password on the website first",
            TooManyDevices = "Too many devices linked", InvalidData = "Invalid data (2FA: 6 digits)",
            ErrorCode = "Error {0}", ConnectionFailed = "Connection failed",
            MenuOpen = "Open", MenuExit = "Exit", FatalError = "Error: {0}",
        };

        public static readonly Strings Italian = new Strings
        {
            Code = "it",
            Culture = CultureInfo.GetCultureInfo("it-IT"),
            InRange = "Nel range", Low = "Bassa", High = "Alta", OldReading = "Lettura vecchia",
            JustNow = "Adesso", MinutesAgo = "{0} min fa", HoursMinutesAgo = "{0} h {1} min fa", DateFormat = "d MMM HH:mm",
            NotConnectedToServer = "Non collegato a un server",
            Last3Hours = "Ultime 3 ore",
            OpenSettingsToConnect = "Apri le impostazioni per collegarti al server G7 Monitor",
            NoReadings3h = "Nessuna lettura nelle ultime 3 ore", WaitingForData = "In attesa dei dati…",
            StatInRange = "Nel range", StatAverage = "Media", StatMinMax = "Min – Max",
            StatusNotConnected = "Non collegato", StatusConnecting = "Connessione…", StatusLive = "In diretta",
            StatusPolling = "Collegato (a intervalli)", StatusUnlinked = "Dispositivo scollegato · accedi di nuovo",
            StatusOffline = "Offline", StatusServerError = "Errore del server", RetryIn = "{0} · nuovo tentativo tra {1} s",
            WidgetSetup = "G7 · Configura",
            Settings = "Impostazioni", ServerCardTitle = "Server G7 Monitor",
            Username = "Nome utente", Password = "Password", TotpCode = "Codice 2FA",
            Connect = "Collega", Connecting = "Connessione…", Linked = "Collegato", LinkedAs = "Collegato come {0}",
            ReadOnlyDevice = "Sola lettura · in diretta", Unlink = "Scollega",
            LowTarget = "Bassa (mg/dL)", HighTarget = "Alta (mg/dL)",
            LaunchAtStartup = "Avvia con Windows", WidgetBackground = "Sfondo del widget",
            ShowOverFullscreen = "Sopra le app a schermo intero",
            Language = "Lingua", LanguageSystem = "Sistema", ExitApp = "Esci", Save = "Salva",
            InvalidServer = "Indirizzo del server non valido", EnterUserPassword = "Inserisci nome utente e password",
            TotpSixDigits = "Codice 2FA: 6 cifre",
            NotG7Server = "Non è un server G7 Monitor", Unreachable = "Server non raggiungibile",
            UnexpectedResponse = "Risposta del server inattesa", Redirected = "Reindirizzamento rifiutato: controlla l'indirizzo",
            WrongCredentials = "Nome utente o password errati", TotpRequired = "Inserisci il codice 2FA",
            WrongTotp = "Codice 2FA errato", TooManyAttempts = "Troppi tentativi, riprova più tardi",
            PasswordChangeRequired = "Prima cambia la password sul sito",
            TooManyDevices = "Troppi dispositivi collegati", InvalidData = "Dati non validi (2FA: 6 cifre)",
            ErrorCode = "Errore {0}", ConnectionFailed = "Connessione non riuscita",
            MenuOpen = "Apri", MenuExit = "Esci", FatalError = "Errore: {0}",
        };

        /// <summary>Languages offered in the settings: setting value and display name (in its own language).</summary>
        public static readonly string[][] Choices =
        {
            new[] { Auto, null },          // display name from Current.LanguageSystem
            new[] { "en", "English" },
            new[] { "it", "Italiano" },
        };

        private static Strings current = English;

        public static Strings Current
        {
            get { return current; }
        }

        /// <summary>"auto" follows the Windows display language (Italian if Windows is in Italian, otherwise English).</summary>
        public static void Apply(string language)
        {
            string code = string.IsNullOrEmpty(language) || language == Auto
                ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                : language;
            current = code == "it" ? Italian : English;
        }

        public string Format(string format, params object[] args)
        {
            return string.Format(Culture, format, args);
        }

        /// <summary>Connection status text; compact = short form for the taskbar widget.</summary>
        public string Status(SyncState state, int retrySeconds, bool compact = false)
        {
            switch (state)
            {
                case SyncState.NotConnected: return StatusNotConnected;
                case SyncState.Live: return StatusLive;
                case SyncState.Polling: return StatusPolling;
                case SyncState.Unlinked: return compact ? Unlink : StatusUnlinked;
                case SyncState.Offline:
                    return compact || retrySeconds <= 0 ? StatusOffline : Format(RetryIn, StatusOffline, retrySeconds);
                case SyncState.ServerError:
                    return compact || retrySeconds <= 0 ? StatusServerError : Format(RetryIn, StatusServerError, retrySeconds);
                default: return StatusConnecting;
            }
        }

        public string PairError(PairError error, string detail)
        {
            switch (error)
            {
                case Services.PairError.NotG7Server: return NotG7Server;
                case Services.PairError.Unreachable: return Unreachable;
                case Services.PairError.UnexpectedResponse: return UnexpectedResponse;
                case Services.PairError.Redirected: return Redirected;
                case Services.PairError.WrongCredentials: return WrongCredentials;
                case Services.PairError.TotpRequired: return TotpRequired;
                case Services.PairError.WrongTotp: return WrongTotp;
                case Services.PairError.TooManyAttempts: return TooManyAttempts;
                case Services.PairError.PasswordChangeRequired: return PasswordChangeRequired;
                case Services.PairError.TooManyDevices: return TooManyDevices;
                case Services.PairError.InvalidData: return InvalidData;
                case Services.PairError.ServerMessage:
                    int code;
                    return int.TryParse(detail, out code) ? Format(ErrorCode, code) : detail;
                default: return ConnectionFailed;
            }
        }
    }
}
