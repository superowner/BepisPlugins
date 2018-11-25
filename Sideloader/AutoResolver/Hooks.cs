﻿using System;
using System.Reflection;
using ExtensibleSaveFormat;
using Harmony;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using ChaCustom;
using Illusion.Extensions;
using Studio;

namespace Sideloader.AutoResolver
{
    public static class Hooks
    {
        public static void InstallHooks()
        {
            ExtendedSave.CardBeingLoaded += ExtendedCardLoad;
            ExtendedSave.CardBeingSaved += ExtendedCardSave;

            ExtendedSave.CoordinateBeingLoaded += ExtendedCoordinateLoad;
            ExtendedSave.CoordinateBeingSaved += ExtendedCoordinateSave;

            ExtendedSave.SceneBeingLoaded += ExtendedSceneLoad;
            ExtendedSave.SceneBeingImported += ExtendedSceneImport;
            //ExtendedSave.SceneBeingSaved += ExtendedSceneSave;

            var harmony = HarmonyInstance.Create("com.bepis.bepinex.sideloader.universalautoresolver");
            harmony.PatchAll(typeof(Hooks));
            harmony.Patch(typeof(Studio.MPCharCtrl).GetNestedType("CostumeInfo", BindingFlags.NonPublic).GetMethod("InitFileList", BindingFlags.Instance | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(Hooks).GetMethod(nameof(StudioCoordinateListPreHook), BindingFlags.Static | BindingFlags.Public)),
                new HarmonyMethod(typeof(Hooks).GetMethod(nameof(StudioCoordinateListPostHook), BindingFlags.Static | BindingFlags.Public)));
        }

        public static bool IsResolving { get; set; } = true;

        #region ChaFile

        private static void IterateCardPrefixes(Action<Dictionary<CategoryProperty, StructValue<int>>, object, IEnumerable<ResolveInfo>, string> action, ChaFile file, IEnumerable<ResolveInfo> extInfo)
        {
            action(StructReference.ChaFileFaceProperties, file.custom.face, extInfo, "");
            action(StructReference.ChaFileBodyProperties, file.custom.body, extInfo, "");
            action(StructReference.ChaFileHairProperties, file.custom.hair, extInfo, "");
            action(StructReference.ChaFileMakeupProperties, file.custom.face.baseMakeup, extInfo, "");

            for (int i = 0; i < file.coordinate.Length; i++)
            {
                var coordinate = file.coordinate[i];
                string prefix = $"outfit{i}.";

                action(StructReference.ChaFileClothesProperties, coordinate.clothes, extInfo, prefix);

                for (int acc = 0; acc < coordinate.accessory.parts.Length; acc++)
                {
                    string accPrefix = $"{prefix}accessory{acc}.";

                    action(StructReference.ChaFileAccessoryPartsInfoProperties, coordinate.accessory.parts[acc], extInfo, accPrefix);
                }
            }
        }

        private static void ExtendedCardLoad(ChaFile file)
        {
            if (!IsResolving)
                return;

            Logger.Log(LogLevel.Debug, $"Loading card [{file.charaFileName}]");

            var extData = ExtendedSave.GetExtendedDataById(file, UniversalAutoResolver.UARExtID);
            List<ResolveInfo> extInfo;

            if (extData == null || !extData.data.ContainsKey("info"))
            {
                Logger.Log(LogLevel.Debug, "No sideloader marker found");
                extInfo = null;
            }
            else
            {
                var tmpExtInfo = (object[])extData.data["info"];
                extInfo = tmpExtInfo.Select(x => ResolveInfo.Unserialize((byte[])x)).ToList();

                Logger.Log(LogLevel.Debug, "Sideloader marker found");
                Logger.Log(LogLevel.Debug, $"External info count: {extInfo.Count}");
                foreach (ResolveInfo info in extInfo)
                    Logger.Log(LogLevel.Debug, $"External info: {info.GUID} : {info.Property} : {info.Slot}");
            }

            IterateCardPrefixes(UniversalAutoResolver.ResolveStructure, file, extInfo);
        }

