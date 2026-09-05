using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [RequireComponent(typeof(HandCardInteraction))]
    public sealed class GameplayCardInteractionBinder : MonoBehaviour
    {
        [SerializeField] private string cardId;
        [SerializeField] private CombatCardKind kind;
        [SerializeField] private string cardName;
        [SerializeField] private string description;
        [SerializeField] private int cost;
        [SerializeField] private string status;
        [SerializeField] private bool canDrag;

        public bool CanDrag => canDrag;

        private void Awake()
        {
            ConfigureInteraction();
        }

        private void OnEnable()
        {
            ConfigureInteraction();
        }

        public void Bind(string id, CombatCardKind cardKind, string displayName, string cardDescription, int cardCost, string cardStatus, bool draggable)
        {
            cardId = id ?? string.Empty;
            kind = cardKind;
            cardName = displayName ?? string.Empty;
            description = cardDescription ?? string.Empty;
            cost = cardCost;
            status = cardStatus ?? string.Empty;
            canDrag = draggable;
            ConfigureInteraction();
        }

        public void ConfigureInteraction()
        {
            var interaction = GetComponent<HandCardInteraction>();
            if (interaction == null)
            {
                return;
            }

            interaction.Initialize(null, null);
            interaction.Configure(
                new CombatCardSnapshot(
                    cardId,
                    kind,
                    cardName,
                    description,
                    value: 0,
                    isUsable: canDrag,
                    isDiscarded: false,
                    status,
                    cost),
                canDrag,
                false);
        }
    }
}
