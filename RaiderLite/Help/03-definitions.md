# Definitions

A definition file tells RaiderLite where each table is in an image, how its bytes are stored, and
how to turn them into values with units. RaiderLite reads RomRaider's XML definition format, so
your existing definition files work unchanged.

## The Definition Manager

To open it, choose **Edit > Definition Manager** or click **Definitions** on the ribbon.

- **Add** adds one or more `.xml` definition files.
- **Remove** takes the selected files off the list. The files themselves aren't touched.
- **Move Up** and **Move Down** change the order of the list.

The list is saved in your settings, so you only need to set it up once.

## How a definition is chosen

Each `<rom>` in a definition has a ROM ID: bytes at a fixed address that identify the
calibration. When you open an image, RaiderLite looks through every file in the list for ROMs
whose ID matches the image, and uses the most specific match (the longest ID). If two matches are
equally specific, it uses the one whose file is nearer the top of the list. Either way, the
other definitions that matched are listed as load warnings, so you can check that the right one
was used: the status bar says how many warnings there are, and **File > Image Properties** lists
them.

An image that belongs to a car in the tune library opens with that car's definition whenever that
definition is among the matches.

## When no definition matches {#no-match}

If no definition in the list matches the image, RaiderLite offers to open it with a definition
file you choose:

1. Choose the `.xml` file.
2. If none of the file's ROMs has a matching ID, **Choose ROM Definition** lists them all. Pick
   the one to use. If you pick the wrong one, the tables show garbage values, so make sure it's
   the right calibration.
3. RaiderLite then offers to add the file to the definition list.

## What is supported

- `<rom base="...">` inheritance, `<scalingbase>`, child ROMs and `xi:include` files.
- Every RomRaider table type: single values and arrays (1D), curves (2D), maps (3D, including
  swapped, flipped and skipped layouts), switches and bit switches.
- Every storage type: signed or unsigned 8, 16 and 32-bit integers, floats, and MOVI20/MOVI20S,
  in either byte order, with bit masks and the Bosch subtract layout.
- Several scalings per table, such as metric and imperial, with expressions as in RomRaider.
- User levels and locked tables. A table that its definition locks can't be edited.
- These checksums: STD, ALT, ALT2, BYTEXOR, COPY, Motronic single and double, E38 PCM, and
  Subaru's "Checksum Fix" table.

When a definition has a mistake that RaiderLite can work around, it records a load warning
(listed in **File > Image Properties**) and carries on. For example, if a `to_byte` expression is
broken, RaiderLite searches for the raw value that gives the value you typed. Tables that can't
be read at all are left out, and a message lists them when the image opens.
