using AnimeJaNai.Addons.Management;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Text.Json.Nodes;

internal static class RealHostChecks
{
    public static async Task<bool> RunAsync(string output, string root)
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        string data = Path.Combine(output, "data", "addons");
        var view = new AddonsView();
        var window = WindowFor(view); window.Show();
        T Find<T>(string name) where T : Control => view.FindControl<T>(name)!;
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Until(Func<bool> condition)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (!condition())
            {
                if (deadline.IsCancellationRequested) throw new Exception("UI timeout: " + Find<TextBlock>("Status").Text);
                await Task.Delay(20);
            }
        }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message + " Status: " + Find<TextBlock>("Status").Text); }
        Process? host = null;
        try
        {
            Click("ConnectButton"); await Until(() => Find<Button>("InstallButton").IsEnabled);
            string hostPath = Path.Combine(root, "addon-host", "ajn-addon.exe");
            host = Process.GetProcessesByName("ajn-addon").Single(p => string.Equals(p.MainModule?.FileName, hostPath, StringComparison.OrdinalIgnoreCase));
            _ = host.SafeHandle; // Retain a process handle so ExitCode remains available after idle exit.
            var installing = view.ReviewPackageAsync(Path.Combine(root, "addon-development", "counter.ajnaddon"), window);
            await Until(() => window.OwnedWindows.Count > 0);
            var review = window.OwnedWindows.Single();
            var permissions = review.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Check(permissions.Length == 3 && permissions.All(c => c.IsChecked == false), "Unexpected real package permissions");
            foreach (var permission in permissions) permission.IsChecked = true;
            review.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Install").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await installing;
            Click("RefreshButton"); await Until(() => Find<Button>("RefreshButton").IsEnabled);
            Check(Find<Button>("StartButton").IsEnabled && Find<Button>("SaveButton").IsEnabled, "Installed addon controls stayed disabled");
            Check(Find<TextBlock>("AddonState").Text!.Contains("Running"), "Approved addon did not auto-activate");
            Find<StackPanel>("SettingFields").Children.OfType<StackPanel>().Single().Children.OfType<TextBox>().Single().Text = "Integration 日本語";
            Click("SaveButton"); await Until(() => Find<Button>("SaveButton").IsEnabled);
            Click("StartButton"); await Until(() => Find<Button>("StartButton").IsEnabled);
            await view.CloseAsync(); window.Close();
            view = new AddonsView(); window = WindowFor(view); window.Show();
            Click("ConnectButton"); await Until(() => Find<Button>("InstallButton").IsEnabled);
            Check(Find<StackPanel>("SettingFields").Children.OfType<StackPanel>().Single().Children.OfType<TextBox>().Single().Text == "Integration 日本語", "Real settings did not survive reconnect");
            async Task<int> CountAsync()
            {
                Find<TextBlock>("ActionResult").Text = "";
                Find<StackPanel>("ActionFields").Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Find<Button>("SaveButton").IsEnabled && !string.IsNullOrEmpty(Find<TextBlock>("ActionResult").Text));
                return JsonNode.Parse(Find<TextBlock>("ActionResult").Text!)!["starts"]!.GetValue<int>();
            }
            Check(await CountAsync() == 1, "Reconnecting duplicated the manual activation");
            Click("StopButton"); await Until(() => Find<Button>("StopButton").IsEnabled);
            Click("StartButton"); await Until(() => Find<Button>("StartButton").IsEnabled);
            Check(await CountAsync() == 2, "Restart did not preserve private storage");
            await Task.Delay(100);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(output, "addon-real-host.png"));
            Click("RemoveButton"); await Until(() => Find<Button>("InstallButton").IsEnabled && Find<TextBlock>("AddonTitle").Text == "No addons installed");
            if (File.Exists(Path.Combine(root, "addon-host", "native-media.json")))
                await NativePackagedChecks.RunAsync(view, window, root, output);
            if (File.Exists(Path.Combine(root, "addon-development", "service-inspector.ajnaddon")))
                await NetworkPackagedChecks.RunAsync(view, window, root, output);
            await view.CloseAsync(); window.Close();
            Check(await Task.Run(() => host.WaitForExit(45000)), "Idle host did not exit");
            Check(host.ExitCode == 0, "Idle host exited with an error");
            Check(!Directory.EnumerateDirectories(Path.Combine(data, "workers")).Any(), "Worker files remained after stop");
            File.WriteAllText(Path.Combine(output, "results.json"), "{\"passed\":true,\"checks\":[\"packaged host launch\",\"real permission review\",\"real Wasm addon\",\"settings reload\",\"manual activation across reconnect\",\"durable private storage\",\"remove cleanup\",\"idle host exit\"]}");
            return true;
        }
        finally
        {
            // This directory belongs only to this test. Release a manual start
            // even if a UI assertion fails, so a failed test leaves no worker.
            if (host is { HasExited: false })
            {
                await using var cleanup = await ManagementClient.ConnectAsync(data);
                await cleanup.CallAsync("addons.remove", new() { ["id"] = "org.animejanai.counter" });
                await cleanup.CallAsync("addons.remove", new() { ["id"] = "org.animejanai.session-controller" });
                await cleanup.CallAsync("addons.remove", new() { ["id"] = "org.animejanai.service-inspector" });
            }
            await view.CloseAsync(); window.Close(); host?.Dispose();
        }
    }
    private static Window WindowFor(AddonsView view) => new() { Width = 1100, Height = 820, Content = view, Title = "AJN addon integration" };
}
