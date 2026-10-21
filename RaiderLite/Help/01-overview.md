# Welcome to RaiderLite

RaiderLite is an editor for ECU calibration tables. It opens raw ROM images, shows their tables
using [RomRaider](https://www.romraider.com) XML definitions, and saves your edits back to the
image. It is a port of RomRaider's table editor and only edits: it has no data logging, dyno,
live data or flashing. Use your usual tools to read and write the ECU.

> **Warning:** changing an ECU calibration can damage an engine. Always keep an untouched copy of
the original image, and check every change before flashing it. RaiderLite comes with no warranty.

## The main window

| Part | What it does |
|---|---|
| **Menu bar** | Every command, under File, Edit, View, Tools, Window and Help. |
| **Ribbon** | The common commands in groups: Image, Edit, Adjust, Interpolate, Axis, View and Tools. Table commands act on the active table window. Hover a button to see what it does and its shortcut. |
| **Left panel** | Two tabs. **Tables** lists the open images and their tables (see [Finding tables](table-list)). **Tunes** shows a car's calibration history (see [Tune history](tunes)). |
| **Table area** | Each open table has its own window (see [Table windows](table-windows)). |
| **Status bar** | Messages on the left. On the right: the current cell's axis values, value, raw value and address, and the minimum, maximum and average of the selected cells. |

## Where to start

- If you're new to RaiderLite, read [Getting started](getting-started).
- To change values, see [Editing values](editing) and [Copy and paste](copy-paste).
- To keep a history of a car's tunes, see [Tune history](tunes).
- For every key, see [Keyboard shortcuts](keyboard-shortcuts).

## Using this help

- Press **F1** in the main window, a table window or a dialog for help on what you're doing.
  Dialogs also have a **?** button in their title bar.
- Click a page or section in the contents on the left. To search, type in the box above the
  contents: it lists the pages that contain every word you typed, and highlights those words on
  the page.
- **Back** and **Forward** (or Alt+Left and Alt+Right) step through the pages you have visited.
- You can select text on a page and copy it with Ctrl+C.
