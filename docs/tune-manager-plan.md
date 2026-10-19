# Tune manager: plan

Status: phases 1 (model and storage), 2 (browsing) and 3 (editing workflow) implemented
2026-10-18; phases 4 and 5 to do. See "Implementation status" at the end.

**Decisions (2026-10-18):**
- **`.crp` files are not read.** They're encrypted, so RaiderLite treats them as opaque
  attachments.
- **Duplicates are kept** as revisions marked "no change".
- **No flash tracking.** RaiderLite doesn't record whether or when a revision was flashed.
- **Backups are skipped:** `- bak1` and `- Copy` files aren't imported.
- **Conversion writes a new folder.** The old folders are left untouched.

A directory-based manager for the calibrations of each car: every change is a **revision**, a
complete image with its notes and files, linked to the revision it was based on. RaiderLite shows
the history as a timeline, works out which tables each revision changed, keeps revisions others
are based on from being changed after the fact, and imports the existing folder-per-change layout.

The design is based on the sample data in `sample_tunes/T6e-tunes/2011 EvoraNA` (the other cars
come later). The next section describes what is in that data, because several design decisions
follow from it.

## What the EvoraNA data shows

Read-only analysis: every image was loaded with RaiderLite.Core (`T6-V00BU_defs.xml`, 1,097
tables) and compared table by table.

### The current convention

```
2011 EvoraNA\
  B132E0091.CRP             stock full flash (program + calibration), 593 KB
  B132E0091_unlocked.crp    the same, unlocked
  B132E0091_TAB.cpt         stock calibration, 25,050 bytes: the image that gets edited
  T6EGLBU01_BIN.cpt         program image, 567 KB
  20260210-silvercarbase\   one folder per change, named date-car[-purpose]
    20260210-silvercar.cpt              the calibration
    20260210-silvercar_converted.crp    the flash file made from it (CRP08, encrypted)
    notes.txt                           the whole changelog so far, newest first
    HighSpeed_*.csv, *.zip              logs taken while driving it
    LearnedData_T6_*.bin                ECU learned-data reads (48 KB, has its own definition)
  ...30 folders, 40 calibrations, 21 flash files
```

`notes.txt` is cumulative. Each folder's copy repeats every earlier entry and adds a new dated
section at the top, ending with `PREV: unlocked`:

```
20260808

* ign base manual: add more timing at 3500rpm
* sensor maf scaling: update
```

Section headings vary (`20260725:`, `20261006 - rich test (diagnostic, based on 20261001 id1050)`).
Some bullets are empty (`* `), and one file starts with a `TODO:` line. The diagnostic folders
(`-richtest`, `-injangle`) have standalone notes with a `Base:` line instead of the cumulative log.

### What the convention doesn't capture

- **Branches are common, not rare.** The data has a closed-loop test that was abandoned
  (`20260313-…closedlooptesting`), a rejected attempt (`20260803 -- TOO LEAN`) beside its retry,
  a rich test, a four-way injection-angle sweep, a `broken` file and a `warmupchangeonly` variant.
- **Several images per folder.** `20261001` holds the 1 October and 6 October calibrations;
  `20261007`, `20261008` and `20261009` hold backups (`- bak1`, `- Copy`) and variants next to
  the main file.
- **Duplicates.** Four calibrations are byte-identical to another one: `20260314` = `20260211`,
  `20260608` = `20260518`, `20260827` = `20260826`, and `20250724\B132E0091_TAB.cpt` = the stock
  file.
