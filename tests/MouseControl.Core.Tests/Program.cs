using System.Text.Json.Nodes;
using System.Reflection;
using MouseControl.Core;

var tests = new (string Name, Action Run)[]
{
    ("seed reproducibility and reset", SeedReproducibility),
    ("zero humanization is seed/model independent", DeterministicBaseline),
    ("Classic is the exact straight baseline at every strength", ClassicBaseline),
    ("Classic optional effects act independently and pause together", ClassicEffects),
    ("timer partition invariance", TimerPartition),
    ("zero vector and component bounds", MotionBounds),
    ("smooth onset and correlated lateral variation", MotionShape),
    ("timer gaps do not catch up", TimerGaps),
    ("invalid settings leave generator unchanged", InvalidMotion),
    ("activation enable/edit/suspend rearm", ActivationRearm),
    ("profile roundtrip and stable identifiers", ProfileRoundtrip),
    ("corrupt and unsupported documents are preserved", CorruptPreservation),
    ("invalid save preserves previous document", InvalidSave),
    ("writer lock failure and retry", LockRetry),
    ("replacement failure preserves document and cleans temporary file", ReplaceFailure),
    ("parallel readers observe complete documents", AtomicReaders),
    ("backend settings retain version-one compatibility", BackendSettingsCompatibility),
    ("nonzero commanded components never reverse", DirectionSigns),
    ("variation scales with the commanded vector", VectorScaling),
    ("maximum strength produces distinct bounded variation", ModelStrength),
    ("all models and overlays persist with optional compatibility", ModelPersistence),
    ("parameter refreshes evolve on nonperiodic bounded schedules", NonperiodicCadence),
    ("combined effects preserve motion invariants and pause together", CombinedEffects),
    ("Bezier signed geometry and segment stitching", BezierGeometry),
    ("embedded third-party license notices", EmbeddedLicenses)
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} behavior checks passed.");
return failed == 0 ? 0 : 1;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void EmbeddedLicenses()
{
    foreach (var resource in new[] { "MouseControl.GhostCursorSharp.License", "MouseControl.GhostCursor.License" })
    {
        using var stream = typeof(MotionGenerator).Assembly.GetManifestResourceStream(resource);
        Assert(stream is not null, "Embedded attribution is missing: " + resource);
        using var reader = new StreamReader(stream!);
        Assert(reader.ReadToEnd().Contains("MIT License", StringComparison.Ordinal), "Embedded license text is incomplete.");
    }
}

static void Near(double actual, double expected, double tolerance = 1e-9) =>
    Assert(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}.");

static void SeedReproducibility()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        var settings = new MotionSettings(.6, -.4, 1, 150, model);
        var a = new MotionGenerator(settings, 42);
        var b = new MotionGenerator(settings, 42);
        var c = new MotionGenerator(settings, 43);
        var first = a.Step(.016);
        Assert(first == b.Step(.016), "Same seeds differ.");
        Assert((first != c.Step(.016)) == (model != MotionModel.Classic), "Unexpected seed dependence.");
        for (var i = 0; i < 300; i++) Assert(a.Step(.013) == b.Step(.013), "Seed path differs.");
        a.ResetSession();
        Assert(a.Step(.016) == first, "Reset did not reproduce fresh session.");
    }
}

static void DeterministicBaseline()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        var baseline = new MotionGenerator(new(.4, -.2), 1);
        var candidate = new MotionGenerator(new(.4, -.2, Model: model), 999);
        var first = candidate.Step(.016);
        Near(first.Dx, .046367015676019474, 1e-14);
        Near(first.Dy, -.023183507838009737, 1e-14);
        Assert(first == baseline.Step(.016), "Zero-strength baseline changed.");
        for (var i = 0; i < 100; i++) Assert(baseline.Step(.01) == candidate.Step(.01), "Zero humanization depends on seed/model.");
    }
}

static MotionDelta Integrate(MotionGenerator generator, int count, double dt)
{
    double dx = 0, dy = 0;
    for (var i = 0; i < count; i++) { var delta = generator.Step(dt); dx += delta.Dx; dy += delta.Dy; }
    return new(dx, dy);
}

