/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Nameless Players", "VisEntities", "2.0.0")]
    [Description("Hides player names by default, allowing players with permission to set custom display names.")]
    public class NamelessPlayers : RustPlugin
    {
        #region Fields

        private static NamelessPlayers _plugin;
        private static Configuration _config;
        private static StoredData _storedData;

        private Dictionary<ulong, string> _originalNames = new Dictionary<ulong, string>();

        #endregion Fields

        #region Stored Data

        private class StoredData
        {
            [JsonProperty("Custom Names")]
            public Dictionary<ulong, string> CustomNames { get; set; } = new Dictionary<ulong, string>();
        }

        private void LoadData()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            if (_storedData == null)
                _storedData = new StoredData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
        }

        #endregion Stored Data

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Name Replacement")]
            public string NameReplacement { get; set; }

            [JsonProperty("Minimum Name Length")]
            public int MinNameLength { get; set; }

            [JsonProperty("Maximum Name Length")]
            public int MaxNameLength { get; set; }

            [JsonProperty("Forbidden Names")]
            public List<string> ForbiddenNames { get; set; }
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

            if (string.Compare(_config.Version, "2.0.0") < 0)
            {
                _config.MinNameLength = defaultConfig.MinNameLength;
                _config.MaxNameLength = defaultConfig.MaxNameLength;
                _config.ForbiddenNames = defaultConfig.ForbiddenNames;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                NameReplacement = "\u200B",
                MinNameLength = 3,
                MaxNameLength = 32,
                ForbiddenNames = new List<string> { "admin", "moderator", "owner", "server" }
            };
        }

        #endregion Configuration

        #region Localization

        private static class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string UsageSetName = "Usage.SetName";
            public const string UsageSetNameAdmin = "Usage.SetNameAdmin";
            public const string NameSet = "Name.Set";
            public const string NameSetOther = "Name.SetOther";
            public const string NameReset = "Name.Reset";
            public const string NameResetOther = "Name.ResetOther";
            public const string NameTooShort = "Name.TooShort";
            public const string NameTooLong = "Name.TooLong";
            public const string NameForbidden = "Name.Forbidden";
            public const string PlayerNotFound = "Player.NotFound";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You do not have permission to use this command.",
                [Lang.UsageSetName] = "Usage: /setname <name>",
                [Lang.UsageSetNameAdmin] = "Usage: /setname <player> <name>",
                [Lang.NameSet] = "Your display name has been set to: {0}",
                [Lang.NameSetOther] = "Set {0}'s display name to: {1}",
                [Lang.NameReset] = "Your display name has been reset.",
                [Lang.NameResetOther] = "Reset {0}'s display name.",
                [Lang.NameTooShort] = "Name must be at least {0} characters.",
                [Lang.NameTooLong] = "Name cannot exceed {0} characters.",
                [Lang.NameForbidden] = "That name is not allowed.",
                [Lang.PlayerNotFound] = "Player not found."
            }, this);
        }

        private static string GetLangText(BasePlayer player, string langKey, params object[] args)
        {
            string userIdString = null;
            if (player != null)
                userIdString = player.UserIDString;

            string message = _plugin.lang.GetMessage(langKey, _plugin, userIdString);

            if (args.Length > 0)
                return string.Format(message, args);

            return message;
        }

        private static void SendReplyLocalized(BasePlayer player, string langKey, params object[] args)
        {
            string message = GetLangText(player, langKey, args);

            if (!string.IsNullOrWhiteSpace(message))
                _plugin.SendReply(player, message);
        }

        #endregion Localization

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            LoadData();
            PermissionUtil.RegisterPermissions();

            cmd.AddChatCommand("setname", this, nameof(cmdSetName));
            cmd.AddChatCommand("resetname", this, nameof(cmdResetName));
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                RestorePlayerName(player);
            }
            _originalNames.Clear();

            _storedData = null;
            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                ApplyPlayerName(player);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            timer.Once(1f, () => ApplyPlayerName(player));
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
            if (data == null)
                return data;

            if (!data.ContainsKey("Player"))
                return data;

            IPlayer iPlayer = data["Player"] as IPlayer;
            if (iPlayer == null)
                return data;

            BasePlayer bp = BasePlayer.FindByID(ulong.Parse(iPlayer.Id));
            if (bp == null)
                return data;

            string customName;
            if (_storedData.CustomNames.TryGetValue(bp.userID, out customName))
            {
                data["Username"] = customName;
            }
            else
            {
                data["Username"] = _config.NameReplacement;
            }

            return data;
        }

        #endregion Oxide Hooks

        #region Player Name Management

        private void ApplyPlayerName(BasePlayer player)
        {
            if (player == null)
                return;

            if (!_originalNames.ContainsKey(player.userID))
                _originalNames[player.userID] = player.displayName;

            string customName;
            if (_storedData.CustomNames.TryGetValue(player.userID, out customName))
            {
                player.displayName = customName;
            }
            else
            {
                player.displayName = _config.NameReplacement;
            }

            player.SendNetworkUpdateImmediate();
        }

        private void RestorePlayerName(BasePlayer player)
        {
            if (player == null)
                return;

            string originalName;
            if (_originalNames.TryGetValue(player.userID, out originalName))
            {
                player.displayName = originalName;
                player.SendNetworkUpdateImmediate();
            }
        }

        private bool IsValidName(string name, BasePlayer player, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                errorMessage = GetLangText(player, Lang.UsageSetName);
                return false;
            }

            if (name.Length < _config.MinNameLength)
            {
                errorMessage = GetLangText(player, Lang.NameTooShort, _config.MinNameLength);
                return false;
            }

            if (name.Length > _config.MaxNameLength)
            {
                errorMessage = GetLangText(player, Lang.NameTooLong, _config.MaxNameLength);
                return false;
            }

            foreach (string forbidden in _config.ForbiddenNames)
            {
                if (name.ToLower().Contains(forbidden.ToLower()))
                {
                    errorMessage = GetLangText(player, Lang.NameForbidden);
                    return false;
                }
            }

            return true;
        }

        #endregion Player Name Management

        #region Commands

        private void cmdSetName(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (args.Length >= 2 && PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
            {
                string targetName = args[0];
                string newName = string.Join(" ", args.Skip(1));

                BasePlayer target = BasePlayer.Find(targetName);
                if (target == null)
                {
                    SendReplyLocalized(player, Lang.PlayerNotFound);
                    return;
                }

                string errorMsg;
                if (!IsValidName(newName, player, out errorMsg))
                {
                    player.ChatMessage(errorMsg);
                    return;
                }

                string originalName;
                if (_originalNames.ContainsKey(target.userID))
                {
                    originalName = _originalNames[target.userID];
                }
                else
                {
                    originalName = target.displayName;
                }

                _storedData.CustomNames[target.userID] = newName;
                SaveData();
                ApplyPlayerName(target);
                SendReplyLocalized(player, Lang.NameSetOther, originalName, newName);
                return;
            }

            if (!PermissionUtil.HasPermission(player, PermissionUtil.SETNAME))
            {
                SendReplyLocalized(player, Lang.NoPermission);
                return;
            }

            if (args.Length == 0)
            {
                SendReplyLocalized(player, Lang.UsageSetName);
                return;
            }

            string name = string.Join(" ", args);

            string error;
            if (!IsValidName(name, player, out error))
            {
                player.ChatMessage(error);
                return;
            }

            _storedData.CustomNames[player.userID] = name;
            SaveData();
            ApplyPlayerName(player);
            SendReplyLocalized(player, Lang.NameSet, name);
        }

        private void cmdResetName(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (args.Length >= 1 && PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
            {
                string targetName = args[0];

                BasePlayer target = BasePlayer.Find(targetName);
                if (target == null)
                {
                    SendReplyLocalized(player, Lang.PlayerNotFound);
                    return;
                }

                string originalName;
                if (_originalNames.ContainsKey(target.userID))
                {
                    originalName = _originalNames[target.userID];
                }
                else
                {
                    originalName = target.displayName;
                }

                _storedData.CustomNames.Remove(target.userID);
                SaveData();
                ApplyPlayerName(target);
                SendReplyLocalized(player, Lang.NameResetOther, originalName);
                return;
            }

            if (!PermissionUtil.HasPermission(player, PermissionUtil.SETNAME))
            {
                SendReplyLocalized(player, Lang.NoPermission);
                return;
            }

            _storedData.CustomNames.Remove(player.userID);
            SaveData();
            ApplyPlayerName(player);
            SendReplyLocalized(player, Lang.NameReset);
        }

        #endregion Commands

        #region Permissions

        private static class PermissionUtil
        {
            public const string SETNAME = "namelessplayers.setname";
            public const string ADMIN = "namelessplayers.admin";

            private static readonly List<string> _permissions = new List<string>
            {
                SETNAME,
                ADMIN
            };

            public static void RegisterPermissions()
            {
                foreach (string permission in _permissions)
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