using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;

namespace ModConfigMenu;

public class ModInfoModel
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public BaseUnityPlugin PluginInstance { get; set; }
    public ConfigFile Config { get; set; }
    public List<ConfigSectionGroup> Sections { get; set; } = new();

    public int TotalSettingsCount
    {
        get
        {
            int count = 0;
            foreach (var sec in Sections)
            {
                count += sec.Entries.Count;
            }
            return count;
        }
    }
}

public class ConfigSectionGroup
{
    public string SectionName { get; set; } = string.Empty;
    public List<ConfigEntryBase> Entries { get; set; } = new();
}

