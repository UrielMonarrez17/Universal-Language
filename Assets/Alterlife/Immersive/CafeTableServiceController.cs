using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Manages spawning, waiter-tray transport, table placement, and clearing of all physical café
    /// dishes, beverages, desserts, and the itemized bill ('l'addition') in Le Café de Paris.
    /// Keeps all templates inactive until ordered so Meta Quest 2 stays well within its draw-call budget.
    /// </summary>
    public sealed class CafeTableServiceController : MonoBehaviour
    {
        [Header("Scene Anchors")]
        public Transform tableCenter;
        public GameObject initialEmptyDinnerPlate;
        public GameObject initialStaticMug;
        public Transform waiterTrayAnchor;

        [Header("Prop Templates (from ParisCafeProps.fbx)")]
        public GameObject servingTrayTemplate;
        public GameObject billTrayTemplate;
        public GameObject espressoTemplate;
        public GameObject cafeCremeTemplate;
        public GameObject teaTemplate;
        public GameObject waterTemplate;
        public GameObject orangeJuiceTemplate;
        public GameObject croqueMonsieurTemplate;
        public GameObject quicheLorraineTemplate;
        public GameObject saladeNicoiseTemplate;
        public GameObject soupeOignonTemplate;
        public GameObject croissantTemplate;
        public GameObject cremeBruleeTemplate;

        [Header("Table Slot Offsets (relative to Your table, y = 0.816 tabletop)")]
        public Vector3 mainDishSlotOffset = new Vector3(0.20f, 0.816f, -0.06f);
        public Vector3 drinkSlotOffset = new Vector3(0.39f, 0.816f, 0.16f);
        public Vector3 dessertSlotOffset = new Vector3(-0.04f, 0.816f, -0.18f);
        public Vector3 billSlotOffset = new Vector3(0.05f, 0.816f, 0.22f);

        GameObject activeTrayInstance;
        readonly List<CafeConsumableItem> trayItems = new List<CafeConsumableItem>();
        readonly List<CafeConsumableItem> tableItems = new List<CafeConsumableItem>();
        CafeBillProp trayBill;
        CafeBillProp tableBill;

        public int ActiveTableItemCount => tableItems.Count;
        public bool HasItemsOnTable => tableItems.Count > 0;
        public bool HasBillOnTable => tableBill != null;
        public CafeBillProp ActiveTableBill => tableBill;
        public IReadOnlyList<CafeConsumableItem> TableItems => tableItems;

        void Awake()
        {
            ResolveDefaults();
            EnsureServingTrayCreated();
            SetTrayVisible(false);
        }

        void ResolveDefaults()
        {
            if (tableCenter == null)
            {
                var go = GameObject.Find("Your table");
                if (go != null) tableCenter = go.transform;
            }
            if (initialEmptyDinnerPlate == null)
            {
                var plate = GameObject.Find("Dinner plate");
                if (plate != null) initialEmptyDinnerPlate = plate;
            }
        }

        void EnsureServingTrayCreated()
        {
            if (activeTrayInstance != null || waiterTrayAnchor == null)
                return;

            if (servingTrayTemplate != null)
            {
                activeTrayInstance = Instantiate(servingTrayTemplate, waiterTrayAnchor);
                activeTrayInstance.name = "WaiterActiveServingTray";
                activeTrayInstance.transform.localPosition = Vector3.zero;
                activeTrayInstance.transform.localRotation = Quaternion.identity;
                activeTrayInstance.transform.localScale = Vector3.one;
            }
            else
            {
                activeTrayInstance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                activeTrayInstance.name = "WaiterActiveServingTray";
                activeTrayInstance.transform.SetParent(waiterTrayAnchor, false);
                activeTrayInstance.transform.localScale = new Vector3(0.42f, 0.012f, 0.42f);
                Destroy(activeTrayInstance.GetComponent<Collider>());
            }
        }

        public void SetTrayVisible(bool visible)
        {
            EnsureServingTrayCreated();
            if (activeTrayInstance != null)
                activeTrayInstance.SetActive(visible);
        }

        public Vector3 GetSlotWorldPosition(Vector3 localOffset)
        {
            Vector3 origin = tableCenter != null ? tableCenter.position : new Vector3(0f, 0f, 0.1f);
            return new Vector3(origin.x + localOffset.x, localOffset.y, origin.z + localOffset.z);
        }

        /// <summary>
        /// Spawns the ordered main dish, beverage, and/or dessert onto Lucas's serving tray at the café counter.
        /// </summary>
        public void LoadOrderOntoWaiterTray(string foodName, string drinkName, string dessertName)
        {
            EnsureServingTrayCreated();
            ClearTrayContentsOnly();
            SetTrayVisible(true);

            var toSpawn = new List<(GameObject tpl, string label, CafeItemKind kind, bool isHot, Vector3 trayLocalPos)>();

            if (!string.IsNullOrWhiteSpace(foodName) && !AlreadyOnTable(CafeItemKind.MainDish))
            {
                var (tpl, hot) = ResolveFoodTemplate(foodName);
                if (tpl != null)
                    toSpawn.Add((tpl, foodName.Trim(), CafeItemKind.MainDish, hot, new Vector3(-0.045f, 0.016f, -0.02f)));
            }

            if (!string.IsNullOrWhiteSpace(drinkName) && !AlreadyOnTable(CafeItemKind.Drink))
            {
                var (tpl, hot) = ResolveDrinkTemplate(drinkName);
                if (tpl != null)
                    toSpawn.Add((tpl, drinkName.Trim(), CafeItemKind.Drink, hot, new Vector3(0.095f, 0.016f, 0.04f)));
            }

            if (!string.IsNullOrWhiteSpace(dessertName) && !AlreadyOnTable(CafeItemKind.Dessert))
            {
                var (tpl, hot) = ResolveDessertTemplate(dessertName);
                if (tpl != null)
                    toSpawn.Add((tpl, dessertName.Trim(), CafeItemKind.Dessert, hot, new Vector3(-0.02f, 0.016f, 0.085f)));
            }

            // Guarantee at least a café crème if player triggered serveOrder with generic wording
            if (toSpawn.Count == 0 && tableItems.Count == 0 && cafeCremeTemplate != null)
                toSpawn.Add((cafeCremeTemplate, "Café crème", CafeItemKind.Drink, true, new Vector3(0f, 0.016f, 0f)));

            Transform trayParent = activeTrayInstance != null ? activeTrayInstance.transform : waiterTrayAnchor;
            for (int i = 0; i < toSpawn.Count; i++)
            {
                var entry = toSpawn[i];
                GameObject inst = Instantiate(entry.tpl, trayParent);
                inst.name = $"Served_{entry.kind}_{i}";
                inst.SetActive(true);
                inst.transform.localPosition = entry.trayLocalPos;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;

                var consumable = inst.GetComponent<CafeConsumableItem>();
                if (consumable == null) consumable = inst.AddComponent<CafeConsumableItem>();
                consumable.itemDisplayName = entry.label;
                consumable.kind = entry.kind;
                consumable.isHotItem = entry.isHot;
                consumable.SetHeldByWaiterTray();
                trayItems.Add(consumable);
            }
        }

        /// <summary>
        /// Moves all items currently on Lucas's serving tray neatly onto the player's table slots.
        /// </summary>
        public void PlaceTrayItemsOnTable()
        {
            for (int i = 0; i < trayItems.Count; i++)
            {
                CafeConsumableItem item = trayItems[i];
                if (item == null) continue;

                item.transform.SetParent(transform, true);

                Vector3 slotOffset = item.kind switch
                {
                    CafeItemKind.MainDish => mainDishSlotOffset,
                    CafeItemKind.Drink => drinkSlotOffset,
                    _ => dessertSlotOffset
                };

                if (item.kind == CafeItemKind.MainDish && initialEmptyDinnerPlate != null)
                    initialEmptyDinnerPlate.SetActive(false);
                if (item.kind == CafeItemKind.Drink && initialStaticMug != null)
                    initialStaticMug.SetActive(false);

                Vector3 worldPos = GetSlotWorldPosition(slotOffset);
                item.ConfigureForTableSlot(worldPos, Quaternion.Euler(0f, 180f, 0f));
                tableItems.Add(item);
            }
            trayItems.Clear();

            if (trayBill != null)
            {
                trayBill.transform.SetParent(transform, true);
                Vector3 billPos = GetSlotWorldPosition(billSlotOffset);
                trayBill.PlaceOnTable(billPos, Quaternion.Euler(0f, 180f, 0f));
                if (tableBill != null && tableBill != trayBill)
                    Destroy(tableBill.gameObject);
                tableBill = trayBill;
                trayBill = null;
            }

            SetTrayVisible(false);
        }

        /// <summary>
        /// Gathers all served plates, cups, and dessert dishes from the player's table back onto Lucas's tray.
        /// </summary>
        public void CollectTableItemsOntoWaiterTray()
        {
            EnsureServingTrayCreated();
            SetTrayVisible(true);
            Transform trayParent = activeTrayInstance != null ? activeTrayInstance.transform : waiterTrayAnchor;

            for (int i = 0; i < tableItems.Count; i++)
            {
                CafeConsumableItem item = tableItems[i];
                if (item == null) continue;
                item.SetHeldByWaiterTray();
                item.transform.SetParent(trayParent, false);
                item.transform.localPosition = new Vector3((i - 1) * 0.065f, 0.016f, (i % 2) * 0.045f);
                item.transform.localRotation = Quaternion.identity;
                trayItems.Add(item);
            }
            tableItems.Clear();

            if (initialEmptyDinnerPlate != null)
                initialEmptyDinnerPlate.SetActive(false);
        }

        /// <summary>
        /// Drops off cleared dishes at the café counter and frees their GameObjects cleanly.
        /// </summary>
        public void DropOffTrayItemsAtKitchen()
        {
            ClearTrayContentsOnly();
            SetTrayVisible(false);
        }

        /// <summary>
        /// Loads the itemized physical check ('l'addition') onto Lucas's tray at the counter.
        /// </summary>
        public void LoadBillOntoWaiterTray(string food, string drink, string dessert)
        {
            EnsureServingTrayCreated();
            ClearTrayContentsOnly();
            SetTrayVisible(true);

            Transform trayParent = activeTrayInstance != null ? activeTrayInstance.transform : waiterTrayAnchor;
            GameObject inst = billTrayTemplate != null
                ? Instantiate(billTrayTemplate, trayParent)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            inst.name = "Served_Bill_LAddition";
            inst.SetActive(true);
            inst.transform.localPosition = new Vector3(0f, 0.016f, 0f);
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = billTrayTemplate != null ? Vector3.one : new Vector3(0.16f, 0.015f, 0.22f);

            var bill = inst.GetComponent<CafeBillProp>();
            if (bill == null) bill = inst.AddComponent<CafeBillProp>();
            bill.PopulateFromOrder(food, drink, dessert);
            bill.SetHeldByWaiterTray();
            trayBill = bill;
        }

        /// <summary>
        /// Resets table and waiter tray completely when the exam restarts.
        /// </summary>
        public void ResetTable()
        {
            ClearTrayContentsOnly();
            for (int i = 0; i < tableItems.Count; i++)
                if (tableItems[i] != null) Destroy(tableItems[i].gameObject);
            tableItems.Clear();

            if (tableBill != null)
            {
                Destroy(tableBill.gameObject);
                tableBill = null;
            }
            if (initialEmptyDinnerPlate != null)
                initialEmptyDinnerPlate.SetActive(true);
            if (initialStaticMug != null)
                initialStaticMug.SetActive(true);

            SetTrayVisible(false);
        }

        void ClearTrayContentsOnly()
        {
            for (int i = 0; i < trayItems.Count; i++)
                if (trayItems[i] != null) Destroy(trayItems[i].gameObject);
            trayItems.Clear();

            if (trayBill != null)
            {
                Destroy(trayBill.gameObject);
                trayBill = null;
            }
        }

        bool AlreadyOnTable(CafeItemKind kind)
        {
            for (int i = 0; i < tableItems.Count; i++)
                if (tableItems[i] != null && tableItems[i].kind == kind)
                    return true;
            return false;
        }

        public (GameObject template, bool isHot) ResolveDrinkTemplate(string drinkName)
        {
            string norm = RestaurantEvaluation.Normalize(drinkName);
            if (norm.Contains("orange") || norm.Contains("jus"))
                return (orangeJuiceTemplate != null ? orangeJuiceTemplate : waterTemplate, false);
            if (norm.Contains("eau") || norm.Contains("plate") || norm.Contains("gazeuse") || norm.Contains("water"))
                return (waterTemplate != null ? waterTemplate : espressoTemplate, false);
            if (norm.Contains("the") || norm.Contains("menthe") || norm.Contains("citron") || norm.Contains("tea"))
                return (teaTemplate != null ? teaTemplate : cafeCremeTemplate, true);
            if (norm.Contains("creme") || norm.Contains("lait") || norm.Contains("latte") || norm.Contains("cappuccino"))
                return (cafeCremeTemplate != null ? cafeCremeTemplate : espressoTemplate, true);
            return (espressoTemplate != null ? espressoTemplate : cafeCremeTemplate, true);
        }

        public (GameObject template, bool isHot) ResolveFoodTemplate(string foodName)
        {
            string norm = RestaurantEvaluation.Normalize(foodName);
            if (norm.Contains("quiche"))
                return (quicheLorraineTemplate != null ? quicheLorraineTemplate : croqueMonsieurTemplate, true);
            if (norm.Contains("salade") || norm.Contains("nicoise"))
                return (saladeNicoiseTemplate != null ? saladeNicoiseTemplate : croqueMonsieurTemplate, false);
            if (norm.Contains("soupe") || norm.Contains("oignon"))
                return (soupeOignonTemplate != null ? soupeOignonTemplate : croqueMonsieurTemplate, true);
            if (norm.Contains("croissant"))
                return (croissantTemplate != null ? croissantTemplate : croqueMonsieurTemplate, true);
            if (norm.Contains("brulee"))
                return (cremeBruleeTemplate != null ? cremeBruleeTemplate : croqueMonsieurTemplate, false);
            return (croqueMonsieurTemplate != null ? croqueMonsieurTemplate : quicheLorraineTemplate, true);
        }

        public (GameObject template, bool isHot) ResolveDessertTemplate(string dessertName)
        {
            string norm = RestaurantEvaluation.Normalize(dessertName);
            if (norm.Contains("brulee") || norm.Contains("creme"))
                return (cremeBruleeTemplate != null ? cremeBruleeTemplate : croissantTemplate, false);
            return (croissantTemplate != null ? croissantTemplate : cremeBruleeTemplate, true);
        }
    }
}