static void ClassicBaseline()
{
    Assert(new MotionSettings().Model == MotionModel.Classic, "New settings do not default to Classic.");
    foreach (var h in new[] { 0.0, .1, .5, 1.0 })
    foreach (var seed in new[] { -19, 0, 42, int.MaxValue })
    foreach (var vector in new[] { (.6, -.3), (-1.0, 1.0), (0.0, -.5), (0.0, 0.0) })
    {
        var settings = new MotionSettings(vector.Item1, vector.Item2, h, 2000, MotionModel.Classic);
        var classic = new MotionGenerator(settings, seed);
        var baseline = new MotionGenerator(settings with { Humanization = 0, Model = MotionModel.Smooth }, 3);
        for (var i = 0; i < 500; i++)
        {
            var dt = i % 3 == 0 ? .001 : i % 3 == 1 ? .013 : .086;
            Assert(classic.Step(dt) == baseline.Step(dt), "Classic changed the exact legacy baseline.");
        }
        classic.ResetSession(); baseline.ResetSession();
        Assert(classic.Step(.017) == baseline.Step(.017), "Classic reset changed the baseline.");
        classic.UpdateSettings(settings); baseline.ResetSession();
        Assert(classic.Step(.017) == baseline.Step(.017), "Classic settings update changed the baseline.");
    }
}

static void ClassicEffects()
{
    foreach (var pulse in new[] { false, true })
    foreach (var corrections in new[] { false, true })
    {
        var settings = new MotionSettings(.25, 0, 1, 150, MotionModel.Classic, pulse, corrections);
        var generator = new MotionGenerator(settings, 73);
        var baseline = new MotionGenerator(settings with { PulseOverlay = false, CorrectionsOverlay = false }, 73);
        var legacyCorrections = new MotionGenerator(settings with { Model = MotionModel.Corrections }, 73);
        var partial = new MotionGenerator(settings with { Humanization = .5 }, 73);
        var changed = false; var lateral = false; var paused = 0;
        for (var i = 0; i < 3000; i++)
        {
            var delta = generator.Step(.005);
            changed |= delta != baseline.Step(.005);
            lateral |= Math.Abs(delta.Dy) > .0001;
            if (i > 200 && delta == default) paused++;
            if (!corrections) Assert(delta.Dy == 0, "Pulse alone bent the Classic baseline.");
            else Assert(delta == legacyCorrections.Step(.005), "Classic Nonlinearity changed the legacy Corrections effect.");
            Assert(partial.Step(.005).Dx > 0, "Partial-strength Classic effects stopped forward motion.");
        }
        Assert(changed == (pulse || corrections), "Classic effect selection did not control variation.");
        Assert(lateral == corrections, "Classic Nonlinearity did not control lateral variation.");
        Assert(pulse ? paused > 100 : paused == 0, "Classic full-strength Pulse pause behavior changed.");
    }
}

static void TimerPartition()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        var settings = new MotionSettings(.7, -.3, .9, Model: model);
        var a = Integrate(new(settings, 12), 1000, .001);
        var b = Integrate(new(settings, 12), 100, .01);
        var jitter = new MotionGenerator(settings, 12);
        double dx = 0, dy = 0;
        for (var i = 0; i < 50; i++)
        {
            var x = jitter.Step(.007); var y = jitter.Step(.013);
            dx += x.Dx + y.Dx; dy += x.Dy + y.Dy;
        }
        Near(a.Dx, b.Dx); Near(a.Dy, b.Dy); Near(a.Dx, dx); Near(a.Dy, dy);
    }
}

static void MotionBounds()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    foreach (var h in new[] { 0.0, .5, 1.0 })
    foreach (var x in new[] { -1.0, 0, 1.0 })
    foreach (var y in new[] { -1.0, 0, 1.0 })
    {
        var generator = new MotionGenerator(new(x, y, h, 150, model), 54);
        for (var i = 0; i < 200; i++)
        {
            var dt = i % 2 == 0 ? .003 : .017;
            var delta = generator.Step(dt);
            Assert(double.IsFinite(delta.Dx) && double.IsFinite(delta.Dy), "Nonfinite output.");
            Assert(Math.Abs(delta.Dx) <= 150 * dt + 1e-10 && Math.Abs(delta.Dy) <= 150 * dt + 1e-10, "Component exceeded bound.");
            if (x == 0 && y == 0) Assert(delta == default, "Zero vector moved.");
        }
    }
}

static void MotionShape()
{
    var baseline = new MotionGenerator(new(.5, 0), 4);
    var first = baseline.Step(.005);
    Assert(first.Dx > 0 && first.Dx < .02, "Onset is not smooth.");
    var previous = first.Dx;
    for (var i = 0; i < 150; i++)
    {
        var delta = baseline.Step(.005);
        Assert(delta.Dx >= previous && delta.Dy == 0, "Baseline onset is not monotonic/straight.");
        previous = delta.Dx;
    }
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        var generator = new MotionGenerator(new(.5, 0, 1, Model: model), 4);
        var last = generator.Step(.005);
        var lateral = false;
        for (var i = 0; i < 1000; i++)
        {
            var delta = generator.Step(.005);
            lateral |= Math.Abs(delta.Dy) > .0001;
            Assert(Math.Abs(delta.Dx - last.Dx) < .03 && Math.Abs(delta.Dy - last.Dy) < .03,
                $"{model} motion changed abruptly at sample {i}: dx change={Math.Abs(delta.Dx - last.Dx)}, dy change={Math.Abs(delta.Dy - last.Dy)}.");
            last = delta;
        }
        Assert(lateral == (model != MotionModel.Classic), "Unexpected base-model lateral variation.");
    }
}

