using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WattBar;

/// <summary>Tiny string table: English text is the key; French, German and Italian follow.</summary>
public static class L10n
{
    public enum Lang { Auto, En, Fr, De, It }

    private static Lang? _selected;

    public static Lang Selected
    {
        get
        {
            if (_selected is Lang l) return l;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\WattBar");
                _selected = Enum.TryParse<Lang>(key?.GetValue("Language") as string, ignoreCase: true, out var v) ? v : Lang.Auto;
            }
            catch { _selected = Lang.Auto; }
            return _selected.Value;
        }
        set
        {
            _selected = value;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\WattBar");
            key.SetValue("Language", value.ToString());
        }
    }

    /// <summary>The language in use: the chosen one, or the Windows display language when automatic.</summary>
    public static Lang Effective => Selected == Lang.Auto ? Detect() : Selected;

    public static string Name(Lang l) => l switch
    {
        Lang.Auto => T("Automatic (Windows)"),
        Lang.Fr => "Français",
        Lang.De => "Deutsch",
        Lang.It => "Italiano",
        _ => "English",
    };

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private static Lang Detect()
    {
        try
        {
            return (GetUserDefaultUILanguage() & 0x3FF) switch { 0x0C => Lang.Fr, 0x07 => Lang.De, 0x10 => Lang.It, _ => Lang.En };
        }
        catch { return Lang.En; }
    }

    public static string T(string key)
    {
        int i = Effective switch { Lang.Fr => 0, Lang.De => 1, Lang.It => 2, _ => -1 };
        return i >= 0 && Table.TryGetValue(key, out var t) && i < t.Length ? t[i] : key;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    // [fr, de, it]
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["Show chart"] = ["Afficher le graphique", "Diagramm anzeigen", "Mostra il grafico"],
        ["Who is using it…"] = ["Qui consomme…", "Wer verbraucht…", "Chi consuma…"],
        ["Who is using it"] = ["Qui consomme", "Wer verbraucht", "Chi consuma"],
        ["Theme"] = ["Thème", "Design", "Tema"],
        ["Follow Windows"] = ["Comme Windows", "Wie Windows", "Come Windows"],
        ["Dark"] = ["Sombre", "Dunkel", "Scuro"],
        ["Light"] = ["Clair", "Hell", "Chiaro"],
        ["Language"] = ["Langue", "Sprache", "Lingua"],
        ["Automatic (Windows)"] = ["Automatique (Windows)", "Automatisch (Windows)", "Automatica (Windows)"],
        ["Package readout"] = ["Lecture du package", "Package-Anzeige", "Lettura del package"],
        ["Every second, raw"] = ["Chaque seconde, brut", "Jede Sekunde, roh", "Ogni secondo, grezzo"],
        ["Every {0} s, block average"] = ["Toutes les {0} s, moyenne du bloc", "Alle {0} s, Blockmittel", "Ogni {0} s, media del blocco"],
        ["Start with Windows"] = ["Lancer avec Windows", "Mit Windows starten", "Avvia con Windows"],
        ["About WattBar…"] = ["À propos de WattBar…", "Über WattBar…", "Informazioni su WattBar…"],
        ["About WattBar"] = ["À propos de WattBar", "Über WattBar", "Informazioni su WattBar"],
        ["Exit"] = ["Quitter", "Beenden", "Esci"],
        ["discharging"] = ["en décharge", "Entladung", "in scarica"],
        ["charging"] = ["en charge", "Ladung", "in carica"],
        ["on AC"] = ["sur secteur", "am Netz", "su rete"],
        ["unknown"] = ["inconnu", "unbekannt", "sconosciuto"],
        ["CPU package {0} W"] = ["Package CPU {0} W", "CPU-Package {0} W", "Package CPU {0} W"],
        ["CPU package {0} W (on AC, battery idle)"] = ["Package CPU {0} W (sur secteur, batterie inactive)", "CPU-Package {0} W (am Netz, Akku inaktiv)", "Package CPU {0} W (su rete, batteria inattiva)"],
        ["Battery power"] = ["Puissance batterie", "Akkuleistung", "Potenza batteria"],
        ["last {0} min"] = ["{0} dernières min", "letzte {0} min", "ultimi {0} min"],
        ["Time window"] = ["Fenêtre de temps", "Zeitfenster", "Finestra temporale"],
        ["Settings: theme, language, start with Windows, about"] = ["Réglages : thème, langue, lancement avec Windows, à propos", "Einstellungen: Design, Sprache, Autostart, Über", "Impostazioni: tema, lingua, avvio con Windows, informazioni"],
        ["Per-process energy estimates (opens a window)"] = ["Estimations d'énergie par processus (ouvre une fenêtre)", "Energieschätzung pro Prozess (öffnet ein Fenster)", "Stime energetiche per processo (apre una finestra)"],
        ["CPU package power (Intel/AMD RAPL): cores, integrated GPU and on-package memory,\nread every second. The battery figure minus this is roughly the display, Wi-Fi,\nSSD and the rest of the machine. The battery gauge is a slow one-minute average;\nthis one reacts instantly."] =
        [
            "Puissance du package CPU (Intel/AMD RAPL) : cœurs, GPU intégré et mémoire sur le package,\nlue chaque seconde. La valeur batterie moins celle-ci correspond à peu près à l'écran, au Wi-Fi,\nau SSD et au reste de la machine. La jauge batterie est une moyenne lente sur une minute ;\ncelle-ci réagit instantanément.",
            "CPU-Package-Leistung (Intel/AMD RAPL): Kerne, integrierte GPU und Speicher im Package,\njede Sekunde gelesen. Akkuwert minus dieser Wert entspricht etwa Display, WLAN,\nSSD und dem Rest des Geräts. Die Akkuanzeige ist ein träger Minutenmittelwert;\ndieser Wert reagiert sofort.",
            "Potenza del package CPU (Intel/AMD RAPL): core, GPU integrata e memoria sul package,\nletta ogni secondo. Il valore della batteria meno questo corrisponde circa a schermo, Wi-Fi,\nSSD e resto della macchina. L'indicatore della batteria è una media lenta su un minuto;\nquesto reagisce all'istante.",
        ],
        ["package "] = ["package ", "Package ", "package "],
        ["package ({0} s avg) "] = ["package (moy. {0} s) ", "Package ({0} s Mittel) ", "package (media {0} s) "],
        ["brightness {0} %"] = ["luminosité {0} %", "Helligkeit {0} %", "luminosità {0} %"],
        ["waiting for data"] = ["en attente de données", "warte auf Daten", "in attesa di dati"],
        ["CPU package, on AC"] = ["package CPU, sur secteur", "CPU-Package, am Netz", "package CPU, su rete"],
        ["{0} h {1} min left"] = ["{0} h {1} min restantes", "noch {0} h {1} min", "{0} h {1} min rimanenti"],
        ["{0} min left"] = ["{0} min restantes", "noch {0} min", "{0} min rimanenti"],
        ["battery"] = ["batterie", "Akku", "batteria"],
        ["package"] = ["package", "Package", "package"],
        ["now"] = ["maintenant", "jetzt", "ora"],
        ["package avg {0} W   ·   peak {1} W   ·   min {2} W"] = ["package moy. {0} W   ·   max {1} W   ·   min {2} W", "Package Ø {0} W   ·   max {1} W   ·   min {2} W", "package media {0} W   ·   max {1} W   ·   min {2} W"],
        ["avg {0} W{1}   ·   peak {2} W   ·   min {3} W"] = ["moy. {0} W{1}   ·   max {2} W   ·   min {3} W", "Ø {0} W{1}   ·   max {2} W   ·   min {3} W", "media {0} W{1}   ·   max {2} W   ·   min {3} W"],
        ["Balanced"] = ["Équilibré", "Ausbalanciert", "Bilanciato"],
        ["High performance"] = ["Performances élevées", "Höchstleistung", "Prestazioni elevate"],
        ["Power saver"] = ["Économie d'énergie", "Energiesparmodus", "Risparmio energia"],
        ["Ultimate performance"] = ["Performances ultimes", "Ultimative Leistung", "Prestazioni massime"],
        ["unknown scheme"] = ["mode inconnu", "unbekannter Plan", "combinazione sconosciuta"],
        ["custom scheme"] = ["mode personnalisé", "eigener Plan", "combinazione personalizzata"],
        ["best efficiency"] = ["efficacité maximale", "beste Energieeffizienz", "massima efficienza"],
        ["best performance"] = ["performances maximales", "beste Leistung", "prestazioni massime"],
        ["WattBar – who is using the battery"] = ["WattBar – qui consomme la batterie", "WattBar – wer verbraucht den Akku", "WattBar – chi consuma la batteria"],
        ["Process"] = ["Processus", "Prozess", "Processo"],
        ["State"] = ["État", "Zustand", "Stato"],
        ["CPU share"] = ["Part CPU", "CPU-Anteil", "Quota CPU"],
        ["Total share"] = ["Part totale", "Gesamtanteil", "Quota totale"],
        ["Screen"] = ["Écran", "Bildschirm", "Schermo"],
        ["Disk"] = ["Disque", "Datenträger", "Disco"],
        ["Network"] = ["Réseau", "Netzwerk", "Rete"],
        ["Other"] = ["Autre", "Sonstiges", "Altro"],
        ["Focus"] = ["Actif", "Fokus", "Attivo"],
        ["Visible"] = ["Visible", "Sichtbar", "Visibile"],
        ["Minimized"] = ["Réduit", "Minimiert", "Ridotto"],
        ["Background"] = ["Arrière-plan", "Hintergrund", "Sfondo"],
        ["Ask for administrator consent each time the collector starts (UAC prompt)"] = ["Demander l'accord administrateur à chaque démarrage du collecteur (invite UAC)", "Bei jedem Start des Collectors nach Administratorrechten fragen (UAC-Abfrage)", "Chiedere il consenso amministratore a ogni avvio del collettore (richiesta UAC)"],
        ["Install a scheduled task once (one prompt), then start it without prompts"] = ["Installer une tâche planifiée une fois (une invite), puis démarrer sans invite", "Einmal eine geplante Aufgabe einrichten (eine Abfrage), danach ohne Abfrage starten", "Installare una volta un'attività pianificata (una richiesta), poi avviare senza richieste"],
        ["Don’t collect per-process data. Nothing else in WattBar needs administrator rights."] = ["Ne pas collecter de données par processus. Rien d'autre dans WattBar ne requiert de droits administrateur.", "Keine Daten pro Prozess erfassen. Nichts anderes in WattBar benötigt Administratorrechte.", "Non raccogliere dati per processo. Nient'altro in WattBar richiede diritti di amministratore."],
        ["Stop collector"] = ["Arrêter le collecteur", "Collector stoppen", "Ferma il collettore"],
        ["Remove scheduled task"] = ["Supprimer la tâche planifiée", "Geplante Aufgabe entfernen", "Rimuovi l'attività pianificata"],
        ["Install task and start collector (one prompt)"] = ["Installer la tâche et démarrer le collecteur (une invite)", "Aufgabe einrichten und Collector starten (eine Abfrage)", "Installa l'attività e avvia il collettore (una richiesta)"],
        ["Start collector"] = ["Démarrer le collecteur", "Collector starten", "Avvia il collettore"],
        ["Start collector (UAC prompt)"] = ["Démarrer le collecteur (invite UAC)", "Collector starten (UAC-Abfrage)", "Avvia il collettore (richiesta UAC)"],
        ["Why administrator rights?  Windows only lets administrators read the Energy Estimation Engine trace that these numbers come from. WattBar therefore runs a small separate collector with elevated rights; it only reads that trace and writes a summary file. Choose how it may start:"] =
        [
            "Pourquoi des droits administrateur ?  Windows ne laisse que les administrateurs lire la trace de l'Energy Estimation Engine dont proviennent ces chiffres. WattBar lance donc un petit collecteur séparé avec des droits élevés ; il ne fait que lire cette trace et écrire un fichier de synthèse. Choisissez comment il peut démarrer :",
            "Warum Administratorrechte?  Windows lässt nur Administratoren die Ablaufverfolgung der Energy Estimation Engine lesen, aus der diese Zahlen stammen. WattBar startet deshalb einen kleinen separaten Collector mit erhöhten Rechten; er liest nur diese Ablaufverfolgung und schreibt eine Zusammenfassung. Wählen Sie, wie er starten darf:",
            "Perché i diritti di amministratore?  Windows permette solo agli amministratori di leggere la traccia dell'Energy Estimation Engine da cui provengono questi numeri. WattBar avvia quindi un piccolo collettore separato con diritti elevati; legge solo quella traccia e scrive un file di riepilogo. Scegliete come può avviarsi:",
        ],
        ["How to read this.  Every minute Windows estimates how much energy each process caused (the numbers behind Task Manager’s “Power usage” column). Shares are that process’s part of everything attributed to processes during that minute.\r\nFinding an offender.  Sort by CPU share. A Background or Minimized process that stays near the top minute after minute is draining the battery without you using it; such rows are magenta. Focus and Visible rows are what you are working in.\r\n≈ CPU W spreads the measured CPU package power of the minute across processes by CPU share: an estimate, not a measurement. Screen energy always goes to the window in front."] =
        [
            "Comment lire.  Chaque minute, Windows estime l'énergie causée par chaque processus (les chiffres derrière la colonne « Consommation d'énergie » du Gestionnaire des tâches). Les parts sont la fraction de ce processus dans tout ce qui a été attribué aux processus durant cette minute.\r\nTrouver un coupable.  Triez par part CPU. Un processus en arrière-plan ou réduit qui reste en tête minute après minute vide la batterie sans que vous l'utilisiez ; ces lignes sont en magenta. Les lignes Actif et Visible sont ce sur quoi vous travaillez.\r\n≈ CPU W répartit la puissance mesurée du package CPU de la minute entre les processus selon leur part CPU : une estimation, pas une mesure. L'énergie de l'écran va toujours à la fenêtre au premier plan.",
            "So lesen Sie das.  Jede Minute schätzt Windows, wie viel Energie jeder Prozess verursacht hat (die Zahlen hinter der Spalte „Energieverbrauch“ im Task-Manager). Anteile sind der Teil dieses Prozesses an allem, was in dieser Minute Prozessen zugeordnet wurde.\r\nDen Verursacher finden.  Nach CPU-Anteil sortieren. Ein Prozess im Hintergrund oder minimiert, der Minute für Minute oben bleibt, leert den Akku, ohne dass Sie ihn benutzen; solche Zeilen sind magenta. Zeilen mit Fokus oder Sichtbar sind das, womit Sie arbeiten.\r\n≈ CPU W verteilt die gemessene CPU-Package-Leistung der Minute nach CPU-Anteil auf die Prozesse: eine Schätzung, keine Messung. Bildschirmenergie geht immer an das Fenster im Vordergrund.",
            "Come leggere.  Ogni minuto Windows stima quanta energia ha causato ogni processo (i numeri dietro la colonna «Consumo energetico» di Gestione attività). Le quote sono la parte di quel processo su tutto ciò che è stato attribuito ai processi in quel minuto.\r\nTrovare il colpevole.  Ordinate per quota CPU. Un processo in sfondo o ridotto che resta in cima minuto dopo minuto scarica la batteria senza che lo usiate; tali righe sono in magenta. Le righe Attivo e Visibile sono ciò su cui state lavorando.\r\n≈ CPU W distribuisce la potenza misurata del package CPU del minuto tra i processi secondo la quota CPU: una stima, non una misura. L'energia dello schermo va sempre alla finestra in primo piano.",
        ],
        ["Per-process collection is switched off. Pick another option below to use it."] = ["La collecte par processus est désactivée. Choisissez une autre option ci-dessous pour l'utiliser.", "Die Erfassung pro Prozess ist ausgeschaltet. Wählen Sie unten eine andere Option.", "La raccolta per processo è disattivata. Scegliete un'altra opzione qui sotto per usarla."],
        ["Registering the scheduled task, please confirm the prompt…"] = ["Enregistrement de la tâche planifiée, veuillez confirmer l'invite…", "Geplante Aufgabe wird eingerichtet, bitte die Abfrage bestätigen…", "Registrazione dell'attività pianificata, confermate la richiesta…"],
        ["The task was not installed (consent refused or registration failed). You can still use the UAC option."] = ["La tâche n'a pas été installée (accord refusé ou échec). L'option UAC reste utilisable.", "Die Aufgabe wurde nicht eingerichtet (abgelehnt oder fehlgeschlagen). Die UAC-Option bleibt verfügbar.", "L'attività non è stata installata (consenso rifiutato o errore). Resta disponibile l'opzione UAC."],
        ["Collector started through the scheduled task, first batch in about a minute…"] = ["Collecteur démarré via la tâche planifiée, premier lot dans une minute environ…", "Collector über die geplante Aufgabe gestartet, erste Daten in etwa einer Minute…", "Collettore avviato tramite l'attività pianificata, primo lotto tra circa un minuto…"],
        ["Could not start the scheduled task. Try removing and reinstalling it."] = ["Impossible de démarrer la tâche planifiée. Essayez de la supprimer et de la réinstaller.", "Die geplante Aufgabe konnte nicht gestartet werden. Entfernen und neu einrichten.", "Impossibile avviare l'attività pianificata. Provate a rimuoverla e reinstallarla."],
        ["Collector starting, first batch in about a minute…"] = ["Collecteur en démarrage, premier lot dans une minute environ…", "Collector startet, erste Daten in etwa einer Minute…", "Collettore in avvio, primo lotto tra circa un minuto…"],
        ["Consent was refused. Without it the collector cannot read the trace."] = ["Accord refusé. Sans lui, le collecteur ne peut pas lire la trace.", "Zustimmung verweigert. Ohne sie kann der Collector die Ablaufverfolgung nicht lesen.", "Consenso rifiutato. Senza di esso il collettore non può leggere la traccia."],
        ["Stopping collector…"] = ["Arrêt du collecteur…", "Collector wird gestoppt…", "Arresto del collettore…"],
        ["Could not signal the collector: {0}"] = ["Impossible de signaler le collecteur : {0}", "Collector konnte nicht benachrichtigt werden: {0}", "Impossibile segnalare il collettore: {0}"],
        ["Scheduled task removed."] = ["Tâche planifiée supprimée.", "Geplante Aufgabe entfernt.", "Attività pianificata rimossa."],
        ["The task was not removed (consent refused or it failed)."] = ["La tâche n'a pas été supprimée (accord refusé ou échec).", "Die Aufgabe wurde nicht entfernt (abgelehnt oder fehlgeschlagen).", "L'attività non è stata rimossa (consenso rifiutato o errore)."],
        ["Per-process collection is switched off."] = ["La collecte par processus est désactivée.", "Die Erfassung pro Prozess ist ausgeschaltet.", "La raccolta per processo è disattivata."],
        ["Collector not running. Start it to see which processes are using the battery."] = ["Collecteur arrêté. Démarrez-le pour voir quels processus consomment la batterie.", "Collector läuft nicht. Starten Sie ihn, um zu sehen, welche Prozesse den Akku verbrauchen.", "Collettore non in esecuzione. Avviatelo per vedere quali processi consumano la batteria."],
        ["Cannot read snapshot: {0}"] = ["Lecture de l'instantané impossible : {0}", "Momentaufnahme nicht lesbar: {0}", "Impossibile leggere l'istantanea: {0}"],
        [" · CPU package {0} W over the minute"] = [" · package CPU {0} W sur la minute", " · CPU-Package {0} W in der Minute", " · package CPU {0} W nel minuto"],
        [" · no heavy background process"] = [" · aucun processus lourd en arrière-plan", " · kein schwerer Hintergrundprozess", " · nessun processo pesante in sfondo"],
        [" · {0} heavy background process(es) highlighted"] = [" · {0} processus lourd(s) en arrière-plan en évidence", " · {0} schwere(r) Hintergrundprozess(e) hervorgehoben", " · {0} processo/i pesante/i in sfondo evidenziato/i"],
        ["Collecting, first batch in about a minute…"] = ["Collecte en cours, premier lot dans une minute environ…", "Erfassung läuft, erste Daten in etwa einer Minute…", "Raccolta in corso, primo lotto tra circa un minuto…"],
        ["Last batch {0}, no update since: the collector may have stopped."] = ["Dernier lot à {0}, rien depuis : le collecteur s'est peut-être arrêté.", "Letzte Daten um {0}, seitdem nichts: der Collector wurde vielleicht beendet.", "Ultimo lotto alle {0}, nessun aggiornamento: il collettore potrebbe essersi fermato."],
        ["Minute ending {0} · {1} processes"] = ["Minute finissant à {0} · {1} processus", "Minute bis {0} · {1} Prozesse", "Minuto fino alle {0} · {1} processi"],
        ["Collector starting…"] = ["Collecteur en démarrage…", "Collector startet…", "Collettore in avvio…"],
        ["Collector stopped; showing its last minute ({0})"] = ["Collecteur arrêté ; dernière minute affichée ({0})", "Collector gestoppt; letzte Minute wird angezeigt ({0})", "Collettore fermato; mostrato l'ultimo minuto ({0})"],
        ["Collector stopped. Start it again to resume."] = ["Collecteur arrêté. Redémarrez-le pour reprendre.", "Collector gestoppt. Zum Fortsetzen erneut starten.", "Collettore fermato. Riavviatelo per riprendere."],
        ["Collector error: {0}"] = ["Erreur du collecteur : {0}", "Collector-Fehler: {0}", "Errore del collettore: {0}"],
        ["version {0}"] = ["version {0}", "Version {0}", "versione {0}"],
        ["Battery discharge power in the Windows 11 tray, with a sparkline icon, a chart flyout, CPU package power and a per-process view fed by Windows’ own energy estimates."] =
        [
            "Puissance de décharge de la batterie dans la zone de notification de Windows 11, avec une icône sparkline, un graphique, la puissance du package CPU et une vue par processus basée sur les estimations d'énergie de Windows.",
            "Akku-Entladeleistung im Infobereich von Windows 11, mit Sparkline-Symbol, Diagramm, CPU-Package-Leistung und einer Prozessansicht auf Basis der Energieschätzungen von Windows.",
            "Potenza di scarica della batteria nell'area di notifica di Windows 11, con icona sparkline, grafico, potenza del package CPU e una vista per processo basata sulle stime energetiche di Windows.",
        ],
        ["MIT License · Made with ♥ by mui, 2026"] = ["Licence MIT · Fait avec ♥ par mui, 2026", "MIT-Lizenz · Mit ♥ gemacht von mui, 2026", "Licenza MIT · Fatto con ♥ da mui, 2026"],
    };
}
