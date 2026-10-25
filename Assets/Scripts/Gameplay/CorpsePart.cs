using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>A part of a <see cref="Corpse"/> with its own rigidbody: tells the body about its hits (Unity reports
    /// them only to the rigidbody's own object).</summary>
    public class CorpsePart : MonoBehaviour
    {
        internal Corpse corpse;
        Rigidbody body;

        void Awake() => body = GetComponent<Rigidbody>();

        void OnCollisionEnter(Collision collision)
        {
            if (corpse != null) corpse.OnImpact(body, collision);
        }
    }
}
