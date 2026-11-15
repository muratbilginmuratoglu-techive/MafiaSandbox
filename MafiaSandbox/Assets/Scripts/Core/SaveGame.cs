using UnityEngine;
using MafiaSandbox.Economy;
using MafiaSandbox.World;

namespace MafiaSandbox.Core
{
    /// <summary>The save slot written every time the player goes to sleep: the day and the cash. (Quests, career, lab, stash and respect keep their own keys.)</summary>
    public static class SaveGame
    {
        const string K = "ms_save_v1", KDay = "ms_save_day_v1", KCash = "ms_save_cash_v1";
        public static bool Has => PlayerPrefs.GetInt(K, 0) == 1;
        public static void Save()
        {
            var dn = DayNightCycle.I; var eco = EconomyManager.I; if (!dn || !eco) return;
            PlayerPrefs.SetInt(K, 1); PlayerPrefs.SetInt(KDay, dn.Day); PlayerPrefs.SetInt(KCash, eco.cash); PlayerPrefs.Save();
        }
        public static int SavedDay => PlayerPrefs.GetInt(KDay, 1);
        public static int SavedCash => PlayerPrefs.GetInt(KCash, 250);
    }
}
