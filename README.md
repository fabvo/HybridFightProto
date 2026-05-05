# Hybrid Fight – Android-Prototyp in Unity

Mini-Kampfspiel für zwei Android-Geräte – mit Solo-Testmodus für Entwicklung ohne Partner.

| Phone-Pose                                     | Aktion                            |
|------------------------------------------------|-----------------------------------|
| Aufrecht vor dir gehalten (wie ein Schild)     | **Block** – ankommender Schaden wird stark reduziert |
| Flach auf der Hand, Display nach oben          | **Angriffsbereit** – über den Bildschirm wischen → feuert Angriff |
| Display nach unten auf eine NFC-Karte gelegt   | **Fokus** – Aufladung steigt; nächster Angriff macht mehr Schaden |

---

## 1. Voraussetzungen

- **Unity 6 (6000.x) LTS**
- Beim Installieren über den Unity Hub **Android Build Support** + *OpenJDK* + *Android SDK & NDK Tools*.
- Mindestens ein NFC-fähiges Android-Gerät und einen NFC-Tag (NTAG213/215/216).

---

## 2. Projekt einrichten

1. Unity Hub → **New project** → Template **3D (Built-In Render Pipeline)** → `HybridFightProto`.
2. Editor schließen, Inhalt der ZIP nach `Assets/` kopieren.
3. **Player Settings**:
   - *Package Name*: `com.example.hybridfight`
   - *Application Entry Point*: **GameActivity** (Unity 6 Default)
   - *Min API Level*: Android 7.0 (API 24) oder höher
   - *Scripting Backend*: IL2CPP, *Target Architectures*: ARM64
   - *Default Orientation*: Portrait
4. Szene: leeres GameObject `Game` mit `Bootstrap`-Component, `Main Camera` drin lassen, `Directional Light` weg.

---

## 3. Build And Run

1. Auf dem Handy **Entwickleroptionen → USB-Debugging** an, USB-Kabel ran, "Immer erlauben" bestätigen.
2. In Unity: **File → Build And Run** (Strg+B).

---

## 4. Wie funktioniert NFC unter Android, und warum war's vorher kaputt

Android hat zwei NFC-Lese-APIs im Vordergrund: **Foreground Dispatch** und **ReaderMode**. Beide haben dieselbe fundamentale Eigenschaft: das System sagt dir **einmal** "Tag erkannt!" und danach gar nichts mehr. Es gibt **kein** "Tag liegt noch drauf" und auch **kein** "Tag wurde abgehoben"-Event.

Für unseren "halte das Handy auf der Karte und es lädt sich auf"-Mechanismus brauchen wir aber genau diese kontinuierliche Information. Die Lösung ist der Standard-Trick aller NFC-Apps die "Tag liegt drauf"-Detection brauchen:

1. Beim Discovery-Event eine **NfcA-Verbindung zum Tag öffnen** (das ist die Low-Level-Schnittstelle, NTAG21x sind alle NfcA).
2. In einem Hintergrund-Thread alle 150ms einen **harmlosen Befehl an den Tag schicken** (GET_VERSION 0x60). Antwortet der Tag → liegt noch drauf. Antwortet er nicht (TagLostException) → wurde abgehoben.
3. Bei jedem erfolgreichen Ping einen "Tag noch da"-Event nach Unity schicken.

Das macht das aktuelle Plugin. Auf der Unity-Seite ist `NfcManager.TagPresent` `true` solange das letzte Event weniger als 0.6s zurückliegt.

---

## 5. adb verstehen und benutzen (deine zwei Fragen)

### Was ist adb?

`adb` (Android Debug Bridge) ist ein Kommandozeilen-Tool, das mit deinem Android-Handy via USB redet. Du brauchst es nur in zwei Situationen:

- Apps schnell deinstallieren / installieren ohne Touchscreen-Geklicke
- Logs vom Handy live auf deinem Rechner mitlesen

Wenn du eine App schon **manuell auf dem Handy deinstalliert hast, ist das absolut äquivalent zu `adb uninstall`**. Du musst nichts mehr machen. Der adb-Befehl ist nur eine Abkürzung wenn man am Rechner sitzt und nicht zum Handy greifen will.

### Wo findest du adb?

