using AnimeJaNai.Addons.Management;
using AnimeJaNaiConfEditor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.Views;

public partial class AddonsView : UserControl
{
    private ManagementClient? client;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer refreshTimer;
    private readonly Dictionary<string, Func<JsonNode?>> readSettings = new(StringComparer.Ordinal);
    private bool busy, loadingList, polling;
    private string DataDirectory => Path.Combine(MainWindowViewModel.DataDir, "addons");
    private string HostPath => Path.Combine(MainWindowViewModel.RootDir, "addon-host", "ajn-addon.exe");
    private string RuntimePath => Path.Combine(MainWindowViewModel.RootDir, "addon-host", "runtime", "wasmtime.exe");
    private string? SelectedId => (Control<ListBox>("AddonList").SelectedItem as ListBoxItem)?.Tag as string;
    private T Control<T>(string name) where T : Control => this.FindControl<T>(name)!;

    public AddonsView()
    {
        AvaloniaXamlLoader.Load(this);
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        refreshTimer.Tick += async (_, _) =>
        {
            if (!busy && !polling && IsEffectivelyVisible && client?.IsConnected == true && SelectedId is not null)
            {
                polling = true;
                try { await RefreshSelectedStatusAsync(); }
                catch (Exception error) { if (!lifetime.IsCancellationRequested) Control<TextBlock>("Status").Text = error.Message; }
                finally { polling = false; if (!busy) UpdateButtons(); }
            }
        };
    }

