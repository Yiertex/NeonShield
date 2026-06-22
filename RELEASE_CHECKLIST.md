# Release-Checkliste

## Vor dem Tag

- [ ] `main` ist mit GitHub synchronisiert.
- [ ] `CHANGELOG.md` enthält ein Datum statt `Unreleased`.
- [ ] Versionsnummer in `NeonShield.csproj` entspricht dem geplanten Tag.
- [ ] `.\Build-Release.ps1` läuft ohne Warnungen oder Fehler.
- [ ] Schnellscan, Tiefenscan, Pause, Fortsetzen und Abbrechen wurden getestet.
- [ ] Scanbericht, Quarantäne, Wiederherstellen und Löschen wurden getestet.
- [ ] Arbeitsspeicher-Scan wurde als Administrator getestet.
- [ ] Prozess-Scan wurde mit deaktiviertem und aktiviertem Online-Abgleich getestet.
- [ ] Installation und Deinstallation wurden auf einem sauberen Windows-Konto getestet.
- [ ] ClamAV-Engine und Signaturen werden installiert und wieder entfernt.
- [ ] Manuelle und automatische NeonShield-Updateprüfung wurden getestet.
- [ ] Keine API-Schlüssel, Signaturdatenbanken oder Quarantänedateien sind im Commit.

## Veröffentlichung

```powershell
git tag v1.7.0
git push origin v1.7.0
```

Danach unter **GitHub → Actions** prüfen:

- [ ] Build ist grün.
- [ ] Installer-Artefakt ist vorhanden.
- [ ] Das stabile GitHub Release ist als „Latest“ markiert.
- [ ] Das Release enthält die Setup-EXE.
- [ ] SHA-256 im Release stimmt mit der hochgeladenen Datei überein.

## Nach der Veröffentlichung

- [ ] Installer auf einem zweiten Windows-System herunterladen und ausführen.
- [ ] Windows-SmartScreen-Verhalten dokumentieren.
- [ ] Eine ältere NeonShield-Version erkennt `v1.7.0` über den Updatekanal.
- [ ] Der heruntergeladene Installer startet erst nach erfolgreicher SHA-256-Prüfung.
