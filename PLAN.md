# RaiderLite — C# port of the RomRaider ECU map editor

Goal: a Windows ECU calibration editor that reads RomRaider XML definitions,
opens/saves raw binary images, and edits tables. Editing only: no logger,
no log analysis, no graphs or 3D plots.

Reference source: `C:\Users\donour\source\RomRaider` (Java/Swing).
Test material: `C:\Users\donour\source\Lotus-T4e\lotusecu-tools\romraider-defs`
plus the `calrom.bin` dumps in `lotusecu-tools\dump`.

## Terminology

RomRaider's table types, all supported:

| RomRaider type | What it is                          | Shown as                         |
|----------------|-------------------------------------|----------------------------------|
| `1D`           | scalar or plain array               | one row of cells                 |
| `2D`           | one axis + data (a curve)           | axis row above the data row      |
| `3D`           | X axis × Y axis grid (a map)        | grid: X axis on top, Y on left   |
| `Switch`       | byte patterns picked by name        | check boxes                      |
| `BitwiseSwitch`| named single bits                   | check boxes                      |

Axes can be `X Axis` / `Y Axis` (read from the ROM) or `Static X Axis` /
`Static Y Axis` (text labels from `<data>` elements).

## Solution layout

```
RaiderLite.slnx
├─ RaiderLite.Core/     class library: no UI dependencies
│   ├─ Expressions/     JEP-compatible expression parser/evaluator
│   ├─ Model/           Rom, RomId, Scale, Table*, DataCell, PresetManager
│   ├─ Definitions/     XML definition loader (DOMRomUnmarshaller + TableScaleUnmarshaller port)
│   ├─ Checksums/       ChecksumManager + STD/ALT/ALT2/BYTEXOR/COPY/E38PCM/MOTRONIC*, Subaru "Checksum Fix"
│   └─ Clipboard/       RomRaider-compatible copy/paste text formats ([Table1D]/[Table2D]/[Table3D]/[Selection1D]/[Selection3D])
├─ RaiderLite/          WinForms app (MDI)
│   ├─ MainForm         menus, ROM/table tree with filter, table toolbar, status bar
│   ├─ TableForm        MDI child per open table: grid, preset/switch panel, description
│   ├─ TableGrid        DataGridView subclass: heat-map colouring, changed-cell borders
│   ├─ DefinitionManagerForm, SettingsForm, RomPropertiesForm, CompareForm
│   └─ AppSettings      JSON settings in %APPDATA%\RaiderLite
├─ RaiderLite.Tests/    xUnit: expressions, byte codec, definition parsing against the
│                       real Lotus defs, load/edit/save round-trips against the real bins
├─ Installer/           WiX 6 MSI (Package.wxs) harvesting a framework-dependent Release publish
└─ Directory.Build.props  product version shared by the app and the installer
```

## Behaviour to replicate (from the RomRaider source)

### Definition parsing
- `<roms>` → `<rom base="...">` inheritance chains (base parsed first, child overlays; loop detection).
- `<romid>`: xmlid, internalidaddress/internalidstring (ASCII or `0x` hex bytes; `force` with address -1),
  filesize (`b`/`kb`/`mb`), `noRamOffset`, `offset`, memmodel `endian=` override, the rest as display info.
- RAM offset = filesize − actual file length, unless `noRamOffset`; `beforeram="true"` tables ignore it.
- Tables merge by name across the chain; `base="other table"` copies a table; `omit="true"` removes it.
- A table that has no `storageaddress` anywhere in the chain is not created (as in RomRaider).
- Attributes: type, storagetype (`uint8/16/32`, `int8/16/32`, `float`, `movi20`, `movi20s`), endian,
  storageaddress (hex, `0x` optional), sizex/sizey, flipx/flipy/swapxy, skipCells, mask, dataLayout
  (`bosch_subtract`), userlevel, locked, logparam (kept, unused), category (`//` = sub-category), description.
- `<scaling>` (multiple per table, keyed by category), `<scalingbase>` + `base=` inheritance,
  expression/to_byte/format/units/fine+coarse increment/min/max. Scale round-trip validation warning.
- `<state name data>` for switches, `<bit name position>` for bitwise switches, `<data>` for static axes,
  `<description>` element, `<checksum type=... />`.
- XInclude (`xi:include href=`) expansion.

