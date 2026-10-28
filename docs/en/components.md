# Components

This page is generated from the program's component catalogue. The label column is the IEC letter code given automatically; the terminal column lists terminal numbers.

## Supply

| Component | Label | Terminals | Description |
|---|---|---|---|
| **DC power supply** | `G1` | L N + − | Produces a steady 24 V DC from 230 V AC (sensors, PLCs, DC coils). Shuts its output down on overload. |
| **Three-phase supply (L1 L2 L3)** | `3~` | L1 L2 L3 | Three-phase mains: 400 V between phases, 230 V phase to neutral. |
| **Three-phase + N + PE supply** | `3~` | L1 L2 L3 N PE | Five-wire TN-S supply: L1, L2, L3, N and PE. |
| **Phase L1** | `L1` | L1 | 230 V phase conductor (formerly R). Shown in brown. |
| **Phase L2** | `L2` | L2 | 230 V phase conductor (formerly S). Shown in black. |
| **Phase L3** | `L3` | L3 | 230 V phase conductor (formerly T). Shown in grey. |
| **Neutral (N)** | `N` | N | Neutral conductor. Phase to neutral is 230 V. Shown in blue. |
| **Protective earth (PE)** | `PE` | PE | Protective earth. Metal enclosures connect to it. Shown in green-yellow. |
| **Control transformer** | `T1` | 1 2 3 4 | Steps the mains voltage down to the control voltage (24 V) and isolates the control circuit. Fuse the secondary against short circuits. |

## Protection

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Fuse** | `F1` | 1 2 | Its wire melts when the current far exceeds the rating. After it blows, use "Replace fuse". |
| **Fuse (3-pole)** | `F1` | 1 2 3 4 5 6 | Three-phase fuse set. |
| **Circuit breaker (1-pole)** | `F1` | 1 2 | Trips on overload (thermal) and short circuit (magnetic: B 4×, C 8×, D 15× rated current). Click its lever in simulation to switch it. |
| **Circuit breaker (3-pole)** | `F1` | 1 2 3 4 5 6 | Circuit breaker protecting all three phases. A fault on one phase opens all three poles. |
| **Motor protection switch** | `Q1` | 1 2 3 4 5 6 | Protects the motor against overload (thermal) and short circuit (magnetic) and is switched by hand. Set it to the motor's rated current. |
| **Thermal overload relay** | `F1` | 1 2 3 4 5 6 | Measures the motor current with its heaters. If the current stays above the setting it trips: contact 95-96 opens the control circuit. Reset it once it has cooled down. |
| **Phase monitoring relay** | `F1` | L1 L2 L3 11 12 14 | Its output relay pulls in only if all three phases are present in the right order (11-14 closes). Prevents the motor from running with a missing or reversed phase. |
| **Residual current device (2-pole)** | `F1` | 1 2 N N' | Trips when the current going out on the phase differs from the current returning on neutral (residual current, 30 mA). Protects people from electric shock. Check it with the "Test" button. |
| **Residual current device (4-pole)** | `F1` | 1 2 3 4 5 6 N N' | Residual current protection for three-phase circuits: trips if the sum of L1, L2, L3 and N currents is not zero. |

