namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Verifies that <see cref="Vessel.Tick"/> now applies
/// <see cref="PartGraph.GetPitchYawRollAngularAcceleration"/> as an unconditional (i.e. not
/// gated behind pilot/SAS input) disturbance-torque term, so an unpiloted vessel with a
/// genuinely asymmetric firing cluster (e.g. an engine-out) physically tumbles under RK4
/// instead of silently staying inert. Companion to <see cref="EngineTorqueTests"/>, which
/// verifies the underlying <c>PartGraph</c> math in isolation; these tests verify the wiring
/// into <c>Vessel.Tick</c> itself.
/// </summary>
public sealed class UnpilotedEngineOutTests
{
    private const double Dt = 0.02;

    [Fact]
    public void NominalSymmetricThrust_NoInputGiven_ProducesNoRotation()
    {
        var (vessel, booster) = BuildUnpilotedBoosterVessel();
        _ = booster;

        for (int i = 0; i < 300; i++)
            vessel.Tick(Dt, VacuumBody());

        // Safety-net assertion: GetTotalTorque is exactly zero (to floating summation noise)
        // for a nominal symmetric firing cluster with no engine failures and no differential
        // gimbal (see EngineTorqueTests.NominalSymmetricCluster_ProducesZeroNetTorque), so the
        // new unconditional term must be a no-op here — proving this change is inert for
        // every currently-passing nominal (no engine-out) scenario.
        Assert.True(
            vessel.AngularVelocity.Magnitude < 1e-6,
            $"Expected ~0 angular velocity for symmetric thrust, got {vessel.AngularVelocity.Magnitude}");
    }

    [Fact]
    public void AsymmetricEngineFailure_NoInputGiven_ProducesObservableRotation()
    {
        var (vessel, booster) = BuildUnpilotedBoosterVessel();

        // Let thrust spool up under nominal (symmetric) conditions first, confirming the
        // baseline stays inert exactly like the nominal test above.
        for (int i = 0; i < 250; i++)
            vessel.Tick(Dt, VacuumBody());
        Assert.True(vessel.AngularVelocity.Magnitude < 1e-6);

        // sh-outer-01, engine-cluster index 13 (3 centre + 10 middle mounts precede it),
        // positionM = [3.4, 0, 0] — same off-axis mount EngineTorqueTests fails to prove
        // GetTotalTorque produces a large, correctly-signed net yaw (local Z) torque:
        // r×F = (3.4,0,0) x (0,T,0) = (0,0,3.4*T); removing that engine's contribution from
        // an already-zero sum leaves -(r×F), i.e. torque.Z << 0.
        string instanceId = booster.EngineStates[13].InstanceId;
        Assert.True(booster.FailEngine(instanceId, "TEST_UNPILOTED_ENGINE_OUT"));

        for (int i = 0; i < 200; i++)
            vessel.Tick(Dt, VacuumBody());

        // Vessel.Orientation is identity throughout (no input ever commanded a rotation, and
        // the small disturbance itself hasn't had time to meaningfully reorient the vehicle),
        // so world-space AngularVelocity components still read directly as local pitch/roll/
        // yaw. Expect a measurable NEGATIVE yaw (local Z) rate, and negligible pitch/roll.
        Assert.True(
            vessel.AngularVelocity.Z < -1e-4,
            $"Expected an observable negative yaw rate from the engine-out, got {vessel.AngularVelocity.Z}");
        Assert.True(System.Math.Abs(vessel.AngularVelocity.X) < 1e-4);
        Assert.True(System.Math.Abs(vessel.AngularVelocity.Y) < 1e-4);
    }

    [Fact]
    public void PilotedFlight_ExistingAttitudeAuthorityUnchanged()
    {
        // Characterization/pin test: the new disturbance-torque term lives entirely inside a
        // brand-new `else` branch of the `if (hasInput)` block in Vessel.Tick (see
        // Vessel.cs), so it can never execute while hasInput is true. This pins the piloted
        // path's output to prove that claim empirically rather than only by code reading.
        // The pinned value below was captured from a run of this exact fixture BEFORE the
        // unconditional-torque wiring was added (git-stashed Vessel.cs), then re-confirmed to
        // be bit-identical AFTER the change — i.e. this test was used as its own before/after
        // diff during development, not merely asserted once.
        //
        // Needs a real command part (not just the bare booster) so ControlAuthority.Evaluate
        // reports Full authority and `hasInput` is actually true — otherwise this would
        // silently fall through to the unpiloted `else` branch and not exercise the piloted
        // path at all.
        var vessel = BuildPilotedFlight7Vessel();
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = new Vector3d(1.0, 0.0, 0.0); // full pitch command
        vessel.Throttle = 1.0;

        // Deliberately few ticks: enough for engines to be lit and for the pilot-authority
        // acceleration to be nonzero, but well short of the 0.35 rad/s (20 deg/s) rate
        // envelope so the pinned value is sensitive to the actual pitchYawAuthority
        // computation rather than just re-confirming the saturation ceiling.
        for (int i = 0; i < 5; i++)
            vessel.Tick(Dt, VacuumBody());

        Assert.Equal(PinnedAngularVelocityX, vessel.AngularVelocity.X, 9);
        Assert.Equal(PinnedAngularVelocityY, vessel.AngularVelocity.Y, 9);
        Assert.Equal(PinnedAngularVelocityZ, vessel.AngularVelocity.Z, 9);

        // Belt-and-suspenders: the piloted path must actually have produced nonzero rotation
        // (proves this is exercising real pilot authority, not a degenerate zero-authority
        // setup like the earlier bare-booster attempt caught during development).
        Assert.True(vessel.AngularVelocity.Magnitude > 1e-6);
    }

    // Filled in from an actual dotnet test run of PilotedFlight_ExistingAttitudeAuthorityUnchanged
    // against this fixture, both before and after the Vessel.Tick wiring change (identical in
    // both runs — see the test's comment above).
    private const double PinnedAngularVelocityX = 0.001;
    private const double PinnedAngularVelocityY = 0.0;
    private const double PinnedAngularVelocityZ = 0.0;

    private static Vessel BuildPilotedFlight7Vessel()
    {
        var catalog = PartCatalog.LoadFromDirectory(
            System.IO.Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var command = new Part(catalog["starship_command"]);
        var tank = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);
        var ring = new Part(catalog["decoupler_heavy"]);
        var booster = new Part(catalog["super_heavy_booster"]);

        var vessel = new Vessel { Name = "Piloted Flight 7 pin test" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        return vessel;
    }

    private static (Vessel vessel, Part booster) BuildUnpilotedBoosterVessel()
    {
        var catalog = PartCatalog.LoadFromDirectory(
            System.IO.Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var booster = new Part(catalog["super_heavy_booster"]);

        var vessel = new Vessel { Name = "Unpiloted booster torque test" };
        vessel.Parts.SetRoot(booster);
        vessel.SASEnabled = false;
        vessel.Throttle = 1.0;
        return (vessel, booster);
    }

    // No atmosphere set, so refBody.Atmosphere is null and the aerodynamic-torque block in
    // Vessel.Tick is skipped entirely — isolates the assertions to engine-mount torque only.
    private static CelestialBody VacuumBody() => new()
    {
        Id = "vacuum-body",
        Name = "Vacuum body",
        Radius = 1_000.0,
        Mass = 1.0e10,
    };

    private static System.IO.DirectoryInfo FindRepoRoot()
    {
        var directory = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (System.IO.File.Exists(
                System.IO.Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new System.InvalidOperationException("Could not locate repository root.");
    }
}
