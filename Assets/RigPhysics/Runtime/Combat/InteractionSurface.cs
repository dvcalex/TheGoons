using UnityEngine;

namespace MechLab
{
    public enum InteractionKind { Metal, Flesh }

    [DisallowMultipleComponent]
    public sealed class InteractionSurface : MonoBehaviour
    {
        public InteractionKind kind = InteractionKind.Metal;
        public static InteractionKind Resolve(Component collider)
        {
            var surface = collider ? collider.GetComponentInParent<InteractionSurface>() : null;
            return surface ? surface.kind : InteractionKind.Metal;
        }
        // Soft tissue dominates a mixed contact. A weapon carries its own surface.
        public static InteractionKind Pair(InteractionKind a, InteractionKind b) =>
            a == InteractionKind.Flesh || b == InteractionKind.Flesh ? InteractionKind.Flesh : InteractionKind.Metal;
    }
}