## Buttons and switches

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Illuminated push button** | `S1` | 13 14 X1 X2 | A start button with a built-in indicator lamp. The lamp has its own terminals (X1-X2) and usually lights when the contactor pulls in. |
| **Stop push button (NC)** | `S1` | 11 12 | Opens the circuit while pressed. Connected in series in stop circuits. |
| **Start push button (NO)** | `S1` | 13 14 | Its contact is closed while held down. In simulation hold the mouse button; Shift-click to latch it. |
| **Double push button (NO + NC)** | `S1` | 11 12 23 24 | One press opens the upper NC and closes the lower NO contact. Used for push-button interlocking. |
| **0-1-2 cam switch** | `Q1` | 1 3 5 2 4 6 8 10 12 | Connects the three poles to 2-4-6 in position 1 and to 8-10-12 in position 2. For two-speed motors or choosing between two circuits. |
| **Reversing cam switch** | `Q1` | 1 3 5 2 4 6 | Turns a motor forward (1) or reverse (2) by hand; position 2 swaps two phases. For reversing without contactors. |
| **Star-delta cam switch** | `Q1` | L1 L2 L3 U1 V1 W1 U2 V2 W2 | Starts a motor by hand in star (Y), then switches to delta (Δ). In Y it bridges U2-V2-W2. |
| **Emergency stop button** | `S1` | 11 12 | Latches when pressed and opens the circuit; it must be twisted (clicked again) to release. |
| **Limit switch (changeover)** | `B1` | 11 14 12 | Switches when a moving machine part touches it. Click it in simulation to actuate. |
| **Limit switch (NO + NC)** | `B1` | 11 12 23 24 | Limit switch with one NC and one NO contact. |
| **Selector switch (0-1)** | `S1` | 13 14 21 22 | Maintained switch: 21-22 closed in 0, 13-14 closed in 1. Used for hand/auto selection and similar. |
| **Selector switch (1-0-2)** | `S1` | 13 14 23 24 | Three-position switch: 13-14 closes in 1, 23-24 in 2, both open in 0. |
| **Three-pole switch** | `Q1` | 1 2 3 4 5 6 | Manually operated three-pole switch. Click to toggle. |
| **Three-pole two-position switch** | `Q1` | 1 2 3 4 5 6 7 8 9 | Each pole switches between two positions; can be used like a reversing cam switch. |

## Relays and contactors

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Counter** | `K1` | A1 A2 R1 R2 | Counts every pulse on the count coil (A1-A2); its contacts switch when the preset is reached. Voltage on R1-R2 resets it. |
| **Flasher relay** | `K1` | A1 A2 | While its coil is energised it opens and closes its contacts with the set times (flashing warning lamp). |
| **Contactor / relay coil** | `K1` | A1 A2 | When rated voltage is applied the contactor pulls in and all contacts with the same label switch. Coil terminals A1-A2. |
| **Timer relay (off-delay)** | `KT1` | A1 A2 | Contacts switch immediately when energised and return after the set time once de-energised. |
| **Timer relay (on-delay)** | `KT1` | A1 A2 | When energised it waits for the set time, then switches its timed contacts. Resets immediately when de-energised. |
| **Star-delta timer relay** | `KT1` | A1 A2 17 18 28 | When energised 17-18 (star) closes; after the set time it opens and 50 ms later 17-28 (delta) closes. The pause keeps both contactors from being closed at once. |

## Contacts

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Three-pole NC contact** | `K1` | 1 2 3 4 5 6 | Three-pole normally closed contact. |
| **Main contacts (3-pole, NO)** | `K1` | 1 2 3 4 5 6 | Main power contacts of a contactor (1-2, 3-4, 5-6). Connect the motor to the mains. |
| **Changeover contact** | `K1` | 11 12 14 | Common terminal (11) connects to 12 at rest and to 14 when the coil pulls in. |
| **Normally closed contact (NC)** | `K1` | 21 22 | Closed while the coil is released and opens when it pulls in. Used for interlocking. Numbered 11-12, 21-22... |
| **Normally open contact (NO)** | `K1` | 13 14 | Closes when its coil (same label) pulls in. Used for latching and interlocks. Numbered 13-14, 23-24... |
| **Overload relay contacts (95-96, 97-98)** | `F1` | 95 96 97 98 | Contacts of the overload relay. 95-96 (NC) goes in series with the contactor coil; 97-98 (NO) lights a fault lamp. Its label must match the relay (F2). |
| **Overload contact 95-96 (NC)** | `F1` | 95 96 | NC contact of the overload relay: in series with the contactor coil, it opens when the relay trips and stops the motor. Its label must match the relay (F2). |
| **Overload contact 97-98 (NO)** | `F1` | 97 98 | NO contact of the overload relay: closes when the relay trips and switches on a fault lamp or horn. |

