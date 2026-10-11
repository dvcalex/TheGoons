using System;
using UnityEngine;

[Serializable]
public sealed class VrPidMotor
{
    public float positionGain = 90f;
    public float positionDamping = 18f;
    public float rotationGain = 65f;
    public float rotationDamping = 14f;
    public float maximumAcceleration = 120f;
    public float maximumAngularAcceleration = 100f;

    private readonly VrPidController[] axes =
    {
        new VrPidController(), new VrPidController(), new VrPidController(),
        new VrPidController(), new VrPidController(), new VrPidController()
    };

    public void Reset()
    {
        foreach (VrPidController axis in axes)
        {
            axis.Reset();
        }
    }

    public void Drive(Rigidbody body, Vector3 target, Quaternion rotation, float authority)
    {
        float dt = Time.fixedDeltaTime;
        Vector3 force = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            Configure(axes[i], positionGain, positionDamping, maximumAcceleration);
            force[i] = axes[i].Update(dt, body.position[i], target[i]);
        }

        force = Vector3.ClampMagnitude(force, maximumAcceleration) * authority;
        if (body.useGravity)
        {
            force -= Physics.gravity * authority;
        }

        body.AddForce(force, ForceMode.Acceleration);

        Quaternion difference = rotation * Quaternion.Inverse(body.rotation);
        if (difference.w < 0f)
        {
            difference = new Quaternion(-difference.x, -difference.y, -difference.z, -difference.w);
        }

        difference.ToAngleAxis(out float angle, out Vector3 direction);
        Vector3 error = Vector3.zero;
        if (angle > 0.001f && direction.sqrMagnitude > 0.001f)
        {
            error = direction.normalized * (angle * Mathf.Deg2Rad);
        }

        Vector3 torque = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            Configure(axes[i + 3], rotationGain, rotationDamping, maximumAngularAcceleration);
            torque[i] = axes[i + 3].Update(dt, 0f, error[i]);
        }

        body.AddTorque(Vector3.ClampMagnitude(torque, maximumAngularAcceleration) * authority, ForceMode.Acceleration);
    }

    private static void Configure(VrPidController axis, float gain, float damping, float limit)
    {
        axis.proportionalGain = gain;
        axis.derivativeGain = damping;
        axis.outputLimit = limit;
    }
}
