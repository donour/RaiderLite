# Table windows

Each open table has its own window in the main window's table area. A window shows the table's
name, its axes, a grid of values and the scaling in use. The first time you open a table, its
window is sized to show the whole table. After that, it opens where you last left it.

## Reading a table

| Kind | How it's shown |
|---|---|
| **Single value** | A small window with the value between **−** and **+** buttons, and its units. |
| **Array (1D)** | One row of cells. |
| **Curve (2D)** | The axis values in a row above the data. |
| **Map (3D)** | A grid of data, with the X axis along the top and the Y axis down the left. |
| **Switch** | A list of named settings to choose from (see [Switches and presets](#switches)). |

- The captions beside the axes and under the data give their names and units. If there's more
  than one scaling, click a caption to choose another (see [Scalings](scalings)).
- Hover over the table's name to read its description.
- The status bar shows the current cell's axis values, value, raw value and address. If several
  cells are selected, it also shows their minimum, maximum and average.
- If the table can't be edited, the caption under the data says why: for example, it's locked by
  the definition, it needs a higher user level, or its image is a locked revision.

## Cell colours

| Colour | Meaning |
|---|---|
| Background shading | Where the value sits between the table's smallest (**Min** colour) and largest (**Max** colour) values. |
| **Axis** colour | Axis cells, unless **Colour axis cells** is ticked in Settings. |
| Red border | The value has increased since the image was opened (or since the revert point). |
| Blue border | The value has decreased. |
| **Warning** colour | The stored value is outside the range its storage type can hold (if **Highlight values outside the storage range** is ticked in Settings). |
| **Selection** colour | The selected cells. |

While a table is being compared, the shading shows the differences instead, and cells that are
the same are grey (see [Comparing](comparing)). You can change every colour in
[Settings](settings), which also has a colour-blind friendly set.

## The window's menus

| Menu | Commands |
|---|---|
| **Table** | Table Properties, Close. |
| **Edit** | Undo, Redo, Undo Selected Changes, Undo All Changes, Set Revert Point, Copy Selection, Copy Table, Paste, the three Interpolate commands, Rescale Axis and Select All Data. See [Editing values](editing). |
| **Compare** | Compares the table with its original values or with the same table in another image. See [Comparing](comparing). |
| **View** | Graph (Ctrl+G), and Fit Window to Table. |

A single-value window has no menu bar. Right-click it for Undo, Redo, Revert to Original, Set
Revert Point, Copy Value, Table Properties and Close.

## Single values

Click the value and type a new one, then press **Enter** to set it or **Esc** to cancel. **Up**
and **Down** step the value by the fine step, and **Shift+Up**, **Shift+Down**, **Page Up** and
**Page Down** step it by the coarse step. The **−** and **+** buttons step it by the fine step,
or by the coarse step with **Shift+click**. The ribbon's **Adjust** commands work on it too.

## Switches and presets {#switches}

A switch table stores one of several byte patterns, each with a name, such as "Enabled" and
"Disabled". Its window lists the names, and the one stored in the image is marked. Click another
name to store its pattern. A bit switch lists named single bits, each of which can be set or
cleared on its own. Hover over a name to see its data or bit. Below the list are the address and
the bytes stored there; if they match none of the patterns, none is marked and "(no state
matches)" is added.

Some ordinary tables also have **presets**: named sets of values listed below the grid. Click a
preset to apply it.

## Arranging windows

- Double-click a table window's title bar, or choose **View > Fit Window to Table**, to size the
  window to its table.
- **Esc** closes the active table window.
- The **Window** menu arranges every open table window. Each layout starts from each table's
  fitted size:

| Command | Layout |
|---|---|
| **Arrange** | Tries several layouts and keeps the one that shows the most of the tables. |
| **Tile Side by Side** | One column per table, each as wide as its table needs. |
| **Tile Stacked** | One row per table, each as tall as its table needs. |
| **Cascade** | Overlapping windows, each at its own size. |
| **Close All Tables** | Closes every table window. |

The **Window** menu also lists the open table windows, so you can switch to one.