static void TimerGaps()
{
    var settings = new MotionSettings(.5, .5, 1);
    var generator = new MotionGenerator(settings, 5);
    Integrate(generator, 50, .02);
    Assert(generator.Step(2) == default, "Gap produced catch-up displacement.");
    Assert(generator.Step(.01) == new MotionGenerator(settings, 5).Step(.01), "Gap did not reset onset.");
    Assert(generator.Step(0) == default && generator.Step(-1) == default, "Nonpositive delta moved.");
    AssertThrows<ArgumentOutOfRangeException>(() => generator.Step(double.NaN));
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void InvalidMotion()
{
    foreach (var settings in new[] {
        new MotionSettings(X: 1.01), new MotionSettings(Y: double.NaN),
        new MotionSettings(Humanization: -1), new MotionSettings(MaxComponentRate: 0), new MotionSettings(MaxComponentRate: 2001),
        new MotionSettings(MaxComponentRate: double.PositiveInfinity), new MotionSettings(Model: (MotionModel)9) })
        AssertThrows<ArgumentOutOfRangeException>(() => new MotionGenerator(settings, 1));
    var valid = new MotionSettings(.5, .2, .5);
    var generator = new MotionGenerator(valid, 9);
    var control = new MotionGenerator(valid, 9);
    AssertThrows<ArgumentOutOfRangeException>(() => generator.UpdateSettings(new(X: 2)));
    Assert(generator.Step(.01) == control.Step(.01), "Failed update changed generator.");
}

static void ActivationRearm()
{
    var gate = new ActivationGate();
    Assert(!gate.Enabled && !gate.Step(true, false), "Gate starts enabled.");
    gate.SetEnabled(true);
    Assert(!gate.Step(true, false), "Held input became active on enable.");
    Assert(!gate.Step(false, false) && gate.Step(true, false), "Release/press did not arm.");
    gate.SetEnabled(true);
    Assert(gate.Step(true, false), "Idempotent enabled update unexpectedly invalidated gate.");
    gate.Invalidate();
    Assert(!gate.Step(true, false), "Edit did not invalidate.");
    gate.Step(false, false);
    Assert(gate.Step(true, false), "Edited gate did not rearm.");
    Assert(!gate.Step(true, true) && !gate.Step(true, false), "Suspend resumed a held input.");
    gate.Step(false, true);
    Assert(!gate.Step(true, false), "Release while suspended incorrectly armed gate.");
    gate.Step(false, false);
    Assert(gate.Step(true, false), "Suspend rearm failed.");
    gate.SetEnabled(false);
    gate.Step(false, false);
    gate.SetEnabled(true);
    Assert(!gate.Step(true, false), "Disabled release incorrectly armed gate.");
}

static void WithStore(Action<ProfileStore, string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "MouseControl.Core.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "settings.json");
    try { test(new(path), path); }
    finally { Directory.Delete(directory, recursive: true); }
}

static ProfileConfiguration Sample()
{
    var configuration = ProfileConfiguration.CreateDefault();
    var first = configuration.Profiles[0] with { Motion = new(.4, -.2, .8, 123, MotionModel.Organic) };
    var second = first with { Id = Guid.NewGuid(), Activation = ActivationBinding.LmbAndRmb };
    return configuration with
    {
        Profiles = new[] { first, second },
        AppSettings = new(new(0x79, HotkeyModifiers.Alt | HotkeyModifiers.Control), second.Id)
    };
}

static void ProfileRoundtrip() => WithStore((store, path) =>
{
    Assert(store.Load().Success && !File.Exists(path), "Missing file must yield defaults without writing.");
    var config = Sample();
    Assert(store.Save(config).Success, "Save failed.");
    var loaded = store.Load();
    Assert(loaded.Success, loaded.Error ?? "Load failed.");
    Assert(loaded.Configuration!.AppSettings == config.AppSettings, "Global settings changed.");
    Assert(loaded.Configuration.Profiles.SequenceEqual(config.Profiles), "Profiles changed.");
    Assert(!File.ReadAllText(path).Contains("enabled", StringComparison.OrdinalIgnoreCase), "Enabled state was persisted.");
    Assert(!new ActivationGate().Enabled, "Fresh runtime was enabled.");
    foreach (var binding in Enum.GetValues<ActivationBinding>())
    {
        config = config with { Profiles = config.Profiles.Select(p => p with { Activation = binding }).ToArray() };
        Assert(store.Save(config).Success && store.Load().Configuration!.Profiles[0].Activation == binding, "Activation roundtrip failed.");
    }
});

static void CorruptPreservation() => WithStore((store, path) =>
{
    var config = Sample();
    Assert(store.Save(config).Success, "Setup save failed.");
    var valid = File.ReadAllText(path);
    var variants = new List<string> { "{bad", "null", "", valid.Replace("\"version\": 1", "\"version\": 2") };
    var mutations = new Action<JsonObject>[] {
        x => x.Remove("version"), x => x["enabled"] = true,
        x => x["profiles"] = new JsonArray(),
        x => x["appSettings"]!["activeProfileId"] = Guid.NewGuid().ToString(),
        x => x["profiles"]![1]!["id"] = x["profiles"]![0]!["id"]!.GetValue<string>(),
        x => x["profiles"]![0]!["motion"]!["x"] = 2,
        x => x["profiles"]![0]!["motion"]!.AsObject().Remove("maxComponentRate"),
        x => x["profiles"]![0]!["activation"] = "Unknown",
        x => x["profiles"]![0]!["color"] = "#zzzzzz",
        x => x["profiles"]![0]!["name"] = " ",
        x => x["appSettings"]!["toggleHotkey"]!["virtualKey"] = 0,
        x => x["appSettings"]!["toggleHotkey"]!["modifiers"] = 32
    };
    foreach (var mutate in mutations)
    {
        var document = JsonNode.Parse(valid)!.AsObject(); mutate(document); variants.Add(document.ToJsonString());
    }
    variants.Add(new string(' ', 1_048_577));
    foreach (var variant in variants)
    {
        File.WriteAllText(path, variant);
        var original = File.ReadAllBytes(path);
        Assert(!store.Load().Success, "Invalid document accepted.");
        Assert(!store.Save(config).Success, "Corrupt document overwritten.");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "Corrupt document bytes changed.");
    }
});