`adb.exe` liegt in deiner Unity-Installation. Auf Windows typischerweise hier:
```
C:\Program Files\Unity\Hub\Editor\<deine Version>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
```
(falls du Android Studio installiert hast, gibt's es auch unter `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`).

### Wie ausführen?

**Variante A: schnell ohne PATH-Setup**
1. Drücke `Win + R`, tippe `powershell`, Enter.
2. Im PowerShell-Fenster: `cd` in den platform-tools-Ordner. Beispiel:
   ```
   cd "C:\Program Files\Unity\Hub\Editor\6000.4.5f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools"
   ```
3. Dann Befehle mit `.\` davor ausführen, z.B. `.\adb devices`.

**Variante B: PATH dauerhaft setzen** (einmal einrichten, dann überall)
1. Windows-Suche → "Umgebungsvariablen für dieses Konto bearbeiten".
2. Bei "Benutzervariablen" → "Path" → "Bearbeiten" → "Neu" → den platform-tools-Ordner einfügen.
3. PowerShell schließen und neu öffnen. Jetzt geht überall `adb devices` direkt.

### Test ob's funktioniert

Mit angeschlossenem und entsperrtem Handy:
```
adb devices
```
sollte etwa zurückgeben:
```
List of devices attached
ABCD1234EFGH    device
```
Wenn da `unauthorized` steht: am Handy den USB-Debugging-Dialog mit "Immer erlauben" bestätigen. Wenn die Liste leer ist: USB-Kabel raus, rein, Handy entsperren, anderen USB-Port probieren, USB-Modus auf MTP/Dateiübertragung stellen.

### Logcat (Logs vom Handy live mitlesen)

In einem PowerShell-Fenster mit angeschlossenem Handy:
```
adb logcat -s Unity NfcUnityActivity *:S
```

Damit siehst du:
- alle `Debug.Log()`-Ausgaben aus deinem Unity-Code (`Unity` Tag)
- alle `Log.i/w/d`-Ausgaben aus dem Java-Plugin (`NfcUnityActivity` Tag)
- alles andere wird unterdrückt (`*:S` = silence)

Beim Auflegen des Tags solltest du jetzt z.B. sehen:
```
I NfcUnityActivity: NFC adapter ready.
D NfcUnityActivity: ReaderMode enabled.
D NfcUnityActivity: NfcA connected, watching presence (UID=04A2B3C4D5)
I Unity: [NFC] Tag #1 placed: 04A2B3C4D5
... (viele Heartbeat-Events von Unity, je nach Log-Konfiguration evtl. nicht sichtbar) ...
D NfcUnityActivity: Tag was lifted (TagLostException). UID=04A2B3C4D5
```

Wenn du das Fenster schließen willst: `Strg + C` drückt logcat ab. Bei langen Sessions solltest du gelegentlich `adb logcat -c` (clear) hinterherschicken.

Pro-Tipp: Du kannst die Ausgabe in eine Datei umleiten:
```
adb logcat -s Unity NfcUnityActivity *:S > C:\temp\hybridfight.log
```

---

## 6. Visuelles Feedback im Spiel

**NFC-Status-Badge** oben (immer sichtbar):
- **Rot** "noch kein Tag erkannt"
- **Pulsierend grün** "TAG LIEGT DRAUF UID:XX" – Tag ist gerade im Feld (Heartbeat-Events kommen rein)
- **Grau** "letzter Tag #N UID:XX vor 1.4s" – wurde abgehoben

**Im Fokus-Modus** pulsiert der Aufladungsbalken sichtbar und füllt sich kontinuierlich.
**Beim Wischen** Linie + Schaden-Popup beim Loslassen.

---

## 7. Solo-Test

App starten → **SOLO TEST**. Aufrecht halten = BLOCK; flach mit Display oben = ANGRIFF (wischen!); Display unten auf NFC-Tag = FOKUS (laden, solange das Tag drauf liegt). Beide respawnen automatisch nach 2s.

---

## 8. Multiplayer

Beide Geräte ins gleiche WLAN. A → **HOST**, IP merken. B → IP eintippen → **JOIN**.

---

## 9. Troubleshooting

| Problem | Lösung |
|---|---|
| **Aufladungsbalken füllt sich nicht obwohl Tag liegt** | Du hast eine alte Plugin-Version. Diese hier nutzt aktives Polling per NfcA. Vorher alte App mit `adb uninstall com.example.hybridfight` (oder per Hand auf dem Handy) deinstallieren, neu bauen. |
| **NFC-Badge bleibt rot, kein Tag erkannt** | Logcat öffnen (Abschnitt 5). Erscheint "NFC adapter ready"? Wenn ja: Antennenposition am Handy mit NFC Tools PRO finden. Wenn "ERROR NFC turned off": NFC im System einschalten. |
| **NFC funktioniert mit NFC Tools, aber nicht in der App** | Kein NfcA-Tag (z.B. NfcF oder NfcV). Logcat zeigt "Tag is not NfcA-compatible". Lösung: NTAG-Sticker oder MIFARE Ultralight verwenden, beide sind NfcA. |
| **Buttons reagieren nicht auf Tap** | Active Input Handling und EventSystem mismatchen. Code wählt automatisch das passende Modul. |
| **App startet nicht / schwarz** | Application Entry Point muss zur Java-Klasse passen: GameActivity ↔ `UnityPlayerGameActivity`. |

---

## 10. Ideen für Weiterentwicklung

- **NFC-Karten mit Funktionen:** UID-Mapping in `GameManager.UpdateMode()`.
- **Swipe-Richtung als Angriffsart:** Vector aus `(SwipeCurrent - SwipeStart).normalized`.
- **Sensor-Toolbox**: separates Test-Menü für Mikrofon, Gyro, Kamera, Pulsmesser etc.
- **Mehr als 2 Spieler:** Auf **Mirror** oder **Netcode for GameObjects** umstellen.

Viel Spaß beim Testen!
