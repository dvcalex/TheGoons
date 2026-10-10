using UnityEngine;
using UnityEngine.Rendering;

// Cosmetic physics on every client. A local kinematic anchor follows the sole
// networked capsule; limb forces never feed back into gameplay physics.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerMovement))]
public sealed class DesktopRagdoll : MonoBehaviour
{
    public Material bodyMaterial;
    public Material headMaterial;
    public float poseSpring = 110f;
    public float poseDamper = 12f;
    public float maximumPoseTorque = 90f;
    public float strideAngle = 28f;
    public float strideLength = 2.5f;
    public float impactRecovery = 5f;

    public Rigidbody[] Bodies { get; private set; }
    private ConfigurableJoint[] joints;
    private Collider[] colliders;
    private Vector3[] teleportPositions;
    private PlayerMovement motor;
    private float phase, driveStrength = 1f;
    private bool previousAuthority;
    private Transform container;
    private Rigidbody anchorBody;
    private Vector3 previousRootPosition, visualVelocity;
    private Quaternion previousRootRotation;

    private void Start()
    {
        motor = GetComponent<PlayerMovement>();
        if (motor.IsServer && !motor.IsClient) return;
        Build();
        previousAuthority = motor.HasControl;
        SetAuthority(previousAuthority);
        if (motor.playerGFX != null) motor.playerGFX.gameObject.SetActive(false);
    }

    private void Build()
    {
        container = new GameObject("Local physics ragdoll").transform;
        // Never parent simulated bodies to a NetworkTransform/rotating Rigidbody.
        // Otherwise parent motion and the joint solver both move them.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(container.gameObject, gameObject.scene);
        container.SetPositionAndRotation(motor.rb.position, motor.rb.rotation);
        var anchorNode = new GameObject("Local capsule anchor");
        anchorNode.transform.SetParent(container, false);
        anchorBody = anchorNode.AddComponent<Rigidbody>();
        anchorBody.isKinematic = true;
        anchorBody.useGravity = false;
        anchorBody.interpolation = RigidbodyInterpolation.Interpolate;
        previousRootPosition = motor.rb.position;
        previousRootRotation = motor.rb.rotation;
        Bodies = new Rigidbody[6];
        joints = new ConfigurableJoint[6];
        colliders = new Collider[6];
        teleportPositions = new Vector3[6];
        // Torso, head, arms, legs. All transforms start aligned with the motor.
        MakeBody(0, "Torso", new Vector3(0f, 1.25f, 0f), 0.23f, 0.66f, 4f, anchorBody,
            Vector3.zero, new Vector3(0f, 1.25f, 0f), bodyMaterial);
        MakeBody(1, "Head", new Vector3(0f, 1.75f, 0f), 0.19f, 0.38f, 1.2f, Bodies[0],
            new Vector3(0f, -0.19f, 0f), new Vector3(0f, 0.31f, 0f), headMaterial);
        MakeBody(2, "Left arm", new Vector3(-0.36f, 1.1f, 0f), 0.10f, 0.64f, 0.8f, Bodies[0],
            new Vector3(0f, 0.30f, 0f), new Vector3(-0.36f, 0.15f, 0f), bodyMaterial);
        MakeBody(3, "Right arm", new Vector3(0.36f, 1.1f, 0f), 0.10f, 0.64f, 0.8f, Bodies[0],
            new Vector3(0f, 0.30f, 0f), new Vector3(0.36f, 0.15f, 0f), bodyMaterial);
        MakeBody(4, "Left leg", new Vector3(-0.18f, 0.51f, 0f), 0.12f, 0.64f, 1.2f, anchorBody,
            new Vector3(0f, 0.30f, 0f), new Vector3(-0.18f, 0.81f, 0f), bodyMaterial);
        MakeBody(5, "Right leg", new Vector3(0.18f, 0.51f, 0f), 0.12f, 0.64f, 1.2f, anchorBody,
            new Vector3(0f, 0.30f, 0f), new Vector3(0.18f, 0.81f, 0f), bodyMaterial);
        // Self collision would fight the motor capsule and cause constant jitter.
        for (int i = 0; i < colliders.Length; i++)
        {
            Physics.IgnoreCollision(colliders[i], motor.capsule);
            for (int j = i + 1; j < colliders.Length; j++) Physics.IgnoreCollision(colliders[i], colliders[j]);
        }
    }

    private void MakeBody(int index, string bodyName, Vector3 position, float radius, float height, float mass,
        Rigidbody connected, Vector3 anchor, Vector3 connectedAnchor, Material material)
    {
        var node = new GameObject(bodyName);
        node.layer = gameObject.layer;
        node.transform.SetParent(container, false);
        node.transform.localPosition = position;
        var collider = node.AddComponent<CapsuleCollider>();
        collider.radius = radius;
        collider.height = height;
        collider.sharedMaterial = motor.capsule.sharedMaterial;
        // Cosmetic bodies must not push networked players/props on any peer.
        collider.enabled = false;
        colliders[index] = collider;
        var body = node.AddComponent<Rigidbody>();
        body.mass = mass;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.solverIterations = 8;
        body.solverVelocityIterations = 4;
        body.maxAngularVelocity = 15f;
        Bodies[index] = body;
        var joint = node.AddComponent<ConfigurableJoint>();
        joints[index] = joint;
        joint.connectedBody = connected;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = anchor;
        joint.connectedAnchor = connectedAnchor;
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
        joint.lowAngularXLimit = new SoftJointLimit { limit = -80f };
        joint.highAngularXLimit = new SoftJointLimit { limit = 80f };
        joint.angularYLimit = new SoftJointLimit { limit = 35f };
        joint.angularZLimit = new SoftJointLimit { limit = 40f };
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.08f;
        joint.projectionAngle = 15f;
        joint.enableCollision = false;

        // Only the mesh is scaled. Rigidbody/collider/joint frames remain unit scale.
        var mesh = new GameObject("Mesh");
        mesh.layer = node.layer;
        mesh.transform.SetParent(node.transform, false);
        mesh.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        // Reuse the prefab's built-in capsule mesh instead of allocating primitive colliders.
        var filter = mesh.AddComponent<MeshFilter>();
        filter.sharedMesh = motor.playerGFX.GetComponentInChildren<MeshFilter>().sharedMesh;
        var renderer = mesh.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = motor.HasControl ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
    }