static void InvalidSave() => WithStore((store, path) =>
{
    var config = Sample();
    Assert(store.Save(config).Success, "Setup failed.");
    var original = File.ReadAllBytes(path);
    Assert(!store.Save(config with { Profiles = Array.Empty<Profile>() }).Success, "Empty profile save accepted.");
    Assert(File.ReadAllBytes(path).SequenceEqual(original), "Failed validation changed previous document.");
});

static void LockRetry() => WithStore((store, path) =>
{
    var config = Sample();
    using (var blocker = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        Assert(!store.Save(config).Success && !File.Exists(path), "Concurrent writer was not rejected safely.");
    Assert(store.Save(config).Success && store.Load().Success, "Retry after lock release failed.");
});

static void ReplaceFailure() => WithStore((store, path) =>
{
    var config = Sample();
    Assert(store.Save(config).Success, "Setup failed.");
    var original = File.ReadAllBytes(path);
    using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        Assert(!store.Save(config with { AppSettings = config.AppSettings with { ToggleHotkey = new(0x78, HotkeyModifiers.None) } }).Success,
            "Replacement should fail when target does not permit deletion.");
    Assert(File.ReadAllBytes(path).SequenceEqual(original), "Failed replacement damaged previous document.");
    Assert(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "Temporary file leaked after replacement failure.");
    Assert(store.Save(config).Success, "Recovery retry failed.");
});

static void AtomicReaders() => WithStore((store, path) =>
{
    var config = Sample();
    Assert(store.Save(config).Success, "Setup failed.");
    using var cancellation = new CancellationTokenSource();
    var reads = 0;
    var reader = Task.Run(() =>
    {
        while (!cancellation.IsCancellationRequested)
        {
            var loaded = new ProfileStore(path).Load();
            Assert(loaded.Success, loaded.Error ?? "Reader observed partial JSON.");
            Interlocked.Increment(ref reads);
        }
    });
    try
    {
        for (var i = 0; i < 60; i++)
        {
            var next = config with { Profiles = config.Profiles.Select(p => p with { Name = $"Generation {i}" }).ToArray() };
            Assert(store.Save(next).Success, "Atomic save failed with concurrent reader.");
        }
    }
    finally { cancellation.Cancel(); reader.GetAwaiter().GetResult(); }
    Assert(reads > 0, "No concurrent reads occurred.");
});

static void BackendSettingsCompatibility() => WithStore((store, path) =>
{
    var configuration = Sample();
    Assert(store.Save(configuration).Success, "Setup save failed.");
    var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    document["appSettings"]!.AsObject().Remove("inputBackend");
    document["appSettings"]!.AsObject().Remove("arduinoPort");
    document["appSettings"]!.AsObject().Remove("startInTray");
    File.WriteAllText(path, document.ToJsonString());
    var old = store.Load();
    Assert(old.Success && old.Configuration!.AppSettings.InputBackend == InputBackendKind.Windows &&
        old.Configuration.AppSettings.ArduinoPort == "" && !old.Configuration.AppSettings.StartInTray,
        "Old version-one settings lost backend or tray defaults.");
    var arduino = configuration with { AppSettings = configuration.AppSettings with
        { InputBackend = InputBackendKind.Arduino, ArduinoPort = "COM9", StartInTray = true } };
    Assert(store.Save(arduino).Success && store.Load().Configuration!.AppSettings == arduino.AppSettings,
        "Arduino selection did not roundtrip.");
    var original = File.ReadAllBytes(path);
    foreach (var settings in new[] {
        arduino.AppSettings with { InputBackend = (InputBackendKind)99 },
        arduino.AppSettings with { ArduinoPort = "" },
        arduino.AppSettings with { ArduinoPort = "COM0" },
        arduino.AppSettings with { ArduinoPort = "COM9/other" },
        arduino.AppSettings with { ArduinoPort = null! } })
    {
        Assert(!store.Save(arduino with { AppSettings = settings }).Success, "Invalid backend settings accepted.");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "Invalid save changed existing backend settings.");
    }
    Assert(ProfileConfiguration.IsArduinoPortName("com10") && !ProfileConfiguration.IsArduinoPortName("COM-1"),
        "COM port validation failed.");
});

