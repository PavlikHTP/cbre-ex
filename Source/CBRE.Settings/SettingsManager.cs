using CBRE.Providers;
using CBRE.Settings.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CBRE.Settings
{
    public static class SettingsManager
    {
        public static Build Build { get; private set; } = new Build();
        public static Game Game { get; private set; } = new Game();
        public static List<RecentFile> RecentFiles { get; set; } = new List<RecentFile>();
        public static List<Setting> Settings { get; set; } = new List<Setting>();
        public static List<Hotkey> Hotkeys { get; set; } = new List<Hotkey>();
        public static List<FavouriteTextureFolder> FavouriteTextureFolders { get; set; } = new List<FavouriteTextureFolder>();

        private static readonly Dictionary<string, GenericStructure> AdditionalSettings = new Dictionary<string, GenericStructure>();
        private static readonly IReadOnlyDictionary<string, float> SpecialTextureOpacities;
        
        private static readonly string AppDataPath;
        private static readonly string TextureCachePath;
        private static readonly string ExecutableSettingsPath;
        private static readonly string SessionFilePath;
        public static string SettingsFile { get; }

        static SettingsManager()
        {
            SpecialTextureOpacities = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                {"null", 0}, {"bevel", 0}, {"tools/toolsnodraw", 0}, {"aaatrigger", 0.5f}, {"clip", 0.5f}, {"hint", 0.5f}, {"origin", 0.5f},
                {"skip", 0.5f}, {"tooltextures/remove_face", 0.5f}, {"tooltextures/invisible_collision", 0.5f}, {"tooltextures/block_light", 0.5f}
            };
            AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CBRE-EX");
            SettingsFile = Path.Combine(AppDataPath, "Settings.vdf");
            TextureCachePath = Path.Combine(AppDataPath, "TextureCache");
            SessionFilePath = Path.Combine(AppDataPath, "session");
            
            string execFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            ExecutableSettingsPath = Path.Combine(execFolder, "Settings.vdf");

            if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
        }

        public static string GetTextureCachePath()
        {
            if (!Directory.Exists(TextureCachePath)) Directory.CreateDirectory(TextureCachePath);
            return TextureCachePath;
        }

        public static float GetSpecialTextureOpacity(string name)
        {
            if (View.DisableToolTextureTransparency || View.GloballyDisableTransparency) return 1.0f;
            return SpecialTextureOpacities.TryGetValue(name, out float val) ? val : 1.0f;
        }

        private static GenericStructure ReadSettingsFile()
        {
            if (File.Exists(SettingsFile)) return GenericStructure.Parse(SettingsFile).FirstOrDefault();
            return File.Exists(ExecutableSettingsPath) ? GenericStructure.Parse(ExecutableSettingsPath).FirstOrDefault() : null;
        }

        public static void Read()
        {
            ClearAll();
            GenericStructure root = ReadSettingsFile();
            if (root == null) return;

            var nodes = root.Children.ToDictionary(x => x.Name);

            if (nodes.TryGetValue("Settings", out var settingsNode))
            {
                foreach (string key in settingsNode.GetPropertyKeys())
                    Settings.Add(new Setting { Key = key, Value = settingsNode[key] });
            }

            if (nodes.TryGetValue("RecentFiles", out var recentsNode))
            {
                foreach (string key in recentsNode.GetPropertyKeys())
                {
                    if (int.TryParse(key, out int i))
                        RecentFiles.Add(new RecentFile { Location = recentsNode[key], Order = i });
                }
            }

            if (nodes.TryGetValue("Hotkeys", out var hotkeysNode))
            {
                foreach (string key in hotkeysNode.GetPropertyKeys())
                {
                    string[] spl = key.Split(':');
                    Hotkeys.Add(new Hotkey { ID = spl[0], HotkeyString = hotkeysNode[key] });
                }
            }

            if (nodes.TryGetValue("AdditionalSettings", out var additionalNode))
            {
                foreach (GenericStructure child in additionalNode.Children)
                {
                    if (child.Children.Count > 0) AdditionalSettings.Add(child.Name, child.Children[0]);
                }
            }

            if (nodes.TryGetValue("FavouriteTextures", out var favNode) && favNode.Children.Any())
            {
                try
                {
                    var ft = GenericStructure.Deserialise<List<FavouriteTextureFolder>>(favNode.Children[0]);
                    if (ft != null)
                    {
                        FavouriteTextureFolders.AddRange(ft);
                        FixFavouriteNames(FavouriteTextureFolders);
                    }
                }
                catch { }
            }

            Serialise.DeserialiseSettings(Settings.ToDictionary(x => x.Key, x => x.Value));
            CBRE.Settings.Hotkeys.SetupHotkeys(Hotkeys);

            if (!File.Exists(SettingsFile)) Write();
        }

        public static void Write()
        {
            Settings.Clear();
            Settings.AddRange(Serialise.SerialiseSettings().Select(s => new Setting { Key = s.Key, Value = s.Value }));

            GenericStructure root = new GenericStructure("CBRE-EX");

            GenericStructure settingsNode = new GenericStructure("Settings");
            foreach (var s in Settings) settingsNode.AddProperty(s.Key, s.Value);
            root.Children.Add(settingsNode);

            GenericStructure recentsNode = new GenericStructure("RecentFiles");
            int i = 1;
            foreach (string file in RecentFiles.OrderBy(x => x.Order).Select(x => x.Location).Where(File.Exists))
            {
                recentsNode.AddProperty(i.ToString(), file);
                i++;
            }
            root.Children.Add(recentsNode);

            Hotkeys = CBRE.Settings.Hotkeys.GetHotkeys().ToList();
            GenericStructure hotkeysNode = new GenericStructure("Hotkeys");
            foreach (var g in Hotkeys.GroupBy(x => x.ID))
            {
                int count = 0;
                foreach (var hotkey in g)
                {
                    hotkeysNode.AddProperty($"{hotkey.ID}:{count}", hotkey.HotkeyString);
                    count++;
                }
            }
            root.Children.Add(hotkeysNode);

            GenericStructure additionalNode = new GenericStructure("AdditionalSettings");
            foreach (var kv in AdditionalSettings)
            {
                GenericStructure child = new GenericStructure(kv.Key);
                child.Children.Add(kv.Value);
                additionalNode.Children.Add(child);
            }
            root.Children.Add(additionalNode);

            GenericStructure favNode = new GenericStructure("FavouriteTextures");
            favNode.Children.Add(GenericStructure.Serialise(FavouriteTextureFolders));
            root.Children.Add(favNode);

            AtomicWrite(SettingsFile, root.ToString());
        }

        private static void AtomicWrite(string filePath, string content)
        {
            string temp = filePath + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(filePath))
            {
                string backup = filePath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temp, filePath, backup);
            }
            else File.Move(temp, filePath);
        }

        public static void SaveSession(IEnumerable<Tuple<string, Game>> files)
        {
            File.WriteAllLines(SessionFilePath, files.Select(x => $"{x.Item1}:{x.Item2.ID}"));
        }

        public static IEnumerable<string> LoadSession()
        {
            if (!File.Exists(SessionFilePath)) return Enumerable.Empty<string>();
            return File.ReadAllLines(SessionFilePath)
                .Select(x =>
                {
                    int i = x.LastIndexOf(':');
                    return i >= 0 ? x.Substring(0, i) : null;
                })
                .Where(x => !string.IsNullOrEmpty(x) && File.Exists(x));
        }

        public static T GetAdditionalData<T>(string key)
        {
            if (!AdditionalSettings.TryGetValue(key, out var additional)) return default;
            try { return GenericStructure.Deserialise<T>(additional); }
            catch { return default; }
        }

        public static void SetAdditionalData<T>(string key, T obj)
        {
            AdditionalSettings[key] = GenericStructure.Serialise(obj);
        }

        private static void FixFavouriteNames(IEnumerable<FavouriteTextureFolder> folders)
        {
            foreach (var f in folders)
            {
                FixFavouriteNames(f.Children);
                f.Items = f.Items.Select(x =>
                {
                    int i = x.IndexOf(':');
                    return i >= 0 ? x.Substring(i + 1) : x;
                }).ToList();
            }
        }

        private static void ClearAll()
        {
            RecentFiles.Clear();
            Settings.Clear();
            Hotkeys.Clear();
            AdditionalSettings.Clear();
            FavouriteTextureFolders.Clear();
        }
    }
}