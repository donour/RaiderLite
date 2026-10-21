# Copy and paste

RaiderLite copies and pastes values as text in RomRaider's clipboard formats. You can move values
between RaiderLite, RomRaider and spreadsheets such as Excel.

## Copying

| Command | Keys | What is copied |
|---|---|---|
| **Copy** (ribbon) or **Edit > Copy Selection** | Ctrl+C or Ctrl+Insert | The selected cells. Cells that aren't selected but lie within the selection's rows and columns are marked `x`. |
| **Copy Whole Table** (ribbon, under Copy) or **Edit > Copy Table** | | The whole table, with its axes. |
| **Copy Value** (right-click a single value) | | The value. |

Values are copied in the table's current scaling, separated by tabs and new lines. A copied table
or selection starts with a header line, such as `[Table3D]` or `[Selection1D]`, that tells
RomRaider and RaiderLite what it is.

## Pasting

Select where the values should go and press **Ctrl+V** (or Shift+Insert), or click **Paste** on
the ribbon.

- A **whole table** copied from RaiderLite or RomRaider replaces the table's values and axes. If
  the tables are different sizes, only the values that fit are pasted.
- A **selection**, or **text without a header** such as cells copied from a spreadsheet, is
  pasted as a block with its first value at the first selected cell (the top-left of the
  selection in a map). Values marked `x` leave their cells alone.

Values are read in the table's current scaling and snap to what the storage can hold. A cell
whose shown value already matches is left alone, so pasting a table onto itself changes nothing.
A paste is one undo step.

## Working with spreadsheets

To edit a map in a spreadsheet, copy its data cells (**Select Data**, then Ctrl+C) and paste them
into the spreadsheet. When you're done, copy the same block of cells from the spreadsheet, select
the top-left data cell in RaiderLite and paste.
