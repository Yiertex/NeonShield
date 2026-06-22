# Changelog

Alle wichtigen Änderungen an NeonShield werden in dieser Datei dokumentiert.

## [1.7.0] - 2026-06-22

### Hinzugefügt

- aufklappbarer Info-Bereich in den Einstellungen
- Anzeige von Autor Yiertex, App-Version, Updatekanal und ClamAV-Version
- eingebetteter Changelog direkt in der Anwendung
- Schnelllinks zu GitHub, Releases, Lizenz und Datenschutz

## [1.6.0] - 2026-06-22

### Hinzugefügt

- stabiler GitHub-Updatekanal mit Prüfung beim Programmstart
- manuelle Updateprüfung in den Einstellungen
- SHA-256-Verifikation vor dem Start heruntergeladener Installer
- eigene Zählung für gesperrte oder unzugängliche Dateien in Scanberichten

### Geändert

- GitHub-Tags erzeugen stabile Releases für den Anwendungskanal
- ClamAV-Zugriffswarnungen werden kompakt als übersprungene Dateien dargestellt
- alte Scanberichte mit reinen Zugriffswarnungen werden beim Laden normalisiert

## [1.5.2] - 2026-06-21

### Hinzugefügt

- Pausieren, Fortsetzen und Abbrechen laufender Scans
- dauerhafte Scanberichte mit Verlauf und Detailansicht
- ClamAV-Arbeitsspeicher-Scan
- Prozess-Scan für ausführbare Dateien laufender Prozesse
- optionaler VirusTotal-Hashabgleich ohne Datei-Upload
- automatischer GitHub-Actions-Build für Windows-Installer

### Geändert

- automatische Versionsübernahme aus Git-Tags
- verbesserte Release- und Installer-Prüfungen
- vollständige MIT-Lizenz und Datenschutz-/Sicherheitsdokumentation
- korrekte Bereitstellung des offiziellen ClamAV-CVD-Zertifikats
- fehlgeschlagene ClamAV-Scans werden nicht mehr als „Sauber“ protokolliert
- zuverlässiger Upload von Installer und SHA-256-Datei in GitHub Releases

## [1.1.0] - 2026-06-20

### Hinzugefügt

- erste NeonShield-Oberfläche
- Schnell-, Tiefen- und benutzerdefinierter Scan
- Quarantäne
- automatischer Download und Update der ClamAV-Engine
