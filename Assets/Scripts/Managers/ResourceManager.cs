using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mahjong.Model;
using UnityEngine;
using Utils;

namespace Managers
{
    public class ResourceManager : MonoBehaviour
    {
        private static readonly string[] TileSuits = { "m", "s", "p", "z" };
        public static ResourceManager Instance { get; private set; }
        private Sprite[] tileSprites;
        private readonly IDictionary<string, Texture2D> textureDict = new Dictionary<string, Texture2D>();
        private IDictionary<string, Sprite> spriteDict;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(LoadSpritesAsync());
        }

        private IEnumerator LoadSpritesAsync()
        {
            LoadDefaultSettings();
            yield return null;
            tileSprites = Resources.LoadAll<Sprite>("Textures/UIElements/tile_ui");
            yield return null;
            // Sprite sheets can contain duplicate sub-sprite names. LINQ
            // ToDictionary throws on duplicates, so keep the first valid entry.
            spriteDict = new Dictionary<string, Sprite>();
            foreach (var sprite in tileSprites)
            {
                if (sprite != null && !spriteDict.ContainsKey(sprite.name))
                    spriteDict.Add(sprite.name, sprite);
            }
            for (int i = 0; i < TileSuits.Length; i++)
            {
                for (int rank = 0; rank <= 9; rank++)
                {
                    var key = $"{rank}{TileSuits[i]}";
                    var texture = Resources.Load<Texture2D>($"Textures/TileTextures/{key}");
                    if (texture != null) textureDict[key] = texture;
                }
                yield return null;
            }
        }

        public Texture2D GetTileTexture(Tile tile)
        {
            var key = GetTileName(tile);
            Texture2D texture;
            if (textureDict.TryGetValue(key, out texture)) return texture;

            // Some tile views are initialized from Awake before the async preload
            // coroutine has populated the cache. Load this one texture on demand.
            texture = Resources.Load<Texture2D>($"Textures/TileTextures/{key}");
            if (texture != null)
            {
                textureDict[key] = texture;
                return texture;
            }

            Debug.LogError($"Missing tile texture '{key}' for tile {tile}. Using a safe fallback.");
            return textureDict.TryGetValue("1m", out texture) ? texture : null;
        }

        public Sprite GetTileSprite(Tile tile)
        {
            if (tileSprites == null)
            {
                Debug.LogError("tileSprite is null, something is wrong, please wait and try again.");
                return null;
            }
            var key = GetTileName(tile);
            Sprite sprite;
            if (spriteDict != null && spriteDict.TryGetValue(key, out sprite)) return sprite;

            // Imported sprite sheets may prefix tile names (for example
            // "tile_4m"). Match the generated key by suffix as a fallback.
            if (spriteDict != null)
            {
                var suffix = spriteDict.FirstOrDefault(pair =>
                    pair.Key.EndsWith(key, System.StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(suffix.Key))
                {
                    spriteDict[key] = suffix.Value;
                    return suffix.Value;
                }
            }

            Debug.LogWarning($"Missing tile sprite '{key}' for tile {tile}. Using a safe fallback.");
            if (spriteDict != null && spriteDict.TryGetValue("1m", out sprite)) return sprite;
            return null;
        }

        public Sprite GetTileSpriteByName(string name)
        {
            Sprite sprite;
            if (spriteDict != null && spriteDict.TryGetValue(name, out sprite)) return sprite;
            Debug.LogError($"Missing tile sprite '{name}'.");
            return null;
        }

        public static string GetTileName(Tile tile)
        {
            int index = tile.IsRed ? 0 : tile.Rank;
            return index + tile.Suit.ToString().ToLower();
        }

        private const string Default_Settings_2 = "Data/default_settings_2";
        private const string Default_Settings_3 = "Data/default_settings_3";
        private const string Default_Settings_4 = "Data/default_settings_4";
        public const string Last_Settings = "/settings.json";
        private readonly IDictionary<GamePlayers, string> defaultSettings = new Dictionary<GamePlayers, string>();

        private void LoadDefaultSettings()
        {
            defaultSettings.Clear();
            defaultSettings.Add(GamePlayers.Two, Resources.Load<TextAsset>(Default_Settings_2).text);
            defaultSettings.Add(GamePlayers.Three, Resources.Load<TextAsset>(Default_Settings_3).text);
            defaultSettings.Add(GamePlayers.Four, Resources.Load<TextAsset>(Default_Settings_4).text);
        }

        public void LoadSettings(out GameSetting gameSetting)
        {
            gameSetting = SerializeUtility.Load<GameSetting>(Last_Settings, defaultSettings[GamePlayers.Four]);
        }

        public void SaveSettings(object setting, string path)
        {
            setting.Save(path);
        }

        public void SaveSettings(GameSetting gameSetting )
        {
            SaveSettings(gameSetting, Last_Settings);
        }

        public void ResetSettings(GameSetting gameSetting)
        {
            Debug.Log("Reset to corresponding default settings");
            var setting = defaultSettings[gameSetting.GamePlayers];
            JsonUtility.FromJsonOverwrite(setting, gameSetting);
        }
    }
}
