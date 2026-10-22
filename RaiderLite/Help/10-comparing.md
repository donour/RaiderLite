# Comparing

RaiderLite can show how a table differs from its original values or from the same table in
another image. It can also list every table that differs between two images.

## Showing your changes

Click **Changes** in the **View** group of the ribbon, or choose **Compare > Show Changes** in a
table window. The cells then show how far each value is from its original value, and cells that
haven't changed are grey. Click **Changes** again, or choose **Compare > Off**, to show the
values again.

"Original" means the values when the image was opened, or at the table's revert point if you set
one.

## Comparing with another image

Open both images. Then, in the table's window, choose **Compare > Compare to ROM** and pick the
other image. The cells show the difference between this table and the other image's table.
Images whose table has a different shape, or that don't have the table, are greyed out.

The rest of the **Compare** menu controls how differences are shown:

| Command | What it does |
|---|---|
| **Absolute difference** | Shows the difference in the table's units. |
| **Percent difference** | Shows the difference as a percentage. |
| **Compare to current values** | Compares with the other table's values as they are now, including unsaved changes. |
| **Compare to original values** | Compares with the other table's values as they were when its image was opened. |
| **Interpolate at this table's axis values** | Compares each cell with the value the other table would look up at this table's breakpoints (see below). |

The caption under the data says what the table is being compared with. While comparing, the
shading follows the size of each difference, and equal cells are grey.

## Comparing tables with different breakpoints {#interpolated-compare}

Two versions of a curve or map can have the same size but different axis breakpoints, so the
cell in the same position means a different operating point in each. Choose **Compare >
Interpolate at this table's axis values** to compare what the ECU would actually look up: for
each cell, RaiderLite finds this table's axis values on the other table's axes and interpolates
the other table's values there (linearly for curves, bilinearly for maps). Points beyond the
other table's first or last breakpoint use the value at that end, as the ECU does. Each cell then
shows this table's value minus the value the other table would give at the same point.

The axis cells are still compared position by position, so they show which breakpoints moved.
Axes are matched by their raw values, so both tables' axes should use the same scaling. Static
(labelled) axes have no values to look up, so they are matched by position.

This also works with **Show Changes**: after you edit or rescale an axis, it shows how the
values looked up at the new breakpoints differ from what the original table gave there.

## Compare Images {#compare-images}

**Tools > Compare Images** (or **Compare** on the ribbon) needs two open images. Choose them in
the two lists and click **Compare**. Each table is listed with its category and whether it's the
same, different, a different size, or missing from one of the images. **Differences only** hides
the tables that are the same. Double-click a table to open both versions side by side, with the
first image's table compared with the second's.

To compare revisions in the tune library, select two revisions in the **Tunes** tab instead (see
[Comparing revisions](tunes#comparing-revisions)).
