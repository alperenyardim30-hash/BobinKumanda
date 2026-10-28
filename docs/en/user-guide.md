# Bobin Kumanda user guide

This guide matches the help page inside the program (<kbd>F1</kbd>).

## Getting started

1. Open a ready-made circuit from **File → Samples** or the **Samples** button on the start screen (for example *Latching*).
2. Start the simulation with <kbd>F5</kbd>.
3. Click the buttons. Hold a start button down, or <kbd>Shift</kbd>-click it to keep it pressed.
4. The event log (bottom panel) shows the sequence: "S1 pressed → K1 pulled in → M1 running".
5. Press <kbd>F5</kbd> again to stop and change the circuit.

> Contacts find their coil by **label**: every contact labelled K1 switches together with coil K1.

## Drawing

![Editor](../images/duzenleyici.png)

- **Adding components:** drag from the palette, or click once and then click on the sheet. Search with <kbd>Ctrl</kbd>+<kbd>F</kbd>. Single-key shortcuts: <kbd>K</kbd> coil, <kbd>A</kbd> NO contact, <kbd>C</kbd> NC contact, <kbd>B</kbd> start, <kbd>D</kbd> stop, <kbd>H</kbd> lamp, <kbd>M</kbd> motor.
- **While placing:** <kbd>R</kbd> rotates, <kbd>F</kbd> mirrors, <kbd>Shift</kbd>+click places several, <kbd>Esc</kbd> cancels.
- **Selecting and moving:** click and drag; drag from empty space for a rubber-band selection. Wires follow at right angles.
- **Labels** follow IEC (K1, S1, H1, F1, M1...). **Properties** are edited in the panel on the right.
- **Undo** <kbd>Ctrl</kbd>+<kbd>Z</kbd> (200 steps). Copied relays get new numbers and their contacts follow the new coil.
- **Sheets** with **+** at the bottom; *sheet connectors* with the same name are the same conductor.
- **Terminal numbers and cross reference** in the View tab: auxiliary contacts are numbered 13-14, 21-22, 33-34... (timed 15-16, 17-18) and every coil lists its contacts.

## Wires

- Click a terminal, then a second one: the wire is routed at right angles. Click empty space for a corner, double-click to end.
- Connecting to the middle of a wire creates a junction. Drag a segment of a selected wire to adjust it.
- Colours follow the phase: L1 brown, L2 black, L3 grey, N blue, PE green-yellow.

## Simulation

The circuit is solved like a real electrical circuit (voltages and currents with phasors). Coils pull in at 85% of rated voltage and drop out below 55%.

| Action | How |
|---|---|
| Start / stop | <kbd>F5</kbd> |
| Pause / resume | <kbd>F6</kbd> |
| Step to next event | <kbd>F10</kbd> |
| Speed | Simulation tab: 0.25× to 10× |
| Push button | Hold the mouse button; <kbd>Shift</kbd>+click latches |
| Switches, selectors, cam switches, emergency stop | Click to change position, or use the slider on the right |
| Sensors | Select and move the slider on the right |
| Proximity sensor | Click to bring an object near |
| Motor load | Select the motor and move "Shaft load" |
| Overload relay, RCD, fuse | **Reset**, **Test**, **Replace fuse** buttons on the right |

## Instruments

- **Multimeter** (Measure tab): click to attach the red (V/Ω) and black (COM) probes. V~ and V= while simulating; Ω with the power off. Measuring Ω on a live circuit gives a warning.
- **Clamp meter**: click a wire; **MAX** holds the starting current.

![Simulation](../images/simulasyon.png)

## Protection and faults

- An unprotected short circuit stops the simulation and flashes the faulty path. A breaker or fuse trips instead.
- Circuit breakers (B/C/D), fuses, overload relays (95-96 / 97-98, reset after cooling), residual current devices (30 mA, with a body-leakage fault on motors), burnt coils, missing phases.
- Click a problem in the Problems tab to show the component.

## Files, export, printing

- Circuits are saved as **.bobin**; unsaved work is backed up every two minutes.
- Old EKTS **.esc** files open and can be saved; components the old program lacks are listed and left out.
- **Document info** fills the title block; **Export as image** writes PNG/JPG; **Print** (<kbd>Ctrl</kbd>+<kbd>P</kbd>) fits A4/A3 landscape. Use "Microsoft Print to PDF" for PDF.