static void DirectionSigns()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    foreach (var vector in new[] { (.001, 1.0), (-.001, 1.0), (1.0, -.001), (-1.0, .001), (.5, -.9), (-.5, -.9) })
    {
        var generator = new MotionGenerator(new(vector.Item1, vector.Item2, 1, 2000, model), 21);
        for (var i = 0; i < 500; i++)
        {
            var delta = generator.Step(i % 2 == 0 ? .005 : .095);
            Assert(delta.Dx * vector.Item1 >= 0 && delta.Dy * vector.Item2 >= 0, $"{model} reversed a nonzero commanded axis.");
        }
    }
}

static void VectorScaling()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        var original = new MotionGenerator(new(.2, -.1, 1, 2000, model), 7);
        var small = new MotionGenerator(new(.02, -.01, 1, 2000, model), 7);
        var oldBaseline = new MotionGenerator(new(.8, -.3, 0, 150, model), 8);
        var rescaledBaseline = new MotionGenerator(new(.8 * 150 / 2000, -.3 * 150 / 2000, 0, 2000, model), 9);
        for (var i = 0; i < 500; i++)
        {
            var a = original.Step(.013); var b = small.Step(.013);
            Near(a.Dx, b.Dx * 10); Near(a.Dy, b.Dy * 10);
            var old = oldBaseline.Step(.013); var normalized = rescaledBaseline.Step(.013);
            Near(old.Dx, normalized.Dx); Near(old.Dy, normalized.Dy);
        }
    }
}

