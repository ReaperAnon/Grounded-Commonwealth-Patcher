using System;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Cache;

using GroundedCommonwealthPatcher.Settings;

using Noggog;

namespace GroundedCommonwealthPatcher
{
    public class Program
    {
        public static Lazy<GCConfig> Config = null!;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddRunnabilityCheck(AssertModPresent)
                .SetAutogeneratedSettings(nickname: "Mod Settings", path: "settings.json", out Config)
                .AddPatch<IFallout4Mod, IFallout4ModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.Fallout4, "Grounded Commonwealth Patch.esp")
                .Run(args);
        }

        public static void AssertModPresent(IRunnabilityState state)
        {
            state.LoadOrder.AssertListsMod("Grounded Commonwealth.esp");
        }

        public static bool WasNpcChanged(Npc.Mask<bool> moddedFields)
        {
            return moddedFields.Flags || moddedFields.Name || moddedFields.ShortName || moddedFields.HairColor; // fill out with changes that should prompt an npc to be patched
        }

        public static bool WasSexChanged(INpcGetter baseNpc, INpcGetter moddedNpc)
        {
            return ((baseNpc.Flags & Npc.Flag.Female) > 0 && (moddedNpc.Flags & Npc.Flag.Female) == 0) || ((baseNpc.Flags & Npc.Flag.Female) == 0 && (moddedNpc.Flags & Npc.Flag.Female) > 0);
        }

        public static void RunPatch(IPatcherState<IFallout4Mod, IFallout4ModGetter> state)
        {
            Fallout4Mod GroundedCommonwealth = Fallout4Mod.CreateFromBinary(Path.Combine(state.DataFolderPath, "Grounded Commonwealth.esp"));

            float minBrightnessVal = -1;
            if (Config.Value.TransferLooks)
                minBrightnessVal = System.Drawing.Color.FromArgb(1, 209, 201, 192).GetBrightness();

            foreach(Npc moddedNpc in GroundedCommonwealth.Npcs)
            {
                List<IModContext<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter>> npcOverrides = moddedNpc.ToLink().ResolveAllContexts<IFallout4Mod, IFallout4ModGetter, INpc, INpcGetter>(state.LinkCache).ToList();
                if (npcOverrides.Count <= 0)
                    throw new Exception($"Couldn't resolve {moddedNpc}");

                // Skip unique NPCs.
                if (npcOverrides.Count == 1)
                    continue;

                Npc.TranslationMask translationMask = new Npc.TranslationMask(true) { Version2 = false, VersionControl = false, FormVersion = false, Fallout4MajorRecordFlags = false, MajorRecordFlagsRaw = false };
                // Skip NPCs that were not overridden by another mod.
                if (moddedNpc.Equals(npcOverrides[0].Record, translationMask))
                    continue;

                bool wasChanged = false;
                Console.WriteLine($"\nProcessing {moddedNpc}");
                Npc newNpc = npcOverrides[0].Record.DeepCopy();

                // Check what GC changed from vanilla.
                Npc.Mask<bool> moddedFields = moddedNpc.GetEqualsMask(npcOverrides[npcOverrides.Count - 1].Record).Translate(x => x = !x);

                // Things go here that are safe to be changed by other mods if GC didn't change them.
                if (moddedFields.Flags)
                {
                    // determine what flags were changed from vanilla
                    var bitMask = moddedNpc.Flags ^ npcOverrides[npcOverrides.Count - 1].Record.Flags;

                    // change the same flags in the winning record
                    newNpc.Flags = (newNpc.Flags & ~bitMask) | (moddedNpc.Flags & bitMask);

                    Console.WriteLine("\t- Flags Changed");
                    wasChanged = true;
                }

                if (newNpc.Items is not null && (moddedFields.Items?.Overall ?? false))
                {
                    List<ContainerEntry> newItems = moddedNpc.Items.EmptyIfNull().Where(entry => !npcOverrides[npcOverrides.Count - 1].Record.Items.EmptyIfNull().Contains(entry)).ToList();
                    if(newItems.Any())
                    {
                        Console.WriteLine("\t- New Items Added");       // REMOVE ITEMS FROM NEW INVENTORY THAT GC REMOVED
                        newNpc.Items.AddRange(newItems);
                        wasChanged = true;
                    }
                }

                if(moddedFields.Factions?.Overall ?? false)
                {
                    if(!newNpc.Factions.Equals(moddedNpc.Factions))
                    {
                        List<RankPlacement> newFactions = moddedNpc.Factions.Where(faction => !npcOverrides[npcOverrides.Count - 1].Record.Factions.Contains(faction)).ToList();

                        if (newFactions.Any())
                        {
                            newNpc.Factions.AddRange(newFactions);

                            Console.WriteLine("\t- Factions Changed");
                            wasChanged = true;
                        }
                    }
                }

                if(moddedFields.Packages?.Overall ?? false)
                {
                    List<IFormLinkGetter<IPackageGetter>> newPackages = moddedNpc.Packages.Where(package => !npcOverrides[npcOverrides.Count - 1].Record.Packages.Contains(package)).ToList();
                    newNpc.Packages.AddRange(newPackages);

                    Console.WriteLine("\t- AI Packages Changed");
                    wasChanged = true;
                }

                if (moddedFields.UseTemplateActors)
                {
                    var bitMask = moddedNpc.UseTemplateActors ^ npcOverrides[npcOverrides.Count - 1].Record.UseTemplateActors;

                    newNpc.UseTemplateActors = (newNpc.UseTemplateActors & ~bitMask) | (moddedNpc.UseTemplateActors & bitMask);

                    Console.WriteLine("\t- Template Actor Flags Changed");
                    wasChanged = true;
                }

                if (moddedFields.TemplateActors?.Overall ?? false)
                {
                    if (moddedNpc.TemplateActors is not null)
                    {
                        if (newNpc.TemplateActors is null)
                            newNpc.TemplateActors = new();

                        newNpc.TemplateActors.TraitTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Traits) > 0 ? moddedNpc.TemplateActors.TraitTemplate : newNpc.TemplateActors.TraitTemplate;
                        newNpc.TemplateActors.StatsTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Stats) > 0 ? moddedNpc.TemplateActors.StatsTemplate : newNpc.TemplateActors.StatsTemplate;
                        newNpc.TemplateActors.FactionsTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Factions) > 0 ? moddedNpc.TemplateActors.FactionsTemplate : newNpc.TemplateActors.FactionsTemplate;
                        newNpc.TemplateActors.SpellListTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.SpellList) > 0 ? moddedNpc.TemplateActors.SpellListTemplate : newNpc.TemplateActors.SpellListTemplate;
                        newNpc.TemplateActors.AiDataTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.AiData) > 0 ? moddedNpc.TemplateActors.AiDataTemplate : newNpc.TemplateActors.AiDataTemplate;
                        newNpc.TemplateActors.AiPackagesTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.AiPackages) > 0 ? moddedNpc.TemplateActors.AiPackagesTemplate : newNpc.TemplateActors.AiPackagesTemplate;
                        newNpc.TemplateActors.ModelOrAnimationTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.ModelOrAnimation) > 0 ? moddedNpc.TemplateActors.ModelOrAnimationTemplate : newNpc.TemplateActors.ModelOrAnimationTemplate;
                        newNpc.TemplateActors.BaseDataTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.BaseData) > 0 ? moddedNpc.TemplateActors.BaseDataTemplate : newNpc.TemplateActors.BaseDataTemplate;
                        newNpc.TemplateActors.InventoryTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Inventory) > 0 ? moddedNpc.TemplateActors.InventoryTemplate : newNpc.TemplateActors.InventoryTemplate;
                        newNpc.TemplateActors.ScriptTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Script) > 0 ? moddedNpc.TemplateActors.ScriptTemplate : newNpc.TemplateActors.ScriptTemplate;
                        newNpc.TemplateActors.DefPackListTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.DefPackList) > 0 ? moddedNpc.TemplateActors.DefPackListTemplate : newNpc.TemplateActors.DefPackListTemplate;
                        newNpc.TemplateActors.AttackDataTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.AttackData) > 0 ? moddedNpc.TemplateActors.AttackDataTemplate : newNpc.TemplateActors.AttackDataTemplate;
                        newNpc.TemplateActors.KeywordsTemplate = (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Keywords) > 0 ? moddedNpc.TemplateActors.KeywordsTemplate : newNpc.TemplateActors.KeywordsTemplate;

                        Console.WriteLine("\t- Template Actors Changed");
                        wasChanged = true;
                    }
                    else newNpc.TemplateActors = null;
                }

                if (moddedFields.ObjectTemplates?.Overall ?? false)
                {
                    newNpc.ObjectTemplates = moddedNpc.ObjectTemplates;
                    Console.WriteLine("\t- Object Templates Changed");
                    wasChanged = true;
                }

                if (moddedFields.CrimeFaction)
                {
                    newNpc.CrimeFaction = moddedNpc.CrimeFaction;
                    Console.WriteLine("\t- Crime Faction Changed");
                }

                if (Config.Value.TransferLooks)
                {
                    bool shouldTransfer = false;
                    try
                    {
                        shouldTransfer = Config.Value.FullLooksTransfer || (npcOverrides[npcOverrides.Count - 1].Record.FaceTintingLayers.First(tint => tint.Index == 1156)?.Color.GetBrightness() ?? 1) < minBrightnessVal;
                    }
                    catch(InvalidOperationException)
                    {
                        // Couldn't find skin tint entry, means game uses the first in the race entry.
                    }

                    if ((WasSexChanged(npcOverrides[npcOverrides.Count - 1].Record, moddedNpc) || // sex changed or
                        shouldTransfer) && // different race or transfer all and
                        ((moddedFields.HeadParts?.Overall ?? false) || // looks were changed
                        (moddedFields.Morphs?.Overall ?? false) ||
                        (moddedFields.FaceTintingLayers?.Overall ?? false) ||
                        (moddedFields.FaceMorphs?.Overall ?? false) ||
                        (moddedFields.BodyMorphRegionValues?.Overall ?? false) ||
                        (moddedFields.Weight?.Overall ?? false) ||
                        moddedFields.HeadTexture ||
                        moddedFields.TextureLighting ||
                        moddedFields.HairColor ||
                        moddedFields.FacialMorphIntensity)
                    )
                    {
                        newNpc.HeadParts.RemoveAll(x => true);
                        newNpc.HeadParts.AddRange(moddedNpc.HeadParts);
                        newNpc.Morphs.RemoveAll(x => true);
                        newNpc.Morphs.AddRange(moddedNpc.Morphs);
                        newNpc.FaceTintingLayers.RemoveAll(x => true);
                        newNpc.FaceTintingLayers.AddRange(moddedNpc.FaceTintingLayers);
                        newNpc.FaceMorphs.RemoveAll(x => true);
                        newNpc.FaceMorphs.AddRange(moddedNpc.FaceMorphs);
                        newNpc.BodyMorphRegionValues = moddedNpc.BodyMorphRegionValues;
                        newNpc.Weight = moddedNpc.Weight;
                        newNpc.HeadTexture = moddedNpc.HeadTexture;
                        newNpc.TextureLighting = moddedNpc.TextureLighting;
                        newNpc.HairColor = moddedNpc.HairColor;
                        newNpc.FacialMorphIntensity = moddedNpc.FacialMorphIntensity;

                        Console.WriteLine("\t- Looks Changed");
                        wasChanged = true;
                    }
                }

                if (moddedFields.Voice)
                {
                    newNpc.Voice = moddedNpc.Voice;

                    Console.WriteLine("\t- Voice Type Changed");
                    wasChanged = true;
                }

                if (moddedFields.DefaultOutfit)
                {
                    newNpc.DefaultOutfit = moddedNpc.DefaultOutfit;

                    Console.WriteLine("\t- Outfit Changed");
                    wasChanged = true;
                }

                if (moddedFields.SleepingOutfit)
                {
                    newNpc.SleepingOutfit = moddedNpc.SleepingOutfit;

                    Console.WriteLine("\t- Sleep Outfit Changed");
                    wasChanged = true;
                }

                // Things go here that have to be consistent regardless of GC changing them or not.
                if (newNpc.Name != moddedNpc.Name || newNpc.ShortName != moddedNpc.ShortName)
                {
                    newNpc.Name = moddedNpc.Name;
                    newNpc.ShortName = moddedNpc.ShortName;

                    Console.WriteLine("\t- Name Changed");
                    wasChanged = true;
                }

                /*if (moddedNpc.TemplateActors is not null && (moddedNpc.UseTemplateActors & Npc.TemplateActorType.Traits) > 0)
                {
                    newNpc.UseTemplateActors |= Npc.TemplateActorType.Traits;
                    if (newNpc.TemplateActors is null)
                        newNpc.TemplateActors = new TemplateActors();

                    newNpc.TemplateActors.TraitTemplate = moddedNpc.TemplateActors.TraitTemplate; // Trait template governs looks and voice type.
                }*/

                if (wasChanged)
                    state.PatchMod.Npcs.Add(newNpc);
            }

            foreach(var moddedCellBlock in GroundedCommonwealth.Cells)
            {
                foreach (var moddedCellSubBlock in moddedCellBlock.SubBlocks)
                {
                    foreach (var moddedCell in moddedCellSubBlock.Cells)
                    {
                        List<IModContext<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter>> cellOverrides = moddedCell.ToLink().ResolveAllContexts<IFallout4Mod, IFallout4ModGetter, ICell, ICellGetter>(state.LinkCache).ToList();

                        string newCellName = cellOverrides[0].Record.Name?.String ?? "";
                        string oldCellName = cellOverrides[cellOverrides.Count - 1].Record.Name?.String ?? "";
                        string moddedCellName = moddedCell.Name?.String ?? "";
                        if ((newCellName != moddedCellName && oldCellName != moddedCellName) || (!cellOverrides[0].Record.Music.Equals(moddedCell.Music) && !cellOverrides[cellOverrides.Count - 1].Record.Music.Equals(moddedCell.Music)))
                        {
                            var newCell = cellOverrides[0].GetOrAddAsOverride(state.PatchMod);
                            Console.WriteLine($"\nProcessing {newCell}");
                            if (oldCellName != moddedCellName)
                            {
                                newCell.Name = moddedCell.Name;
                                Console.WriteLine("\t- Name Changed");
                            }

                            if(!cellOverrides[cellOverrides.Count - 1].Record.Music.Equals(moddedCell.Music))
                            {
                                newCell.Music = moddedCell.Music;
                                Console.WriteLine("\t- Music Changed");
                            }
                        }
                    }
                }
            }

            Console.WriteLine("\nProcessing finished.");
        }
    }
}
