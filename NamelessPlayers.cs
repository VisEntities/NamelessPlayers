/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Nameless Players", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class NamelessPlayers : RustPlugin
    {
        #region Fields

        private static NamelessPlayers _plugin;
        private static Configuration _config;

        private Dictionary<ulong, string> _originalNames = new Dictionary<ulong, string>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Name Replacement")]
            public string NameReplacement { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                NameReplacement = "\u200B"
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                RestorePlayerName(player);
            }
            _originalNames.Clear();

            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                HidePlayerName(player);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            timer.Once(1f, () => HidePlayerName(player));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;
            _originalNames.Remove(player.userID);
        }

        // This hook is exposed by Better Chat plugin (https://umod.org/plugins/better-chat)
        private Dictionary<string, object> OnBetterChat(Dictionary<string, object> data)
        {
            if (data == null || !data.ContainsKey("Player"))
                return data;

            IPlayer iPlayer = data["Player"] as IPlayer;
            if (iPlayer == null)
                return data;

            BasePlayer bp = BasePlayer.FindByID(ulong.Parse(iPlayer.Id));
            if (bp == null)
                return data;

            if (PermissionUtil.HasPermission(bp, PermissionUtil.IGNORE))
                return data;

            data["Username"] = _config.NameReplacement;
            return data;
        }

        #endregion Oxide Hooks

        #region Player Name Obfuscation

        private void HidePlayerName(BasePlayer player)
        {
            if (player == null)
                return;

            if (PermissionUtil.HasPermission(player, PermissionUtil.IGNORE))
                return;

            if (!_originalNames.ContainsKey(player.userID))
            {
                _originalNames.Add(player.userID, player.displayName);
            }

            player.displayName = _config.NameReplacement;
            player.SendNetworkUpdateImmediate();
        }

        private void RestorePlayerName(BasePlayer player)
        {
            if (player == null)
                return;

            if (_originalNames.ContainsKey(player.userID))
            {
                player.displayName = _originalNames[player.userID];
                player.SendNetworkUpdateImmediate();
            }
        }

        #endregion Player Name Obfuscation

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "namelessplayers.ignore";
            private static readonly List<string> _permissions = new List<string>
            {
                IGNORE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions
    }
}