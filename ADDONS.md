# Addon Manager developer preview

The Addons tab connects to the companion host in `mpv-AnimeJaNai/addons` on branch `feature/addon-foundation`. This Manager branch is `feature/addon-manager`, based on the 3.6.1 stability fixes. It does not change the existing player configuration format.

Put the self-contained host at `<AJN root>/addon-host/ajn-addon.exe` and its verified runtime at `<AJN root>/addon-host/runtime/wasmtime.exe`. Open Addons and choose Connect host. The host starts on demand without a console. Development tests can set `ANIMEJANAI_ROOT` and `ANIMEJANAI_DATA_DIR` to separate writable folders; data then lives under `<data directory>/addons`.

Choose Install local addon and select an `.ajnaddon` package. Review its identity and check only the permissions you want to grant. All checkboxes start unchecked. Installation is bound to the reviewed archive's hash. Local development packages do not establish publisher identity; website/catalog installation is later work.

The page provides status, start/stop, previous-version rollback, removal, typed settings, declared actions and recent logs. Settings are validated by the host. Incompatible saved fields show a repair notice and defaults for review; reading the form does not overwrite them. Remove stops the addon but preserves its data. Manual starts persist after Manager closes; activation that belongs only to Manager ends when its last relevant connection closes.

Addon labels/descriptions/settings are rendered with trusted native controls. Addon HTML, assemblies or external configuration executables are never loaded. The management protocol is separate from the restricted guest broker. `Services/AddonHostClient.cs` is a verbatim copy of the host's `sdk/csharp/ManagementClient.cs`.

This preview does not yet connect native media sessions, GPU frame samples, encoded output, network devices or credentials. It is not a Plex or bias-light addon.

## Verify the controls

From this repository in PowerShell 7 with .NET SDK 10:

```powershell
dotnet run --project ./tests/AddonUi/AddonUi.csproj -c Release '-p:UsedAvaloniaProducts=' -- ./addon-ui-results
```

The headless Avalonia test opens no desktop windows. It checks settings through save/reload, actions, start/stop, explicit permissions, and package-hash binding. It writes `results.json`, `addon-manager.png` and `addon-permissions.png`. The MSBuild property disables the dependency's usage-telemetry task during testing; it does not disable compilation or validation. The private pipe requires a normal same-user Windows token.

Pass an integrated preview root as a second argument to test the actual packaged host instead of the UI fixture. That root needs `addon-host/ajn-addon.exe`, `addon-host/runtime/wasmtime.exe` and `addon-development/counter.ajnaddon`. This path starts the host from Manager, installs the example with reviewed permissions, changes settings, closes/reopens the page with a manual activation, invokes real Wasm actions, checks durable storage, removes the addon and verifies clean idle exit. It uses a new isolated data directory under the test output and captures `addon-real-host.png`. CI builds the pinned matching host and runs both scenarios.