## Sensors

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Float switch** | `B1` | 11 12 14 | Switches when the liquid level rises to the upper value and returns at the lower value. Set the level with the slider on the right in simulation. |
| **Photocell (light sensor)** | `B1` | 11 12 14 | Actuates when the light level falls below the setting (at dusk); used for street and garden lighting. |
| **Pressure switch** | `B1` | 11 12 14 | Switches when the pressure reaches the set point and returns when it drops by the differential (compressor, booster pump). |
| **Capacitive proximity sensor** | `B1` | BN + BK BU − | Also detects non-metal objects (liquid, plastic, grain). Wired the same way as an inductive sensor. |
| **Inductive proximity sensor** | `B1` | BN + BK BU − | Gives an output when a metal object approaches. 3-wire, 24 V DC: brown (+), blue (−), black (output). A PNP output switches the load to +, NPN to −. Click it in simulation to bring an object near. |
| **Thermostat** | `B1` | 11 12 14 | In heating mode it is actuated below the set point (the heater runs) and releases above it. Cooling mode is the opposite. |

## Lamps and loads

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Fan** | `M1` | X1 X2 | Small single-phase motor that speeds up when energised (ventilation, panel cooling). |
| **Heater** | `E1` | X1 X2 | Resistive load that turns electrical energy into heat. Set its power in the properties. |
| **Horn / buzzer / siren** | `P1` | X1 X2 | Sounds when energised. Can be set as horn, buzzer, siren or bell; sound can be turned off on the View tab. |
| **Indicator lamp** | `H1` | X1 X2 | Lights up when voltage is applied. Colour and rated voltage can be changed in the properties. |
| **Solenoid valve** | `Y1` | A1 A2 | Opens the valve (water, air, gas) when its coil is energised. Its coil voltage is chosen like a contactor coil. |

## Motors

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Single-phase motor** | `M1` | U1 U2 Z1 Z2 | Main winding (U1-U2) and auxiliary winding with capacitor (Z1-Z2). Swap the auxiliary winding ends to reverse. |
| **Single-phase motor (centrifugal switch)** | `M1` | U1 U2 Z1 Z2 | The centrifugal switch disconnects the auxiliary winding once the motor speeds up. |
| **Two-speed motor (separate windings)** | `M1` | 1U 1V 1W 2U 2V 2W PE | Motor with two separate star windings: 1U-1V-1W low speed, 2U-2V-2W high speed. Never supply both at once. |
| **Three-phase motor** | `M1` | U V W PE | Three-phase induction motor. Swapping two phases reverses it; with a missing phase it cannot start and overheats. |
| **Star-delta motor (6 terminals)** | `M1` | U1 V1 W1 W2 U2 V2 PE | Motor with open terminal box. For star, join W2-U2-V2; for delta, connect U1-W2, V1-U2, W1-V2. |
| **Brake motor** | `M1` | U V W PE B1 B2 | The motor cannot turn until the brake coil (B1-B2) is energised. Supply the coil together with the motor; when power is cut the brake stops it at once (cranes, lifts). |
| **Dahlander motor (Δ/YY)** | `M1` | 1U 1V 1W 2U 2V 2W PE | Two-speed motor with one winding. Low speed: supply 1U-1V-1W (delta). High speed: supply 2U-2V-2W and bridge 1U-1V-1W together (double star). |
| **DC motor** | `M1` | A1 + A2 − | DC motor. Its speed follows the voltage; swapping the poles (A1 +, A2 −) reverses it. |

## Ready-made systems

| Component | Label | Terminals | Description |
|---|---|---|---|
| **Door system 1** | `SYS1` | U V W S1a S1b S1c S1d S2a S2b S2c S2d | Motor-driven sliding door. It presses the limit switches when fully open and fully closed. |
| **Door system 2** | `SYS1` | U V W S1a S1b S1c S1d S2a S2b S2c S2d | Door that opens upwards, with a limit switch at each end. |
| **Elevator (5 floors)** | `SYS1` | U V W S1a S1b S1c S1d S2a S2b S2c S2d S3a S3b S3c S3d S4a S4b S4c S4d S5a S5b S5c S5d | Five-floor elevator. The car presses that floor's limit switch when it arrives. |

## Other

| Component | Label | Terminals | Description |
|---|---|---|---|
| **PE busbar** | `PE` | 1 2 3 4 5 6 | Earth busbar with all terminals joined. Protective conductors of motor and panel enclosures meet here. |
| **Sheet connector** | `1` | 1 | All arrows with the same name are the same conductor, even on other sheets. Shortens long wires and links between sheets. |
| **Terminal** | `X1` | 1 2 | Terminal block: both ends are joined inside. The connection point between the panel and outside devices. Give its number in the properties (X1:3). |

71 components in total.
