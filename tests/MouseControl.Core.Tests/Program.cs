using System.Text.Json.Nodes;
using MouseControl.Core;

var tests = new (string Name, Action Run)[]
{
    ("seed reproducibility and reset", SeedReproducibility),
    ("zero humanization is seed/model independent", DeterministicBaseline),
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
    ("backend settings retain version-one compatibility", BackendSettingsCompatibility)
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

static void Near(double actual, double expected, double tolerance = 1e-9) =>
    Assert(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}.");

static void SeedReproducibility()
{
    var settings = new MotionSettings(.6, -.4, 1, 150, MotionModel.Organic);
    var a = new MotionGenerator(settings, 42);
    var b = new MotionGenerator(settings, 42);
    var c = new MotionGenerator(settings, 43);
    var first = a.Step(.016);
    Assert(first == b.Step(.016), "Same seeds differ.");
    Assert(first != c.Step(.016), "Different seeds should vary.");
    for (var i = 0; i < 300; i++) Assert(a.Step(.013) == b.Step(.013), "Seed path differs.");
    a.ResetSession();
    Assert(a.Step(.016) == first, "Reset did not reproduce fresh session.");
}

static void DeterministicBaseline()
{
    var a = new MotionGenerator(new(.4, -.2), 1);
    var b = new MotionGenerator(new(.4, -.2, Model: MotionModel.Organic), 999);
    for (var i = 0; i < 100; i++) Assert(a.Step(.01) == b.Step(.01), "Zero humanization depends on seed/model.");
}

static MotionDelta Integrate(MotionGenerator generator, int count, double dt)
{
    double dx = 0, dy = 0;
    for (var i = 0; i < count; i++) { var delta = generator.Step(dt); dx += delta.Dx; dy += delta.Dy; }
    return new(dx, dy);
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
                "Correlated motion changed abruptly.");
            last = delta;
        }
        Assert(lateral, "Humanization produced no lateral variation.");
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
    File.WriteAllText(path, document.ToJsonString());
    var old = store.Load();
    Assert(old.Success && old.Configuration!.AppSettings.InputBackend == InputBackendKind.Windows &&
        old.Configuration.AppSettings.ArduinoPort == "", "Old version-one settings lost backend defaults.");
    var arduino = configuration with { AppSettings = configuration.AppSettings with
        { InputBackend = InputBackendKind.Arduino, ArduinoPort = "COM9" } };
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
