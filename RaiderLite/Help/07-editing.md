# Editing values

Edits change the image straight away (and the cells snap to values the storage can actually hold,
so what you see is what is saved). Nothing is written to disk until you save.

## Selecting cells

- Click a cell, or drag across several. **Shift+click** extends the selection; **Ctrl+click** adds
  or removes a cell.
- Use the arrow keys to move, and **Shift+arrow** to extend the selection.
- **Ctrl+A** selects every cell, including the axes.
- **Select Data** on the ribbon (or **Edit > Select All Data**) selects every data cell without
  the axes. In a map, clicking the empty top-left corner does the same.

Axis cells can be edited too: select them like any other cell.

## Setting a value

Type a number while a table has the focus: it goes into the **Value** box on the ribbon. Then:

- **Enter** sets every selected cell to it.
- **Ctrl+Enter** multiplies every selected cell by it.
- **Esc** goes back to the table without changing anything.

You can also type into the **Value** box directly and click its **✓** (set) or **×** (multiply)
button.

## Stepping values up and down

The ribbon's **Adjust** group holds two steps, **Fine** and **Coarse**, taken from the table's
scaling. You can edit either step; the new step is kept for that scaling.

| Keys | Ribbon | Action |
|---|---|---|
| **+** or Ctrl+Up | Coarse **+** | Increase by the coarse step |
| **_** or Ctrl+Down | Coarse **−** | Decrease by the coarse step |
| **]** or Ctrl+Shift+Up | Fine **+** | Increase by the fine step |
| **\[** or Ctrl+Shift+Down | Fine **−** | Decrease by the fine step |
| **\*** or Ctrl+Enter | Value **×** | Multiply by the value box |

## Interpolating

Interpolation replaces the cells between the edges of the selection with a straight line
between the edge cells. Along an axis, the line follows the axis values, as RomRaider does: a
cell halfway along the axis gets the value halfway between. A plain array, with no axis, is
interpolated by position. The line is straight in the stored (raw) values, so with a non-linear
scaling the shown values may not be evenly spaced.

| Command | Keys | What it does |
|---|---|---|
| **Both** | Shift+I | Interpolates a curve along its axis; in a map, along the rows and the columns. |
| **Vertical** | Shift+V | Interpolates down each selected column, along the Y axis. |
| **Horizontal** | Shift+H | Interpolates along each selected row, along the X axis. |

In a map, the commands work on the rectangle around the selection: select two corners, or
whole rows or columns, and every cell between the edges is replaced. Selected axis cells are
interpolated too.

## Undo and redo

**Ctrl+Z** undoes the last edit and **Ctrl+Y** (or Ctrl+Shift+Z) redoes it. Each image has its
own history, shared by all its tables, and a command such as a paste or interpolation is one
step. The main window's **Edit** menu, and the tooltips of the ribbon's **Undo** and **Redo**
buttons, name the step they will undo or redo.

## Reverting

| Command | What it does |
|---|---|
| **Revert** (ribbon) or **Undo Selected Changes** | Puts the selected cells back to their original values. |
| **Revert Whole Table** (ribbon, under Revert) or **Undo All Changes** | Puts every cell of the table back. |
| **Set Revert Point** | Makes the current values the table's new "original" values: later reverts return to them, and the change borders and comparisons start from them. |

Reverting is itself an edit, so it can be undone.

## Read-only tables

Some tables can't be edited. The caption under the data says why:

- **locked**: the definition locks the table.
- **requires user level N**: raise the user level in **View > User Level**.
- **debug table**: debug-level tables are read-only unless **Allow editing debug-level (5)
  tables** is ticked in [Settings](settings).
- **checksum table**: the table holds a checksum that RaiderLite keeps up to date.
- The image is a locked revision (see [Locked revisions](tunes#locked-revisions)).
