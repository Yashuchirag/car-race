CAR RACE
========

A circuit racing game: four cars, six circuits, AI opponents and LAN multiplayer.
Made by Chirag Chandrashekar.


RUNNING THE GAME
----------------

1. Unzip the whole folder somewhere, for example your Desktop. Do not run the game
   from inside the zip, and keep every file together: CarRace.exe needs the folders
   next to it.
2. Double-click CarRace.exe.

The first time, Windows may show "Windows protected your PC". That is because the
game is not signed with a paid certificate, not because anything is wrong. Click
"More info", then "Run anyway". Windows only asks once.

Needs: 64-bit Windows 10 or 11 and a graphics card that supports DirectX 12
(most PCs and laptops from the last several years). About 600 MB of disk space.

If the game runs slowly, press Esc during a race and set Quality to Medium or Low.


CONTROLS
--------

                       Keyboard       Xbox-style controller
Steer                  A / D          Left stick
Throttle               W              Right trigger
Brake                  S              Left trigger
Reverse                hold S at a standstill
Handbrake              Space          A
Shift up / down        E / Q          RB / LB   (manual gearbox only)
Back onto the track    R              View
Restart the race       Backspace      Menu
Change camera          C              Y
Pause and settings     Esc
Quit the game          QUIT button on the home screen (bottom left)

Keyboard steering is smoothed for you, so short taps are fine.


PLAYING TOGETHER (LAN)
----------------------

Everyone must be on the same network: the same Wi-Fi or router. It does not work
over the internet.

1. One person clicks HOST LAN GAME in the lobby and picks the circuit, AI cars
   and laps.
2. Everyone else clicks JOIN LAN GAME. The host's game should appear in the list.
   If it does not, type the host's IP address and click JOIN.
   (The host can find their IP address by opening Command Prompt and typing
   ipconfig; it is the "IPv4 Address" line, usually 192.168.x.x.)
3. Everyone clicks READY, then the host starts the race.

The first time you host or join, Windows Firewall asks whether Car Race may use
the network. Tick "Private networks" and click Allow. If you clicked Cancel by
mistake, LAN games will not be found: open "Allow an app through Windows
Firewall" in the Start menu and tick Private for CarRace.

Some public or school Wi-Fi networks block players from seeing each other. A
home network or a phone hotspot works.


CREDITS
-------

Circuit layouts: derived from OpenStreetMap data,
(c) OpenStreetMap contributors, available under the Open Database License (ODbL)
v1.0. See LICENSE-circuits.txt. Elevation from SRTM (public domain), via
OpenTopoData. The Airfield Test Circuit is an original layout.

Textures, sky and materials: ambientCG (Lennart Demes) and Poly Haven
(Dimitrios Savva, Greg Zaal, Jarod Guest and other Poly Haven authors), CC0.

Models and sounds: Kenney (Nature, City, Racing kits; UI, interface and impact
sounds), CC0.

Fonts in the sponsor boards: Lato and EB Garamond (SIL Open Font License),
Roboto (Apache 2.0).

Cars, wheels, trackside scenery and trees were modelled for this game. Engine,
tyre and wind sounds are synthesised in the game, not recorded.

Built with Unity.
