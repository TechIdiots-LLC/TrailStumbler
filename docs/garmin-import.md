# Garmin .img import

TrailStumbler reads classic (unencrypted) Garmin `.img` maps, such as the trail maps GPS Trailmasters and TrailIntel
export, into layers: trails, points and areas. The decoder is in `TrailStumbler.Core/Parsing/Garmin`. It started as a
port of GarminBridge's Python decoder and has since been corrected in several places: line coordinates, the
directory's position, POI names and routable lines' names. Each fix was checked against GPS Trailmasters' own KMZ
and GPX of the same trails.

## What's read

- **Lines:** each map tile's most detailed level. Names come from the label file (LBL), or for routable lines from
  their road record in the routing file (NET).
- **Points:** the name comes from the label, or for a POI from its POI record in the label file.
- **Areas:** except the background grid.
- **Background maps** in a combined file are skipped, unless the import is asked to include them. A background map is
  one described as a background map or basemap, or with mkgmap's default "OSM street map" next to a map that isn't.
  Included background maps go into layers of their own.

## Trail kinds

A map's TYP file holds only drawing styles, not names, so each line type's meaning was found by matching the map's
lines against GPS Trailmasters' New Hampshire KMZ. The KMZ describes every trail, and the share given is how much of
each KMZ kind lies within 10 m of that type. The two makes number the same kinds differently:

| Kind (KMZ description)             | GPS Trailmasters ATV (2023) | TrailIntel ATV        | Who may use it              |
|------------------------------------|-----------------------------|-----------------------|-----------------------------|
| UTV/ATV 60 Inches and Motorcycles  | 0x01 (100%)                 | 0x03 (82%)            | up to 60 in, motorcycles    |
| UTV/ATV 60 Inches, No Motorcycles  | 0x02 (99%)                  | 0x02 (83%)            | up to 60 in, no motorcycles |
| Corridor                           | 0x03 (99%)                  | 0x04 (87%)            | all                         |
| ATV 50 Inches and Motorcycles      | 0x04 (100%)                 | 0x07 (80%)            | up to 50 in, motorcycles    |
| ATV Access Trail                   | 0x0b (98%)                  | 0x08 (92%)            | all                         |
| Closed ATV Trail                   | 0x1d (99%)                  | 0x06 (72%)            | none                        |
| Private Trail                      |                             | 0x05 (from its names) | all                         |

TrailIntel's snowmobile map has no KMZ to check against, so its kinds come from its trails' names:

| Type        | Kind                                  | Names seen                                 |
|-------------|---------------------------------------|--------------------------------------------|
| 0x0b        | Provincial / ITS Trail                | PROVINCIAL 83, PROVINCIAL 13               |
| 0x0d        | Corridor                              | 4A, 96 F1, NH 3A                           |
| 0x04        | Regional Trail                        | REGIONAL 303, REGIONAL 386                 |
| 0x02        | Local / Club Trail                    | LOCAL, CLUB, LOCAL ACCESS                  |
| 0x07        | Trail                                 | a mix: PROVINCIAL 83, LOCAL, REGIONAL 319  |
| 0x08        | Access, Private or Unmaintained Trail | ACCESS TRAIL, UNMAINTAINED TRAIL           |
| 0x03        | Approximate Trail                     | ITS 82 APPROXIMATE                         |
| 0x06, 0x0c  | Danger / Caution                      | DANGER - LAKE MALARTIC, CAUTION            |

The product is recognised from the map's MapSource name ("TrailIntel ATV Map NE", "TrailIntel Snowmobile Map NE",
"GPS Trailmasters ATV New England V12.00"). A map of any other make gets one stable colour per type, and no kinds or
restrictions.

Points: 0x2a is food, 0x2f01 fuel, 0x2f0b parking and trailheads, 0x2c04 scenic places, parks and camping. TrailIntel's
maps put everything else under 0x6400 (Garmin's generic man-made feature): garages, ATMs, fire and police, post
offices, stores, homes, about a third of their points. Its category comes from words in the name (`GARAGE`,
`MECANIQUE`, `ATM`, `CASERNE`, `POSTAL`, …, table in `GarminTypeClasses.NameCategories`), matched as whole words
without accents; a name with none keeps the generic marker. A 0x6400 point with no name says nothing, so it isn't
imported (3,320 of 12,107 on the two NE maps).

## Who may use a trail

A line feature can have three properties: `closed`, `no_motorcycles` and `max_width_in`. They're set only when the
data says so plainly:

- a known Garmin map's trail kind, as in the tables above;
- a KML or GPX description in GPS Trailmasters' wording: one that begins "Closed", says "No Motorcycles", or gives
  "NN Inches". The name is never read for this, since "Closed Loop" is a name;
- the properties themselves, when a GeoJSON file already has them.

Every other trail is open to every vehicle. A route for a motorcycle leaves out closed and no-motorcycle trails; an
ATV (50 in) leaves out closed trails; a UTV (60 in) leaves out closed trails and 50-inch ones.