static void ModelStrength()
{
    var paths = new Dictionary<MotionModel, MotionDelta[]>();
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        if (model == MotionModel.Classic) continue; // Exact baseline is covered separately.
        const double nominal = .25 * 150;
        const double dt = .005;
        var generator = new MotionGenerator(new(.25, 0, 1, 150, model), 73);
        Integrate(generator, 200, dt);
        var path = Enumerable.Range(0, 2400).Select(_ => generator.Step(dt)).ToArray();
        paths.Add(model, path);
        var gains = path.Select(p => p.Dx / dt / nominal).ToArray();
        var mean = gains.Average();
        var deviation = Math.Sqrt(gains.Average(g => Math.Pow(g - mean, 2)));
        var lateralRms = Math.Sqrt(path.Average(p => Math.Pow(p.Dy / dt / nominal, 2)));
        Assert(deviation > .10 && lateralRms > .10, $"{model} maximum-strength variation is too weak: gain SD={deviation}, lateral RMS={lateralRms}.");
        var bounds = model switch
        {
            MotionModel.Smooth => (.45, 1.55), MotionModel.Organic => (.15, 1.85),
            MotionModel.Wind => (.25, 1.75), MotionModel.Pulse => (0.0, 1.80),
            MotionModel.Corrections => (.55, 1.45), MotionModel.Bezier => (0.0, 3.00), _ => throw new InvalidOperationException()
        };
        Assert(gains.Min() >= bounds.Item1 - .004 && gains.Max() <= bounds.Item2,
            $"{model} exceeded gain bounds.");
        if (model == MotionModel.Pulse)
        {
            Assert(path.Count(p => p == default) > 100 && gains.Max() > 1.5, "Pulse lacks a brief pause or burst.");
            var partial = new MotionGenerator(new(.25, 0, .5, 150, model), 73);
            Integrate(partial, 200, dt);
            Assert(Enumerable.Range(0, 2400).Select(_ => partial.Step(dt)).All(p => p.Dx > 0),
                "Partial-strength Pulse unexpectedly stopped forward motion.");
        }
        if (model == MotionModel.Corrections)
            Assert(path.Count(p => p.Dy > 0) > 600 && path.Count(p => p.Dy < 0) > 600, "Corrections lacks alternating lateral arcs.");
        Console.WriteLine($"  {model}: gain SD={deviation:F3}, lateral RMS={lateralRms:F3}, pauses={path.Count(p => p == default)}");
    }
    foreach (var a in paths.Keys)
    foreach (var b in paths.Keys.Where(b => (int)b > (int)a))
        Assert(paths[a].Zip(paths[b]).Any(pair => Math.Abs(pair.First.Dx - pair.Second.Dx) > 1e-5 ||
            Math.Abs(pair.First.Dy - pair.Second.Dy) > 1e-5), $"{a}/{b} models are not distinct.");
}

static void ModelPersistence() => WithStore((store, path) =>
{
    Assert((int)MotionModel.Smooth == 0 && (int)MotionModel.Organic == 1 && (int)MotionModel.Wind == 2 &&
        (int)MotionModel.Pulse == 3 && (int)MotionModel.Corrections == 4 && (int)MotionModel.Bezier == 5 &&
        (int)MotionModel.Classic == 6, "Existing enum identities changed or Classic was not appended.");
    var config = Sample();
    foreach (var model in Enum.GetValues<MotionModel>())
    foreach (var pulse in new[] { false, true })
    foreach (var corrections in new[] { false, true })
    {
        config = config with { Profiles = config.Profiles.Select(p => p with
            { Motion = p.Motion with { Model = model, PulseOverlay = pulse, CorrectionsOverlay = corrections } }).ToArray() };
        Assert(store.Save(config).Success && store.Load().Configuration!.Profiles.All(p =>
            p.Motion.Model == model && p.Motion.PulseOverlay == pulse && p.Motion.CorrectionsOverlay == corrections),
            $"{model} did not persist.");
    }
    var oldDocument = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    foreach (var profile in oldDocument["profiles"]!.AsArray())
    {
        profile!["motion"]!.AsObject().Remove("pulseOverlay");
        profile["motion"]!.AsObject().Remove("correctionsOverlay");
    }
    File.WriteAllText(path, oldDocument.ToJsonString());
    var old = store.Load();
    Assert(old.Success && old.Configuration!.Profiles.All(p => !p.Motion.PulseOverlay && !p.Motion.CorrectionsOverlay),
        "Old profiles require new effect flags or fail to default them off.");
});

// Observe the actual scheduler state without adding diagnostics to the shipped public API.
static T State<T>(MotionGenerator generator, string field) => (T)typeof(MotionGenerator)
    .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(generator)!;

