using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Shooter;

namespace CheatEducation
{
    // 自分（ローカルプレイヤー）が所有する各コンポーネントを見つけるための共通ヘルパー。
    //
    // 現実のチートが「自分のプレイヤー構造体・武器構造体のアドレスを特定する」工程に相当する。
    // ここではリフレクション（＝メモリ上の非公開値を読み書きする Cheat Engine 相当の手口を
    // マネージド空間で安全に代用）も使う。
    public static class CheatUtil
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // 自分のプレイヤー NetworkObject（居なければ null）
        public static NetworkObject LocalPlayer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.SpawnManager == null) return null;
            return nm.SpawnManager.GetLocalPlayerObject();
        }

        public static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // 自分が所有する ModularWeapon を全部集める（武器は別 NetworkObject として複数スポーンされる）
        public static List<ModularWeapon> OwnedWeapons()
        {
            var list = new List<ModularWeapon>();
#if UNITY_2022_2_OR_NEWER
            var all = Object.FindObjectsByType<ModularWeapon>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<ModularWeapon>();
#endif
            foreach (var w in all)
                if (w != null && w.IsOwner) list.Add(w);
            return list;
        }

        // ModularWeapon が参照している WeaponData（private serialized field）をリフレクションで取得
        static FieldInfo s_weaponDataField;
        public static WeaponData GetWeaponData(ModularWeapon w)
        {
            if (w == null) return null;
            s_weaponDataField ??= typeof(ModularWeapon).GetField("weaponData", F);
            return s_weaponDataField?.GetValue(w) as WeaponData;
        }

        // 自分が所有する AmmoHandler を全部
        public static List<AmmoHandler> OwnedAmmoHandlers()
        {
            var list = new List<AmmoHandler>();
#if UNITY_2022_2_OR_NEWER
            var all = Object.FindObjectsByType<AmmoHandler>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<AmmoHandler>();
#endif
            foreach (var a in all)
                if (a != null && a.IsOwner) list.Add(a);
            return list;
        }

        // AmmoHandler の private NetworkVariable<int> m_CurrentClipAmmo / m_ClipSize を取得
        static FieldInfo s_curAmmoField, s_clipSizeField;
        public static NetworkVariable<int> GetClipAmmoVar(AmmoHandler a)
        {
            if (a == null) return null;
            s_curAmmoField ??= typeof(AmmoHandler).GetField("m_CurrentClipAmmo", F);
            return s_curAmmoField?.GetValue(a) as NetworkVariable<int>;
        }
        public static int GetClipSize(AmmoHandler a)
        {
            if (a == null) return 0;
            s_clipSizeField ??= typeof(AmmoHandler).GetField("m_ClipSize", F);
            return (int)(s_clipSizeField?.GetValue(a) ?? 0);
        }

        // 自分の AimController
        public static AimController LocalAimController()
        {
            var p = LocalPlayer();
            return p != null ? p.GetComponentInChildren<AimController>(true) : null;
        }

        // AimController の private NetworkVariable<Vector3> m_NetworkedPreciseAimPosition
        static FieldInfo s_preciseAimField;
        public static NetworkVariable<Vector3> GetPreciseAimVar(AimController a)
        {
            if (a == null) return null;
            s_preciseAimField ??= typeof(AimController).GetField("m_NetworkedPreciseAimPosition", F);
            return s_preciseAimField?.GetValue(a) as NetworkVariable<Vector3>;
        }

        // 自分の CoreMovement / CoreStatsHandler
        public static CoreMovement LocalMovement()
        {
            var p = LocalPlayer();
            return p != null ? p.GetComponentInChildren<CoreMovement>(true) : null;
        }
        public static CoreStatsHandler LocalStats()
        {
            var p = LocalPlayer();
            return p != null ? p.GetComponentInChildren<CoreStatsHandler>(true) : null;
        }
    }
}
