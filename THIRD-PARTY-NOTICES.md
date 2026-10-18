# Third-party notices

The code in this repository is MIT licensed (see [LICENSE](LICENSE)), with the exceptions below.

## The game itself

Thexder is not part of this repository. The name, the art, levels, sounds, music, movies and the original
program belong to their owners: the original Thexder (1985) to Game Arts, and Thexder for Windows 95 (1995)
to Sierra On-Line and its successors. None of these files are included or distributed here, and none of them
are covered by the MIT license. You need your own copy of the game CD to play.

Two data files in `Include\` come from the game rather than from this project's own work, and are not
covered by the MIT license either:

| File | What it is |
|---|---|
| `Include\enemy-types.json` | The enemies' statistics (speed, hit points, weapons, animation frames), taken from the 1995 program so that the game plays the same |
| `Include\credits.txt` | The credits shown in Help > About, as listed in the game's own help file, plus this project's credits |

`Include\assets.txt` lists the names, sizes and SHA-256 checksums of the game files the program checks for.
It holds no game content.

The screenshots in `.github\images\` show the original game's art and are included only to show what this
project does. The banner (`logo.png`) and the application icon (`Include\Thexder.ico`) are drawn by this
project's own code.

## Indeo 3 codebooks (FFmpeg)

`Include\indeo3-codebooks.json` holds the Indeo Video 3 codebook tables used to decode the game's movies. They
are derived from FFmpeg's `libavcodec/indeo3data.h` and keep FFmpeg's license, the GNU Lesser General Public
License version 2.1 or later: https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html

## Libraries

| Package | License | Source |
|---|---|---|
| WPF-UI | MIT | https://github.com/lepoco/wpfui |
| NAudio | MIT | https://github.com/naudio/NAudio |
| Serilog, Serilog.Sinks.File | Apache 2.0 | https://github.com/serilog/serilog |
