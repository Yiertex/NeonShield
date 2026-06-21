# Changelog

Alle wichtigen Änderungen an NeonShield werden in dieser Datei dokumentiert.

## [1.4.1] - 2026-06-21

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
