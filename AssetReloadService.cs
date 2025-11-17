using Google.Protobuf.Collections;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tibia.Protobuf.Appearances;

namespace Assets_Editor
{
    internal static class AssetReloadService
    {
        internal enum AssetFolderKind
        {
            Unknown,
            Modern,
            Legacy
        }

        internal sealed class LegacyAssetOptions
        {
            public int Version { get; set; }
            public bool Transparent { get; set; }
        }

        public sealed class AssetPreviewInfo
        {
            public AssetFolderKind Kind { get; init; }
            public string AssetsPath { get; init; }
            public string DatPath { get; init; }
            public string SprPath { get; init; }
            public int ObjectCount { get; init; }
            public int OutfitCount { get; init; }
            public int EffectCount { get; init; }
            public int MissileCount { get; init; }
            public uint DatSignature { get; init; }
            public uint SprSignature { get; init; }
            public int CatalogCount { get; init; }
            public int SpriteCatalogCount { get; init; }
            public bool TransparentSprites { get; init; }
            public int LegacyVersion { get; init; }
        }

        private static readonly JsonSerializerSettings CatalogSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        internal static AssetFolderKind DetectFolderKind(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return AssetFolderKind.Unknown;
            }

            string normalized = NormalizeFolder(folderPath);
            if (File.Exists(Path.Combine(normalized, "catalog-content.json")))
            {
                return AssetFolderKind.Modern;
            }

            if (File.Exists(Path.Combine(normalized, "Tibia.dat")) && File.Exists(Path.Combine(normalized, "Tibia.spr")))
            {
                return AssetFolderKind.Legacy;
            }

            return AssetFolderKind.Unknown;
        }

        internal static AssetFolderKind Reload(string folderPath, LegacyAssetOptions legacyOptions = null)
        {
            string normalized = NormalizeFolder(folderPath);
            AssetFolderKind kind = DetectFolderKind(normalized);

            if (kind == AssetFolderKind.Unknown)
            {
                throw new InvalidOperationException("Selected folder does not contain a supported assets structure.");
            }

            ClearGlobalState();

            if (kind == AssetFolderKind.Modern)
            {
                LoadModernAssets(normalized);
            }
            else
            {
                if (legacyOptions == null)
                {
                    throw new InvalidOperationException("Legacy options are required to load this client.");
                }
                LoadLegacyAssets(normalized, legacyOptions);
            }

            return kind;
        }

        internal static AssetPreviewInfo GetPreview(string folderPath, LegacyAssetOptions legacyOptions = null)
        {
            string normalized = NormalizeFolder(folderPath);
            AssetFolderKind kind = DetectFolderKind(normalized);
            if (kind == AssetFolderKind.Unknown)
            {
                throw new InvalidOperationException("Selected folder does not contain a supported assets structure.");
            }

            return kind == AssetFolderKind.Modern
                ? BuildModernPreview(normalized)
                : BuildLegacyPreview(normalized, legacyOptions);
        }

        private static string NormalizeFolder(string folderPath)
        {
            string path = folderPath.Trim();
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path += Path.DirectorySeparatorChar;
            }

