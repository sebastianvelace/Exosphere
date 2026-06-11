-- Gravity Turn Launch to 200km circular orbit
-- Uses the Exosphere autopilot scripting API
--
-- A gravity turn is an efficient launch profile that uses the rocket's
-- natural tendency to pitch over as it gains speed. Rather than manually
-- steering, we tilt slightly then let aerodynamic forces and gravity do
-- the work of bending the trajectory toward horizontal.

local TARGET_ORBIT = 200000  -- 200km target circular orbit altitude (meters)

-- ============================================================
-- LAUNCH SEQUENCE
-- ============================================================

-- Full throttle and ignite first stage
THROTTLE(1.0)
STAGE()

-- Wait for liftoff — ensure we have cleared the launch clamps
-- before initiating any guidance commands
WAIT_UNTIL(function() return ALT() > 50 end)

-- ============================================================
-- GRAVITY TURN PITCH PROGRAM
-- ============================================================

-- At 1km, begin the initial pitch-over to start the gravity turn.
-- We pitch east (heading 90) to take advantage of Earth's rotation.
WAIT_UNTIL(function() return ALT() > 1000 end)
PITCH_TO(80)   -- 10 degrees off vertical — gentle initial tip-over

-- By 10km we are moving fast enough for aerodynamic forces to
-- help steer. Increase pitch-over rate.
WAIT_UNTIL(function() return ALT() > 10000 end)
PITCH_TO(60)   -- 30 degrees from vertical

-- At 25km we are above the densest atmosphere. Drag losses drop
-- significantly. Increase horizontal component aggressively.
WAIT_UNTIL(function() return ALT() > 25000 end)
PITCH_TO(45)   -- 45 degrees — equal vertical and horizontal thrust

-- At 45km we are nearly out of the atmosphere. Most of our thrust
-- should now be building horizontal velocity for orbit.
WAIT_UNTIL(function() return ALT() > 45000 end)
PITCH_TO(15)   -- nearly horizontal, building orbital velocity

-- ============================================================
-- STAGE SEPARATION (if applicable)
-- ============================================================
-- Uncomment and adjust the altitude threshold if using a multi-stage rocket
-- WAIT_UNTIL(function() return ALT() > 60000 end)
-- STAGE()  -- jettison first stage, ignite second stage

-- ============================================================
-- APOAPSIS TARGETING
-- ============================================================

-- Keep burning until our apoapsis (highest point of the trajectory)
-- reaches the target orbit altitude. Then cut engines and coast.
WAIT_UNTIL(function() return AP() >= TARGET_ORBIT end)
THROTTLE(0.0)

PRINT("Apoapsis reached: " .. math.floor(AP()/1000) .. "km — coasting to apoapsis")

-- ============================================================
-- CIRCULARIZATION BURN
-- ============================================================

-- Warp time forward until we are close to apoapsis, then execute
-- a prograde burn to raise the periapsis to match the apoapsis,
-- resulting in a circular orbit.
WARP_TO_APOAPSIS()

-- CIRCULARIZE() calculates the required delta-v for a Hohmann
-- circularization burn at the current apoapsis.
EXECUTE_MANEUVER(CIRCULARIZE())

-- ============================================================
-- MISSION COMPLETE
-- ============================================================

PRINT("Orbit achieved: " .. math.floor(PE()/1000) .. "km x " .. math.floor(AP()/1000) .. "km")