static void NonperiodicCadence()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    {
        if (model == MotionModel.Classic) continue; // Straight Classic has no random refresh schedule.
        var generator = new MotionGenerator(new(.25, 0, 1, Model: model), 73);
        if (model == MotionModel.Bezier)
        {
            var segmentPeriods = new HashSet<double>();
            for (var i = 0; i < 6000; i++)
            {
                var period = State<double>(generator, "_bezierPeriod");
                Assert(period is >= .45 and <= 1.20, "Bezier segment duration left bounds.");
                segmentPeriods.Add(period); generator.Step(.005);
            }
            Assert(segmentPeriods.Count > 20, "Bezier segments have a constant duration.");
            Console.WriteLine($"  Bezier: {segmentPeriods.Count} distinct segment durations");
            continue;
        }
        var field = model == MotionModel.Corrections ? "_nextCorrectionRefreshCell" : "_nextNoiseRefreshCell";
        var bounds = model switch
        {
            MotionModel.Organic => (.035, .15), MotionModel.Wind => (.15, .50),
            MotionModel.Corrections => (.18, .70), _ => (.08, .22)
        };
        long previousDeadline = 0;
        var intervals = new List<long>();
        for (var i = 0; i < 6000; i++)
        {
            var deadline = State<long>(generator, field);
            if (deadline != previousDeadline)
            {
                intervals.Add(deadline - previousDeadline);
                previousDeadline = deadline;
            }
            generator.Step(.005);
        }
        Assert(intervals.Count > 30 && intervals.Distinct().Count() >= 8, $"{model} has constant or insufficiently varied refresh timing.");
        Assert(intervals.All(n => n >= Math.Ceiling(bounds.Item1 / .005) && n <= Math.Ceiling(bounds.Item2 / .005)),
            $"{model} refresh interval left its bounds.");
        Assert(intervals.Zip(intervals.Skip(1)).All(pair => Math.Abs(pair.First - pair.Second) <=
            Math.Ceiling(.35 * (bounds.Item2 - bounds.Item1) / .005) + 1), $"{model} hold durations jump across their full range.");
        Console.WriteLine($"  {model}: {intervals.Distinct().Count()} distinct refresh intervals, {intervals.Min() * 5}–{intervals.Max() * 5} ms");
    }
    var pulse = new MotionGenerator(new(.25, 0, 1, Model: MotionModel.Pulse), 73);
    var periods = new HashSet<double>(); var pauses = new HashSet<double>();
    for (var i = 0; i < 6000; i++)
    {
        var period = State<double>(pulse, "_pulsePeriod");
        var pause = State<double>(pulse, "_pulsePauseFraction");
        Assert(period is >= .50 and <= 1.00 && pause is >= .12 and <= .24, "Pulse period/pause outside bounds.");
        periods.Add(period); pauses.Add(pause); pulse.Step(.005);
    }
    Assert(periods.Count > 20 && pauses.Count > 20, "Pulse cycle or pause duration repeats unchanged.");
}

static void CombinedEffects()
{
    foreach (var model in Enum.GetValues<MotionModel>())
    foreach (var pulse in new[] { false, true })
    foreach (var corrections in new[] { false, true })
    {
        var settings = new MotionSettings(.23, -.11, 1, 2000, model, pulse, corrections);
        var a = new MotionGenerator(settings, 91); var same = new MotionGenerator(settings, 91);
        var fine = new MotionGenerator(settings, 91);
        var baseline = new MotionGenerator(settings with { Humanization = 0 }, 101);
        var straight = new MotionGenerator(new(.23, -.11, 0, 2000), 1);
        var zero = new MotionGenerator(settings with { X = 0, Y = 0 }, 1);
        var scaled = new MotionGenerator(settings with { X = .05, Y = -.02 }, 91);
        var smaller = new MotionGenerator(settings with { X = .005, Y = -.002 }, 91);
        var partial = new MotionGenerator(settings with { Humanization = .5 }, 91);
        var last = default(MotionDelta);
        var paused = 0;
        for (var i = 0; i < 1500; i++)
        {
            var delta = a.Step(.01);
            Assert(delta == same.Step(.01), "Combined effects lost seed reproducibility.");
            var first = fine.Step(.003); var second = fine.Step(.007);
            Near(delta.Dx, first.Dx + second.Dx); Near(delta.Dy, first.Dy + second.Dy);
            Assert(delta.Dx >= 0 && delta.Dy <= 0 && Math.Abs(delta.Dx) <= 20 && Math.Abs(delta.Dy) <= 20,
                "Combined effects violated component bound/sign.");
            Assert(baseline.Step(.01) == straight.Step(.01), "Overlays changed zero-strength baseline.");
            Assert(zero.Step(.01) == default, "Overlay moved a zero vector.");
            var large = scaled.Step(.01); var small = smaller.Step(.01);
            Near(large.Dx, small.Dx * 10); Near(large.Dy, small.Dy * 10);
            var middle = partial.Step(.01);
            Assert(middle.Dx >= 0 && middle.Dy <= 0 && Math.Abs(middle.Dx) <= 20 && Math.Abs(middle.Dy) <= 20,
                "Partial-strength combined effects violated bounds/sign.");
            if (i > 100 && delta == default) paused++;
            // Continuous 5 ms interpolation remains smooth even at combined envelope/clamp edges.
            Assert(Math.Abs(delta.Dx - last.Dx) < 2 && Math.Abs(delta.Dy - last.Dy) < 2, "Combined output has an abrupt velocity jump.");
            last = delta;
        }
        if (pulse || model == MotionModel.Pulse)
            Assert(paused > 70, "Full-strength Pulse did not pause both combined components.");
        Assert(a.Step(.101) == default && a.Step(.01) == new MotionGenerator(settings, 91).Step(.01),
            "Combined effect gap did not reset safely.");
        a.ResetSession(); Assert(a.Step(.01) == new MotionGenerator(settings, 91).Step(.01), "Combined reset differs.");
    }
    foreach (var model in new[] { MotionModel.Pulse, MotionModel.Corrections })
    {
        var settings = new MotionSettings(.23, .11, 1, Model: model);
        var plain = new MotionGenerator(settings, 77);
        var duplicate = new MotionGenerator(settings with
            { PulseOverlay = model == MotionModel.Pulse, CorrectionsOverlay = model == MotionModel.Corrections }, 77);
        for (var i = 0; i < 1000; i++) Assert(plain.Step(.01) == duplicate.Step(.01), "Matching overlay applied twice.");
    }
    foreach (var model in new[] { MotionModel.Smooth, MotionModel.Organic, MotionModel.Wind, MotionModel.Bezier, MotionModel.Classic })
    {
        var settings = new MotionSettings(.23, .11, 1, Model: model);
        var plain = new MotionGenerator(settings, 17);
        var combined = new MotionGenerator(settings with { PulseOverlay = true, CorrectionsOverlay = true }, 17);
        var distinct = false;
        for (var i = 0; i < 1000; i++)
        {
            distinct |= plain.Step(.01) != combined.Step(.01);
            foreach (var field in new[] { "_speedNoise", "_sideNoise", "_speedTarget", "_sideTarget" })
                Assert(State<double>(plain, field) == State<double>(combined, field), "Overlay changed base random stream.");
            if (model == MotionModel.Bezier)
            {
                Assert(State<double>(plain, "_bezierSpeed") == State<double>(combined, "_bezierSpeed") &&
                    State<double>(plain, "_bezierSide") == State<double>(combined, "_bezierSide"), "Overlay changed Bezier random stream.");
            }
        }
        Assert(distinct, "Overlay combination did not affect output.");
    }
}