### Binary codec
- Big/little endian, signed/unsigned 8/16/32, IEEE float (RomRaider's legacy float-endian quirk),
  MOVI20/MOVI20S, bit masks (read shifted, write only bits inside the mask), Bosch-subtract layout,
  3D cell ordering with swapxy/flipx/flipy/skipCells.
- Cells write through to the image immediately; cells sharing an address are kept in sync.
- Value clamping to the storage type's range.

### Editing (table window + table toolbar)
- Select cells (mouse drag, shift/ctrl, keyboard). Type a number → "set value" box → Enter applies to selection.
- Increment/decrement fine & coarse (`+ _ [ ] Ctrl+↑↓ Ctrl+Shift+↑↓`), multiply (`*`, Ctrl+Enter).
- Interpolate (Shift+I), vertical (Shift+V), horizontal (Shift+H), axis-aware like RomRaider.
- Real↔raw via expression / to_byte; approximation search when to_byte is missing.
- Scale selector per table (switches axes to the same category when possible).
- Copy selection / copy table / paste in RomRaider's clipboard formats (interoperable with RomRaider and Excel).
- Undo selected, undo all (back to revert point), set revert point. Plus a real Ctrl+Z undo stack.
- Heat-map colouring (min→max HSB blend), red/blue borders for increased/decreased cells,
  warning colour for out-of-range values, static axis labels.
- Switch / bitwise-switch / preset check boxes with description.
- Compare a table against another open ROM's table or against its original values (absolute or %).
- User level (1–5): tables above the level are read-only and optionally hidden.

### Application
- Open one or more images; definition auto-detected by internal ID across the configured definition files;
  if none match, let the user pick a definition and force-load.
- Save / Save As (checksums updated on save), close with unsaved-changes prompt, recent files.
- ROM tree: ROM → categories → tables, filter box, double-click to open.
- ROM properties (romid info), table properties (address, storage, scale…).
- Definition manager (add/remove/reorder definition files), settings dialog.

## Phases
1. Core model + expression engine + definition loader + codec, with tests against real defs/bins. — done
2. WinForms editor: tree, table windows, toolbar, editing ops, clipboard, open/save. — done
3. Checksums, compare, undo stack, properties dialogs, settings/definition manager. — done (first pass)

## Status / notes
- `dotnet test` runs 218 tests. Tests using the Lotus files or RomRaider's test definitions skip
  automatically if those paths don't exist (override with `RAIDERLITE_LOTUS_ROOT` / `RAIDERLITE_ROMRAIDER_ROOT`).
- Settings live in `%APPDATA%\RaiderLite\settings.json`; `RAIDERLITE_SETTINGS` overrides the path.
- Image paths passed on the command line (or dropped on the window) are opened.
- Deliberate deviations from RomRaider (bug fixes): a `<scaling base=...>` without `to_byte` keeps the
  base's to_byte; the table-name address lookup isn't applied to axes; masked writes honour little-endian;
  the approximation search returns its best match; preset values handle 32-bit/signed data; an
  unparsable to_byte falls back to approximation instead of failing.
- Rescale Axis (not in RomRaider; Edit menu of a table window, or Axis > Rescale on the ribbon):
  new breakpoints for one axis of a curve or map (stretch from the first breakpoint, even spacing,
  or typed), with the data resampled so ECU-style lookups (linear between breakpoints, clamped at
  the ends) give the same values at every new breakpoint. Beyond the old range the edge values are
  held (the ECU behaves as before) or the end slopes extrapolated. Repeated breakpoints (padding,
  steps) are kept. The dialog previews the stored axis and the largest change of any lookup within
  the old range; one undo step. Code: `RaiderLite.Core/Model/AxisRescaler.cs`.
- Known definition defect: `T4E090_defs.xml`, table "inj: afr base", Lambda scaling has
  `to_byte="((x14.6)-5)*20"` (missing `*`). RaiderLite warns and approximates.

## Releasing
1. Bump `<Version>` in `Directory.Build.props`. Windows Installer only compares the first three
   fields, so change major, minor or patch.
2. `dotnet build Installer -c Release` (or a Release build of the solution; Debug builds skip the
   installer). Output: `Installer\bin\Release\RaiderLite-<version>-x64.msi`. Copy released MSIs
   elsewhere: the next build deletes the previous one.

The MSI installs per-machine to `Program Files\RaiderLite` with a Start menu shortcut and needs the
.NET 10 Desktop Runtime (the app itself prompts with a download link if it is missing). Running a
newer MSI upgrades in place: the old version is removed inside the same transaction, so a failure
restores it. An older MSI refuses to install over a newer one; a rebuilt MSI with the same version
replaces the installed one. Settings in `%APPDATA%\RaiderLite` are never touched. Never change the
`UpgradeCode` in `Package.wxs`. No network updates, by design.

## Possible next steps
- Table search by description, per-table "favourites".
- Remember open tables per image; window positions.
- XDF import (RomRaider's conversion layer) if ever needed.

Out of scope: logger, dyno, RAM tune, XDF/VDF conversion layers, live data overlay, dataflow simulations.
