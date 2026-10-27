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

    // Temporal fix: a chain toggle written as one trailing temporalfix model.
    foreach (var name in new[] { "temporalfix_s1_v1_op18_fp16", "temporalfix_s1_v1.1_op18_fp16",
                                 "temporalfix_s2_v1_op18_fp16", "temporalfix_s3_v1_op18_fp16", "upscaler" })
        File.WriteAllBytes(Path.Combine(data, "onnx", name + ".onnx"), []);
    if (MainWindowViewModel.TemporalFixModelName(1) != "temporalfix_s1_v1.1_op18_fp16")
        throw new Exception("Temporal fix must pick the newest model version, not the last in ordinal order");
    var chain = vm.CurrentSlot.Chains[0];
    chain.AddModel();
    chain.Models[0].Name = "upscaler";
    string Section(string conf) => File.ReadAllText(conf).Replace("\r", "");
    string off = Path.Combine(data, "tfix-off.conf");
    vm.WriteAnimeJaNaiConf(off);
    if (Section(off).Contains("temporalfix") || Section(off).Contains("model_2_"))
        throw new Exception("Temporal fix off must not write a model");
    foreach (int strength in new[] { 1, 2, 3 })
    {
        chain.EnableTemporalFix = true;
        chain.TemporalFixStrength = strength;
        chain.TemporalFixSceneThreshold = 0.2M;
        string full = Path.Combine(data, $"tfix-{strength}.conf");
        vm.WriteAnimeJaNaiConf(full);
        string expected = $"chain_1_model_2_name={MainWindowViewModel.TemporalFixModelName(strength)}\n";
        if (!Section(full).Contains(expected) || !Section(full).Contains("chain_1_model_2_temporal_scene_threshold=0.2\n"))
            throw new Exception($"Full config lacks the temporal fix model for strength {strength}:\n{Section(full)}");
        var reread = vm.ReadAnimeJaNaiConf(full).UpscaleSlots.First(s => s.SlotNumber == "1").Chains[0];
        if (!reread.EnableTemporalFix || reread.TemporalFixStrength != strength || reread.TemporalFixSceneThreshold != 0.2M ||
            reread.Models.Count != 1 || reread.Models[0].Name != "upscaler")
            throw new Exception($"Full config lost temporal fix strength {strength}");

        string profile = Path.Combine(data, $"tfix-profile-{strength}.conf");
        vm.WriteAnimeJaNaiCurrentProfileConf(profile);
        chain.EnableTemporalFix = false;
        vm.ReadAnimeJaNaiConfToCurrentSlot(profile, false);
        chain = vm.CurrentSlot.Chains[0];
        if (!chain.EnableTemporalFix || chain.TemporalFixStrength != strength || chain.Models.Count != 1)
            throw new Exception($"Profile import lost temporal fix strength {strength}");
        Console.WriteLine($"PASS temporal fix strength={strength}");
    }
    chain.EnableTemporalFix = false;
    string offAgain = Path.Combine(data, "tfix-off-again.conf");
    vm.WriteAnimeJaNaiConf(offAgain);
    if (Section(offAgain) != Section(off))
        throw new Exception("Turning temporal fix off must restore the original config");
    Console.WriteLine("PASS temporal fix off restores the config");

    // Hand-written chains: only a plain trailing temporalfix model becomes the toggle.
    string hand = Path.Combine(data, "tfix-hand.conf");
    File.WriteAllText(hand,
        "[global]\nconfig_version=3\n" +
        "[slot_1]\nprofile_name=Mid\nchain_1_model_1_name=temporalfix_s2_v1_op18_fp16\nchain_1_model_2_name=upscaler\n" +
        "[slot_2]\nprofile_name=Resized\nchain_1_model_1_name=upscaler\nchain_1_model_2_resize_factor_before_upscale=50\nchain_1_model_2_name=temporalfix_s2_v1_op18_fp16\n" +
        "[slot_3]\nprofile_name=Trailing\nchain_1_model_1_name=upscaler\nchain_1_model_2_name=temporalfix_s3_v1_op18_fp16\n");
    var slots = vm.ReadAnimeJaNaiConf(hand).UpscaleSlots;
    var mid = slots.First(s => s.SlotNumber == "1").Chains[0];
    if (mid.EnableTemporalFix || mid.Models.Count != 2 || !mid.Models[0].AllModels.Contains("temporalfix_s2_v1_op18_fp16"))
        throw new Exception("A mid-chain temporalfix model must stay a normal, selectable model");
    if (slots.First(s => s.SlotNumber == "2").Chains[0] is var resized && (resized.EnableTemporalFix || resized.Models.Count != 2))
        throw new Exception("A resized trailing temporalfix model must stay a normal model");
    var trailing = slots.First(s => s.SlotNumber == "3").Chains[0];
    if (!trailing.EnableTemporalFix || trailing.TemporalFixStrength != 3 || trailing.Models.Count != 1)
        throw new Exception("A plain trailing temporalfix model must become the toggle");
    if (trailing.Models[0].AllModels.Any(m => m.StartsWith("temporalfix")))
        throw new Exception("Temporal fix models must not be offered in the model list");
    Console.WriteLine("PASS hand-written temporal fix chains");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
