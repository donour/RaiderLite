# Tune history

The **Tunes** tab keeps every version of a car's calibration as a numbered **revision**, with its
image, notes and other files. It shows the history as a timeline with branches, works out which
tables each revision changed, and keeps revisions that others are based on from changing after
the fact.

Everything is kept in ordinary folders, with a `car.json` index for each car, so you can read the
history without RaiderLite.

## Terms

| Term | Meaning |
|---|---|
| **Library** | The folder that holds your cars. By default it's `Documents\RaiderLite Tunes`; you can change it in [Settings](settings). |
| **Car** | One ECU's calibration history, such as "2011 Evora NA". A car always uses one definition. |
| **Revision** | One complete image, with its notes and files. It has a number (such as 037), a date, a label, and the revision it was based on. Revision 000 is where the history starts: usually the stock image, or an imported one. |
| **Branch** | Two revisions based on the same revision. Branches are normal: a test, a rejected attempt and its retry, or a sweep of variants. |
| **Locked** | Revision 000, and any revision that others are based on. A locked revision's image can't be changed. |
| **No change** | A revision whose image is identical to the one it's based on. It's greyed out with a "no change" tag, and kept for its notes, files and date. |
| **Rejected** | A revision you marked as a failure, with a reason ("too lean"). It's hidden unless you choose to show it, and then drawn struck through. |
| **Hidden** | A revision put out of the way, such as a backup or an abandoned variant. It stays on disk; choose **Show Hidden and Rejected Revisions** to see it (in italics) and compare it. |

## Starting a car {#new-car}

There are two ways to start a car:

- **File > New Car** starts from the stock image. Choose the stock image, name the car, and give
  the first revision a label ("Stock" by default).
- **File > Import Image as New Car** starts from any image you already have, such as the one
  currently in the car. Its revision 000 keeps the image's file name, so it still matches any
  flash files made from it.

Either way, RaiderLite creates a folder for the car in the library and copies the image into it.
Your original file isn't changed. The image must match a definition in the
[definition list](definitions). If you import an image that is open with unsaved changes,
RaiderLite asks whether the first revision should include them.

## The Tunes tab

At the top of the tab is the car list. Below it are a search box, the timeline, and the selected
revision's details.

- The **timeline** lists the car's revisions, newest first, with a graph on the left. The main
  line runs straight down, and side branches run in further lanes, joining the revision they were
  based on.
- Images that are open in the editor are drawn in bold, with a ring around their dot. Locked
  revisions have a lock icon.
- Hover over a revision to see its date, the revision it was based on, the tables it changed and
  the start of its notes.
- Double-click a revision (or press **Enter**) to open its image. The left panel then switches
  to its tables.
- Side branches with nothing new for a week are folded into one row. Double-click the row to
  show them. To fold them again, right-click and choose **Fold Finished Side Branches**.
- Type in the search box to list the revisions whose label, number, notes, rejection reason or
  changed tables contain every word you typed.

The **⋯** button beside the car list (Tune library options) has New Revision, New Car, Import Image as New Car, Refresh,
Open Car Folder, Open Library Folder, Choose Library Folder, and **Show Hidden and Rejected
Revisions**.

## Revision details

Select a revision to see its details below the timeline:

- **Notes**: what you changed and why. Type straight into the box; the notes are saved in the
  revision's `notes.txt` when you move on. Each revision keeps only its own note.
- **Changed from**: the tables this revision changed from the one it's based on, with the number
  of cells changed in each. Double-click a table to open it in both revisions side by side, with
  this revision's table compared with the parent's.
- Changed tables that your notes don't seem to mention are marked **⚠**. Click **Add N to the
  notes** to add a `* table: ` line for each, ready for you to complete.
- **Files**: the other files kept with the revision, such as logs, flash files and learned-data
  reads. Double-click a file to open it, drop files on the list to add them, or right-click for
  Open, Show in Explorer and Add Files.
- **Open** opens the revision's image. **Show in Explorer** shows the image in its folder.

The first time you save a revision that has changes but no notes, RaiderLite starts its notes
with a line for each table you changed.

## Making a new revision {#new-revisions}

Make a new revision for every change you want to keep, and especially before you flash an image
into the car. That way, what went into the car stays exactly as it was flashed.

1. Open or select the revision to start from.
2. Choose **File > New Revision** (Ctrl+Shift+N), click **New Revision** on the ribbon, or
   right-click the revision and choose **New Revision from This**.
3. Give it a label and, if you like, a note, and click **Create**.

RaiderLite copies the image into a new folder named with the number, date and label, and opens
it. The new image is named `yyyyMMdd-car-number`, with the original image's extension. If the
revision you started from has unsaved changes, RaiderLite asks whether to save them in that
revision first or to put them in the new revision (leaving the old one as it was saved).

## Locked revisions {#locked-revisions}

Once another revision is based on a revision, the earlier revision is locked: changing it would
change what the later ones were based on. Revision 000 is always locked.

A locked revision's image opens read-only. A bar above the tables says why it's locked and has a
**New Revision from N** button. If you start typing a value, RaiderLite offers to make a new
revision. If you save, it offers to save to another file instead.

## Comparing revisions {#comparing-revisions}

Select two revisions in the timeline (Ctrl+click the second). The details then list every table
that differs between them. Double-click a table to open both versions side by side, with the
newer revision's table compared with the older one's.

## Managing revisions {#revision-commands}

Right-click a revision in the timeline:

| Command | What it does |
|---|---|
| **Open** | Opens its image. |
| **New Revision from This** | Starts a new revision based on it. |
| **Rename** | Changes its label. Its folder is renamed to match. |
| **Reject** / **Clear Rejection** | Marks it as a failure, with a reason, or clears the mark. Not for revision 000. |
| **Hide** / **Unhide** | Takes it out of the timeline, or brings it back. Not for revision 000. |
| **Copy Changelog** | Copies the notes of the revision and every revision it descends from to the clipboard, newest first, each under a dated heading. This is the cumulative `notes.txt` style. |
| **Add Files** | Copies files into the revision's folder. |
| **Show in Explorer** | Shows the image in its folder. |
| **Delete** | Moves the revision's folder to the Recycle Bin. You can select several revisions and press **Delete**. Revision 000 can't be deleted. |

## On disk

```
RaiderLite Tunes\                 the library
  2011 Evora NA\                  a car
    car.json                      RaiderLite's index for this car
    000 2025-07-24 Stock\         a revision: number, date, label
      B132E0091_TAB.cpt           its image (exactly one per revision)
      B132E0091.CRP               other files: flash files, logs...
    001 2025-07-24 Unlock\
      20250724-2011-evora-na-001.cpt
      notes.txt                   this revision's note only
```

RaiderLite finds revisions by number through `car.json`, so renaming the label part of a folder
is harmless. Notes are plain text that you can edit outside RaiderLite. If an image is changed
outside RaiderLite, its details say so.