    private void SetAuthority(bool local)
    {
        for (int i = 0; i < Bodies.Length; i++)
        {
            Bodies[i].isKinematic = false;
            Bodies[i].GetComponentInChildren<Renderer>().shadowCastingMode = local ?
                ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }

    private void FixedUpdate()
    {
        if (Bodies == null) return;
        if (previousAuthority != motor.HasControl)
        {
            previousAuthority = motor.HasControl;
            SetAuthority(previousAuthority);
        }
        float dt = Time.fixedDeltaTime;
        // Remote NetworkTransform drives Transform, not necessarily rb velocity.
        Vector3 rootPosition = motor.HasControl ? motor.rb.position : transform.position;
        Quaternion rootRotation = motor.HasControl ? motor.rb.rotation : transform.rotation;
        Vector3 displacement = rootPosition - previousRootPosition;
        if (displacement.sqrMagnitude > 4f)
        {
            // A respawn/network teleport should carry the puppet, not stretch it.
            Quaternion turn = rootRotation * Quaternion.Inverse(previousRootRotation);
            foreach (var body in Bodies)
            {
                body.position = rootPosition + turn * (body.position - previousRootPosition);
                body.rotation = turn * body.rotation;
            }
            ResetVelocity();
            anchorBody.position = rootPosition;
            anchorBody.rotation = rootRotation;
            visualVelocity = Vector3.zero;
        }
        else
            visualVelocity = motor.HasControl ? motor.RelativeVelocity : displacement / dt;
        previousRootPosition = rootPosition;
        previousRootRotation = rootRotation;
        anchorBody.MovePosition(rootPosition);
        anchorBody.MoveRotation(rootRotation);
        float crouch = motor.CrouchAmount;
        float speed = Vector3.ProjectOnPlane(visualVelocity, Vector3.up).magnitude;
        bool grounded = motor.isGrounded;
        if (grounded) phase += speed / Mathf.Max(0.5f, strideLength) * Mathf.PI * 2f * dt;
        float gait = Mathf.Sin(phase) * strideAngle * Mathf.Clamp01(speed / motor.walkSpeed) * (1f - crouch * 0.75f);
        if (!grounded) gait = 0f;
        Vector3 localVelocity = Quaternion.Inverse(rootRotation) * visualVelocity;
        float lean = Mathf.Clamp(localVelocity.z * 1.4f, -9f, 9f);
        float sideLean = Mathf.Clamp(-localVelocity.x, -7f, 7f);
        driveStrength = Mathf.MoveTowards(driveStrength, motor.isStunned ? 0.08f : 1f, impactRecovery * dt);
        var drive = new JointDrive { positionSpring = poseSpring, positionDamper = poseDamper,
            maximumForce = maximumPoseTorque * driveStrength };
        for (int i = 0; i < joints.Length; i++)
        {
            joints[i].slerpDrive = drive;
            float weight = visualVelocity.y < 0f ? motor.fallingGravityWeight : 1f;
            // Match the root's requested gravity so springs aren't holding up a heavy puppet.
            Bodies[i].AddForce(Vector3.up * motor.gravity * weight, ForceMode.Acceleration);
        }
        joints[0].connectedAnchor = new Vector3(0f, 1.25f - 0.9f * crouch, 0f);
        joints[4].connectedAnchor = new Vector3(-0.18f, 0.81f - 0.38f * crouch, 0f);
        joints[5].connectedAnchor = new Vector3(0.18f, 0.81f - 0.38f * crouch, 0f);
        // All starting joint frames are identity; inverse local pose is the joint target.
        Pose(0, lean + crouch * 8f, sideLean);
        Pose(1, motor.HeadPitch - lean * 0.5f, -sideLean * 0.5f);
        Pose(2, -gait - crouch * 45f, -8f);
        Pose(3, gait - crouch * 45f, 8f);
        Pose(4, gait + crouch * 65f, 0f);
        Pose(5, -gait + crouch * 65f, 0f);
    }

    private void Pose(int index, float pitch, float roll) =>
        joints[index].targetRotation = Quaternion.Inverse(Quaternion.Euler(pitch, 0f, roll));

    public void AddVelocityChange(Vector3 velocity)
    {
        if (Bodies == null) return;
        foreach (var body in Bodies) if (!body.isKinematic) body.AddForce(velocity, ForceMode.VelocityChange);
    }
    public void ResetVelocity()
    {
        if (Bodies == null) return;
        foreach (var body in Bodies) if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
    }
    public void MoveRootTo(Vector3 position)
    {
        if (Bodies == null) return;
        Vector3 offset = position - motor.rb.position;
        for (int i = 0; i < Bodies.Length; i++) teleportPositions[i] = Bodies[i].position + offset;
        motor.rb.position = position;
        anchorBody.position = position;
        anchorBody.rotation = motor.rb.rotation;
        previousRootPosition = position;
        previousRootRotation = motor.rb.rotation;
        for (int i = 0; i < Bodies.Length; i++) Bodies[i].position = teleportPositions[i];
    }

    private void OnEnable()
    {
        if (container != null) container.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (container != null) container.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (container != null) Destroy(container.gameObject);
    }
}
