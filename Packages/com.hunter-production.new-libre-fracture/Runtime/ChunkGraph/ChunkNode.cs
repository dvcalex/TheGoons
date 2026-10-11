using UnityEngine;

namespace LibreFracture
{
    [RequireComponent(typeof(Rigidbody))]
    public class ChunkNode : MonoBehaviour
    {
        [Tooltip("Minimum collision impulse required to turn this chunk dynamic. 0 = any collision.")]
        [SerializeField] private float minImpactImpulse = 0f;

        private Rigidbody _rigidBody;
        public Rigidbody Rigidbody
        {
            get
            {
                if (_rigidBody == null)
                    _rigidBody = GetComponent<Rigidbody>();
                return _rigidBody;
            }
        }

        //TODO: If we use VHACD Colliders generation, there may be more than 1 collider for chunk. Consider refactor this to be a collection.
        private Collider _collider;
        private Collider Collider
        {
            get
            {
                if (_collider == null)
                    _collider = GetComponent<Collider>();
                return _collider;
            }
        }

        private MeshRenderer _renderer;

        private float _boundingVolume;
        public float BoundingVolume
        {
            get
            {
                if (_boundingVolume == 0)
                {
                    var size = Collider.bounds.size;
                    _boundingVolume = size.x * size.y * size.z;
                }
                return _boundingVolume;
            }
        }

        public bool IsDynamic => !Rigidbody.isKinematic;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            MakeKinematic();
        }

        private void OnEnable()
        {
            if (_renderer == null)
                _renderer = GetComponent<MeshRenderer>();

            _renderer.enabled = true;
            Collider.enabled = true;
            MakeKinematic();
        }

        private void OnDisable()
        {
            if (_renderer != null)
                _renderer.enabled = false;
            if (Collider != null)
                Collider.enabled = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if(collision.gameObject.layer != LayerMask.NameToLayer("Destructive"))
                return;

            if (collision.gameObject.layer == LayerMask.NameToLayer("Broken"))
                return;

            Debug.Log("HIT!!");

            if (IsDynamic)
                return;

            if (collision.impulse.magnitude < minImpactImpulse)
                return;

            MakeDynamic();
        }

        public void MakeKinematic()
        {
            Rigidbody.isKinematic = true;
            gameObject.layer = LayerMask.NameToLayer("Nonbroken");
        }

        public void MakeDynamic()
        {
            Rigidbody.isKinematic = false;
            gameObject.layer = LayerMask.NameToLayer("Broken");
            Rigidbody.WakeUp();
        }
    }
}