        private static void ExtendedCardSave(ChaFile file)
        {
            List<ResolveInfo> resolutionInfo = new List<ResolveInfo>();

            void IterateStruct(Dictionary<CategoryProperty, StructValue<int>> dict, object obj, IEnumerable<ResolveInfo> extInfo, string propertyPrefix = "")
            {
                foreach (var kv in dict)
                {
                    int slot = kv.Value.GetMethod(obj);

                    //No need to attempt a resolution info lookup for empty accessory slots and pattern slots
                    if (slot == 0)
                        continue;

                    //Check if it's a vanilla item
                    if (slot < 100000000)
                        if (ResourceRedirector.ListLoader.InternalDataList[kv.Key.Category].ContainsKey(slot))
                            continue;

                    //For accessories, make sure we're checking the appropriate category
                    if (kv.Key.Category.ToString().Contains("ao_"))
                    {
                        ChaFileAccessory.PartsInfo AccessoryInfo = (ChaFileAccessory.PartsInfo)obj;

                        if ((int)kv.Key.Category != AccessoryInfo.type)
                        {
                            //If the current category does not match the accessory's category do not attempt a resolution info lookup
                            continue;
                        }
                    }

                    var info = UniversalAutoResolver.LoadedResolutionInfo.FirstOrDefault(x => x.Property == kv.Key.ToString() &&
                                                                                              x.LocalSlot == slot);

                    if (info == null)
                        continue;

                    var newInfo = info.DeepCopy();
                    newInfo.Property = $"{propertyPrefix}{newInfo.Property}";

                    kv.Value.SetMethod(obj, newInfo.Slot);

                    resolutionInfo.Add(newInfo);
                }
            }

            IterateCardPrefixes(IterateStruct, file, null);

            ExtendedSave.SetExtendedDataById(file, UniversalAutoResolver.UARExtID, new PluginData
            {
                data = new Dictionary<string, object>
                {
                    ["info"] = resolutionInfo.Select(x => x.Serialize()).ToList()
                }
            });
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ChaFile), "SaveFile", new[] { typeof(BinaryWriter), typeof(bool) })]
        public static void ChaFileSaveFilePostHook(ChaFile __instance, bool __result, BinaryWriter bw, bool savePng)
        {
            Logger.Log(LogLevel.Debug, $"Reloading card [{__instance.charaFileName}]");

            var extData = ExtendedSave.GetExtendedDataById(__instance, UniversalAutoResolver.UARExtID);

            var tmpExtInfo = (List<byte[]>)extData.data["info"];
            var extInfo = tmpExtInfo.Select(ResolveInfo.Unserialize).ToList();

            Logger.Log(LogLevel.Debug, $"External info count: {extInfo.Count}");
            foreach (ResolveInfo info in extInfo)
                Logger.Log(LogLevel.Debug, $"External info: {info.GUID} : {info.Property} : {info.Slot}");

            void ResetStructResolveStructure(Dictionary<CategoryProperty, StructValue<int>> propertyDict, object structure, IEnumerable<ResolveInfo> extInfo2, string propertyPrefix = "")
            {
                foreach (var kv in propertyDict)
                {
                    var extResolve = extInfo.FirstOrDefault(x => x.Property == $"{propertyPrefix}{kv.Key.ToString()}");

                    if (extResolve != null)
                    {
                        kv.Value.SetMethod(structure, extResolve.LocalSlot);

                        Logger.Log(LogLevel.Debug, $"[UAR] Resetting {extResolve.GUID}:{extResolve.Property} to internal slot {extResolve.LocalSlot}");
                    }
                }
            }

            IterateCardPrefixes(ResetStructResolveStructure, __instance, extInfo);
        }

        #endregion

        #region ChaFileCoordinate

        private static void IterateCoordinatePrefixes(Action<Dictionary<CategoryProperty, StructValue<int>>, object, IEnumerable<ResolveInfo>, string> action, ChaFileCoordinate coordinate, IEnumerable<ResolveInfo> extInfo)
        {
            action(StructReference.ChaFileClothesProperties, coordinate.clothes, extInfo, "");

            for (int acc = 0; acc < coordinate.accessory.parts.Length; acc++)
            {
                string accPrefix = $"accessory{acc}.";

                action(StructReference.ChaFileAccessoryPartsInfoProperties, coordinate.accessory.parts[acc], extInfo, accPrefix);
            }
        }

        private static void ExtendedCoordinateLoad(ChaFileCoordinate file)
        {
            if (!IsResolving)
                return;

            Logger.Log(LogLevel.Debug, $"Loading coordinate [{file.coordinateName}]");

            var extData = ExtendedSave.GetExtendedDataById(file, UniversalAutoResolver.UARExtID);
            List<ResolveInfo> extInfo;

            if (extData == null || !extData.data.ContainsKey("info"))
            {
                Logger.Log(LogLevel.Debug, "No sideloader marker found");
                extInfo = null;
            }
            else
            {
                var tmpExtInfo = (object[])extData.data["info"];
                extInfo = tmpExtInfo.Select(x => ResolveInfo.Unserialize((byte[])x)).ToList();

                Logger.Log(LogLevel.Debug, "Sideloader marker found");
                Logger.Log(LogLevel.Debug, $"External info count: {extInfo.Count}");
                foreach (ResolveInfo info in extInfo)
                    Logger.Log(LogLevel.Debug, $"External info: {info.GUID} : {info.Property} : {info.Slot}");
            }

            IterateCoordinatePrefixes(UniversalAutoResolver.ResolveStructure, file, extInfo);
        }

        private static void ExtendedCoordinateSave(ChaFileCoordinate file)
        {
            List<ResolveInfo> resolutionInfo = new List<ResolveInfo>();

            void IterateStruct(Dictionary<CategoryProperty, StructValue<int>> dict, object obj, IEnumerable<ResolveInfo> extInfo, string propertyPrefix = "")
            {
                foreach (var kv in dict)
                {
                    int slot = kv.Value.GetMethod(obj);

                    //No need to attempt a resolution info lookup for empty accessory slots and pattern slots
                    if (slot == 0)
                        continue;

                    //Check if it's a vanilla item
                    if (slot < 100000000)
                        if (ResourceRedirector.ListLoader.InternalDataList[kv.Key.Category].ContainsKey(slot))
                            continue;

                    //For accessories, make sure we're checking the appropriate category
                    if (kv.Key.Category.ToString().Contains("ao_"))
                    {
                        ChaFileAccessory.PartsInfo AccessoryInfo = (ChaFileAccessory.PartsInfo)obj;

                        if ((int)kv.Key.Category != AccessoryInfo.type)
                        {
                            //If the current category does not match the accessory's category do not attempt a resolution info lookup
                            continue;
                        }
                    }

                    var info = UniversalAutoResolver.LoadedResolutionInfo.FirstOrDefault(x => x.Property == kv.Key.ToString() &&
                                                                                              x.LocalSlot == slot);

                    if (info == null)
                        continue;

                    var newInfo = info.DeepCopy();
                    newInfo.Property = $"{propertyPrefix}{newInfo.Property}";

                    kv.Value.SetMethod(obj, newInfo.Slot);

                    resolutionInfo.Add(newInfo);
                }
            }

            IterateCoordinatePrefixes(IterateStruct, file, null);

            ExtendedSave.SetExtendedDataById(file, UniversalAutoResolver.UARExtID, new PluginData
            {
                data = new Dictionary<string, object>
                {
                    ["info"] = resolutionInfo.Select(x => x.Serialize()).ToList()
                }
            });
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ChaFileCoordinate), nameof(ChaFileCoordinate.SaveFile), new[] { typeof(string) })]
        public static void ChaFileCoordinateSaveFilePostHook(ChaFileCoordinate __instance, string path)
        {
            Logger.Log(LogLevel.Debug, $"Reloading coordinate [{path}]");

            var extData = ExtendedSave.GetExtendedDataById(__instance, UniversalAutoResolver.UARExtID);

            var tmpExtInfo = (List<byte[]>)extData.data["info"];
            var extInfo = tmpExtInfo.Select(ResolveInfo.Unserialize);

            Logger.Log(LogLevel.Debug, $"External info count: {extInfo.Count()}");
            foreach (ResolveInfo info in extInfo)
                Logger.Log(LogLevel.Debug, $"External info: {info.GUID} : {info.Property} : {info.Slot}");

            void ResetStructResolveStructure(Dictionary<CategoryProperty, StructValue<int>> propertyDict, object structure, IEnumerable<ResolveInfo> extInfo2, string propertyPrefix = "")
            {
                foreach (var kv in propertyDict)
                {
                    var extResolve = extInfo.FirstOrDefault(x => x.Property == $"{propertyPrefix}{kv.Key.ToString()}");

                    if (extResolve != null)
                    {
                        kv.Value.SetMethod(structure, extResolve.LocalSlot);

                        Logger.Log(LogLevel.Debug, $"[UAR] Resetting {extResolve.GUID}:{extResolve.Property} to internal slot {extResolve.LocalSlot}");
                    }
                }
            }

            IterateCoordinatePrefixes(ResetStructResolveStructure, __instance, extInfo);
        }

        #endregion

        #region Studio
        private static void ExtendedSceneLoad(string path)
        {
            PluginData extData = ExtendedSave.GetSceneExtendedDataById(UniversalAutoResolver.UARExtID);

            if (extData != null && extData.data.ContainsKey("info"))
            {
                List<StudioResolveInfo> extInfo;
                Dictionary<int, ObjectInfo> dicObjectInfo = FindAllObjectInfo(FindType.All);

                var tmpExtInfo = (object[])extData.data["info"];
                extInfo = tmpExtInfo.Select(x => StudioResolveInfo.Unserialize((byte[])x)).ToList();

                //foreach (var x in dicObjectInfo)
                //    Logger.Log(LogLevel.Info, $"dicObjectInfo:{x}");
                //if (Singleton<Studio.Studio>.Instance.sceneInfo.dicChangeKey != null)
                //    foreach (var x in Singleton<Studio.Studio>.Instance.sceneInfo.dicChangeKey)
                //        Logger.Log(LogLevel.Info, $"dicChangeKey:{x}");
                //foreach (StudioResolveInfo extResolve in extInfo)
                //    Logger.Log(LogLevel.Info, $"External info: GUID:{extResolve.GUID} ID:{extResolve.Slot} dicKey:{extResolve.DicKey}");

                //When a scene is loaded the dicKey remains unchanged. Read it from the StudioResolveInfo and match it to objects in the scene
                foreach (StudioResolveInfo extResolve in extInfo)
                {
                    if (dicObjectInfo[extResolve.DicKey] is OIItemInfo item)
                    {
                        var intResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == item.no && x.GUID == extResolve.GUID);
                        if (intResolve != null)
                        {
                            //found a match to a corrosponding internal mod
                            Logger.Log(LogLevel.Info, $"[UAR] Resolving {extResolve.GUID} {item.no}->{intResolve.LocalSlot}");
                            Traverse.Create(item).Property("no").SetValue(intResolve.LocalSlot);
                        }
                        else //we didn't find a match, check if we have the same GUID loaded
                        {
                            if (UniversalAutoResolver.LoadedStudioResolutionInfo.Any(x => x.GUID == extResolve.GUID))
                                //we have the GUID loaded, so the user has an outdated mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Outdated mod detected! [{extResolve.GUID}]");
                            else
                                //did not find a match, we don't have the mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Missing mod detected! [{extResolve.GUID}]");
                        }
                    }
                    if (dicObjectInfo[extResolve.DicKey] is OILightInfo light)
                    {
                        var intResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == light.no && x.GUID == extResolve.GUID);
                        if (intResolve != null)
                        {
                            //found a match to a corrosponding internal mod
                            Logger.Log(LogLevel.Info, $"[UAR] Resolving {extResolve.GUID} {light.no}->{intResolve.LocalSlot}");
                            Traverse.Create(light).Property("no").SetValue(intResolve.LocalSlot);
                        }
                        else //we didn't find a match, check if we have the same GUID loaded
                        {
                            if (UniversalAutoResolver.LoadedStudioResolutionInfo.Any(x => x.GUID == extResolve.GUID))
                                //we have the GUID loaded, so the user has an outdated mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Outdated mod detected! [{extResolve.GUID}]");
                            else
                                //did not find a match, we don't have the mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Missing mod detected! [{extResolve.GUID}]");
                        }
                    }
                }
            }
        }
        private static void ExtendedSceneImport(string path)
        {
            PluginData extData = ExtendedSave.GetSceneExtendedDataById(UniversalAutoResolver.UARExtID);

            if (extData != null && extData.data.ContainsKey("info"))
            {
                List<StudioResolveInfo> extInfo;
                Dictionary<int, ObjectInfo> dicObjectInfo = FindAllObjectInfo(FindType.All);
                Dictionary<int, ObjectInfo> dicObjectInfoImport = FindAllObjectInfo(FindType.Import);
                Dictionary<int, int> dicChangeKey = new Dictionary<int, int>();

                var tmpExtInfo = (object[])extData.data["info"];
                extInfo = tmpExtInfo.Select(x => StudioResolveInfo.Unserialize((byte[])x)).ToList();

                int Counter = 0;
                int OldPosition = 0;

                //foreach (var x in dicObjectInfoImport)
                //    Logger.Log(LogLevel.Info, $"dicObjectInfoImport:{x}");
                //foreach (var x in dicChangeKey)
                //    Logger.Log(LogLevel.Info, $"dicChangeKey:{x}");
                //foreach (StudioResolveInfo extResolve in extInfo)
                //    Logger.Log(LogLevel.Info, $"External info: GUID:{extResolve.GUID} ID:{extResolve.Slot} dicKey:{extResolve.DicKey}");


                //When a scene is imported, the dicKey is changed. Create a dictionary of old/new values.
                foreach (var x in dicObjectInfo)
                {
                    //Logger.Log(LogLevel.Info, $"dicObjectInfo:{x} Pos:{Counter}");
                    if (dicObjectInfoImport.ContainsKey(Counter))
                    {
                        //New item added by import
                        dicChangeKey.Add(OldPosition, Counter);
                        //Logger.Log(LogLevel.Warning, dicObjectInfoImport[Counter]);
                        OldPosition++;
                    }
                    Counter++;
                }


                //Match objects from the StudioResolveInfo to objects in the scene using the old/new dicKey dictionary
                foreach (StudioResolveInfo extResolve in extInfo)
                {
                    int NewDicKey = dicChangeKey[extResolve.ObjectOrder];
                    //Logger.Log(LogLevel.Info, $"Original dicKey: {extResolve.DicKey} New dicKey:{NewDicKey}");
                    if (dicObjectInfo[NewDicKey] is OIItemInfo item)
                    {
                        var intResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == item.no && x.GUID == extResolve.GUID);
                        if (intResolve != null)
                        {
                            //found a match to a corrosponding internal mod
                            Logger.Log(LogLevel.Info, $"[UAR] Resolving {extResolve.GUID} {item.no}->{intResolve.LocalSlot}");
                            Traverse.Create(item).Property("no").SetValue(intResolve.LocalSlot);
                        }
                        else //we didn't find a match, check if we have the same GUID loaded
                        {
                            if (UniversalAutoResolver.LoadedStudioResolutionInfo.Any(x => x.GUID == extResolve.GUID))
                                //we have the GUID loaded, so the user has an outdated mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Outdated mod detected! [{extResolve.GUID}]");
                            else
                                //did not find a match, we don't have the mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Missing mod detected! [{extResolve.GUID}]");
                        }
                    }
                    if (dicObjectInfo[NewDicKey] is OILightInfo light)
                    {
                        var intResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == light.no && x.GUID == extResolve.GUID);
                        if (intResolve != null)
                        {
                            //found a match to a corrosponding internal mod
                            Logger.Log(LogLevel.Info, $"[UAR] Resolving {extResolve.GUID} {light.no}->{intResolve.LocalSlot}");
                            Traverse.Create(light).Property("no").SetValue(intResolve.LocalSlot);
                        }
                        else //we didn't find a match, check if we have the same GUID loaded
                        {
                            if (UniversalAutoResolver.LoadedStudioResolutionInfo.Any(x => x.GUID == extResolve.GUID))
                                //we have the GUID loaded, so the user has an outdated mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Outdated mod detected! [{extResolve.GUID}]");
                            else
                                //did not find a match, we don't have the mod
                                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Missing mod detected! [{extResolve.GUID}]");
                        }
                    }
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(SceneInfo), "Save", new[] { typeof(string) })]
        public static void SavePrefix()
        {
            Dictionary<int, ObjectInfo> dicObjectInfo = FindAllObjectInfo(FindType.All);
            PluginData extData = new PluginData();
            List<StudioResolveInfo> resolutionInfo = new List<StudioResolveInfo>();
            int Counter = 0;

            foreach (var kv in dicObjectInfo)
            {
                if (kv.Value is OIItemInfo item)
                {
                    if (item.no >= 100000000)
                    {
                        var extResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.Where(x => x.LocalSlot == item.no).FirstOrDefault();
                        if (extResolve != null)
                        {
                            //Logger.Log(LogLevel.Warning, $"dicKey:{item.dicKey} Counter:{Counter}");
                            StudioResolveInfo intResolve = new StudioResolveInfo();
                            intResolve.GUID = extResolve.GUID;
                            intResolve.Slot = extResolve.Slot;
                            intResolve.LocalSlot = extResolve.LocalSlot;
                            intResolve.DicKey = item.dicKey;
                            intResolve.ObjectOrder = Counter;
                            resolutionInfo.Add(intResolve);

                            //set item ID back to default
                            //Logger.Log(LogLevel.Info, $"Setting ID {item.no}->{extResolve.Slot}");
                            Traverse.Create(item).Property("no").SetValue(extResolve.Slot);
                        }
                    }
                }
                else if (kv.Value is OILightInfo light)
                {
                    if (light.no >= 100000000)
                    {
                        var extResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.Where(x => x.LocalSlot == light.no).FirstOrDefault();
                        if (extResolve != null)
                        {
                            StudioResolveInfo intResolve = new StudioResolveInfo();
                            intResolve.GUID = extResolve.GUID;
                            intResolve.Slot = extResolve.Slot;
                            intResolve.LocalSlot = extResolve.LocalSlot;
                            intResolve.DicKey = light.dicKey;
                            intResolve.ObjectOrder = Counter;
                            resolutionInfo.Add(intResolve);

                            //set item ID back to default
                            //Logger.Log(LogLevel.Info, $"Setting ID {light.no}->{extResolve.Slot}");
                            Traverse.Create(light).Property("no").SetValue(extResolve.Slot);
                        }
                    }
                }
                Counter++;
            }

            if (!resolutionInfo.IsNullOrEmpty())
            {
                ExtendedSave.SetSceneExtendedDataById(UniversalAutoResolver.UARExtID, new PluginData
                {
                    data = new Dictionary<string, object>
                    {
                        ["info"] = resolutionInfo.Select(x => x.Serialize()).ToList()
                    }
                });
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SceneInfo), "Save", new[] { typeof(string) })]
        public static void SavePostfix()
        {
            //Set item IDs back to the resolved ID
            PluginData extData = ExtendedSave.GetSceneExtendedDataById(UniversalAutoResolver.UARExtID);

            if (extData != null && extData.data.ContainsKey("info"))
            {
                List<StudioResolveInfo> extInfo;
                Dictionary<int, ObjectInfo> dicObjectInfo = FindAllObjectInfo(FindType.All);

                var tmpExtInfo = (List<byte[]>)extData.data["info"];
                extInfo = tmpExtInfo.Select(x => StudioResolveInfo.Unserialize(x)).ToList();

                //foreach (StudioResolveInfo extResolve in extInfo)
                //    Logger.Log(LogLevel.Info, $"External info: GUID:{extResolve.GUID} ID:{extResolve.Slot} dicKey:{extResolve.DicKey}");

                foreach (StudioResolveInfo extResolve in extInfo)
                {
                    if (dicObjectInfo[extResolve.DicKey] is OIItemInfo item)
                    {
                        var intResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == item.no && x.GUID == extResolve.GUID);
                        //Logger.Log(LogLevel.Info, $"Setting ID {item.no}->{intResolve.LocalSlot}");
                        Traverse.Create(item).Property("no").SetValue(intResolve.LocalSlot);
                    }
                    if (dicObjectInfo[extResolve.DicKey] is OILightInfo light)
                    {
                        var intResolve = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == light.no && x.GUID == extResolve.GUID);
                        //Logger.Log(LogLevel.Info, $"Setting ID {light.no}->{intResolve.LocalSlot}");
                        Traverse.Create(light).Property("no").SetValue(intResolve.LocalSlot);
                    }
                }
            }
        }

        private enum FindType { All, Import }
        private static Dictionary<int, ObjectInfo> FindAllObjectInfo(FindType loadType)
        {
            Dictionary<int, ObjectInfo> dicObjectInfo = new Dictionary<int, ObjectInfo>();

            //Get all objects in the scene
            if (loadType == FindType.All)
            {
                foreach (var kv in Singleton<Studio.Studio>.Instance.sceneInfo.dicObject)
                {
                    dicObjectInfo.Add(kv.Key, kv.Value);
                    FindObjectsRecursive(kv.Value, ref dicObjectInfo);
                }
            }
            //Get all objects that were imported
            else if (loadType == FindType.Import)
            {
                foreach (var kv in Singleton<Studio.Studio>.Instance.sceneInfo.dicImport)
                {
                    dicObjectInfo.Add(kv.Key, kv.Value);
                    FindObjectsRecursive(kv.Value, ref dicObjectInfo);
                }
            }

            return dicObjectInfo;
        }
        private static void FindObjectsRecursive(ObjectInfo objectInfo, ref Dictionary<int, ObjectInfo> dicObjectInfo)
        {
            if (objectInfo is OICharInfo)
            {
                var charInfo = (OICharInfo)objectInfo;
                foreach (var kv in charInfo.child)
                {
                    foreach (ObjectInfo oi in kv.Value)
                    {
                        dicObjectInfo.Add(oi.dicKey, oi);
                        FindObjectsRecursive(oi, ref dicObjectInfo);
                    }
                }
            }
            else if (objectInfo is OIItemInfo)
            {
                var charInfo = (OIItemInfo)objectInfo;
                foreach (var oi in charInfo.child)
                {
                    dicObjectInfo.Add(oi.dicKey, oi);
                    FindObjectsRecursive(oi, ref dicObjectInfo);
                }
            }
            else if (objectInfo is OIFolderInfo)
            {
                var folderInfo = (OIFolderInfo)objectInfo;
                foreach (var oi in folderInfo.child)
                {
                    dicObjectInfo.Add(oi.dicKey, oi);
                    FindObjectsRecursive(oi, ref dicObjectInfo);
                }
            }
            else if (objectInfo is OIRouteInfo)
            {
                var routeInfo = (OIRouteInfo)objectInfo;
                foreach (var oi in routeInfo.child)
                {
                    dicObjectInfo.Add(oi.dicKey, oi);
                    FindObjectsRecursive(oi, ref dicObjectInfo);
                }
            }
            //other types don't have children
        }

        #endregion

        #region Resolving Override Hooks
        //Prevent resolving when loading the list of characters in Chara Maker since it is irrelevant here
        [HarmonyPrefix, HarmonyPatch(typeof(CustomCharaFile), "Initialize")]
        public static void CustomScenePreHook()
        {
            IsResolving = false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CustomCharaFile), "Initialize")]
        public static void CustomScenePostHook()
        {
            IsResolving = true;
        }
        //Prevent resolving when loading the list of coordinates in Chara Maker since it is irrelevant here
        [HarmonyPrefix, HarmonyPatch(typeof(CustomCoordinateFile), "Initialize")]
        public static void CustomCoordinatePreHook()
        {
            IsResolving = false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CustomCoordinateFile), "Initialize")]
        public static void CustomCoordinatePostHook()
        {
            IsResolving = true;
        }
        //Prevent resolving when loading the list of characters in Studio since it is irrelevant here
        [HarmonyPrefix, HarmonyPatch(typeof(Studio.CharaList), "InitFemaleList")]
        public static void StudioFemaleListPreHook()
        {
            IsResolving = false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Studio.CharaList), "InitFemaleList")]
        public static void StudioFemaleListPostHook()
        {
            IsResolving = true;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(Studio.CharaList), "InitMaleList")]
        public static void StudioMaleListPreHook()
        {
            IsResolving = false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Studio.CharaList), "InitMaleList")]
        public static void StudioMaleListPostHook()
        {
            IsResolving = true;
        }
        //Prevent resolving when loading the list of coordinates in Studio since it is irrelevant here
        public static void StudioCoordinateListPreHook()
        {
            IsResolving = false;
        }
        public static void StudioCoordinateListPostHook()
        {
            IsResolving = true;
        }
        #endregion
    }
}