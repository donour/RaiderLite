# Settings

To change the settings, choose **Edit > Settings** or click **Settings** on the ribbon. They're
saved when you click **OK**, and kept when you upgrade or uninstall RaiderLite.

## Tables and editing

| Setting | What it does |
|---|---|
| **User level** | Which tables you can edit, from 1 (Beginner) to 5 (Debug). The definition gives each table a level, and tables above yours are read-only. Also on the **View** menu. |
| **Preferred scaling category** | The scaling that tables open in, if they have one with this category (such as Metric or Imperial). See [Scalings](scalings). |
| **Switch axis scaling with the table's scaling** | When you choose a scaling for a table's data, its axes switch to the same category. |
| **Show tables above the user level (read-only)** | Lists the tables you can't edit, rather than hiding them. |
| **Allow editing debug-level (5) tables** | Without this, debug-level tables stay read-only even at user level 5. |
| **Highlight values outside the storage range** | Shows stored values outside the range of their storage type in the **Warning** colour. |
| **Sort tables alphabetically** | Sorts the tables in each category by name. |
| **Open and close tables with a single click** | When this isn't ticked, double-click a table to open or close it. |
| **Show graphs when opening tables** | Opens curves and maps with their graph showing. Ctrl+G still shows or hides it. |
| **Colour axis cells** | Shades axis cells like data cells, instead of using the **Axis** colour. |
| **Minimum cell width** and **Cell height** | The size of the cells in table windows, in pixels. |

## Tune library

**Tune library folder** is the folder that holds your cars (see [Tune history](tunes)). Click
**Browse** to choose it. If you leave it empty, RaiderLite uses `Documents\RaiderLite Tunes`.

## Colours

Click a colour to change it:

| Colour | Used for |
|---|---|
| **Min** and **Max** | The shading of cells, from the table's smallest value to its largest. |
| **Selection** | Selected cells. |
| **Increased** and **Decreased** | The border of a cell whose value went up or down. |
| **Axis** | Axis cells. |
| **Warning** | Values outside the storage range. |

**Default Colours** puts back RomRaider's colours. **Colour-blind Friendly** shades from light
orange to dark red and uses blue, sky blue, yellow and black for the rest. Those colours stay
distinct with red-green or blue-yellow colour blindness, and the shading changes in lightness
throughout, so it reads even without colour.

## Where settings are kept

Settings are kept in `%APPDATA%\RaiderLite\settings.json`, along with the definition list, recent
images and window positions. To use another settings file, set the `RAIDERLITE_SETTINGS`
environment variable to its path.