- **Calibrations were changed after they were flashed.** In 6 of the 21 folders with a flash
  file, the `.cpt` was edited after the `.crp` was made from it (`20260211`, `20260807`,
  `20260822`, `20260826`, `20261007`, `20261008`). What went into the car is not the saved
  calibration. The 6 October base used for the injection-angle sweep was also edited afterwards
  (flow rate 10000 to 10500, angle 64 to 178), so the sweep's real base no longer exists as a
  file. (Found during this analysis only. RaiderLite itself won't read `.crp` files.)
- **Notes and changes drift apart.** The `20260608` section describes the logger change that is
  actually in `20260627`. `20260725` mentions the AFR map but only the MAF table changed.
  `20260805` and `20260807` changed tables the notes don't mention. `20260926` has no notes but
  changed the sport rev limit. The notes for `20261007` say the injection angle was zeroed, but the
  file has it scaled to 26, which is what the `20261008` notes describe.
- **The calibration is the same size in every folder** (25,050 bytes). No bytes outside the
  definition's tables ever changed, so table-level diffs explain every change.

### Design consequences

1. A revision is **one image**, not one folder. Backups and variants become their own revisions.
2. **Branches must be easy to see.** Dead ends should fold away, and siblings should be visibly
   siblings.
3. **A revision others are based on must never change.** Once a new revision is made from it, it
   locks, and any further change goes into the new one. Making the next revision as soon as an
   image goes into the car keeps what was flashed; that habit would have prevented all six
   mismatches above.
4. **Compute changes; don't rely on memory.** Show the changed tables next to the notes, and point
   out tables the notes don't mention.
5. Keep **each revision's own note**, and generate the cumulative changelog when it's wanted.
6. **Logs, flash files and learned data belong to the revision that was in the car.** They are kept
   with it.

## Concepts

| Term | Meaning |
|---|---|
| **Library** | A folder chosen in Settings (e.g. `Documents\Tunes`). Each subfolder that RaiderLite manages is a car. |
| **Car** | One ECU's calibration history, e.g. "2011 Evora NA", fixed to one definition (`T6-V00BU`). |
| **Revision** | One complete image plus its notes and files. It has a number (`037`), a date, a label and a parent. Revision `000` is stock. |
| **Parent** | The revision it was based on. Two revisions with the same parent are a branch. |
| **Locked** | Stock, or other revisions are based on it. A locked revision opens read-only. |
| **No change** | A revision whose image is identical to its parent's: a duplicate kept for its notes, files and date. Shown greyed with a "no change" tag. Computed from the hashes, not stored. |
| **Rejected** | A verdict with a reason ("too lean"). Rejected revisions are drawn struck through and can be hidden. |
| **Hidden** | Folded away: backups and abandoned variants. Still on disk, still comparable. |

## Storage format

```
Tunes\                                   ← library (Settings > Tune library)
  2011 Evora NA\
    car.json                             ← RaiderLite's index for this car
    000 2025-07-24 Stock\
      B132E0091_TAB.cpt                  ← the image (always exactly one per revision)
      B132E0091.CRP                      ← other files: reference images, flash files, logs...
      B132E0091_unlocked.crp
      T6EGLBU01_BIN.cpt
    001 2025-07-24 Unlock, disable rear O2\
      B132E0091_disable_rear_o2.cpt
      B132E0091_disable_rear_o2.crp
      notes.txt                          ← this revision's note only
    ...
    037 2026-10-16 Race TC slip target\
      20261016-silvercar-id1050.cpt
      notes.txt
```

- **One folder per revision**, as you do now, because revisions have files of their own. The
  folder name (`number date label`) is for people. RaiderLite identifies revisions by number
  through `car.json`, so renaming the label part is harmless.
- **The image keeps its file name.** Imported images keep their names, so they still match
  any flash files made from them. New revisions are named `yyyyMMdd-<car>-<number>.cpt`, matching
  the existing style.
- **`notes.txt` holds only that revision's note**, as plain text you can edit outside RaiderLite.
- **`car.json`** holds what can't live in file names:

```json
{
  "format": 1,
  "name": "2011 Evora NA",
  "definition": "T6-V00BU",
  "revisions": [
    {
      "number": 12, "folder": "012 2026-08-03 MAF rescale", "image": "20260803-silvercar.cpt",
      "sha256": "…", "created": "2026-08-03T22:15", "parent": 11, "label": "MAF rescale",
      "rejected": "Too lean", "hidden": false,
      "importedFrom": "20260803-silvercarbase  -- TOO LEAN\\20260803-silvercar.cpt"
    }
  ]
}
```

- **The image's hash** lets RaiderLite notice an image changed outside the app. A locked revision
  whose hash no longer matches is flagged "changed outside RaiderLite", never silently trusted.
- **Missing or extra items:** a folder without an entry shows up as an unindexed revision that
  needs a parent. A missing `car.json` can be rebuilt by inferring parents, the same way the
  importer does.
- **Writes are atomic** (temporary file, then replace), like image saves today.

## UX

### Main window: a Tunes tab beside Tables

The left panel gets two tabs. **Tunes** shows the current car's timeline and the selected
revision's details. **Tables** is today's table tree. Opening a revision switches to Tables;
the title bar and a breadcrumb show which revision is open.

```
┌ RaiderLite — 2011 Evora NA › 037 Race TC slip target ─────────────────────────────────────┐
│ [ribbon … | Image: Open · Save · New Revision … ]                                         │
├────────────────────────────────────┬──────────────────────────────────────────────────────┤
│ [ Tunes ] [ Tables ]               │                                                      │
│ 2011 Evora NA · T6-V00BU        ▾  │        (table windows, as today)                     │
│ Filter notes and tables…           │                                                      │
│ ● 037 16 Oct Race TC slip target ✎ │                                                      │
│ ● 036  9 Oct Inj eff, warmup, tip… │                                                      │
│ ● 035  9 Oct Warmup change only    │                                                      │
│ ● 034  8 Oct Inj eff re-axis       │                                                      │
│ ● 033  7 Oct Inj eff shift      🔒 │                                                      │
│ ⋮                                  │                                                      │
│ ├▸ 026–030 6 Oct Rich test, inj a… │  ← a finished side branch, folded to one line        │
│ ● 025  1 Oct ID1050XDS injectors   │                                                      │
│ ⋮                                  │                                                      │
│ ● 000 24 Jul 2025 Stock         🔒 │                                                      │
├────────────────────────────────────┤                                                      │
│ 036 · Inj eff, warmup, tip-in      │                                                      │
│ 9 Oct 2026 · based on 035          │                                                      │
│ Notes                              │                                                      │
│ * inj efficiency: retune from logs │                                                      │
│ * inj warmup factor manual: …      │                                                      │
│ Changed from 035 (6)               │                                                      │
│  inj: deadtime base       4 cells  │                                                      │
│  inj: efficiency        212 cells  │                                                      │
│  load: alphaN base   ⚠ not in notes│                                                      │
│ Files (2)                          │                                                      │
│  HighSpeed_20261012_….csv          │                                                      │
│ [Open] [New Revision] [⋯]          │                                                      │
└────────────────────────────────────┴──────────────────────────────────────────────────────┘
```

### The timeline

- **Newest first,** because current work is at the top. Each row shows the number, date, label
  and status icons: 🔒 locked, struck-through
  text for rejected.
- **The main line is drawn straight.** It runs from stock to the newest revision: at each fork it
  follows the child whose branch holds the newest revision, so the line being worked on stays
  straight however long old test branches are. Side branches step one lane right, git-style,
  with at most two or three lanes in practice.
- **Finished side branches fold to one line** (`▸ 026–030 Rich test, inj angle ×4`). This applies
  when nothing in a branch is newer than a week. Click to expand.
- **Hidden and rejected revisions** sit behind a "Show hidden" toggle in the ⋯ menu.
- **The filter** matches labels, notes text and changed-table names, so "maf" finds every
  revision that touched the MAF table, whether or not the notes say so.

### Revision details

Below the timeline, for the selected revision:

- **Heading:** number, label (click to rename), date, and parent (click to select it).
- **Notes:** an editable text box, saved to `notes.txt` when it loses focus.
- **Changed from parent:** each table with a count of changed cells. Double-click opens the
  parent's and this revision's table side by side, using the existing comparison windows. A ⚠
  marks tables the notes don't seem to mention, by loose matching of the table name. Clicking the
  ⚠ adds a `* <table>: ` line to the notes.
- **Files:** attachments with type icons. Double-click opens a file and right-click offers Show
  in Explorer. Drop files here to copy them into the revision folder.
- **Buttons:** Open, New Revision, and ⋯ (Compare with…, Reject…, Hide, Copy
  Changelog, Show in Explorer).

### Opening and editing

- **Open** (double-click or Enter) loads the revision's image like any other image. Recent Files
  remembers revisions too, by path.
- **An editable revision** saves in place with Save (Ctrl+S). The hash in `car.json` and the
  changed-table list update on save.
- **A locked revision** opens read-only. A bar under the ribbon reads:
  `033 is locked: revisions are based on it.   [New Revision from 033]`
  Typing in a table, pasting or any other edit shows the same choice as a dialog, with New
  Revision as the default. Choosing it creates the child, reopens the same table windows in
  the same places on the new revision, and applies nothing until you edit again.
- **Save As** still works and writes a loose file, which is useful for handing an image to
  someone.

### New revision

The **New Revision** dialog (Ctrl+Shift+N) asks for:

- **Based on:** the selected or open revision.
- **Label:** pre-filled with the parent's label, selected so typing replaces it.
- **Note:** optional.

It creates `NNN yyyy-MM-dd Label\`, copies the image, writes `car.json` and opens the image.
That one action replaces copying a folder, renaming files and pasting the old notes.

**Variants** (later): "New Variants…" makes several siblings at once, as in the injection-angle
sweep.

### Notes and changelog

- **New revisions start with an empty note.** At the first save, RaiderLite fills it with one
  `* <table>: ` line per changed table, in the bullet style you already use. You complete each
  line with the reason.
- **Copy Changelog** generates the cumulative format you use today, from stock to the selected
  revision along its parents, newest first. It's useful for sharing or for a printed log.

### Flash files

RaiderLite doesn't flash, doesn't track flashes, and doesn't read or write `.crp` files. A flash
file made with your usual tool can be dropped onto the revision's files like any other attachment.

### Comparing

- Select two revisions (Ctrl+click) and choose **Compare**. This opens the existing Compare
  Images list of differing tables, without opening either image for editing.
- The details of a revision always list what changed from its parent.
- Inside a table, Compare > another revision of the same car works like comparing with another
  open image.

### Library and cars

- **Settings > Tune library:** the folder. The Tunes tab lists the cars in a drop-down at the top.
- **New Car…** asks for a name and the stock image. It copies the image into `000 … Stock` and
  reads the definition from it.
- **Import…** converts an old-style folder (see below).
- **Opening a loose image** that isn't in a car works as it does today. If its ROM ID matches a
  car, the info bar offers "Add to 2011 Evora NA as a new revision".

### Import (converting the current convention)

**Import Tunes…** asks for an old-style car folder and a destination car name in the library.
It **only reads the source folder** and writes a new car, so nothing in the original folder is
moved, renamed or changed.

1. **Find images.**
   - Every file that matches a definition by size and ROM ID becomes a candidate revision.
   - Files whose names mark them as backups (`bak`, `backup`, `- Copy`) are skipped and listed in
     the report. In the EvoraNA folder that leaves 38 of the 40 `.cpt` files.
   - Program images (`T6EGLBU01_BIN.cpt`), `.crp` flash files and everything else are attachments.
2. **Keep duplicates.** A byte-identical image becomes a revision of its own, parented to the
   identical one, so it shows as "no change" with its own note and files. This applies to
   `20260314`, `20260608`, `20260827` and the copy of the stock file in `20250724`.
3. **Flash files** (`.crp`) are copied as attachments only.
4. **Order and infer parents.**
   - Revisions are numbered by the image's modified time. That's the only clock in the data that
     can be relied on: folder dates and file-name dates disagree in places, e.g.
     `20260314\20260214-silvercar.cpt` saved on 19 Feb.
   - Each revision's parent is the older revision it differs from in the fewest tables, then the
     fewest bytes. Ties go to the most recent, which keeps a duplicate's descendants on one line.
   - **Variants:** images in one folder with no main file are siblings, sharing the best parent
     outside the folder. This applies to the four inj-angle files.
   - A row is flagged **check** when the second-best parent is nearly as close, or when the best
     one still differs in 3 or more tables.
5. **Split the notes.** A revision's note is the dated sections in its `notes.txt` that its
   parent's `notes.txt` doesn't have. Empty bullets and the `PREV:` line are dropped. Standalone
   notes (no cumulative history) are kept whole. A note whose date doesn't match its image is
   flagged.
6. **Labels and status:**
   - **Labels** come from the folder or file suffix, after removing the words every folder shares
     (`silvercarbase`, `silvercar`), e.g. "closed-loop testing", "rich test", "inj angle 450".
     Otherwise the label is the first note bullet or the changed tables.
   - `TOO LEAN` and `broken` mean rejected, with the text as the reason.
7. **Attach files.** Logs, learned data and other files go to the revision of their folder. When a
   folder has several images, a log timestamped in its name (`HighSpeed_20261006_202405`) goes to
   the newest image saved before it, which is the one that was in the car.
8. **Review, then write.**
   - A review window lists every proposed revision: include, number, date, label, parent
     (editable), changes, rejected, hidden, files, and source path.
   - A preview of the timeline sits beside it.
   - Nothing is written until you click Import, and then only into the new car folder.
   - Afterwards a report lists the skipped backups, the duplicates and the flagged rows.

#### Expected result for 2011 EvoraNA

These are the importer's rules applied by hand to the analysis above. `?` marks parents the
importer would flag for checking. Folded branches are shown indented.

38 revisions. Skipped backups: `20261007\…- just angle reaxis - Copy.cpt` and
`20261008\…- bak1.cpt`.

| # | Date | Label | Parent | Changed tables | Source folder |
|---|---|---|---|---|---|
| 000 | 2025-07-24 | Stock | – | – | (top level) |
| 001 | 2025-07-24 | No change | 000 | 0 | 20250724 |
| 002 | 2025-07-24 | Unlock, disable rear O2 | 001 | 6 | 20250724 |
| 003 | 2026-02-10 | Silver car base | 002 | 10 | 20260210 |
| 004 | 2026-02-19 | Ign, knock, VVT rescale | 003 | 4 | 20260211 |
| 005 | 2026-02-19 | No change | 004 | 0 | 20260314 |
|   006 | 2026-03-13 | Closed-loop testing | 005 | 10 | 20260313 |
| 007 | 2026-03-27 | Disable P0139, P0159 | 005 | 2 | 20260327 |
| 008 | 2026-05-18 | Radiator fan temps | 007 | 6 | 20260518 |
| 009 | 2026-05-18 | No change | 008 | 0 | 20260608 |
| 010 | 2026-06-27 | High-speed logger | 009 | 1 | 20260627 |
| 011 | 2026-07-12 | MAF for 3.5" intake | 010 | 1 | 20260712 (4 logs) |
| 012 | 2026-07-25 | MAF rescale | 011 | 1 | 20260725 (1 log) |
| 013 | 2026-07-26 | MAF rescale | 012 | 1 | 20260726 |
|   014 | 2026-08-03 | MAF rescale, rejected: too lean | 013 | 1 | 20260803 -- TOO LEAN (2 logs) |
| 015 | 2026-08-04 | MAF rescale, second try | 013 ? | 1 | 20260803 |
| 016 | 2026-08-05 | MAF, alphaN, AFR peak 12.2 | 015 | 3 | 20260804 |
| 017 | 2026-08-05 | alphaN trim | 016 | 2 | 20260805 |
| 018 | 2026-08-07 | Ign base, MBT axes | 017 | 4 | 20260807 |
| 019 | 2026-08-22 | 93 octane timing | 018 | 4 | 20260808 (logs) |
| 020 | 2026-08-23 | alphaN, MAF from logs | 019 | 2 | 20260822 (learned data) |
| 021 | 2026-08-27 | MAF idle | 020 | 1 | 20260826 |
| 022 | 2026-08-27 | No change | 021 | 0 | 20260827 (learned data) |
| 023 | 2026-09-01 | MAF, ign, AFR, rev limit 7350 | 022 | 5 | 20260901 |
| 024 | 2026-09-26 | Sport rev limit | 023 | 1 | 20260926 |
| 025 | 2026-10-01 | ID1050XDS injectors | 024 | 10 | 20261001 (5 logs) |
|   026 | 2026-10-06 | Rich test | 025 | 6 | 20261006-richtest |
|   027–030 | 2026-10-06 | Inj angle 0, 360, 540, 450 | 026 ? | 4–5 | 20261006-injangle |
| 031 | 2026-10-06 | ID1050, flow 10500, angle 178 | 030 ? | 2 | 20261001 |
|   032 | 2026-10-07 | Broken, rejected | 031 ? | 5 | 20261007 |
| 033 | 2026-10-07 | Inj eff shift, inj angle | 026 ? | 4 | 20261007 |
| 034 | 2026-10-08 | Inj eff re-axis and retune | 033 | 1 | 20261008 |
| 035 | 2026-10-09 | Warmup change only | 034 | 1 | 20261009 |
| 036 | 2026-10-09 | Inj eff, warmup, tip-in, deadtime, alphaN | 035 | 6 | 20261009 |
| 037 | 2026-10-16 | Race TC slip target | 036 | 2 | 20261016 |

The 6–7 October rows are where the data is most tangled. The sweep's base was overwritten, and
the 7 October file mixes the rich test's OBD threshold with the 1 October flow rate. Expect to set
those parents by hand in the review window.

## Implementation

### Core (`RaiderLite.Core/Tunes/`, no UI)

| Type | Job |
|---|---|
| `TuneLibrary` | Lists cars in the library folder; creates cars. |
| `Car` | Loads and saves `car.json` and notes; revision tree (parent, children, main line, lanes); new revision; reject and hide; hash check; atomic writes. |
| `Revision` | Number, folder, image path, label, dates, parent, rejected, hidden, attachments. |
| `ImageDiff` | Table-level diff of two images for one definition: a per-definition map from byte to tables, so a diff is a byte compare plus lookup, with no full `Rom` load. Results are cached by hash pair. `CompareImagesForm` moves onto it. |
| `TimelineLayout` | Lane assignment and folding of finished branches, kept pure so it can be tested. |
| `Changelog` | Generates the cumulative notes format. Matches notes to table names loosely, for the ⚠ hints. |
| `LegacyNotes` | Parses cumulative `notes.txt` into dated sections. |
| `LegacyImporter` | Builds an `ImportPlan` from an old-style folder (steps 1–7), then `Execute(plan, destination)`. It refuses a destination inside the source and an existing non-empty folder. |

### App (`RaiderLite/`)

| Piece | Job |
|---|---|
| `TunesPanel` (UserControl) | Car drop-down, filter, timeline, details splitter; the timeline's menu. |
| `CarSession` | The shown car: each revision's changes (worked out off the UI thread) and notes; search matching. |
| `RevisionCommands` | Delete, rename, reject, copy changelog, add files: the prompts, confirmations and error messages. They report whether anything changed; the panel shows the result. |
| `RevisionTimeline` | Owner-drawn list: lanes, icons, folded rows, multi-select for Compare. |
| `RevisionDetails` | Notes box, changed-tables list with ⚠, files list (drag and drop), buttons. |
| `NewRevisionForm`, `ImportTunesForm` (review grid with timeline preview), `NewCarForm` | Dialogs, on `DialogBase`. |
| `MainForm` (tune-manager members in `MainForm.Tunes.cs`) | Tunes and Tables tabs on the left panel; `OpenRevisionAsync`; locked-revision info bar; title and breadcrumb; reopening table windows on a new revision. |
| `Rom.IsReadOnly` | Enforced in Core (cell edits refused), so `TableForm`, the grid, paste and the ribbon all respect it. An edit attempt raises an event that MainForm answers with the New Revision prompt. |
| `RibbonBar` | New Revision in the Image group (Ctrl+Shift+N). |
| `AppSettings` | `TuneLibrary`, `LastCar`, show-hidden toggle. |

### Phases

1. **Model and storage:** `Car`, `Revision`, `car.json`, notes, `ImageDiff` (and moving Compare
   Images onto it), `TimelineLayout`. Tests use synthetic images and inline definitions in a temp
   folder.
2. **Browsing:** Tunes tab, timeline, details, Open, Compare, folded branches, filter.
3. **Editing workflow:** `Rom.IsReadOnly` and locking, New Revision, the edit-a-locked-revision
   prompt, notes with suggested lines and ⚠ hints, Reject, Hide, files, Copy
   Changelog, New Car.
4. **Import:** `LegacyNotes`, `LegacyImporter` and the review window. Unit tests build a
   synthetic old-style folder. The real check is importing 2011 EvoraNA into a new folder and
   comparing the result with the table above together with you.
5. **Later:** New Variants, combining branches table by table, and the other cars.

Tests stay self-contained: nothing in `RaiderLite.Tests` reads `sample_tunes`.

## Still open

Smaller choices, with the default I'd use if you have no preference:

- **Library location:** `Documents\RaiderLite Tunes`, changeable in Settings.
- **New image names:** `yyyyMMdd-<car>-<number>.cpt` (e.g. `20261018-evora-na-038.cpt`).

## Implementation status

### Phase 1: model and storage (done)

- `RaiderLite.Core/Model/ImageDiff.cs`
  - `ImageDiff.Compare(template, a, b)` compares two images of one definition without loading
    either. Only differing bytes are decoded, through a loaded template's cells. It handles axes
    (counted for their table), aliases, masks (bits outside the mask aren't a change) and the
    Bosch subtract layout, and reports changed bytes no table covers.
  - `ImageDiff.CompareRoms` is the by-name comparison that Compare Images now uses.
  - `DataCell` reads a value from any image's bytes (`ReadValue(byte[])`).
- `RaiderLite.Core/Model/SafeFile.cs`: atomic writes (temporary file, flush, replace). Image saves
  in `MainForm` use it too.
- `RaiderLite.Core/Tunes/`
  - `Car`: `car.json` (format 1), revisions, lineage, locking (`LockReasonOf`), "no change", new
    revision, notes, label (renames the folder), parent (refuses loops), reject, hide,
    image hash check (`CheckImage`, `ImageSaved`), attachments and unindexed folders.
  - `Car.Open` refuses paths that leave the car folder, duplicate numbers or folders, parent loops,
    and newer formats.
  - `Revision`, `TuneLibrary` (`FindCars`, `FindCarOfImage`).
  - `TimelineLayout.Build`: lanes, pass-through lines and bends, and folding of finished side
    branches, grouped by parent. `TimelineLayout.NodesFor(car, showHidden)` skips hidden and
    rejected revisions and attaches their children to the nearest shown ancestor.
- Tests: `ImageDiffTests` (including 300 random edits checked against full loads), `CarTests`
  (temp folders) and `TimelineLayoutTests`. All synthetic.

### Phase 2: browsing (done)

- **Left panel:** Tunes and Tables tabs (also View > Tunes / Tables). The last tab used is
  remembered, and Ctrl+F switches to Tables.
- **`TunesPanel`:**
  - **Header:** the car drop-down (the library's car folders, last one remembered) and a ⋯ menu:
    Show Hidden and Rejected, Refresh, Open Car Folder, Open Library Folder, Choose Library Folder.
  - **Empty states:** a missing library, an empty one, or a car whose index can't be read shows a
    message with Choose Library Folder.
  - **Filter:** every word must match the number, label, folder, notes, rejection or a changed
    table's name. Matches show as a plain list, newest first.
- **`RevisionTimeline`** (owner-drawn list):
  - The lane graph is drawn from `TimelineLayout`.
  - **Nodes:** filled; hollow for "no change" and for hidden or rejected revisions; ringed when
    the image is open.
  - **Text:** the number and date, the label (struck through when rejected, "· no change"), and
    a lock icon.
  - **Folds:** a fold row lists its revisions. Double-click or Enter expands it; "Fold Finished
    Side Branches" folds it again.
  - **Interaction:** tooltips with the details and the first lines of the notes, multi-select,
    and a context menu.
- **`RevisionDetails`:**
  - **One revision:** the label, the date and parent, the lock state, rejection, and a
    warning if the image changed outside RaiderLite.
    - **Notes:** editable, saved to `notes.txt` when focus leaves.
    - **Changes from the parent:** tables with changed-cell counts and bytes no table covers.
      Double-click opens both revisions' table side by side, comparing.
    - **Files:** double-click opens one; right-click offers Show in Explorer.
    - **Buttons:** Open and Show in Explorer.
  - **Two selected revisions:** their differing tables, with the same double-click.
  - **A fold:** its revisions and a Show Folded Revisions button.
  - **Layout:** the lists are as tall as their rows (scrolling beyond 8 tables or 4 files). The
    notes take the rest, never under four lines.
- **`CarSession`:** loads the car's definition (found by XML id among the configured definition
  files) with its stock image as a template. It works out every revision's changes against its
  parent off the UI thread, and again after an image is saved.
- **`CarRegistry`:** one `Car` per folder for the whole app, so saves in the editor and edits in
  the panel never overwrite each other's `car.json`.
- **`MainForm`:**
  - `OpenRevisionAsync` opens a revision with the car's definition, even when several match.
  - `OpenRevisionComparisonAsync` opens one table of two revisions side by side.
  - Open revisions are named "037 Label" in the tree, the table windows and Compare, and the title
    bar reads "RaiderLite — car › revision".
  - **Save guard:** saving over a locked revision's image is refused, with an offer to Save As
    instead. Saving an editable revision updates its hash in `car.json` and refreshes its changes.
- **Settings:** a tune library folder row (default `Documents\RaiderLite Tunes`, stored as empty).
- **Checked in the real app** on a demo car built from copies of 2011 EvoraNA:
  - the timeline and folding, expanding the 6 October branch
  - the details of revisions with notes, changes and files
  - the filter ("3.5" finds 011 by its notes)
  - a side-by-side comparison (027 inj: angle against 026, −180°)
  - the guard on a locked revision (image unchanged), saving an editable one
  - the empty state and Settings

### Phase 3: editing workflow (done)

- **Read-only images (Core):** `Rom.ReadOnlyReason` (with `ReadOnlyChanged`). While it is set,
  `DataCell.SetBinValue` throws `ImageReadOnlyException` and `Table.ReadOnlyReason` reports it, so
  every edit path (typing, paste, increments, interpolation, presets, Rescale Axis) refuses
  without writing. Undo and redo check it in `MainForm`, since they write past that check.
  MainForm sets it on every open image of a locked revision, and clears it when the revision
  unlocks or the image is saved elsewhere.
- **Lock bar:** under the ribbon while the current image is a locked revision. It reads "036 is
  locked because other revisions are based on it…" and has a New Revision from 036… button. The
  table footer says "(locked revision)".
- **Edit attempts:** typing a value into a locked table, or any refused edit, undo or redo, shows
  "036 is locked" with New Revision… (the default) and Cancel.
- **New Revision** (File menu and Ctrl+Shift+N, the ribbon's Image group, the lock bar, a
  revision's context menu, the image's context menu in the tree):
  - **Dialog** (`NewRevisionForm`): based on (any revision), label (the parent's, selected), and
    an optional note.
  - **Unsaved changes in the parent:** save them in the parent first (unless it is locked), or
    carry them into the new revision.
  - **Afterwards:** the new revision opens, the parent's table windows reopen on it in the same
    places, and the parent's image closes.
- **Notes:**
  - **Suggested lines:** the first save of a revision that has changes and no notes writes one
    `* table: ` line per changed table (`Changelog.SuggestedLines`). It also runs when changes
    are carried into a new revision.
  - **Hints:** in the details, changed tables the notes don't mention (`Changelog.Mentions`:
    loose, per line, word prefixes, ignoring two-letter words) are marked ⚠ in orange, with an
    "Add N to the notes" link. The marks follow the notes as they are typed.
- **Revision context menu:** Open, New Revision from This…, Rename… (renames the folder;
  open images follow it), Reject… or Clear Rejection, Hide or Unhide, Copy Changelog
  (`Changelog.Build`: the lineage, newest first, "yyyyMMdd - 012 Label" headings), Add Files…,
  Show in Explorer.
- **Files:** drop files on the details to copy them into the revision folder, or use Add Files…
  in the files list's menu.
- **New Car** (File menu, the ⋯ menu, the empty state): a name and a stock image. The definition
  is the most specific match among the configured definition files. The car is created in the
  library and shown.
- **Ribbon:** New Revision took Image Properties' place in the Image group's small column, so the
  ribbon still fits at 1400 px. Image Properties stays in the File menu and the image's context
  menu.
- **Tests:** `ChangelogTests` covers note lines, loose mentions (with lines from the 2011
  EvoraNA notes), suggested lines, the cumulative changelog, and the read-only image refusing
  edits and Rescale Axis.
- **Checked in the real app** on the demo car:
  - the lock bar, and New Revision from it (038 from 036, label replaced, tables reopened)
  - typing into a locked table → the prompt, with Cancel changing nothing
  - editing and saving 038 → `* inj efficiency: ` suggested
  - the ⚠ hint and its link on 018 (`* sensor maf scaling: ` added)
  - New Car from the empty state
  - Rename of an open revision, then saving it (to the renamed folder)
  - Copy Changelog, and Reject (hidden from the timeline, its parent selected)

### Since phase 3

- **Delete revisions:** a revision's context menu (Delete…), the Delete key, or several selected
  at once. The confirmation lists the revisions, the folders going to the Recycle Bin, and which
  revisions will be re-based on the nearest surviving ancestor. Open images of a deleted
  revision are closed first, asking about unsaved changes. Stock can't be deleted.
  - `Car.DeleteRevision` removes the folder through a callback (the app sends it to the Recycle
    Bin), then re-bases the children. If removing the folder fails, nothing changes.
  - Numbers aren't reused: `car.json` keeps `nextNumber` once the highest-numbered revision has
    been deleted.
- **Labels** are stored as typed ("Dyno: base"). Only the folder name is cleaned for the file
  system.
- **Main line:** it now runs to the newest revision, not the branch with the most revisions, so
  deleting or adding revisions in a test branch doesn't move the current line aside.
- **Details pane:** the sections scroll when even their smallest sizes don't fit, so they never
  overlap; Open and Show in Explorer stay pinned below them; "Imported from" moved into a tooltip.
