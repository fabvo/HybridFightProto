# Hybrid Fight – Android-Prototyp in Unity

Mini-Kampfspiel für zwei Android-Geräte – mit Solo-Testmodus für Entwicklung ohne Partner.

| Phone-Pose                                     | Aktion                            |
|------------------------------------------------|-----------------------------------|
| Aufrecht vor dir gehalten (wie ein Schild)     | **Block** – ankommender Schaden wird stark reduziert |
| Flach auf der Hand, Display nach oben          | **Angriffsbereit** – über den Bildschirm wischen → feuert Angriff |
| Display nach unten auf eine NFC-Karte gelegt   | **Fokus** – Aufladung steigt; nächster Angriff macht mehr Schaden |

**Drei Modi auf dem Lobby-Screen:**
- **HOST** + **JOIN** = klassisches 2-Spieler-Match über lokales WLAN.
- **SOLO TEST** = lokal gegen eine KI, die dich periodisch angreift. Du und die KI respawnen automatisch nach 2 s, damit du Tilt, NFC und Wischgesten beliebig lange testen kannst.

---

## 1. Voraussetzungen

- **Unity 6 (6000.x) LTS** (das hier mitgelieferte Plugin ist exakt darauf ausgelegt). Geht auch mit Unity 2022 LTS, dann musst du eine kleine Anpassung machen → siehe Abschnitt 9.
- Beim Installieren über den Unity Hub das Modul **Android Build Support** mitinstallieren – inklusive *OpenJDK* und *Android SDK & NDK Tools*.
- Zwei Android-Geräte mit **NFC-Hardware** (so gut wie alle Geräte ab ~2018) – für Solo reicht eines.
- Mindestens ein **NFC-Tag** zum Testen. Günstig sind NTAG213/215/216-Sticker (≈ 5 € für 10 Stück bei Amazon/AliExpress – Stichwort „NFC tag NTAG213").

---

## 2. Projekt anlegen

1. Unity Hub → **New project** → Template **3D (Built-In Render Pipeline)**, Name z. B. `HybridFightProto`.
2. Sobald das Projekt offen ist, Editor schließen.
3. Den Inhalt des hier mitgelieferten `Assets/`-Ordners in den `Assets/`-Ordner deines Unity-Projekts kopieren – so dass danach existieren:
   - `Assets/Scripts/Bootstrap.cs`, `GameManager.cs`, `NetworkController.cs`, `NfcManager.cs`, `TiltDetector.cs`, `UIManager.cs`
   - `Assets/Plugins/Android/AndroidManifest.xml`
   - `Assets/Plugins/Android/NfcUnityActivity.java`
4. Projekt wieder öffnen.

---

## 3. Build-Plattform auf Android umstellen

1. **File → Build Settings…** → Plattform **Android** → **Switch Platform**.
2. **Player Settings…** öffnen, dann setzen:

   **Player → Other Settings**
   - *Package Name*: `com.example.hybridfight`
     ⚠️ Genau dieser Name ist wichtig – das Java-Plugin liegt im Package `com.example.hybridfight`. Wenn du den Package-Namen ändern willst, musst du sowohl in `NfcUnityActivity.java` (`package …;`) als auch in `AndroidManifest.xml` (`android:name="…"`) den neuen Namen eintragen.
   - *Application Entry Point*: **GameActivity** (Standard in Unity 6 – passt zu unserem Plugin).
   - *Minimum API Level*: **Android 7.0 (API 24)** oder höher.
   - *Target API Level*: Automatic (highest installed) ist okay.
   - *Scripting Backend*: **IL2CPP**.
   - *Target Architectures*: **ARM64** anhaken (für aktuelle Geräte zwingend; ARMv7 zusätzlich, falls eines deiner Geräte älter ist).
   - *Internet Access*: **Require**.

   **Player → Resolution and Presentation**
   - *Default Orientation*: **Portrait** (Hochformat).

---

## 4. Szene aufbauen

1. **File → New Scene** → *Basic (Built-in)* oder *Empty*. Speichern als `Assets/Scenes/Main.unity`.
2. **Directional Light kannst du löschen** – das Spiel hat keine 3D-Objekte, das Licht wird nicht gebraucht.
3. **Main Camera lass drin.** Eine UI mit *Screen Space Overlay* braucht zwar keine Kamera zum Rendern, aber Unity warnt sonst über einen fehlenden `AudioListener` und manche Shader-Pipelines legen sich quer. Die Kamera macht nichts und kostet nichts.
4. Hierarchy-Rechtsklick → **Create Empty**, das neue GameObject `Game` benennen.
5. Mit `Game` markiert im Inspector → **Add Component** → `Bootstrap` suchen und hinzufügen. (Bootstrap erzeugt beim Start UI, Tilt, NFC, Netzwerk und Spiellogik.)
6. **File → Build Settings → Add Open Scenes** klicken, damit `Main.unity` als gebaute Szene markiert ist.

Hierarchy sollte am Ende so aussehen:
```
Main Camera
Game        (mit Bootstrap-Component)
```

---

## 5. APK bauen

1. **File → Build Settings → Build** (oder *Build And Run* wenn dein Telefon per USB hängt und USB-Debugging an ist).
2. Speicherort wählen, Datei z. B. `HybridFight.apk`.
3. Beim ersten Build dauert Gradle ein paar Minuten – es kompiliert auch unsere `NfcUnityActivity.java`.

Bei Erfolg liegt die APK am gewählten Ort.

---

## 6. APK auf Geräten installieren

1. APK auf das Telefon übertragen (USB, Cloud, Telegram an dich selbst – egal).
2. **Einstellungen → Apps → Spezielle Berechtigungen → Unbekannte Apps installieren** → die App, mit der du die APK öffnest (z. B. Dateimanager, Chrome), erlauben unbekannte Apps zu installieren.
3. APK in der Dateien-App antippen → installieren.
4. Beim ersten Start: Android fragt nach NFC-Erlaubnis – erlauben. NFC selbst muss in den Schnelleinstellungen aktiviert sein.

---

## 7. Solo-Test (ein Gerät)

1. App starten → **SOLO TEST** drücken.
2. Du landest sofort im Game-Screen mit einer **„Trainer"-Leiste oben**, einem Solo-Badge und einem grün leuchtenden „BEREIT" in der Mitte.
3. Mechaniken testen:
   - **Handy aufrecht halten** → Mode wechselt zu *BLOCK*. Wenn der Trainer dich dabei trifft, verlierst du nur 2 HP statt 8–18.
   - **Handy auf NFC-Karte legen** (Display unten) → Mode wechselt zu *FOKUS*, Aufladungsbalken füllt sich. Maximalwert in ~3 s.
   - **Handy hochnehmen, flach auf die Hand** → *ANGRIFF*. Mit dem Finger über den Bildschirm wischen → Trainer kassiert Basis + Aufladung.
4. Stirbt der Trainer, respawnt er nach 2 s. Stirbst du, respawnst du auch – dauerhaft testbar.

---

## 8. Multiplayer-Test (zwei Geräte)

1. Beide Geräte ins **gleiche WLAN** bringen (am besten ein Hotspot deines Routers, kein „Gast"-Netz – manche Gast-Netze blockieren Geräte-zu-Geräte-Verbindung).
2. App auf Gerät A starten → **HOST** drücken. Im Statusfeld erscheint die lokale IP (z. B. `192.168.1.42`).
3. App auf Gerät B starten → IP von A im Eingabefeld eintippen → **JOIN**. Status wechselt auf **Verbunden!** und das Spielfeld erscheint auf beiden.
4. Wer zuerst auf 0 HP fällt, sieht „VERLOREN", der andere „GEWONNEN!". Match neu starten = beide Apps neu öffnen.

---

## 9. NFC-Tag bespielen (optional)

Für den Prototyp musst du eigentlich nichts auf den Tag schreiben – das Spiel reagiert nur auf die UID, die jeder NFC-Tag ab Werk hat. Wenn du später unterschiedliche Karten unterschiedliche Aktionen auslösen lassen willst:

1. Im Play Store **„NFC Tools"** von *wakdev* installieren (kostenlos, sehr verbreitet).
2. App öffnen → Tab **WRITE** → **Add a record** → z. B. *Text* → `focus_card_1` eintippen → **OK** → **Write / 4 bytes**.
3. Telefon mit aktivem NFC an den Tag halten – „Write complete!".
4. Mit dem Tab **READ** kannst du jederzeit prüfen, was auf dem Tag steht und welche UID er hat.

Im Spiel siehst du die UID im Logcat (`adb logcat | grep NfcUnity`), und in `NfcManager.LastTagId` steht sie nach jeder Berührung – damit kannst du in `GameManager.UpdateMode()` später unterschiedliche Karten verschieden behandeln.

---

## 10. Falls du Unity 2022 LTS statt Unity 6 nutzt

Das mitgelieferte Plugin extends `UnityPlayerGameActivity` (Unity 6 GameActivity-Standard). In Unity 2022 ist diese Klasse oft nicht vorhanden. Zwei Wege:

**Variante A (einfacher):** In **Player Settings → Other Settings** den *Application Entry Point* von GameActivity auf **Activity** umstellen. Dann zusätzlich:
- In `NfcUnityActivity.java`: Import-Zeile auf `import com.unity3d.player.UnityPlayerActivity;` ändern und das `extends UnityPlayerGameActivity` zu `extends UnityPlayerActivity` machen.
- In `AndroidManifest.xml`: `android:theme="@style/UnityThemeSelector"` und die `<meta-data>`-Zeile mit `android.app.lib_name` durch `<meta-data android:name="unityplayer.UnityActivity" android:value="true" />` ersetzen.

**Variante B:** Bei Unity 6 bleiben – das Plugin funktioniert dort out-of-the-box.

---

## 11. Troubleshooting

| Problem | Lösung |
|---|---|
| **„cannot find symbol – import com.unity3d.player.UnityPlayerActivity"** beim Build | Du baust mit Unity 6 GameActivity, aber das Plugin importiert die alte Klasse. Aktuelles Plugin (das hier mitgelieferte) extends `UnityPlayerGameActivity` – stell sicher, dass die Java-Datei aktuell ist. |
| **Build erfolgreich, aber App startet nicht / schwarzer Bildschirm** | Player Settings → *Application Entry Point* muss zur Java-Klasse passen: `UnityPlayerGameActivity` ↔ GameActivity, `UnityPlayerActivity` ↔ Activity. Theme im Manifest analog (`BaseUnityGameActivityTheme` ↔ GameActivity, `UnityThemeSelector` ↔ Activity). |
| **„Verbindung fehlgeschlagen"** beim Joinen | Beide im gleichen WLAN? IP korrekt? Manche Router (Gastnetz, AP-Isolation) blockieren Peer-Verbindungen. Im Notfall ein Handy als Hotspot, das andere joint dem Hotspot. |
| **Tag wird nicht erkannt** | NFC eingeschaltet? Tag direkt am NFC-Bereich des Geräts (oft hinten oben oder mittig)? Im `adb logcat` nach `NfcUnityActivity` filtern – dort siehst du, ob das Plugin überhaupt aufgerufen wird. |
| **Modi springen unruhig zwischen Idle und Block/Attack** | In `TiltDetector.cs` das Feld `threshold` auf 0.8 erhöhen oder `smoothing` senken. |
| **Gradle-Fehler „Manifest merger failed"** | Häufig wenn du Theme/Meta-Data in der Manifest geändert hast und beides nicht zusammen passt. Manifest aus diesem Repo 1:1 übernehmen. |

---

## 12. Wo du als nächstes anpacken kannst

- **Analoge Komponente**: NFC-Karten an verschiedenen Orten in der Wohnung verteilen, jede löst andere Effekte aus (Heilkarte, Doppelschaden, etc.). Im `NfcManager.LastTagId` hast du die UID, in `GameManager.UpdateMode()` kannst du je nach UID unterschiedliche Modes setzen.
- **Ausrichtung der Geräte zueinander**: Über Bluetooth-Beacons oder UWB-Ranging (ab Pixel 6 / iPhone 11 verfügbar) lässt sich grob bestimmen, ob du auf den Gegner zielst.
- **Swipe-Richtung als Angriffsart**: In `GameManager.DetectSwipe()` ist die Swipe-Distanz schon da, die Richtung des Vektors gibt es gratis mit dazu (`(t.position - swipeStart).normalized`). Vorwärts = Stoß, seitlich = Hieb.
- **Mehr als zwei Spieler**: Statt rohem TCP würde ich dann auf **Mirror** oder Unity **Netcode for GameObjects** umstellen.
- **KI-Trainer schwerer machen**: In `NetworkController.UpdateSoloAi()` die Frequenz und den Schadens-Range anpassen, oder die KI gelegentlich blocken lassen (eigene PlayerMode-Variable + entsprechend reduzierter eingehender Schaden).

Viel Spaß beim Testen!
