using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace ModConfigMenu;

public static class ModManager
{
    private static List<ModInfoModel> _cachedMods;
    private static readonly HashSet<ConfigFile> DirtyConfigs = new();

    public static List<ModInfoModel> GetInstalledMods(bool forceRefresh = false)
    {
        if (_cachedMods != null && !forceRefresh)
        {
            return _cachedMods;
        }

        var list = new List<ModInfoModel>();
        var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Discover all active plugins registered in BepInEx Chainloader
        if (Chainloader.PluginInfos != null)
        {
            foreach (var kvp in Chainloader.PluginInfos)
            {
                PluginInfo pluginInfo = kvp.Value;
                if (pluginInfo == null)
                {
                    continue;
                }

                string guid = pluginInfo.Metadata?.GUID ?? kvp.Key;
                seenGuids.Add(guid);

                ConfigFile config = pluginInfo.Instance != null ? pluginInfo.Instance.Config : null;
                if (config == null)
                {
                    string fallbackCfg = Path.Combine(Paths.ConfigPath, $"{guid}.cfg");
                    if (File.Exists(fallbackCfg))
                    {
                        try
                        {
                            config = new ConfigFile(fallbackCfg, false, pluginInfo.Metadata);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger.LogWarning($"Failed to load config for {guid}: {ex.Message}");
                        }
                    }
                }

                var model = new ModInfoModel
                {
                    Guid = guid,
                    Name = !string.IsNullOrEmpty(pluginInfo.Metadata?.Name) ? pluginInfo.Metadata.Name : guid,
                    Version = pluginInfo.Metadata?.Version != null ? pluginInfo.Metadata.Version.ToString() : "1.0.0",
                    PluginInstance = pluginInfo.Instance,
                    Config = config
                };

                PopulateSections(model);
                list.Add(model);
            }
        }

        // 2. Discover any additional .cfg files in Paths.ConfigPath (e.g. standalone configs)
        if (Directory.Exists(Paths.ConfigPath))
        {
            try
            {
                foreach (string filePath in Directory.GetFiles(Paths.ConfigPath, "*.cfg"))
                {
                    string filename = Path.GetFileNameWithoutExtension(filePath);
                    if (filename.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) || seenGuids.Contains(filename))
                    {
                        continue;
                    }

                    try
                    {
                        var config = new ConfigFile(filePath, false);
                        var model = new ModInfoModel
                        {
                            Guid = filename,
                            Name = filename,
                            Version = "",
                            PluginInstance = null,
                            Config = config
                        };

                        PopulateSections(model);
                        if (model.Sections.Count > 0)
                        {
                            seenGuids.Add(filename);
                            list.Add(model);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning($"Failed to read standalone config {filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Error reading config directory: {ex.Message}");
            }
        }

        // Sort: Put this mod (Mod Config Menu) at the end or sort alphabetically
        _cachedMods = list
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return _cachedMods;
    }

    private static void PopulateSections(ModInfoModel model)
    {
        model.Sections.Clear();
        if (model.Config == null)
        {
            return;
        }

        var groups = new Dictionary<string, ConfigSectionGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (ConfigDefinition key in model.Config.Keys)
        {
            ConfigEntryBase entry = model.Config[key];
            if (entry == null)
            {
                continue;
            }

            string sectionName = string.IsNullOrWhiteSpace(key.Section) ? "General" : key.Section.Trim();
            if (!groups.TryGetValue(sectionName, out ConfigSectionGroup group))
            {
                group = new ConfigSectionGroup { SectionName = sectionName };
                groups[sectionName] = group;
            }

            group.Entries.Add(entry);
        }

        // Sort sections: "General" first, then alphabetically
        model.Sections = groups.Values
            .OrderBy(g => g.SectionName.Equals("General", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(g => g.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Sort entries inside each section alphabetically by key
        foreach (var sec in model.Sections)
        {
            sec.Entries = sec.Entries.OrderBy(e => e.Definition.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public static void MarkConfigDirty(ConfigFile config)
    {
        if (config != null)
        {
            DirtyConfigs.Add(config);
        }
    }

    public static void SaveAllConfigs()
    {
        // Save all marked dirty configs
        foreach (ConfigFile config in DirtyConfigs)
        {
            try
            {
                config.Save();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to save config {config.ConfigFilePath}: {ex.Message}");
            }
        }
        DirtyConfigs.Clear();

        // Also ensure all cached mods' configs are saved if requested
        if (_cachedMods != null)
        {
            foreach (var mod in _cachedMods)
            {
                if (mod.Config != null)
                {
                    try
                    {
                        mod.Config.Save();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogError($"Failed to save config for {mod.Name}: {ex.Message}");
                    }
                }
            }
        }
    }

    public static void ResetModToDefaults(ModInfoModel mod)
    {
        if (mod?.Config == null)
        {
            return;
        }

        foreach (var sec in mod.Sections)
        {
            foreach (var entry in sec.Entries)
            {
                if (entry.DefaultValue != null)
                {
                    entry.BoxedValue = entry.DefaultValue;
                }
            }
        }

        MarkConfigDirty(mod.Config);
        try
        {
            mod.Config.Save();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Failed to save reset config for {mod.Name}: {ex.Message}");
        }
    }
}

