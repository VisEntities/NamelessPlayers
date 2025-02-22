/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Nameless Players", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class NamelessPlayers : RustPlugin
    {
        #region Fields

        private static NamelessPlayers _plugin;
        private const string INVISIBLE_NAME = "\u200B";
        private Dictionary<ulong, string> _originalNames = new Dictionary<ulong, string>();

        #endregion Fields

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

            player.displayName = INVISIBLE_NAME;
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