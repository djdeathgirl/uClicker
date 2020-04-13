﻿using System;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace uClicker
{
    [CreateAssetMenu(menuName = "uClicker/Manager")]
    public class ClickerManager : ClickerComponent
    {
        [Serializable]
        public class StaticConfig
        {
            public Currency Currency;
            public Clickable Clickable;
            public Building[] AvailableBuildings;
            public Upgrade[] AvailableUpgrades;
        }

        [Serializable]
        public class Persistent : ISerializationCallbackReceiver
        {
            [NonSerialized] public Building[] EarnedBuildings = new Building[0];
            public int[] EarnedBuildingsCount = new int[0];
            [NonSerialized] public Upgrade[] EarnedUpgrades = new Upgrade[0];
            public float TotalAmount;
            [SerializeField] private GUIDContainer[] _earnedBuildings;
            [SerializeField] private GUIDContainer[] _earnedUpgrades;

            public void OnBeforeSerialize()
            {
                _earnedBuildings = Array.ConvertAll(EarnedBuildings, input => input.GUIDContainer);
                _earnedUpgrades = Array.ConvertAll(EarnedUpgrades, input => input.GUIDContainer);
            }

            public void OnAfterDeserialize()
            {
                EarnedBuildings = Array.ConvertAll(_earnedBuildings, input => (Building) Lookup[input.Guid]);
                EarnedUpgrades = Array.ConvertAll(_earnedUpgrades, input => (Upgrade) Lookup[input.Guid]);
            }
        }

        public SaveConfig SaveConfig = new SaveConfig();
        public StaticConfig Config;
        public Persistent Save;

        public UnityEvent OnTick;
        public UnityEvent OnBuyUpgrade;
        public UnityEvent OnBuyBuilding;

        private void OnDisable()
        {
            // Clear save on unload so we don't try deserializing the save between play/stop
            Save = new Persistent();
        }

        public void Click()
        {
            float amount = Config.Clickable.Amount;

            ApplyClickPerks(ref amount);
            ApplyCurrencyPerk(ref amount);

            bool updated = UpdateTotal(amount);
            UpdateUnlocks();
            if (updated)
            {
                OnTick.Invoke();
            }
        }

        public void Tick()
        {
            float amount = PerSecondAmount();

            bool updated = UpdateTotal(amount);
            UpdateUnlocks();
            if (updated)
            {
                OnTick.Invoke();
            }
        }

        public void BuyBuilding(string id)
        {
            Building building = GetBuildingByName(id);

            if (building == null)
            {
                return;
            }

            if (!CanBuild(building))
            {
                return;
            }

            int indexOf = Array.IndexOf(Save.EarnedBuildings, building);

            float cost = indexOf == -1 ? building.Cost : BuildingCost(building);

            if (!Deduct(cost))
            {
                return;
            }

            if (indexOf >= 0)
            {
                Save.EarnedBuildingsCount[indexOf]++;
            }
            else
            {
                Array.Resize(ref Save.EarnedBuildings, Save.EarnedBuildings.Length + 1);
                Array.Resize(ref Save.EarnedBuildingsCount, Save.EarnedBuildingsCount.Length + 1);
                Save.EarnedBuildings[Save.EarnedBuildings.Length - 1] = building;
                Save.EarnedBuildingsCount[Save.EarnedBuildingsCount.Length - 1] = 1;
            }

            UpdateUnlocks();
            OnBuyBuilding.Invoke();
        }

        public void BuyUpgrade(string id)
        {
            Upgrade upgrade = null;
            foreach (Upgrade availableUpgrade in Config.AvailableUpgrades)
            {
                if (availableUpgrade.name == id)
                {
                    upgrade = availableUpgrade;
                    break;
                }
            }

            if (upgrade == null)
            {
                return;
            }

            if (!CanUpgrade(upgrade))
            {
                return;
            }

            if (!Deduct(upgrade.Cost))
            {
                return;
            }

            Array.Resize(ref Save.EarnedUpgrades, Save.EarnedUpgrades.Length + 1);
            Save.EarnedUpgrades[Save.EarnedUpgrades.Length - 1] = upgrade;
            UpdateUnlocks();
            OnBuyUpgrade.Invoke();
        }

        public int BuildingCost(Building building)
        {
            int indexOf = Array.IndexOf(Save.EarnedBuildings, building);

            return (int) (building.Cost * Mathf.Pow(1 + Config.Currency.PercentIncr,
                indexOf == -1 ? 0 : Save.EarnedBuildingsCount[indexOf]));
        }

        public void SaveProgress()
        {
            string value = JsonUtility.ToJson(Save, true);
            switch (SaveConfig.SaveType)
            {
                case SaveConfig.SaveTypeEnum.SaveToPlayerPrefs:
                    PlayerPrefs.SetString(SaveConfig.SaveName, value);
                    break;
                case SaveConfig.SaveTypeEnum.SaveToFile:
                    File.WriteAllText(SaveConfig.FullSavePath, value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void LoadProgress()
        {
            string json;
            switch (SaveConfig.SaveType)
            {
                case SaveConfig.SaveTypeEnum.SaveToPlayerPrefs:
                    json = PlayerPrefs.GetString(SaveConfig.SaveName);
                    break;
                case SaveConfig.SaveTypeEnum.SaveToFile:
                    if (!File.Exists(SaveConfig.FullSavePath))
                    {
                        return;
                    }

                    json = File.ReadAllText(SaveConfig.FullSavePath);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            JsonUtility.FromJsonOverwrite(json, Save);
            UpdateUnlocks();
            OnTick.Invoke();
            OnBuyBuilding.Invoke();
            OnBuyUpgrade.Invoke();
        }

        private bool Deduct(float cost)
        {
            if (Save.TotalAmount < cost)
            {
                return false;
            }

            bool updated = UpdateTotal(-cost);
            if (updated)
            {
                OnTick.Invoke();
            }

            return true;
        }

        private bool UpdateTotal(float amount)
        {
            Save.TotalAmount += amount;
            return amount != 0;
        }

        private float PerSecondAmount()
        {
            float amount = 0;

            ApplyBuildingPerks(ref amount);
            ApplyCurrencyPerk(ref amount);
            return amount;
        }

        private Building GetBuildingByName(string id)
        {
            Building building = null;

            foreach (Building availableBuilding in Config.AvailableBuildings)
            {
                if (availableBuilding.name == id)
                {
                    building = availableBuilding;
                }
            }

            return building;
        }

        private void UpdateUnlocks()
        {
            foreach (Building availableBuilding in Config.AvailableBuildings)
            {
                availableBuilding.Unlocked |= CanBuild(availableBuilding);
            }

            foreach (Upgrade availableUpgrade in Config.AvailableUpgrades)
            {
                availableUpgrade.Unlocked |= CanUpgrade(availableUpgrade);
            }
        }

        private bool CanBuild(Building building)
        {
            return IsUnlocked(building.Requirements);
        }

        private bool CanUpgrade(Upgrade upgrade)
        {
            bool unlocked = true;
            unlocked &= Array.IndexOf(Save.EarnedUpgrades, upgrade) == -1;
            unlocked &= IsUnlocked(upgrade.Requirements);

            return unlocked;
        }

        private bool IsUnlocked(Requirement[] requirements)
        {
            bool unlocked = true;
            foreach (Requirement requirement in requirements)
            {
                unlocked &= requirement.UnlockUpgrade == null ||
                            Array.IndexOf(Save.EarnedUpgrades, requirement.UnlockUpgrade) != -1;
                unlocked &= requirement.UnlockBuilding == null ||
                            Array.IndexOf(Save.EarnedBuildings, requirement.UnlockBuilding) != -1;
                unlocked &= Save.TotalAmount >= requirement.UnlockAmount;
            }

            return unlocked;
        }

        private void ApplyClickPerks(ref float amount)
        {
            foreach (Upgrade upgrade in Save.EarnedUpgrades)
            {
                foreach (UpgradePerk upgradePerk in upgrade.UpgradePerk)
                {
                    if (upgradePerk.TargetClickable == Config.Clickable)
                    {
                        switch (upgradePerk.Operation)
                        {
                            case Operation.Add:
                                amount += upgradePerk.Amount;
                                break;
                            case Operation.Multiply:
                                amount *= upgradePerk.Amount;
                                break;
                        }
                    }
                }
            }
        }

        private void ApplyBuildingPerks(ref float amount)
        {
            for (int i = 0; i < Save.EarnedBuildings.Length; i++)
            {
                Building building = Save.EarnedBuildings[i];
                int buildingCount = Save.EarnedBuildingsCount[i];
                amount += building.Amount * buildingCount;

                foreach (Upgrade upgrade in Save.EarnedUpgrades)
                {
                    foreach (UpgradePerk upgradePerk in upgrade.UpgradePerk)
                    {
                        if (upgradePerk.TargetBuilding == building)
                        {
                            switch (upgradePerk.Operation)
                            {
                                case Operation.Add:
                                    amount += upgradePerk.Amount;
                                    break;
                                case Operation.Multiply:
                                    amount *= upgradePerk.Amount;
                                    break;
                            }
                        }
                    }
                }
            }
        }

        private void ApplyCurrencyPerk(ref float amount)
        {
            foreach (Upgrade upgrade in Save.EarnedUpgrades)
            {
                foreach (UpgradePerk upgradePerk in upgrade.UpgradePerk)
                {
                    if (upgradePerk.TargetCurrency == Config.Currency)
                    {
                        switch (upgradePerk.Operation)
                        {
                            case Operation.Add:
                                amount += upgradePerk.Amount;
                                break;
                            case Operation.Multiply:
                                amount *= upgradePerk.Amount;
                                break;
                        }
                    }
                }
            }
        }
    }
}