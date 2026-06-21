# NeonShield

NeonShield ist eine native Windows-Oberfläche für die Open-Source-Engine
[ClamAV](https://www.clamav.net/). Die Anwendung stellt selbst keine
Virensignaturen bereit, sondern startet die lokal installierten Programme
`clamscan.exe` und `freshclam.exe`.

**Autor und Publisher:** Yiertex

## Funktionen

- Schnellscan für Desktop, Dokumente, Downloads und temporäre Dateien
- Tiefenscan über alle lokalen Festplatten
- Benutzerdefinierter Ordnerscan
- Arbeitsspeicher- und Prozess-Scan
- Pausieren, Fortsetzen und Abbrechen laufender Scans
- dauerhafte Scanberichte mit Verlauf und Funddetails
- optionaler VirusTotal-Hashabgleich ohne Datei-Upload
- Automatische Quarantäne mit Wiederherstellen und endgültigem Löschen
- Aktualisierung der ClamAV-Signaturen über `freshclam`
- Dunkle, violette Neon-Oberfläche

## Voraussetzungen

- Windows 10 oder neuer
- Für den Installer sind keine weiteren Laufzeiten erforderlich.
- Für lokale Builds wird das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) benötigt.

Der Installer lädt die offizielle ClamAV-Engine und Signaturdatenbank
automatisch. Eine Internetverbindung ist bei Installation und Updates nötig.

## Bauen und starten

```powershell
dotnet build .\NeonShield.slnx
dotnet run --project .\src\NeonShield\NeonShield.csproj
```

Eine eigenständige Windows-EXE lässt sich so erzeugen:

```powershell
.\Build-Release.ps1
```

Das Ergebnis liegt anschließend unter `release\win-x64\NeonShield.exe`.

## Installer mit automatischer ClamAV-Engine

Mit installiertem [Inno Setup 6](https://jrsoftware.org/isdl.php):

```powershell
.\Build-Installer.ps1
```

Der fertige Installer liegt unter `release\installer`.

### Automatischer Build auf GitHub

Der Workflow `.github/workflows/build-installer.yml` erstellt den
Windows-Installer automatisch:

- bei Änderungen auf `main`,
- manuell über **Actions → Build Windows installer → Run workflow**,
- beim Push eines Versionstags wie `v1.4.1`.

Bei einem normalen Build kann die Setup-EXE auf der Seite des Workflow-Laufs
unter **Artifacts** heruntergeladen werden. Ein Versionstag erzeugt zusätzlich
automatisch ein GitHub-Prerelease und hängt die Setup-EXE inklusive
SHA-256-Prüfsumme an.

Beispiel für eine Veröffentlichung über GitHub Desktop:

1. Änderungen committen und pushen.
2. In GitHub Desktop **Repository → Open in Terminal** öffnen.
3. `git tag v1.4.1` ausführen.
4. `git push origin v1.4.1` ausführen.

Während der Installation:

- wird die aktuelle offizielle ClamAV-Windows-Engine von
  `github.com/Cisco-Talos/clamav` heruntergeladen,
- wird der SHA-256-Digest aus der offiziellen GitHub-Release-API geprüft,
- werden die offiziellen Virensignaturen mit `freshclam` geladen.

Der Download umfasst derzeit mehr als 200 MB für die Engine sowie zusätzlich
die Signaturdatenbanken. Eine Internetverbindung ist erforderlich.

Bei jedem Programmstart prüft NeonShield erneut:

- ob eine neuere ClamAV-Engine verfügbar ist,
- ob aktuellere Virensignaturen verfügbar sind.

Bei der Deinstallation werden die verwaltete Engine, die Signaturdatenbank und
die FreshClam-Konfiguration entfernt. Quarantänedateien werden aus
Sicherheitsgründen nicht automatisch gelöscht.

Bei einer manuellen Ausführung ohne Installer lädt NeonShield die Engine beim
ersten Start automatisch. Alternativ kann unter **Einstellungen** ein bereits
vorhandener Ordner ausgewählt werden, der `clamscan.exe` und `freshclam.exe`
enthält.

```text
C:\Program Files\ClamAV
```

## Sicherheitshinweise

- ClamAV ist eine Malware-Erkennungsengine, keine vollständige Endpoint-
  Security-Suite. NeonShield ersetzt den Microsoft Defender Echtzeitschutz
  nicht.
- Gefundene Dateien werden standardmäßig verschoben und nicht gelöscht.
- Das Wiederherstellen eines Funds kann gefährlich sein. Nur Dateien
  wiederherstellen, die nachweislich ein Fehlalarm sind.
- Ein kompletter Tiefenscan kann lange dauern und auf geschützte Windows-
  Ordner ohne Administratorrechte nicht zugreifen.
- Für aussagekräftige Ergebnisse müssen die ClamAV-Signaturen aktuell sein.
- Der optionale VirusTotal-Abgleich überträgt SHA-256-Hashes und ist
  standardmäßig deaktiviert. Beachte die Einschränkungen der VirusTotal
  Public API.

## Datenablage

Einstellungen und Quarantäne-Metadaten liegen unter:

```text
%LOCALAPPDATA%\NeonShield
```

Die isolierten Dateien erhalten zufällige Namen mit der Endung `.qtn`.

## Datenschutz, Sicherheit und Lizenz

- [Datenschutz](PRIVACY.md)
- [Sicherheitsrichtlinie](SECURITY.md)
- [Änderungsverlauf](CHANGELOG.md)
- [MIT-Lizenz](LICENSE)

ClamAV bleibt separate Drittsoftware unter der GNU GPL v2. Weitere Hinweise
stehen in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
