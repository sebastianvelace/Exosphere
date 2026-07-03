# Starship Flight 7 physics baseline

The default vehicle is explicitly the January 2025 Flight 7 Block 2 stack, not a
generic or future Starship. SpaceX changes the vehicle between flights, so mixing
Flight 7 geometry with later V3 mass or Raptor 3 performance would be less accurate.

## Publicly confirmed values

- Flight 7 flew 33 Raptor engines on Super Heavy and six Raptor engines on Ship.
  SpaceX also confirms hot-stage separation and the 13-engine booster return burns:
  <https://www.spacex.com/launches/starship-flight-7>
- FAA describes the integrated vehicle as approximately 400 ft tall and 30 ft in
  diameter, with LOX/methane propulsion:
  <https://www.faa.gov/space/stakeholder_engagement/spacex_starship/starship_super_heavy>
- SpaceX's public 2020 user's guide documents the 9 m diameter and the intended
  fully reusable two-stage architecture:
  <https://www.spacex.com/media/starship_users_guide_v1.pdf>

SpaceX does not publish a Flight 7 component-by-component dry-mass, propellant or
Raptor 2 performance ledger. Values below are therefore an engineering baseline,
not falsely labelled as exact proprietary data.

## Simulation baseline

| Quantity | Value |
| --- | ---: |
| Integrated height | 123.1 m |
| Core diameter | 9.0 m |
| Liftoff mass | 4,800 t |
| Total dry mass | 300 t |
| Total propellant | 4,500 t |
| Ship wet / dry mass | 1,300 t / 100 t |
| Super Heavy + ring wet / dry mass | 3,500 t / 200 t |
| Super Heavy sea-level thrust | 74.4 MN |
| Super Heavy vacuum thrust | 83.5 MN |
| Ship vacuum thrust | 13.5 MN |
| LOX/CH4 oxidizer-to-fuel ratio | 3.55 |

The dry-mass allocation between nose, tanks, engine bay and hot-stage ring is chosen
to preserve those stage totals and produce a physically meaningful center of mass.
Tests lock the totals, not the uncertain internal allocation.

## Physics contracts

- Mass is invariant across planets.
- Weight is `mass * local gravity`, evaluated at the actual altitude.
- Gravity acceleration is independent of vehicle mass.
- Thrust varies with ambient pressure. Pressure is not capped at one atmosphere;
  the 92-bar Venus surface therefore prevents a sea-level Raptor nozzle from
  producing positive net thrust.
- Booster delta-v includes Ship as carried mass until stage separation.
- Propellant consumption follows the declared 3.55 mixture ratio.
- Aerodynamic projected area uses the declared 123.1 m by 9 m envelope and changes
  continuously between axial and broadside attitudes.
- Manual pitch, yaw and roll authority derives from current thrust, gimbal angle,
  engine lever arm and propellant-dependent moments of inertia. Main-engine-off
  control falls back to conservative reaction-control authority.
- `[G]` MECO/hot-staging is decided solely by `AscentController` via
  `AscentStagingPolicy.ShouldHotStageSuperHeavy` (~2.3 km/s, ~65 km, 6% booster reserve).
  `MissionManager` must not cut throttle on fuel depletion.
- Soft landing permission in `Universe` uses `AscentStagingPolicy.SoftLandingSpeedMps` (3 m/s),
  harmonized with `EDLController` touchdown target.
- Gravity remains N-body in the inertial integrator. Local TWR and weight use the
  dominant body's field at the vessel's current position.

The regression suite validates Earth, Moon, Mars, Jupiter, Saturn and Venus cases.
