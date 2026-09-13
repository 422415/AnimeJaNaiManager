using AnimeJaNaiConfEditor.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Text.Json.Nodes;

internal static class NetworkUiChecks
{
    public static async Task RunAsync(AddonsView view, Window window, FixtureServer server, string output)
    {
        T Find<T>(string name) where T : Control => view.FindControl<T>(name)!;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        async Task Until(Func<bool> ready)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!ready()) await Task.Delay(20, timeout.Token);
        }
        Check(Find<StackPanel>("NetworkSection").IsVisible, "Granted service permission did not expose controls.");
        const string origin = "http://127.0.0.1:19001", id = "org.example.ui";
        string hash = new('a', 64);
        var review = view.ApproveDestinationAsync(window, id, hash, "Local test service", origin);
        await Until(() => window.OwnedWindows.Count > 0);
        var dialog = window.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == origin), "Destination origin must be visible.");
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("127.0.0.1") == true), "Resolved addresses must be visible.");
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("send processed video and audio") == true), "An addon with media output permission must disclose media delivery in destination review.");
        using (var image = dialog.CaptureRenderedFrame()) image!.Save(Path.Combine(output, "addon-destination-consent.png"));
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await review; Check(server.Destinations.Count == 0, "Cancelled review granted destination access.");
        review = view.ApproveDestinationAsync(window, id, hash, "Local test service", origin);
        await Until(() => window.OwnedWindows.Count > 0);
        window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Allow service").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await review;
        Check(server.Destinations.Count == 1 && server.NetworkReviewHash == hash && server.NetworkReviewId == "review-one", "Consent must use the reviewed package and destination.");
        var selected = (JsonObject)server.Destinations[0]!.DeepClone();
        var credential = view.SetNetworkCredentialAsync(window, id, hash, selected);
        await Until(() => window.OwnedWindows.Count > 0);
        dialog = window.OwnedWindows.Single();
        var value = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CredentialValue");
        Check(value.PasswordChar != '\0' && string.IsNullOrEmpty(value.Text), "Credential must start empty and masked.");
        value.Text = "synthetic-ui-value";
        using (var image = dialog.CaptureRenderedFrame()) image!.Save(Path.Combine(output, "addon-credential-consent.png"));
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await credential;
        Check(server.CredentialValue == "synthetic-ui-value" && server.NetworkReviewHash == hash && string.IsNullOrEmpty(value.Text), "Saved credential must be package-bound and cleared from the input.");
        var buttons = Find<StackPanel>("NetworkFields").GetVisualDescendants().OfType<Button>();
        buttons.Single(b => b.Content as string == "Remove credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => server.CredentialValue is null && Find<Button>("RefreshButton").IsEnabled);
        Check(server.Destinations.Count == 1, "Removing a credential removed destination access.");
        Find<StackPanel>("NetworkFields").GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Remove access").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => server.Destinations.Count == 0 && Find<Button>("RefreshButton").IsEnabled);
        Check(server.Profiles.Count == 1, "Destination revocation changed media access.");
    }
}
