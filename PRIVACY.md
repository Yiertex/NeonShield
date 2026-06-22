# Datenschutz

NeonShield verarbeitet Scan-Ziele, Berichte, Einstellungen und
Quarantänedaten standardmäßig lokal auf dem Windows-Gerät.

## Lokale Daten

Die Anwendung speichert unter `%LOCALAPPDATA%\NeonShield`:

- Einstellungen,
- ClamAV-Signaturdatenbanken,
- Scanberichte,
- Quarantäne-Metadaten und isolierte Dateien,
- den verschlüsselten VirusTotal-API-Schlüssel, sofern hinterlegt.

Der VirusTotal-Schlüssel wird mit Windows DPAPI an das aktuelle
Windows-Benutzerkonto gebunden verschlüsselt.

## Netzwerkzugriffe

NeonShield greift auf folgende Dienste zu:

- GitHub API und GitHub Releases, um offizielle ClamAV-Versionen sowie
  NeonShield-Updates zu prüfen und herunterzuladen,
- `database.clamav.net`, um ClamAV-Signaturen mit `freshclam` zu aktualisieren,
- optional `www.virustotal.com`, wenn der Benutzer den
  VirusTotal-Hashabgleich ausdrücklich aktiviert.

## VirusTotal

Beim optionalen VirusTotal-Abgleich werden ausschließlich SHA-256-Hashes von
ausführbaren Dateien laufender Prozesse übertragen. NeonShield lädt dabei
keine Dateien zu VirusTotal hoch.

Ein Hash kann dennoch Rückschlüsse darauf ermöglichen, welche Software auf
dem Gerät ausgeführt wird. Die Funktion ist deshalb standardmäßig deaktiviert.

Die kostenlose VirusTotal Public API ist laut VirusTotal auf 500 Anfragen pro
Tag und vier Anfragen pro Minute begrenzt. Sie darf nicht in kommerziellen
Produkten oder bestimmten geschäftlichen Abläufen verwendet werden. Für solche
Anwendungen ist eine passende VirusTotal-Lizenz erforderlich.

Weitere Informationen:

- https://docs.virustotal.com/reference/public-vs-premium-api
- https://docs.virustotal.com/reference/overview