static void BezierGeometry()
{
    var type = typeof(MotionGenerator).Assembly.GetType("MouseControl.Core.ThirdParty.GhostCursorBezier")!;
    var create = type.GetMethod("Create")!; var at = type.GetMethod("At")!; var derivative = type.GetMethod("Derivative")!;
    foreach (var end in new[] { new MotionDelta(2, 3), new MotionDelta(-2, 3), new MotionDelta(2, -3),
        new MotionDelta(-2, -3), new MotionDelta(0, -3), default(MotionDelta) })
    {
        var curve = create.Invoke(null, new object[] { new Random(19), end, 1.0 })!;
        var repeated = create.Invoke(null, new object[] { new Random(19), end, 1.0 })!;
        Assert(curve.Equals(repeated), "Bezier geometry lost seeded reproducibility.");
        Assert((MotionDelta)at.Invoke(curve, new object[] { 0.0 })! == default &&
            (MotionDelta)at.Invoke(curve, new object[] { 1.0 })! == end, "Bezier signed endpoints changed.");
        double dx = 0, dy = 0;
        const int samples = 2000;
        for (var i = 0; i <= samples; i++)
        {
            var value = (MotionDelta)derivative.Invoke(curve, new object[] { (double)i / samples })!;
            var weight = i == 0 || i == samples ? .5 : 1;
            dx += value.Dx * weight / samples; dy += value.Dy * weight / samples;
        }
        Near(dx, end.Dx, 1e-6); Near(dy, end.Dy, 1e-6);
    }
    foreach (var vector in new[] { (.5, .25), (-.5, .25), (.5, -.25), (-.5, -.25) })
    {
        var generator = new MotionGenerator(new(vector.Item1, vector.Item2, 1, 150, MotionModel.Bezier), 29);
        var last = generator.Step(.005); var priorElapsed = State<double>(generator, "_bezierElapsed");
        var boundaries = 0;
        for (var i = 0; i < 6000; i++)
        {
            var next = generator.Step(.005); var elapsed = State<double>(generator, "_bezierElapsed");
            if (elapsed < priorElapsed)
            {
                boundaries++;
                Assert(Math.Abs(next.Dx - last.Dx) < .04 && Math.Abs(next.Dy - last.Dy) < .04,
                    "Bezier segment boundary caused a displacement jump.");
            }
            Assert(next.Dx * vector.Item1 >= 0 && next.Dy * vector.Item2 >= 0, "Bezier left a signed quadrant.");
            priorElapsed = elapsed; last = next;
        }
        Assert(boundaries > 20, "Bezier stitch check observed too few segments.");
    }
}
