# Rescale Axis

**Rescale Axis** gives one axis of a curve or map new breakpoints, and resamples the data so the
ECU looks up the same values as before. Use it to widen a table's range, such as extending an
RPM axis or moving breakpoints to where you need more resolution, without changing how the
engine runs today. RomRaider doesn't have this command.

To open it, click **Rescale** in the **Axis** group of the ribbon, or choose **Edit > Rescale
Axis** in a table window. It's available for curves and maps whose axes are stored in the image.

## The dialog

| Setting | What it does |
|---|---|
| **Axis** | For a map, the axis to rescale: X or Y. |
| **Now** | The axis's current breakpoints. |
| **Method** | **Stretch** keeps the first breakpoint and the spacing pattern, and stretches the axis to a new last breakpoint. **Even spacing** spreads the breakpoints evenly between a new first and last. **Enter every breakpoint** lets you type them all. |
| **New range** | The new first and last breakpoints, depending on the method. |
| **Breakpoints** | The new breakpoints in the axis's units, separated by spaces. You can paste a copied row of axis cells here. |
| **Beyond the old range** | What the data does where the new axis reaches past the old one (see below). |

As you type, the **Preview** shows the axis as it will be stored (after rounding to what the
storage can hold) and the largest change of any lookup within the old range.

## How the data is resampled

The ECU looks up a table by interpolating linearly between breakpoints, and clamps at the first
and last ones. RaiderLite works out each new data value so that this lookup gives the same
result at every new breakpoint as the old table did. Between the new breakpoints, lookups stay
the same unless an old breakpoint falls there: the bend the table had at that old breakpoint is
straightened out. The preview shows the largest such change. Everything is worked out on the
stored (raw) values, as the ECU does.

Beyond the old range you choose what happens:

- **Hold the edge values**: the table keeps its edge values there, which is what the ECU does
  today, so its behaviour doesn't change until you tune that region.
- **Extrapolate the end slopes**: the values continue the slope of the first or last segment,
  so the ECU's behaviour changes there.

Repeated breakpoints, such as padding at the end of an axis or deliberate steps, are kept.

## Undoing

The rescale is a single edit: one **Ctrl+Z** puts the axis and the data back.
