using AnimeJaNai.Addons.Management;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

if (args.Length is not (1 or 2)) { Console.WriteLine("AddonUi <new-output-directory> [integrated-preview-root]"); return 2; }
string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR", Path.Combine(output, "data"));
Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT", args.Length == 2 ? Path.GetFullPath(args[1]) : output);
await using var server = args.Length == 1 ? new FixtureServer(Path.Combine(output, "data", "addons")) : null;
var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
try
{
await session.Dispatch<bool>(async () =>
{
    if (args.Length == 2) return await RealHostChecks.RunAsync(output, Path.GetFullPath(args[1]));
    Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    var view = new AddonsView();
    var window = new Window { Width = 1100, Height = 820, Content = view, Title = "AJN addon preview" };
    window.Show();
    T Find<T>(string name) where T : Control => view.FindControl<T>(name)!;
    void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    async Task Until(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ready()) { await Task.Delay(20, deadline.Token); }
    }
    void Check(bool value, string message) { if (!value) throw new Exception(message + " Status: " + Find<TextBlock>("Status").Text); }

    Click("ConnectButton");
    await Until(() => Find<Button>("InstallButton").IsEnabled);
    Check(Find<TextBlock>("AddonTitle").Text == "Sample addon", "Addon was not loaded");
    Check(Find<StackPanel>("SettingFields").Children.Count == 4, "Missing typed settings controls");
    await Task.Delay(100);
    using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(output, "addon-manager.png"));
    var fields = Find<StackPanel>("SettingFields").Children.OfType<StackPanel>().ToArray();
    fields[0].Children.OfType<CheckBox>().Single().IsChecked = false;
    fields[1].Children.OfType<TextBox>().Single().Text = "35.5";
    fields[2].Children.OfType<ComboBox>().Single().SelectedItem = "quiet";
    fields[3].Children.OfType<TextBox>().Single().Text = "Saved 日本語";
    Click("SaveButton");
    await Until(() => server!.Values["rate"]?.GetValue<double>() == 35.5 && Find<Button>("SaveButton").IsEnabled);
    Check(server!.Values["enabled"]!.GetValue<bool>() == false && server.Values["name"]!.GetValue<string>() == "Saved 日本語", "Settings were not saved faithfully");
    Click("RefreshButton"); await Until(() => Find<Button>("RefreshButton").IsEnabled);
    Check(Find<StackPanel>("SettingFields").Children.OfType<StackPanel>().Last().Children.OfType<TextBox>().Single().Text == "Saved 日本語", "Saved values were lost on refresh");
    server.InvalidSettings = ["rate"];
    Click("RefreshButton"); await Until(() => Find<Button>("RefreshButton").IsEnabled);
    Check(Find<TextBlock>("SettingsNotice").IsVisible && server.Values["rate"]!.GetValue<double>() == 35.5,
        "Repair must explain the problem without changing saved data");
    Find<StackPanel>("SettingFields").Children.OfType<StackPanel>().ElementAt(1).Children.OfType<TextBox>().Single().Text = "21";
    Click("SaveButton"); await Until(() => Find<Button>("SaveButton").IsEnabled);
    Check(server.Values["rate"]!.GetValue<double>() == 21 && !Find<TextBlock>("SettingsNotice").IsVisible, "Could not repair incompatible setting");
    var action = Find<StackPanel>("ActionFields").Children.OfType<Button>().Single();
    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await Until(() => Find<TextBlock>("ActionResult").Text?.Contains("action received") == true);
    Click("StartButton"); await Until(() => server.Running && Find<Button>("StartButton").IsEnabled);
    Click("StopButton"); await Until(() => !server.Running && Find<Button>("StopButton").IsEnabled);

    var review = view.ReviewPackageAsync("fixture.ajnaddon", window);
    await Until(() => window.OwnedWindows.Count > 0);
    var dialog = window.OwnedWindows.Single();
    var permissions = dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
    Check(permissions.Length == 2 && permissions.All(p => p.IsChecked == false), "Permissions must start unchecked");
    permissions[0].IsChecked = true;
    using (var frame = dialog.CaptureRenderedFrame()) frame!.Save(Path.Combine(output, "addon-permissions.png"));
    dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Install").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await review;
    Check(server.Granted.SequenceEqual(new[] { "storage.read" }), "Approval did not match selected permissions");
    Check(server.ReviewHash == new string('a', 64), "Install approval was not bound to the reviewed hash");

    Check(Find<StackPanel>("MediaSection").IsVisible, "Approved session permission did not expose media controls");
    string selectedPath = Path.Combine(output, "chosen video.mp4");
    var sourceReview = view.ApproveSourceAsync(window, "org.example.ui", new string('a', 64), selectedPath);
    await Until(() => window.OwnedWindows.Count > 0);
    dialog = window.OwnedWindows.Single();
    Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == selectedPath), "Source consent must show the exact selected file");
    using (var frame = dialog.CaptureRenderedFrame()) frame!.Save(Path.Combine(output, "addon-media-consent.png"));
    dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await sourceReview;
    Check(server.Sources.Count == 0, "Cancelling media consent granted file access");
    sourceReview = view.ApproveSourceAsync(window, "org.example.ui", new string('a', 64), selectedPath);
    await Until(() => window.OwnedWindows.Count > 0);
    window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Allow file").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await sourceReview;
    Check(server.Sources.Count == 1 && server.MediaReviewHash == new string('a', 64), "Media consent was not bound to the reviewed addon");
    Directory.CreateDirectory(Path.Combine(output, "data"));
    const string savedConfiguration = "[global]\nconfig_version=3\nbackend=DirectML\n[slot_3]\nname=Saved custom\n";
    File.WriteAllText(Path.Combine(output, "data", "animejanai.conf"), savedConfiguration);
    var profileReview = view.ApproveProfileAsync(window, "org.example.ui", new string('a', 64));
    await Until(() => window.OwnedWindows.Count > 0);
    dialog = window.OwnedWindows.Single();
    dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ProfileName").Text = "Saved 日本語 profile";
    using (var frame = dialog.CaptureRenderedFrame()) frame!.Save(Path.Combine(output, "addon-profile-consent.png"));
    dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Allow profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await profileReview;
    Check(server.ProfileConfiguration == savedConfiguration && server.Profiles.Count == 1, "Approved profile did not preserve the saved configuration");
    Find<StackPanel>("MediaFields").GetVisualDescendants().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await Until(() => server.Sources.Count == 0 && Find<Button>("RefreshButton").IsEnabled);
    Check(server.Profiles.Count == 1 && !server.Running, "Revocation changed an unrelated selection or left the addon running");
    await view.CloseAsync(); window.Close();
    File.WriteAllText(Path.Combine(output, "results.json"), "{\"passed\":true,\"checks\":[\"typed settings\",\"save and reload\",\"incompatible setting repair\",\"action\",\"start/stop\",\"permissions default denied\",\"exact selected grant\",\"review hash\",\"media consent cancellation\",\"exact file and package consent\",\"saved profile snapshot\",\"media access revocation\"]}");
    return true;
}, CancellationToken.None);
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL " + error);
    File.WriteAllText(Path.Combine(output, "results.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
    return 1;
}
finally
{
    // Dispatch can complete its awaiter inline on the UI thread. Dispose joins
    // that thread, so it must run on a different thread even after successful checks.
    await Task.Run(session.Dispose).ConfigureAwait(false);
}
Console.WriteLine("PASS Addon Manager controls, settings, lifecycle and permission review; screenshots: " + output);
return 0;

public class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AnimeJaNaiConfEditor.App>()
        .UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class FixtureServer : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task serving;
    public bool Running;
    public string[] Granted = [];
    public string? ReviewHash;
    public string[] InvalidSettings = [];
    public JsonArray Sources = [], Profiles = [];
    public string? MediaReviewHash, ProfileConfiguration;
    public JsonObject Values = new() { ["enabled"] = true, ["rate"] = 20.0, ["mode"] = "normal", ["name"] = "Hello" };
    public FixtureServer(string directory) { serving = Task.Run(() => ServeAsync(directory)); }
    private async Task ServeAsync(string directory)
    {
        try
        {
            using var pipe = new NamedPipeServerStream(ManagementClient.PipeName(directory), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(lifetime.Token);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            while (!lifetime.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(lifetime.Token); if (line is null) return;
                var request = JsonNode.Parse(line)!; string method = request["method"]!.GetValue<string>();
                var parameters = (JsonObject)request["params"]!;
                JsonNode? result = method switch
                {
                    "manager.hello" => new JsonObject { ["major"] = 1, ["nativeMediaAvailable"] = true },
                    "addons.list" => new JsonObject { ["addons"] = new JsonArray(new JsonObject { ["id"] = "org.example.ui", ["name"] = "Sample addon", ["version"] = "0.1.0", ["running"] = Running, ["manual"] = true, ["hash"] = new string('a', 64), ["mediaPermission"] = true }), ["nextCursor"] = null },
                    "addons.settings" => JsonNode.Parse("""{"definitions":{"enabled":{"type":"boolean","label":"Enabled"},"rate":{"type":"number","label":"Sample rate","description":"A sample numeric setting."},"mode":{"type":"choice","label":"Mode","choices":["normal","quiet"]},"name":{"type":"string","label":"Greeting","maxLength":100}},"actions":{"check":{"label":"Check status","description":"Run an addon action."}}} """),
                    "addons.logs" => new JsonArray("Sample addon connected.", "Settings and messages are isolated from player profiles."),
                    "addons.action" => new JsonObject { ["status"] = "action received" },
                    "addons.inspect" => new JsonObject { ["manifest"] = new JsonObject { ["id"] = "org.example.ui", ["name"] = "Sample addon", ["version"] = "0.1.0", ["permissions"] = new JsonArray("storage.read", "storage.write") }, ["hash"] = new string('a', 64) },
                    "media.selections" => new JsonObject { ["sources"] = Sources.DeepClone(), ["profiles"] = Profiles.DeepClone() },
                    _ => null,
                };
                if (method == "addons.settings")
                {
                    result!["values"] = Values.DeepClone();
                    result["invalidSettings"] = new JsonArray(InvalidSettings.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
                    if (InvalidSettings.Length > 0) result["values"]!["rate"] = 20;
                }
                if (method == "addons.configure") { Values = (JsonObject)parameters["changes"]!.DeepClone(); InvalidSettings = []; result = Values.DeepClone(); }
                if (method == "addons.start") Running = true;
                if (method == "addons.stop") Running = false;
                if (method == "addons.installDev")
                {
                    Granted = ((JsonArray)parameters["permissions"]!).Select(v => v!.GetValue<string>()).ToArray();
                    ReviewHash = parameters["expectedHash"]!.GetValue<string>();
                }
                if (method.StartsWith("media.") && method != "media.selections") MediaReviewHash = parameters["expectedHash"]!.GetValue<string>();
                if (method == "media.approveSource") Sources.Add(new JsonObject { ["id"] = "source-one", ["name"] = "Chosen video", ["path"] = parameters["path"]!.DeepClone() });
                if (method == "media.approveProfile")
                {
                    ProfileConfiguration = parameters["configuration"]!.GetValue<string>();
                    Profiles.Add(new JsonObject { ["id"] = "profile-one", ["name"] = parameters["name"]!.DeepClone(), ["slot"] = parameters["slot"]!.DeepClone(), ["backend"] = parameters["backend"]!.DeepClone() });
                }
                if (method == "media.revoke")
                {
                    Running = false;
                    if (parameters["kind"]!.GetValue<string>() == "source") Sources.Clear(); else Profiles.Clear();
                }
                var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = result };
                await writer.WriteLineAsync(response.ToJsonString());
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
    }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); await serving; lifetime.Dispose(); }
}
