using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Alterlife.Immersive
{
    public enum WaiterServiceState
    {
        AtTable,
        WalkingToKitchen,
        PreparingAtKitchen,
        ReturningWithTray,
        ServingAtTable,
        ClearingAtTable
    }

    /// <summary>
    /// Orchestrates Lucas Moreau's realistic physical café service choreography:
    /// - Walks smoothly around the west side of the terrace table to the café pickup counter.
    /// - Loads exact ordered dishes, beverages, or desserts onto his brass-rimmed serving tray.
    /// - Walks back carrying the tray ('CarryTray_Walk'), presents ('Serve_Present'), and places items on the table.
    /// - Clears finished/declined plates and cups ('ClearTable'), carries them back to the counter,
    ///   and returns with the physical itemized check tray ('l'addition').
    /// </summary>
    public sealed class WaiterServiceChoreographer : MonoBehaviour
    {
        [Header("Scene References")]
        public Transform waiterActor;
        public WaiterPresentation presentation;
        public WaiterAnimationController animationController;
        public CafeTableServiceController tableService;

        [Header("Obstacle-Free Café Terrace Waypoints")]
        public Vector3 tableServicePoint = new Vector3(-0.80f, 0f, 1.25f);
        public Vector3 terraceCornerPoint = new Vector3(-2.62f, 0f, 1.05f);
        public Vector3 kitchenCounterPoint = new Vector3(-2.72f, 0f, -2.02f);
        public Vector3 kitchenLookTarget = new Vector3(-2.72f, 0f, -2.85f);

        [Header("Motion Timing")]
        public float walkSpeed = 1.45f;
        public float turnSpeed = 7.5f;
        public float counterPrepSeconds = 1.15f;

        public WaiterServiceState CurrentState { get; private set; } = WaiterServiceState.AtTable;
        public bool IsBusyWithService => CurrentState != WaiterServiceState.AtTable || pendingActions.Count > 0;

        enum ActionType { ServeOrder, ServeDessert, ClearTable, BringBill }

        struct ServiceAction
        {
            public ActionType type;
            public string food;
            public string drink;
            public string dessert;
            public bool thenBringBill;
        }

        readonly Queue<ServiceAction> pendingActions = new Queue<ServiceAction>();
        Coroutine activeRoutine;

        void Awake()
        {
            ResolveReferences();
        }

        void ResolveReferences()
        {
            if (presentation == null)
                presentation = FindAnyObjectByType<WaiterPresentation>();
            if (waiterActor == null && presentation != null)
                waiterActor = presentation.actor;
            if (animationController == null && waiterActor != null)
                animationController = waiterActor.GetComponent<WaiterAnimationController>();
            if (tableService == null)
                tableService = FindAnyObjectByType<CafeTableServiceController>();
        }

        public void ResetToTable()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }
            pendingActions.Clear();
            CurrentState = WaiterServiceState.AtTable;

            ResolveReferences();
            if (presentation != null) presentation.SuspendFacingOverride = false;
            if (animationController != null)
            {
                animationController.SetWalkingState(false, false);
            }
            if (waiterActor != null)
                waiterActor.position = tableServicePoint;
            tableService?.ResetTable();
        }

        public void RequestServeOrder(string food, string drink, string dessert)
        {
            if (Application.isBatchMode)
            {
                // Instant deterministic delivery for headless Play Mode validation
                ResolveReferences();
                tableService?.LoadOrderOntoWaiterTray(food, drink, dessert);
                tableService?.PlaceTrayItemsOnTable();
                return;
            }

            EnqueueAction(new ServiceAction
            {
                type = ActionType.ServeOrder,
                food = food,
                drink = drink,
                dessert = dessert
            });
        }

        public void RequestServeDessert(string dessert)
        {
            if (string.IsNullOrWhiteSpace(dessert))
                return;

            if (Application.isBatchMode)
            {
                ResolveReferences();
                tableService?.LoadOrderOntoWaiterTray("", "", dessert);
                tableService?.PlaceTrayItemsOnTable();
                return;
            }

            EnqueueAction(new ServiceAction
            {
                type = ActionType.ServeDessert,
                dessert = dessert
            });
        }

        public void RequestClearTable(bool thenBringBill, string food = "", string drink = "", string dessert = "")
        {
            if (Application.isBatchMode)
            {
                ResolveReferences();
                if (tableService != null && tableService.HasItemsOnTable)
                {
                    tableService.CollectTableItemsOntoWaiterTray();
                    tableService.DropOffTrayItemsAtKitchen();
                }
                if (thenBringBill && tableService != null)
                {
                    tableService.LoadBillOntoWaiterTray(food, drink, dessert);
                    tableService.PlaceTrayItemsOnTable();
                }
                return;
            }

            EnqueueAction(new ServiceAction
            {
                type = ActionType.ClearTable,
                food = food,
                drink = drink,
                dessert = dessert,
                thenBringBill = thenBringBill
            });
        }

        public void RequestBringBill(string food, string drink, string dessert)
        {
            ResolveReferences();
            if (tableService != null && tableService.HasItemsOnTable)
            {
                RequestClearTable(thenBringBill: true, food: food, drink: drink, dessert: dessert);
                return;
            }

            if (Application.isBatchMode)
            {
                tableService?.LoadBillOntoWaiterTray(food, drink, dessert);
                tableService?.PlaceTrayItemsOnTable();
                return;
            }

            EnqueueAction(new ServiceAction
            {
                type = ActionType.BringBill,
                food = food,
                drink = drink,
                dessert = dessert
            });
        }

        void EnqueueAction(ServiceAction action)
        {
            pendingActions.Enqueue(action);
            if (activeRoutine == null && isActiveAndEnabled && gameObject.activeInHierarchy)
                activeRoutine = StartCoroutine(ProcessActionQueue());
        }

        IEnumerator ProcessActionQueue()
        {
            ResolveReferences();
            while (pendingActions.Count > 0)
            {
                ServiceAction next = pendingActions.Dequeue();
                switch (next.type)
                {
                    case ActionType.ServeOrder:
                        yield return ExecuteFetchAndServe(next.food, next.drink, next.dessert);
                        break;
                    case ActionType.ServeDessert:
                        yield return ExecuteFetchAndServe("", "", next.dessert);
                        break;
                    case ActionType.ClearTable:
                        yield return ExecuteClearTable(next.thenBringBill, next.food, next.drink, next.dessert);
                        break;
                    case ActionType.BringBill:
                        yield return ExecuteBringBill(next.food, next.drink, next.dessert);
                        break;
                }
            }

            CurrentState = WaiterServiceState.AtTable;
            if (presentation != null) presentation.SuspendFacingOverride = false;
            if (animationController != null) animationController.SetWalkingState(false, false);
            activeRoutine = null;
        }

        IEnumerator ExecuteFetchAndServe(string food, string drink, string dessert)
        {
            if (waiterActor == null) yield break;

            // Brief pause so Lucas begins saying "Tout de suite !" before turning toward the café
            yield return new WaitForSeconds(0.55f);

            // 1. Walk to Café Counter empty-handed
            CurrentState = WaiterServiceState.WalkingToKitchen;
            if (presentation != null) presentation.SuspendFacingOverride = true;
            animationController?.SetWalkingState(true, false);

            yield return WalkToPoint(terraceCornerPoint);
            yield return WalkToPoint(kitchenCounterPoint);

            // 2. Prepare items on brass serving tray at café counter
            CurrentState = WaiterServiceState.PreparingAtKitchen;
            animationController?.SetWalkingState(false, true);
            yield return FaceWorldPoint(kitchenLookTarget, 0.35f);
            tableService?.LoadOrderOntoWaiterTray(food, drink, dessert);
            yield return new WaitForSeconds(counterPrepSeconds);

            // 3. Walk back carrying tray smoothly
            CurrentState = WaiterServiceState.ReturningWithTray;
            animationController?.SetWalkingState(true, true);
            yield return WalkToPoint(terraceCornerPoint);
            yield return WalkToPoint(tableServicePoint);

            // 4. Present & set items neatly on player's table
            CurrentState = WaiterServiceState.ServingAtTable;
            animationController?.SetWalkingState(false, true);
            if (presentation != null) presentation.SuspendFacingOverride = false;
            animationController?.TriggerServePresent();
            yield return new WaitForSeconds(0.75f);

            tableService?.PlaceTrayItemsOnTable();
            animationController?.SetWalkingState(false, false);
            yield return new WaitForSeconds(0.45f);
            CurrentState = WaiterServiceState.AtTable;
        }

        IEnumerator ExecuteClearTable(bool thenBringBill, string food, string drink, string dessert)
        {
            if (waiterActor == null) yield break;

            yield return new WaitForSeconds(0.45f);

            if (tableService != null && tableService.HasItemsOnTable)
            {
                // 1. Lean and collect dishes onto tray at table
                CurrentState = WaiterServiceState.ClearingAtTable;
                animationController?.SetWalkingState(false, true);
                animationController?.TriggerClearTable();
                yield return new WaitForSeconds(0.75f);

                tableService.CollectTableItemsOntoWaiterTray();
                yield return new WaitForSeconds(0.45f);

                // 2. Carry cleared dishes back to the café counter
                CurrentState = WaiterServiceState.WalkingToKitchen;
                if (presentation != null) presentation.SuspendFacingOverride = true;
                animationController?.SetWalkingState(true, true);
                yield return WalkToPoint(terraceCornerPoint);
                yield return WalkToPoint(kitchenCounterPoint);

                CurrentState = WaiterServiceState.PreparingAtKitchen;
                animationController?.SetWalkingState(false, false);
                yield return FaceWorldPoint(kitchenLookTarget, 0.35f);
                tableService.DropOffTrayItemsAtKitchen();
                yield return new WaitForSeconds(0.75f);

                if (thenBringBill)
                {
                    // Pick up l'addition right at the counter before returning!
                    tableService.LoadBillOntoWaiterTray(food, drink, dessert);
                    animationController?.SetWalkingState(false, true);
                    yield return new WaitForSeconds(0.55f);

                    CurrentState = WaiterServiceState.ReturningWithTray;
                    animationController?.SetWalkingState(true, true);
                    yield return WalkToPoint(terraceCornerPoint);
                    yield return WalkToPoint(tableServicePoint);

                    CurrentState = WaiterServiceState.ServingAtTable;
                    animationController?.SetWalkingState(false, true);
                    if (presentation != null) presentation.SuspendFacingOverride = false;
                    animationController?.TriggerServePresent();
                    yield return new WaitForSeconds(0.65f);

                    tableService.PlaceTrayItemsOnTable();
                    animationController?.SetWalkingState(false, false);
                }
                else
                {
                    // Walk back to table empty-handed ready for dessert or bill
                    CurrentState = WaiterServiceState.ReturningWithTray;
                    animationController?.SetWalkingState(true, false);
                    yield return WalkToPoint(terraceCornerPoint);
                    yield return WalkToPoint(tableServicePoint);
                    if (presentation != null) presentation.SuspendFacingOverride = false;
                    animationController?.SetWalkingState(false, false);
                }
            }
            else if (thenBringBill)
            {
                yield return ExecuteBringBill(food, drink, dessert);
            }

            CurrentState = WaiterServiceState.AtTable;
        }

        IEnumerator ExecuteBringBill(string food, string drink, string dessert)
        {
            if (waiterActor == null) yield break;

            yield return new WaitForSeconds(0.45f);

            CurrentState = WaiterServiceState.WalkingToKitchen;
            if (presentation != null) presentation.SuspendFacingOverride = true;
            animationController?.SetWalkingState(true, false);
            yield return WalkToPoint(terraceCornerPoint);
            yield return WalkToPoint(kitchenCounterPoint);

            CurrentState = WaiterServiceState.PreparingAtKitchen;
            animationController?.SetWalkingState(false, true);
            yield return FaceWorldPoint(kitchenLookTarget, 0.35f);
            tableService?.LoadBillOntoWaiterTray(food, drink, dessert);
            yield return new WaitForSeconds(0.75f);

            CurrentState = WaiterServiceState.ReturningWithTray;
            animationController?.SetWalkingState(true, true);
            yield return WalkToPoint(terraceCornerPoint);
            yield return WalkToPoint(tableServicePoint);

            CurrentState = WaiterServiceState.ServingAtTable;
            animationController?.SetWalkingState(false, true);
            if (presentation != null) presentation.SuspendFacingOverride = false;
            animationController?.TriggerServePresent();
            yield return new WaitForSeconds(0.65f);

            tableService?.PlaceTrayItemsOnTable();
            animationController?.SetWalkingState(false, false);
            CurrentState = WaiterServiceState.AtTable;
        }

        IEnumerator WalkToPoint(Vector3 target)
        {
            target.y = waiterActor.position.y;
            while (Vector3.Distance(waiterActor.position, target) > 0.04f)
            {
                Vector3 to = target - waiterActor.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0004f)
                {
                    Quaternion desired = Quaternion.LookRotation(to.normalized, Vector3.up);
                    waiterActor.rotation = Quaternion.Slerp(waiterActor.rotation, desired, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
                }
                waiterActor.position = Vector3.MoveTowards(waiterActor.position, target, walkSpeed * Time.deltaTime);
                yield return null;
            }
            waiterActor.position = target;
        }

        IEnumerator FaceWorldPoint(Vector3 lookTarget, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                Vector3 to = lookTarget - waiterActor.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0004f)
                {
                    Quaternion desired = Quaternion.LookRotation(to.normalized, Vector3.up);
                    waiterActor.rotation = Quaternion.Slerp(waiterActor.rotation, desired, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
