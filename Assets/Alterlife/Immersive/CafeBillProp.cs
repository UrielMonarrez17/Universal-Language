using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Physical grabbable French café bill tray ('L'Addition') showing the real itemized order
    /// and total in Euros matching what the player actually ordered with Lucas Moreau.
    /// </summary>
    public sealed class CafeBillProp : MonoBehaviour
    {
        public TMP_Text receiptText;
        public string LastFormattedReceipt { get; private set; } = "";
        public int ComputedTotalEuros { get; private set; }

        Vector3 tableSlotPos;
        Quaternion tableSlotRot = Quaternion.identity;
        bool placedOnTable;
        bool returningToTable;

        XRGrabInteractable grabInteractable;
        Rigidbody rb;

        void Awake()
        {
            SetupGrabAndPhysics();
            EnsureReceiptLabel();
        }

        void OnEnable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.AddListener(OnGrabbed);
                grabInteractable.selectExited.AddListener(OnReleased);
            }
        }

        void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnGrabbed);
                grabInteractable.selectExited.RemoveListener(OnReleased);
            }
        }

        void SetupGrabAndPhysics()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            if (GetComponentInChildren<Collider>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.015f, 0f);
                box.size = new Vector3(0.18f, 0.03f, 0.24f);
            }

            grabInteractable = GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
                grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
            grabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grabInteractable.throwOnDetach = false;
            grabInteractable.useDynamicAttach = true;
        }

        void EnsureReceiptLabel()
        {
            if (receiptText != null) return;
            var go = new GameObject("ReceiptText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.0175f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(0.115f, 0.175f);
            tmp.fontSize = 0.095f;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.color = new Color(0.14f, 0.11f, 0.09f, 1f);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            receiptText = tmp;
        }

        public void SetHeldByWaiterTray()
        {
            placedOnTable = false;
            returningToTable = false;
            if (grabInteractable != null) grabInteractable.enabled = false;
        }

        public void PlaceOnTable(Vector3 worldPos, Quaternion worldRot)
        {
            tableSlotPos = worldPos;
            tableSlotRot = worldRot;
            placedOnTable = true;
            returningToTable = false;
            transform.SetPositionAndRotation(worldPos, worldRot);
            if (grabInteractable != null) grabInteractable.enabled = true;
        }

        public void PopulateFromOrder(string food, string drink, string dessert)
        {
            EnsureReceiptLabel();

            var lines = new List<(string label, int price)>();
            if (!string.IsNullOrWhiteSpace(food))
                lines.Add((food.Trim(), LookupMenuPrice(food, 12)));
            if (!string.IsNullOrWhiteSpace(drink))
                lines.Add((drink.Trim(), LookupMenuPrice(drink, 4)));
            if (!string.IsNullOrWhiteSpace(dessert))
                lines.Add((dessert.Trim(), LookupMenuPrice(dessert, 5)));

            if (lines.Count == 0)
            {
                lines.Add(("Café crème", 4));
                lines.Add(("Croissant", 3));
            }

            int total = 0;
            var sb = new StringBuilder();
            sb.AppendLine("<b>LE CAFÉ DE PARIS</b>");
            sb.AppendLine("<size=75%>L'ADDITION · MERCI</size>");
            sb.AppendLine("----------------");
            for (int i = 0; i < lines.Count; i++)
            {
                total += lines[i].price;
                sb.AppendLine($"{lines[i].label}  <b>{lines[i].price} €</b>");
            }
            sb.AppendLine("----------------");
            sb.AppendLine($"<b>TOTAL : {total} €</b>");
            sb.Append("<size=70%>Service compris</size>");

            ComputedTotalEuros = total;
            LastFormattedReceipt = sb.ToString();
            if (receiptText != null)
                receiptText.text = LastFormattedReceipt;
        }

        public static int LookupMenuPrice(string itemName, int fallbackPrice)
        {
            string norm = RestaurantEvaluation.Normalize(itemName);
            if (norm.Contains("croque")) return 12;
            if (norm.Contains("quiche")) return 13;
            if (norm.Contains("salade") || norm.Contains("nicoise")) return 11;
            if (norm.Contains("soupe") || norm.Contains("oignon")) return 10;
            if (norm.Contains("jus") || norm.Contains("orange")) return 5;
            if (norm.Contains("creme brulee") || norm.Contains("brulee")) return 7;
            if (norm.Contains("cafe creme") || norm.Contains("creme")) return 4;
            if (norm.Contains("the") || norm.Contains("menthe") || norm.Contains("citron")) return 4;
            if (norm.Contains("eau") || norm.Contains("cafe") || norm.Contains("espresso") || norm.Contains("croissant")) return 3;
            return fallbackPrice;
        }

        void OnGrabbed(SelectEnterEventArgs _)
        {
            returningToTable = false;
        }

        void OnReleased(SelectExitEventArgs _)
        {
            if (placedOnTable)
            {
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                returningToTable = true;
            }
        }

        void Update()
        {
            if (!returningToTable || !placedOnTable) return;
            float t = 1f - Mathf.Exp(-9f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, tableSlotPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, tableSlotRot, t);
            if (Vector3.Distance(transform.position, tableSlotPos) < 0.004f)
            {
                transform.SetPositionAndRotation(tableSlotPos, tableSlotRot);
                returningToTable = false;
            }
        }
    }
}
