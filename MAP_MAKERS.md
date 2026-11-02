# TRC Tune Loader: a guide for map makers

TRC Tune Loader finds courses that are built into a map. Name a few objects in your map the way this guide shows, and every player with the mod gets them in **Time attack → Tracks**. That means time attacks with sectors, battles, races with grids, speed traps and the road check, with no one having to build the course by hand first.

Everything here is plain Unity objects with the right names. There's no script or package to add.

## The rules for names

- Each name starts with `TRC`, then what the object is, then the course or road name: `TRC Start Akina Downhill`.
- Capitals, spaces and underscores don't matter. `TRC_Start_Akina_Downhill`, `trc start akina downhill` and `TRC Start Akina Downhill` are all the same.
- Copies Unity numbers, like `TRC Mainroad Akina (1)`, or Blender's `.001` and `.002`, are fine: they're the same object. The one without a number comes first, then `.001`, then `.002`.
  - A road or a path split into pieces: every piece is used.
  - Two starts or two finishes for one course: the one without a number is used (the mod's list says so).
  - Sector, grid and trap numbers aren't copy numbers: `TRC Akina Sector 2` is sector 2.
- The course name is whatever comes after the word: `Akina Downhill`, `Tsukuba Circuit`, `Wangan Highway`.
  - **"Circuit"** in the name makes it a circuit (a loop).
  - **"Highway"** makes it a highway course.
  - Anything else is a touge course.

## Setting up a line

Starts, finishes and speed traps are lines across the road. Each one is an **empty object** (a marker is enough):

1. Put it in the **middle of the road**, on the line.
2. Turn it so its **blue arrow (Z)** points the way you drive.
3. Set its **X scale** to the road width in metres, for example 14. Under 2 means the default, 12 m.

An object that's turned off still counts, so you can use your own start and finish signs and hide them. The mod shows them while someone is running that course, and puts them back after.

## The objects

| What | Name | Notes |
| --- | --- | --- |
| Start line | `TRC Start Akina Downhill` | Needed. Set up as above. |
| Finish line | `TRC Finish Akina Downhill` | Needed, except on a circuit: its start line is also the finish line. |
| Main road | `TRC Mainroad Akina` (or `TRC Main road Akina`) | The road mesh itself, **with its collider**. Name the object that has the collider. A road split into pieces can be `TRC Mainroad Akina.001`, `.002` and so on. Every course whose name starts with "Akina" uses it: if the whole car is off this road for half a second, the run stops. Flying above it counts as on it, so jumps along the road are fine. |
| Path | `TRC Path Akina` | Strongly recommended: the line down the middle of the road. No "Downhill" or "Uphill" needed: like the road, every course whose name starts with "Akina" uses it, read from that course's start line. See **The path** below. |
| Sectors | `TRC Akina Sector 1`, `Sector 2`, … | Optional. An empty object where each sector ends; the last sector ends at the finish. No direction needed either: the mod puts them in order along each course, so Akina Downhill and Akina Uphill both get them the right way round. Without them the course is split evenly. |
| Race grid | Nothing, if the path runs on past the start line | The easy way: carry the path on behind the start line (see **Race grids from the path** below) and cars line up along it, two-wide or in a line, whichever the race host picks. Or place your own: `TRC Akina Downhill Grid 1` … `Grid 16`, an empty object where each car lines up, 1 being pole, blue arrow the way the cars face. Placed spots win over the path. |
| Speed traps on a course | `TRC Akina Downhill Trap 1`, `Trap 2`, … | Optional. Set up like a line, numbered in driving order. They show during a time attack and can score a speed trap race. |
| Free-roam speed trap | `TRC Speed Trap Akina Bridge` | Optional. Anywhere on the map, set up like a line. Cars are caught going either way. Name them like `TRC Speed Trap Akina 1`, `TRC Speed Trap Akina 2` and they're grouped under "Akina" in the mod. |

**Only the start and finish need the direction.** The road, the path and the sectors are the same road whichever way you drive, so name them with just the road's name (`TRC Path Akina`, `TRC Akina Sector 1`). If one course needs something different, give it its own with the full course name (`TRC Path Akina Downhill`, `TRC Akina Downhill Sector 1`): a course's own always comes first. Placed grid spots and speed traps on a course do still need the full course name, because cars face a different way in each direction.

### You get the other direction for free

- **Downhill:** a course whose name ends in "Downhill" also gets its uphill twin. "Akina Downhill" makes "Akina Uphill", starting at the finish and ending at the start, with the path and sectors reversed. To make your own uphill instead, add `TRC Start Akina Uphill` and `TRC Finish Akina Uphill`: it uses the shared `TRC Path Akina` and sectors too.
- **Circuits:** "Tsukuba Circuit" also gets "Tsukuba Circuit Reverse", driven the other way from the same line. Placed grid spots aren't flipped: the twin needs its own.

## The path

The path is a line down the middle of the road, from the start line to the finish line. With it, the mod knows exactly where the road goes. It uses that to:

- catch shortcuts
- show how far along everyone is in battles and races
- measure the live delta properly through hairpins

A course in your map that has its path is ready to use from the moment the map loads. Without one, someone has to drive it once with **Record path** first.

### How to make it

The path is a **strip of faces one face across**, down the middle of the road. Lying flat or standing up, both work: the mod finds the middle of the strip. Two easy ways in Blender (or any 3D program):

- **Duplicate the road's own centre line.** If the road mesh has a line of vertices down the middle, select it, duplicate it (`Shift + D`, then right-click so it stays in place) and separate it (`P`, Selection). Then extrude it **straight up or down** by about 0.3 m (`E`, then `Z`, and move it a little). You get a thin "fence" of faces standing on the line. Keep it **under 1 m** tall.
- **Duplicate the road's centre faces.** If there's no line down the middle, select **one row of faces** along the middle of the road, duplicate and separate them the same way. No extruding needed, and it doesn't matter how wide the faces are.

Either way:

1. Run it from the start line to the finish line (or further: see **Race grids from the path** below). Keep every gap between vertices **under 25 m**.
2. **It has to have faces.** Unity throws away edges that have no faces when it imports a model, so a bare line of vertices disappears.
3. Use **one row** of faces, not the whole road: the middle of the row is the path. Two rows works if each face is under about 2 m wide.
4. Lift it a little above the road, or leave it on the road. Height doesn't matter much.
5. Import it into Unity, and in the model's import settings turn on **Read/Write** (the mod can't read the shape without it).
6. Name the object `TRC Path Akina`, turn its **Mesh Renderer off** (so nobody sees it) and give it **no collider**. A long path can be in pieces: `TRC Path Akina`, `TRC Path Akina.001` and so on.

