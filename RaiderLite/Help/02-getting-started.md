# Getting started

The first time you use RaiderLite, tell it where your definition files are. After that, opening
an image and editing it takes a few clicks.

## 1. Add your definitions

Choose **Edit > Definition Manager** (or **Definitions** on the ribbon) and click **Add** to add
your RomRaider definition files (`.xml`). A definition says where each table is in the image and
how to show its values. RaiderLite uses the same files as RomRaider. See
[Definitions](definitions).

## 2. Open an image

Choose **File > Open Image** (Ctrl+O), click **Open** on the ribbon, or drop an image file on the
window. RaiderLite picks the definition whose ROM ID matches the image, and lists the image's
tables by category in the **Tables** tab. See [Images](images).

## 3. Open a table

Click a table in the list to open it, and click it again to close it. To find a table by name,
type in the filter box above the list (Ctrl+F). See [Finding tables](table-list).

## 4. Change values

Select cells with the mouse or keyboard, then do one of these:

- Type a number and press **Enter** to set the cells to it.
- Press **+** or **_** (or Ctrl+Up and Ctrl+Down) to step them up or down.
- Use the ribbon's **Adjust** and **Interpolate** commands.

A changed cell gets a coloured border: red if its value went up, blue if it went down. Ctrl+Z
undoes a change. See [Editing values](editing).

## 5. Save

**File > Save Image** (Ctrl+S) writes the image and recalculates its checksums. **Save Image As**
(Ctrl+Shift+S) writes a copy under a new name. The original file is replaced only after the new
one has been written completely.

## 6. Keep a history (optional)

To keep every version of a car's calibration with notes, choose **File > Import Image as New
Car**, or **New Car** if you have the stock image. The **Tunes** tab then lists the car's
revisions, and **New Revision** (Ctrl+Shift+N) makes a copy to edit. See [Tune history](tunes).
