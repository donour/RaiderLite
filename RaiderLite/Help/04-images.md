# Images

An image is the calibration read from an ECU: a file of raw bytes. RaiderLite edits the image in
place, so what you see is what gets saved.

## Opening images

- Choose **File > Open Image** (Ctrl+O) or click **Open** on the ribbon. You can choose several
  files at once.
- **File > Open Recent** lists the last ten images.
- Drop image files on the main window.
- Image paths given on the command line open at start-up.

RaiderLite reads raw binary images (`.bin`, `.rom` and `.hex`) and Lotus `.cpt` calibrations,
which are also raw images. You can have several images open at once; each one is a top-level
entry in the **Tables** tab.

When an image opens, RaiderLite checks its checksums:

- If some don't match, it says how many are valid. They're recalculated when you save.
- If some can't be checked at all (for example, they cover bytes the image doesn't have), it
  lists them. Those aren't updated when you save, and saving asks before going ahead. This
  usually means the definition doesn't fit the image.

## Saving

- To save, choose **File > Save Image** (Ctrl+S) or click **Save** on the ribbon.
- To save under a new name, choose **File > Save Image As** (Ctrl+Shift+S). The open image then
  refers to the new file.

Saving recalculates every checksum the definition lists. The image is written to a temporary file
first, which replaces the original only once it's complete, so a failed save can't leave a
half-written file.

These commands act on the image of the active table window, or on the image selected in the
**Tables** tab.

## Closing

**File > Close Image** closes the selected image, and **Close All Images** closes them all. If an
image has unsaved changes, RaiderLite asks whether to save them first. It also asks when you
exit.

## Image properties {#properties}

To see an image's properties, choose **File > Image Properties**, or right-click the image in the
**Tables** tab. They include:

- the file, its size, and the definition used,
- everything the definition says about the ROM, such as make, model, ECU ID and memory model,
- the number of tables, and any that couldn't be read,
- the checksums, and how many of them are valid,
- whether there are unsaved changes.

## Read-only images

When other revisions in the tune library are based on a revision, that revision is locked. Its
image opens read-only, with a bar above the tables saying why and a **New Revision** button. See
[Locked revisions](tunes#locked-revisions).
