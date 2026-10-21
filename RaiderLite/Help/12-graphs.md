# Graphs

Curves and maps can be shown as a graph beside their table: a line graph for a curve, and lines
or a 3D surface for a map. Graphs are read-only views of the table, drawn with the graphics card
(Direct2D) where possible, and they follow every edit as you make it.

## Showing a graph

- Press **Ctrl+G** in a table window, click **Graph** in the **View** group of the ribbon, or
  choose **View > Graph** in the table window. Do the same again to hide it.
- To show graphs every time a table opens, tick **Show graphs when opening tables** in
  [Settings](settings).

A curve's graph goes below the table. A map's graph goes to the right of the table or below
it, whichever leaves the graph more room.

## Using a graph

- Hover over a point to see its axis values and value.
- Click a point to select its cell in the table.
- Selected cells are marked in the graph.
- Once you change a value, the original values are drawn as dashed lines, so you can see the
  change. If the table is being compared with another image, the graph also shows that image's
  values (see [Comparing](comparing)).
- Right-click the graph and choose **Copy Image** to copy it to the clipboard as a picture.

## Maps

A map's graph has a small toolbar:

| Button | What it does |
|---|---|
| **Lines** | One line per row of the map, coloured from the first row to the last. |
| **Surface** | A 3D surface of the map. Drag to rotate it. |
| **Transpose** | Draws the lines per column instead of per row. |
| **Reset View** | Turns the surface back to its starting angle. You can also double-click the surface. |

RaiderLite remembers whether you last used lines or the surface.