            return path;
        }

        private static void ClearGlobalState()
        {
            MainWindow.appearances = null;
            MainWindow.catalog = null;
            MainWindow.AllSprList = new List<ShowList>();
            MainWindow.SprLists = new ConcurrentDictionary<int, MemoryStream>();
            MainWindow.MainSprStorage = null;
            MainWindow.sprites = new Dictionary<uint, Sprite>();
            MainWindow.LegacyClient = false;
            MainWindow._datPath = string.Empty;
            MainWindow._sprPath = string.Empty;
            MainWindow.DatSignature = 0;
            MainWindow.SprSignature = 0;
            MainWindow.ObjectCount = 0;
            MainWindow.OutfitCount = 0;
            MainWindow.EffectCount = 0;
            MainWindow.MissileCount = 0;
        }

        private static void LoadModernAssets(string assetsPath)
        {
            MainWindow.LegacyClient = false;
            MainWindow._assetsPath = assetsPath;

            string catalogFile = Path.Combine(assetsPath, "catalog-content.json");
            if (!File.Exists(catalogFile))
            {
                throw new FileNotFoundException($"Catalog file not found at {catalogFile}");
            }

            string json = File.ReadAllText(catalogFile);
            MainWindow.catalog = JsonConvert.DeserializeObject<List<MainWindow.Catalog>>(json, CatalogSerializerSettings) ?? new List<MainWindow.Catalog>();
            if (MainWindow.catalog.Count == 0)
            {
                throw new InvalidOperationException("Catalog file does not contain any entries.");
            }

            MainWindow._datPath = Path.Combine(assetsPath, MainWindow.catalog[0].File);
            using (FileStream appStream = new FileStream(MainWindow._datPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                MainWindow.appearances = Appearances.Parser.ParseFrom(appStream);
            }

            UpdateCountsFromAppearances(MainWindow.appearances);
            PrepareModernSpriteCaches(MainWindow.catalog);
        }

        private static AssetPreviewInfo BuildModernPreview(string assetsPath)
        {
            string catalogFile = Path.Combine(assetsPath, "catalog-content.json");
            if (!File.Exists(catalogFile))
            {
                throw new FileNotFoundException($"Catalog file not found at {catalogFile}");
            }

            string json = File.ReadAllText(catalogFile);
            var catalog = JsonConvert.DeserializeObject<List<MainWindow.Catalog>>(json, CatalogSerializerSettings) ?? new List<MainWindow.Catalog>();
            if (catalog.Count == 0)
            {
                throw new InvalidOperationException("Catalog file does not contain any entries.");
            }

            string datPath = Path.Combine(assetsPath, catalog[0].File);
            Appearances appearances;
            using (FileStream appStream = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                appearances = Appearances.Parser.ParseFrom(appStream);
            }

            return new AssetPreviewInfo
            {
                Kind = AssetFolderKind.Modern,
                AssetsPath = assetsPath,
                DatPath = datPath,
                ObjectCount = appearances.Object.Count,
                OutfitCount = appearances.Outfit.Count,
                EffectCount = appearances.Effect.Count,
                MissileCount = appearances.Missile.Count,
                CatalogCount = catalog.Count,
                SpriteCatalogCount = catalog.Count(c => string.Equals(c.Type, "sprite", StringComparison.OrdinalIgnoreCase))
            };
        }

        private static void LoadLegacyAssets(string assetsPath, LegacyAssetOptions options)
        {
            MainWindow.LegacyClient = true;
            MainWindow._assetsPath = assetsPath;
            MainWindow._datPath = Path.Combine(assetsPath, "Tibia.dat");
            MainWindow._sprPath = Path.Combine(assetsPath, "Tibia.spr");

            var legacyAppearance = new LegacyAppearance();
            legacyAppearance.ReadLegacyDat(MainWindow._datPath, options.Version);

            MainWindow.appearances = legacyAppearance.Appearances;
            MainWindow.DatSignature = legacyAppearance.Signature;
            MainWindow.ObjectCount = legacyAppearance.ObjectCount;
            MainWindow.OutfitCount = legacyAppearance.OutfitCount;
            MainWindow.EffectCount = legacyAppearance.EffectCount;
            MainWindow.MissileCount = legacyAppearance.MissileCount;

            SpriteStorage spriteStorage = new SpriteStorage(MainWindow._sprPath, options.Transparent);
            MainWindow.MainSprStorage = spriteStorage;
            MainWindow.SprLists = spriteStorage.SprLists ?? new ConcurrentDictionary<int, MemoryStream>();
            MainWindow.sprites = spriteStorage.Sprites ?? new Dictionary<uint, Sprite>();
            MainWindow.SprSignature = spriteStorage.Signature;

            MainWindow.AllSprList = new List<ShowList>();
            foreach (uint spriteId in MainWindow.sprites.Keys.OrderBy(id => id))
            {
                MainWindow.AllSprList.Add(new ShowList { Id = spriteId });
            }
        }

        private static AssetPreviewInfo BuildLegacyPreview(string assetsPath, LegacyAssetOptions options)
        {
            if (options == null)
            {
                throw new InvalidOperationException("Legacy options are required to inspect this client.");
            }

            string datPath = Path.Combine(assetsPath, "Tibia.dat");
            string sprPath = Path.Combine(assetsPath, "Tibia.spr");
            if (!File.Exists(datPath) || !File.Exists(sprPath))
            {
                throw new FileNotFoundException("Legacy client requires Tibia.dat and Tibia.spr files.");
            }

            DatInfo info;
            using (var stream = File.OpenRead(datPath))
            using (var reader = new BinaryReader(stream))
            {
                info = DatStructure.ReadAppearanceInfo(reader);
            }

            uint sprSignature = 0;
            using (var sprStream = File.OpenRead(sprPath))
            using (var sprReader = new BinaryReader(sprStream))
            {
                sprSignature = sprReader.ReadUInt32();
            }

            return new AssetPreviewInfo
            {
                Kind = AssetFolderKind.Legacy,
                AssetsPath = assetsPath,
                DatPath = datPath,
                SprPath = sprPath,
                ObjectCount = info.ObjectCount,
                OutfitCount = info.OutfitCount,
                EffectCount = info.EffectCount,
                MissileCount = info.MissileCount,
                DatSignature = info.Signature,
                SprSignature = sprSignature,
                TransparentSprites = options.Transparent,
                LegacyVersion = options.Version
            };
        }

        private static void PrepareModernSpriteCaches(List<MainWindow.Catalog> catalog)
        {
            var spriteCatalogs = catalog
                .Where(c => string.Equals(c.Type, "sprite", StringComparison.OrdinalIgnoreCase))
                .ToList();

            MainWindow.SprLists = new ConcurrentDictionary<int, MemoryStream>();
            foreach (var entry in spriteCatalogs)
            {
                for (int spriteId = entry.FirstSpriteid; spriteId <= entry.LastSpriteid; spriteId++)
                {
                    MainWindow.SprLists[spriteId] = null;
                }
            }

            uint maxSpriteId = spriteCatalogs.Count == 0
                ? 0
                : (uint)(spriteCatalogs.Max(r => r.LastSpriteid) + 1);

            MainWindow.AllSprList = new List<ShowList>();
            if (!MainWindow.SprLists.ContainsKey(0))
            {
                MainWindow.SprLists[0] = null;
            }

            if (maxSpriteId == 0 && MainWindow.SprLists.Count > 0)
            {
                maxSpriteId = (uint)(MainWindow.SprLists.Keys.Max() + 1);
            }

            for (uint i = 0; i < maxSpriteId; i++)
            {
                MainWindow.AllSprList.Add(new ShowList { Id = i });
            }

            if (MainWindow.AllSprList.Count == 0)
            {
                MainWindow.AllSprList.Add(new ShowList { Id = 0 });
            }
        }

        private static void UpdateCountsFromAppearances(Appearances appearances)
        {
            MainWindow.ObjectCount = GetLastId(appearances.Object);
            MainWindow.OutfitCount = GetLastId(appearances.Outfit);
            MainWindow.EffectCount = GetLastId(appearances.Effect);
            MainWindow.MissileCount = GetLastId(appearances.Missile);
        }

        private static ushort GetLastId(RepeatedField<Appearance> appearances)
        {
            if (appearances == null || appearances.Count == 0)
            {
                return 0;
            }

            return (ushort)appearances[^1].Id;
        }
    }
}