One path is enough for both directions: "Akina Uphill" uses it the other way round. It doesn't matter which end you start drawing from.

### Race grids from the path

Carry the path on **past the start line**, back down the road the cars come from, and races line up along it: no grid objects to place. Carry it on **past the finish line** too, and the other direction (Akina Uphill) lines up there. The course itself still starts and ends at the lines.

- For 16 cars in a line, run it about **130 m** past the line. Two-wide needs about **70 m**.
- Keep it down the middle of the road, the same as the rest of the path.
- If you place `TRC ... Grid` objects for a course, those are used instead.

**Another way:** an empty object named `TRC Path Akina` with **child objects in order along the road**, each an empty placed down the middle of the road. This is slower to place than a strip, but it needs no model.

Line Renderers don't survive the map upload, so don't use one for the path.

## A full example

For a touge road called Akina, run downhill and uphill, with three sectors, race grids both ways and two speed traps:

```
TRC Mainroad Akina              (the road mesh, with its collider)
TRC Start Akina Downhill        (empty: top of the hill, blue arrow downhill, X scale 12)
TRC Finish Akina Downhill       (empty: bottom of the hill, blue arrow downhill, X scale 12)
TRC Path Akina                  (thin strip down the middle, renderer off, Read/Write on,
                                 running on 130 m past the start and past the finish, for the race grids)
TRC Akina Sector 1              (empty, where a sector ends)
TRC Akina Sector 2              (empty, where the next one ends)
TRC Speed Trap Akina 1          (empty across the road, X scale the width)
TRC Speed Trap Akina 2
```

Players get "Akina Downhill" and "Akina Uphill", both with sectors and both ready to race, plus the two speed traps (grouped as "Akina") in free roam.

## Checking your map

1. Load your map in CarX with TRC Tune Loader installed and drive onto it.
2. Open the mod's window (Ctrl + T) and go to **Time attack → Tracks**. Your courses are listed there, with any problems under them in amber. Some examples:
   - "No 'TRC Finish …' object"
   - "Can't read the path: turn on Read/Write"
   - "The path stops 40 m before the finish"
3. Changed something? Press **Look for course objects again**.
4. The mod also writes what it found to `CarX Drift Racing Online\kino\mods\TRCTuneLoader\TRCTuneLoader.log`: every course, every road and how many pieces it found, and every problem.

## Official courses

Times on any map are saved for the player. Courses only count as **official TRC courses** (for TRC leaderboards and ranked events) on maps TRC has added to the mod. If you'd like your map's courses to count, contact TRC.
