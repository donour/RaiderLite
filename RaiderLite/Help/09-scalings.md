# Scalings

A scaling turns a table's stored bytes into values with units. Definitions often give a table
several scalings, such as metric, imperial or raw, and you can switch between them at any time.
Switching changes only how the values are shown and typed. The image is never changed.

## Choosing a scaling

- Click the caption under a table's data (it shows "Values: units ▾") and choose a scaling.
- Click an axis caption to choose that axis's scaling.
- Or use the **View** group on the ribbon: **Values**, **X axis** and **Y axis** list the active
  table's scalings. A list is disabled when there's only one to choose from.

A scaling named **Raw value** shows the stored numbers themselves, before any conversion. It's
only there if the definition gives the table one.

## Which scaling a table opens with

A table opens in its scaling whose category matches **Preferred scaling category** in
[Settings](settings) ("Metric" unless you change it), or its scaling named "Default". If it has
neither, it opens in its first scaling.

If **Switch axis scaling with the table's scaling** is ticked in Settings, choosing a scaling for
a table's data also switches its axes to the scaling of the same category, where they have one.

## Steps

Each scaling has its own fine and coarse steps for stepping values up and down. The ribbon's
**Fine** and **Coarse** boxes show the steps of the current scaling. If you edit them, the new
steps are kept for that scaling until you close RaiderLite. See [Editing values](editing).
