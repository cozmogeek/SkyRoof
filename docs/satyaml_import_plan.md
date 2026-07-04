# Plan: fold gr-satellites satyaml data into SkyRoof's SatNOGS DB

## Goal
During SkyRoof's satellite-data download, also fetch the gr-satellites `satyaml/` files;
during import, match them by NORAD and attach a nullable **`GrSatsInfo`** sub-object to each
transmitter in `Satellites.json`. These are the fields a downstream consumer (FskDemod's
`ParamResolver`) needs and that the SatNOGS DB does not carry — most importantly **deviation**.

## Field decision (driven by FskDemod code)
From `FskDemod/Core/ParamResolver.cs`, the only yaml-sourced scalars used are
**deviation, framing, modulation**, with **baudrate** used to pick the right transmitter.
A **telemetry** decoder name is added as a forward-looking label (see below). Everything else
in the yaml (frequency, alternative_names, image/files products) is ignored — SkyRoof already
has names/frequencies, and there is no telemetry decoder yet.

### `GrSatsInfo` (nullable sub-object on the transmitter)
```csharp
public class GrSatsInfo
{
  public string? modulation { get; set; }
  public double? baudrate  { get; set; }
  public double? deviation { get; set; }
  public string? framing   { get; set; }
  public string? telemetry { get; set; }   // real decoder name; null for ax25/csp/none
}
```

### Why `telemetry` is a single nullable string (not a list)
The satyaml `data:` block names data products, each tagged with a `telemetry:` decoder string
that resolves to a Python `construct` beacon parser in `gr-satellites/python/telemetry/`.
The **yaml field is only a label/selector** — the decode logic lives in ~53 Python modules,
not in the yaml. Real engineering-value decode in C# would require porting those `construct`
definitions and is a separate future project.

Distribution across the 414-file snapshot:
- `ax25` (248 sats) and `csp` (30 sats) are framing/transport, **not** engineering beacons —
  discarded here (SkyRoof/FskDemod already produce AX.25/CSP frames via deframers).
- Genuine per-satellite beacon decoders (`funcube`, `sat_1kuns_pf`, `snet`, …) exist in only
  ~55 satellites, mostly one-offs.

Verified by resolving each transmitter's `data:` key-name references against the satellite-level
`data:` map: **once `ax25`/`csp` are discarded, the max distinct real decoders on any single
transmitter is 1; zero transmitters reference more than one.** The only 3 sats listing two real
decoders (SMOG-P, SMOG-1, ATL-1) split them across *different* transmitters. So a single string
suffices. As defensive insurance against future yaml changes, the importer **logs a warning and
keeps the first** if it ever sees >1 real decoder on one transmitter.

Import rule for `telemetry`: resolve the transmitter's `data:` references, take the single
`telemetry:` value, and **store null if it is `ax25` or `csp`**.

## Changes (SkyRoof only — FskDemod consumption is a separate follow-up)

### 1. Download — fetch the satyaml zip
In `SkyRoof/Satellites/SatnogsDb.cs`:
- Add `DownloadSatyaml()`: one `GetByteArrayAsync` of
  `https://codeload.github.com/daniestevez/gr-satellites/zip/refs/heads/main`,
  saved to `Downloads/gr-satellites.zip`.
- Call it from `DownloadAll()` as a 4th step (re-number the progress events).
- Wrap so a satyaml failure is **non-fatal** — log and continue (enrichment, not core data;
  unlike JE9PEL it must not block the import).
- Extract `python/satyaml/*.yml` into `Downloads/satyaml/` using
  `System.IO.Compression.ZipFile`/`ZipArchive` (built into .NET 10), filtering entries whose
  path contains `/python/satyaml/` and ends with `.yml`.

### 2. New parser — `SatyamlDb.cs` (port from FskDemod, no new dependency)
Add `SkyRoof/Satellites/SatyamlDb.cs`. Port the tiny line-based parser from
`FskDemod/IO/SatyamlDb.cs` (no YamlDotNet dependency; SkyRoof has no YAML lib).
Extend it to also resolve the per-transmitter `telemetry` decoder:
- Parse the top-level `data:` map (key name -> telemetry decoder string).
- For each transmitter, resolve its `data:` key references to a single real decoder
  (discard `ax25`/`csp`; warn + keep first if >1).
- Build `Dictionary<int norad, List<GrSatsInfo>>`.
- Keep `Find(norad, baud)` "nearest baudrate" logic to pair a yaml transmitter to a
  SatNOGS transmitter.

### 3. Data model — nullable `GrSatsInfo` on the transmitter
In `SkyRoof/Satellites/SatnogsDbTransmitter.cs`:
```csharp
public GrSatsInfo? gr_sats { get; set; }   // null when no satyaml match
```
plus the `GrSatsInfo` class above. Nullable ⇒ absent/`null` in JSON for the majority of
transmitters with no yaml entry; no schema break for existing readers.

### 4. Import — match by NORAD, attach per transmitter
In `SatnogsDb.cs`, add `ImportSatyaml()` and call it from `ImportAll()` after
`ImportSatnogsTransmitters()` (so transmitters exist):
- Load the parsed `SatyamlDb` from `Downloads/satyaml/`.
- For each satellite with a `norad_cat_id` that has a yaml entry, for each of its
  `Transmitters`, pick the nearest-baud `GrSatsInfo` (`tx.baud` -> `Find`) and assign
  `tx.gr_sats`.
- Non-fatal if the satyaml folder is missing. Leave `CheckFilesPresent()` unchanged
  (do not make satyaml required).

### 5. `Satellites.json` output
`SaveToFile()` already serializes the whole graph via Newtonsoft, so `gr_sats` flows through
automatically — no serializer change. Result per transmitter:
```json
{ "description": "1k2 BPSK downlink", "mode": "BPSK", "baud": 1200,
  "gr_sats": { "modulation": "DBPSK", "baudrate": 1200, "deviation": null,
               "framing": "AO-40 FEC", "telemetry": "funcube" } }
```

## Follow-up (out of scope, noted)
FskDemod's `SatnogsDb.Load` and `ParamResolver` would later be updated to read `gr_sats`
straight from `Satellites.json`, retiring the hardcoded
`c:\Proj\Forks\gr-satellites\python\satyaml` path in `FskDemod/IO/SatyamlDb.cs`. Real telemetry
decoding (porting `construct` beacon definitions to C#) is a separate future project; the
`telemetry` field is only a label/selector for it.

## Risks / decisions baked in
- **NORAD-only matching**: yaml is keyed by `norad`; satyaml's name-based fallback is dropped
  since SkyRoof transmitters always have a satellite NORAD. Satellites missing `norad_cat_id`
  get no enrichment.
- **Multiple yaml transmitters per satellite** are disambiguated by nearest baud — same
  heuristic FskDemod already trusts.
- **Non-fatal enrichment**: any satyaml download/parse failure must not break the existing
  satellites/transmitters/tle/JE9PEL import.
- **`telemetry` `>1` guard**: importer logs a warning and keeps the first if a single
  transmitter ever resolves to more than one real decoder (not seen in the current snapshot).
