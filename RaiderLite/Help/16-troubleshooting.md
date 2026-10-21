# Troubleshooting

## "Definition Not Found" when opening an image

None of the files in the definition list has a ROM ID that matches the image.

- Check that the right definition file is in **Edit > Definition Manager**.
- If you're sure which definition fits, choose **Yes** and open the image with it. See
  [When no definition matches](definitions#no-match).

## Values look like garbage

The definition doesn't fit the image: the tables are being read from the wrong places. This
often comes with checksum warnings when the image opens. Check **File > Image Properties** for
the definition that was used and any load warnings, and see
[How a definition is chosen](definitions#how-a-definition-is-chosen).

## Checksums don't match when opening

RaiderLite checks the image's checksums when it opens. Checksums that don't match are
recalculated when you save, so an image edited by a tool that didn't update them is fixed by
saving it. Checksums that can't be checked at all are listed and left alone when you save; that
usually means the definition doesn't fit the image. See [Saving](images#saving).

## A table can't be edited

The caption under the table's data says why. See
[Read-only tables](editing#read-only-tables).

## A table is missing from the list

- It may be above your user level: raise it in **View > User Level**, or tick **View > Display
  High Level Tables**.
- The filter box above the list may still contain text. Press **Esc** in it to clear it.
- If the definition gives the table no address, the table isn't created (as in RomRaider).

## "Revision Locked"

Other revisions are based on this revision, so its image can't change. Make a new revision from
it. See [Locked revisions](tunes#locked-revisions).

## Starting afresh

All settings are in `%APPDATA%\RaiderLite\settings.json`. Close RaiderLite and rename or delete
that file to go back to the defaults. You'll need to add your definitions again.