    public async Task ManagerOpenedAsync()
    {
        await RunAsync(async () =>
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(HostPath)) return;
            string installed = Path.Combine(DataDirectory, "installed");
            if (Directory.Exists(installed) && Directory.EnumerateDirectories(installed).Take(128).Any(d => File.Exists(Path.Combine(d, "active.json"))))
                await ConnectAsync();
        }, announceSuccess: false);
    }

    public async Task CloseAsync()
    {
        refreshTimer.Stop(); lifetime.Cancel();
        if (client is not null) await client.DisposeAsync();
    }

    private async Task ConnectAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The addon preview currently supports Windows.");
        if (client?.IsConnected == true) { await RefreshListAsync(); return; }
        if (client is not null) await client.DisposeAsync();
        try { client = await ManagementClient.ConnectAsync(DataDirectory, lifetime.Token); }
        catch (Exception error) when (error is TimeoutException or IOException)
        {
            if (!File.Exists(HostPath) || !File.Exists(RuntimePath))
                throw new IOException("This build does not include the addon host. Use the addon preview package to enable this tab.");
            Control<TextBlock>("Status").Text = "Starting addon host…";
            var info = new ProcessStartInfo(HostPath)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(HostPath)!,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "serve", DataDirectory, RuntimePath }) info.ArgumentList.Add(argument);
            if (File.Exists(Path.Combine(MainWindowViewModel.RootDir, "addon-host", "native-media.json")))
                info.ArgumentList.Add(MainWindowViewModel.RootDir);
            using var started = Process.Start(info) ?? throw new IOException("Could not start the addon host.");
            _ = DrainAsync(started.StandardOutput);
            _ = DrainAsync(started.StandardError);
            var timer = Stopwatch.StartNew();
            while (true)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                try { client = await ManagementClient.ConnectAsync(DataDirectory, lifetime.Token); break; }
                catch (Exception retry) when (retry is TimeoutException or IOException)
                {
                    if (timer.Elapsed > TimeSpan.FromSeconds(20) || started.HasExited)
                        throw new IOException("The addon host did not become available. Check that its runtime files are complete.", retry);
                    await Task.Delay(150, lifetime.Token);
                }
            }
        }
        refreshTimer.Start();
        await RefreshListAsync();
        Control<TextBlock>("Status").Text = "Connected. Addon storage and settings are kept separately from player profiles.";
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        try { char[] buffer = new char[1024]; while (await reader.ReadAsync(buffer) != 0) { } }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
    }

    private async Task<JsonNode?> CallAsync(string method, JsonObject? parameters = null) =>
        await (client ?? throw new IOException("Connect the addon host first.")).CallAsync(method, parameters, lifetime.Token);

    private async Task RefreshListAsync()
    {
        string? selected = SelectedId;
        var data = await client!.ListAsync(lifetime.Token);
        var list = Control<ListBox>("AddonList");
        var items = data.Select(node => new ListBoxItem
        {
            Tag = node!["id"]!.GetValue<string>(),
            Content = new TextBlock { Text = RowText(node), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) },
        }).ToArray();
        loadingList = true;
        try { list.ItemsSource = items; list.SelectedItem = items.FirstOrDefault(i => (string)i.Tag! == selected) ?? items.FirstOrDefault(); }
        finally { loadingList = false; }
        await LoadSelectedAsync();
    }

    private static string RowText(JsonNode node) => node["name"]!.GetValue<string>() + "\n" +
        (node["running"]?.GetValue<bool>() == true ? "Running" : "Stopped") + " · " + (node["version"]?.GetValue<string>() ?? "Unavailable");

    private async Task LoadSelectedAsync()
    {
        readSettings.Clear(); Control<StackPanel>("SettingFields").Children.Clear(); Control<StackPanel>("ActionFields").Children.Clear();
        Control<TextBlock>("ActionResult").Text = ""; Control<TextBox>("LogText").Text = "";
        Control<Button>("SaveButton").IsVisible = false;
        Control<TextBlock>("SettingsNotice").IsVisible = false;
        Control<StackPanel>("MediaSection").IsVisible = false;
        Control<StackPanel>("NetworkSection").IsVisible = false;
        string? id = SelectedId;
        if (id is null) { Control<TextBlock>("AddonTitle").Text = "No addons installed"; Control<TextBlock>("AddonState").Text = "Choose Install local addon to add a development package."; return; }
        Control<TextBlock>("AddonTitle").Text = id;
        await RefreshSelectedStatusAsync();
        var data = (JsonObject)(await CallAsync("addons.settings", new() { ["id"] = id }))!;
        var values = (JsonObject)data["values"]!;
        if (data["invalidSettings"] is JsonArray { Count: > 0 } invalid)
        {
            Control<TextBlock>("SettingsNotice").Text = "This addon version cannot use some saved settings: " +
                string.Join(", ", invalid.Select(v => v!.GetValue<string>())) + ". Defaults are shown for those fields. Review and save to apply them; saved data has not been changed.";
            Control<TextBlock>("SettingsNotice").IsVisible = true;
        }
        foreach (var (key, schema) in data["definitions"] as JsonObject ?? new JsonObject())
        {
            var definition = (JsonObject)schema!;
            var field = new StackPanel { Spacing = 5 };
            field.Children.Add(new TextBlock { Text = definition["label"]!.GetValue<string>(), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (definition["description"] is JsonValue description)
                field.Children.Add(new TextBlock { Text = description.GetValue<string>(), TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            switch (definition["type"]!.GetValue<string>())
            {
                case "boolean":
                    var checkbox = new CheckBox { IsChecked = values[key]!.GetValue<bool>(), Content = "Enabled" };
                    field.Children.Add(checkbox); readSettings[key] = () => JsonValue.Create(checkbox.IsChecked == true); break;
                case "choice":
                    var choices = ((JsonArray)definition["choices"]!).Select(c => c!.GetValue<string>()).ToArray();
                    var select = new ComboBox { ItemsSource = choices, SelectedItem = values[key]!.GetValue<string>(), HorizontalAlignment = HorizontalAlignment.Stretch };
                    field.Children.Add(select); readSettings[key] = () => JsonValue.Create(select.SelectedItem as string ?? ""); break;
                case "number":
                    var number = new TextBox { Text = values[key]!.ToJsonString() };
                    field.Children.Add(number);
                    readSettings[key] = () => (double.TryParse(number.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double n) ||
                        double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) && double.IsFinite(n)
                        ? JsonValue.Create(n) : throw new FormatException("Enter a valid number for " + definition["label"]!.GetValue<string>() + "."); break;
                default:
                    var text = new TextBox { Text = values[key]!.GetValue<string>(), MaxLength = definition["maxLength"]?.GetValue<int>() ?? 4096 };
                    field.Children.Add(text); readSettings[key] = () => JsonValue.Create(text.Text ?? ""); break;
            }
            Control<StackPanel>("SettingFields").Children.Add(field);
        }
        Control<Button>("SaveButton").IsVisible = readSettings.Count > 0;
        foreach (var (key, schema) in data["actions"] as JsonObject ?? new JsonObject())
        {
            var action = new Button { Content = schema!["label"]!.GetValue<string>(), HorizontalAlignment = HorizontalAlignment.Left };
            if (schema["description"] is JsonValue description) ToolTip.SetTip(action, description.GetValue<string>());
            action.Click += async (_, _) => await RunAsync(async () =>
            {
                var result = await CallAsync("addons.action", new() { ["id"] = id, ["action"] = key });
                Control<TextBlock>("ActionResult").Text = result?.ToJsonString() ?? "Action completed.";
                await RefreshSelectedStatusAsync();
            });
            Control<StackPanel>("ActionFields").Children.Add(action);
        }
        await RefreshMediaAsync();
        await RefreshNetworkAsync();
    }

    private async Task RefreshSelectedStatusAsync()
    {
        string? id = SelectedId; if (id is null) return;
        var list = await client!.ListAsync(lifetime.Token);
        if (SelectedId != id) return;
        var row = list.FirstOrDefault(n => n!["id"]!.GetValue<string>() == id);
        if (row is null) return;
        selectedHash = row["hash"]?.GetValue<string>();
        mediaPermission = row["mediaPermission"]?.GetValue<bool>() == true;
        networkPermission = row["networkPermission"]?.GetValue<bool>() == true;
        credentialPermission = row["credentialPermission"]?.GetValue<bool>() == true;
        Control<TextBlock>("AddonTitle").Text = row["name"]!.GetValue<string>();
        Control<TextBlock>("AddonState").Text = id + " · " + (row["version"]?.GetValue<string>() ?? "Unavailable") + "\n" +
            (row["running"]!.GetValue<bool>() ? "Running" : "Stopped") + (row["error"] is JsonValue error ? "\n" + error.GetValue<string>() : "");
        Control<Button>("StartButton").Tag = row["manual"]?.GetValue<bool>() == true;
        if (Control<ListBox>("AddonList").SelectedItem is ListBoxItem item && item.Content is TextBlock label) label.Text = RowText(row);
        var logs = (JsonArray)(await CallAsync("addons.logs", new() { ["id"] = id }))!;
        if (SelectedId == id) Control<TextBox>("LogText").Text = string.Join(Environment.NewLine, logs.Select(l => l!.GetValue<string>()));
    }

    private async Task RunAsync(Func<Task> action, bool announceSuccess = true)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true; UpdateButtons();
        try { await action(); if (announceSuccess) Control<TextBlock>("Status").Text = "Done."; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { Control<TextBlock>("Status").Text = error.Message; }
        finally { busy = false; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        bool connected = client?.IsConnected == true;
        Control<Button>("ConnectButton").IsEnabled = !busy;
        foreach (string name in new[] { "InstallButton", "RefreshButton" }) Control<Button>(name).IsEnabled = connected && !busy;
        foreach (string name in new[] { "StartButton", "StopButton", "RollbackButton", "RemoveButton", "SaveButton" })
            Control<Button>(name).IsEnabled = connected && !busy && SelectedId is not null && (name != "StartButton" || Control<Button>(name).Tag is true);
        Control<ListBox>("AddonList").IsEnabled = !busy;
        Control<StackPanel>("SettingFields").IsEnabled = !busy;
        Control<StackPanel>("ActionFields").IsEnabled = !busy;
        Control<StackPanel>("MediaSection").IsEnabled = connected && !busy;
        Control<StackPanel>("NetworkSection").IsEnabled = connected && !busy;
    }

    private async void ConnectClick(object? sender, RoutedEventArgs e) => await RunAsync(ConnectAsync, announceSuccess: false);
    private async void RefreshClick(object? sender, RoutedEventArgs e) => await RunAsync(RefreshListAsync);
    private async void SelectionChanged(object? sender, SelectionChangedEventArgs e) { if (!loadingList) await RunAsync(LoadSelectedAsync, announceSuccess: false); }
    private async void StartClick(object? sender, RoutedEventArgs e) => await SelectedOperationAsync("addons.start");
    private async void StopClick(object? sender, RoutedEventArgs e) => await SelectedOperationAsync("addons.stop");
    private async void RollbackClick(object? sender, RoutedEventArgs e) => await SelectedOperationAsync("addons.rollback");
    private async void RemoveClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (SelectedId is not string id) return;
        await CallAsync("addons.remove", new() { ["id"] = id }); await RefreshListAsync();
        Control<TextBlock>("Status").Text = "Addon removed. Its settings and saved data are preserved.";
    }, announceSuccess: false);
    private async Task SelectedOperationAsync(string method) => await RunAsync(async () =>
    {
        if (SelectedId is not string id) return;
        await CallAsync(method, new() { ["id"] = id }); await RefreshListAsync();
    });
    private async void SaveClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (SelectedId is not string id) return;
        var changes = new JsonObject(); foreach (var (key, read) in readSettings) changes[key] = read();
        await CallAsync("addons.configure", new() { ["id"] = id, ["changes"] = changes });
        Control<TextBlock>("SettingsNotice").IsVisible = false;
    });

    private async void InstallClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var owner = TopLevel.GetTopLevel(this) as Window ?? throw new IOException("Could not open the package picker.");
        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an addon package", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("AJN addon") { Patterns = new[] { "*.ajnaddon" } } },
        });
        string? path = picked.FirstOrDefault()?.TryGetLocalPath(); if (path is null) return;
        await ReviewPackageAsync(path, owner);
    });

    internal async Task ReviewPackageAsync(string path, Window owner)
    {
        var inspected = (JsonObject)(await CallAsync("addons.inspect", new() { ["path"] = path }))!;
        var manifest = (JsonObject)inspected["manifest"]!;
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = manifest["name"]!.GetValue<string>(), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = manifest["id"]!.GetValue<string>() + " · " + manifest["version"]!.GetValue<string>(), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Local development package. Publisher identity has not been verified.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Choose the permissions to grant:", FontWeight = FontWeight.SemiBold });
        var grants = new List<(string Permission, CheckBox Check)>();
        foreach (var value in (JsonArray)manifest["permissions"]!)
        {
            string permission = value!.GetValue<string>();
            string label = permission switch
            {
                "log.write" => "Write addon log messages",
                "storage.read" => "Read this addon's saved data",
                "storage.write" => "Save this addon's own data",
                "sessions.manage" => "Process media files and profiles you separately approve",
                "frames.read" => "Read small image samples from media you approve",
                "network.connect" => "Exchange data with services and devices you approve",
                "credentials.use" => "Use saved credentials for services you approve",
                _ => permission,
            };
            var check = new CheckBox { Content = label, IsChecked = false };
            grants.Add((permission, check)); content.Children.Add(check);
        }
        if (grants.Count == 0) content.Children.Add(new TextBlock { Text = "This addon requests no additional permissions." });
        var dialog = new Window { Title = "Install addon", Width = 580, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close(false);
        var install = new Button { Content = "Install" }; install.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(install); content.Children.Add(buttons);
        if (!await dialog.ShowDialog<bool>(owner)) return;
        var approved = new JsonArray(grants.Where(g => g.Check.IsChecked == true).Select(g => (JsonNode?)JsonValue.Create(g.Permission)).ToArray());
        await CallAsync("addons.installDev", new() { ["path"] = path, ["expectedHash"] = inspected["hash"]!.DeepClone(), ["permissions"] = approved });
        await RefreshListAsync();
    }
}
