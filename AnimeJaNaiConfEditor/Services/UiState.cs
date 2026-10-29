using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace AnimeJaNaiConfEditor.Services
{
    // The Manager's own UI state (sidebar width, which sections are expanded), kept in
    // manager-ui.json next to animejanai.conf so the engine config holds only engine settings.
    // A missing or unreadable file just means defaults.
    public sealed class UiState
    {
        public const double DefaultSidebarWidth = 260;

        public double SidebarWidth { get; set; } = DefaultSidebarWidth;
        public Dictionary<string, bool> Expanded { get; set; } = [];

        private static UiState? _current;
        private static string? _path;
        private static Timer? _saveTimer;
        private static readonly object _lock = new();

        public static UiState Current
        {
            get
            {
                lock (_lock)
                {
                    return _current ??= new UiState();
                }
            }
        }

        public static void Load(string dataDir)
        {
            lock (_lock)
            {
                _path = Path.Combine(dataDir, "manager-ui.json");
                try
                {
                    _current = File.Exists(_path)
                        ? JsonSerializer.Deserialize<UiState>(File.ReadAllText(_path)) ?? new UiState()
                        : new UiState();
                }
                catch (Exception)
                {
                    _current = new UiState();
                }
                _current.Expanded ??= [];
            }
        }

        public bool? GetExpanded(string key)
        {
            lock (_lock)
            {
                return Expanded.TryGetValue(key, out var value) ? value : null;
            }
        }

        public void SetExpanded(string key, bool value)
        {
            lock (_lock)
            {
                if (Expanded.TryGetValue(key, out var old) && old == value)
                    return;
                Expanded[key] = value;
            }
            ScheduleSave();
        }

        public void SetSidebarWidth(double width)
        {
            lock (_lock)
            {
                SidebarWidth = width;
            }
            ScheduleSave();
        }

        // Expanding and dragging come in bursts; write once they settle.
        private static void ScheduleSave()
        {
            lock (_lock)
            {
                _saveTimer ??= new Timer(_ => Save());
                _saveTimer.Change(500, Timeout.Infinite);
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                if (_path == null || _current == null)
                    return;
                try
                {
                    File.WriteAllText(_path, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception)
                {
                    // a read-only install keeps working, it just doesn't remember the layout
                }
            }
        }
    }
}
