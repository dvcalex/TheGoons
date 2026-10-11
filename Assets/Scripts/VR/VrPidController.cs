using System;
using UnityEngine;

// Adapted from Jalopy-Jetstream's PID_Controller.cs (Vazgriz PID controller).
// Original reference: https://github.com/vazgriz/PID_Controller
[Serializable]
public sealed class VrPidController
{
    public float proportionalGain = 90f;
    public float integralGain;
    public float derivativeGain = 18f;
    public float integralSaturation = 1f;
    public float outputLimit = 120f;

    private float previousError;
    private float integral;
    private bool initialized;

    public float Update(float dt, float currentValue, float targetValue)
    {
        if (dt <= 0f)
        {
            return 0f;
        }

        float error = targetValue - currentValue;
        integral = Mathf.Clamp(integral + error * dt, -integralSaturation, integralSaturation);
        float derivative = 0f;
        if (initialized)
        {
            derivative = (error - previousError) / dt;
        }

        previousError = error;
        initialized = true;
        float output = proportionalGain * error + integralGain * integral + derivativeGain * derivative;
        return Mathf.Clamp(output, -outputLimit, outputLimit);
    }

    public void Reset()
    {
        previousError = 0f;
        integral = 0f;
        initialized = false;
    }
}
