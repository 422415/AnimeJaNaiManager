using AnimeJaNaiConfEditor.ViewModels;
using ReactiveUI.Builder;

try
{
    RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
    string data = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(data, "onnx"));
    Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR", data);
    File.WriteAllText(Path.Combine(data, "animejanai.conf"),
        "[global]\nconfig_version=2\nbackend=DirectML\ndefault_slot=0\n" +
        "[slot_1]\nprofile_name=Round trip\nchain_1_rife=yes\nchain_1_rife_ensemble=yes\n");
    var vm = new MainWindowViewModel();
    vm.SelectedSlotNumber = "1";
    if (vm.AnimeJaNaiConf.AutoDisplayRefresh)
        throw new Exception("Automatic display refresh must default to off");
    foreach (bool enabled in new[] { true, false })
    {
        vm.AnimeJaNaiConf.AutoDisplayRefresh = enabled;
        string full = Path.Combine(data, $"display-refresh-{enabled}.conf");
        vm.WriteAnimeJaNaiConf(full);
        if (vm.ReadAnimeJaNaiConf(full).AutoDisplayRefresh != enabled)
            throw new Exception($"Full config lost automatic display refresh={enabled}");
        if (vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf")).AutoDisplayRefresh != enabled)
            throw new Exception($"Autosave lost automatic display refresh={enabled}");
        Console.WriteLine($"PASS automatic display refresh={enabled} export/reload and autosave");
    }
    if (vm.AnimeJaNaiConf.CameraCadence || vm.AnimeJaNaiConf.CameraCadenceActive)
        throw new Exception("Camera pan correction must default to off");
    vm.AnimeJaNaiConf.AutoDisplayRefresh = true;
    vm.AnimeJaNaiConf.UserSelectTensorRt();
    foreach (bool enabled in new[] { true, false })
    {
        vm.AnimeJaNaiConf.CameraCadence = enabled;
        string full = Path.Combine(data, $"camera-cadence-{enabled}.conf");
        vm.WriteAnimeJaNaiConf(full);
        var reloaded = vm.ReadAnimeJaNaiConf(full);
        var autosaved = vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf"));
        if (reloaded.CameraCadence != enabled || autosaved.CameraCadence != enabled ||
            reloaded.CameraCadenceActive != enabled || !reloaded.AutoDisplayRefresh ||
            !autosaved.AutoDisplayRefresh)
            throw new Exception($"Camera correction={enabled} or previous refresh preference was lost");
        Console.WriteLine($"PASS camera correction={enabled} export/reload and autosave; refresh preference retained");
    }
    vm.AnimeJaNaiConf.CameraCadence = true;
    vm.AnimeJaNaiConf.UserSelectDirectMl();
    if (vm.AnimeJaNaiConf.CameraCadenceActive || !vm.AnimeJaNaiConf.CameraCadence)
        throw new Exception("DirectML must suspend correction without discarding the preference");
    vm.AnimeJaNaiConf.UserSelectTensorRt();
    if (!vm.AnimeJaNaiConf.CameraCadenceActive)
        throw new Exception("Returning to TensorRT lost camera correction");
    vm.AnimeJaNaiConf.CameraCadence = false;
    Console.WriteLine("PASS camera correction backend availability");

    foreach (bool ensemble in new[] { true, false })
    {
        vm.CurrentSlot.Chains[0].RifeEnsemble = ensemble;
        string profile = Path.Combine(data, $"ensemble-{ensemble}.conf");
        vm.WriteAnimeJaNaiCurrentProfileConf(profile);
        // Change the destination first so a skipped assignment cannot pass.
        vm.CurrentSlot.Chains[0].RifeEnsemble = !ensemble;
        vm.ReadAnimeJaNaiConfToCurrentSlot(profile, false);
        if (vm.CurrentSlot.Chains[0].RifeEnsemble != ensemble)
            throw new Exception($"Profile import lost RIFE ensemble={ensemble}");
        Console.WriteLine($"PASS ensemble={ensemble}");
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
