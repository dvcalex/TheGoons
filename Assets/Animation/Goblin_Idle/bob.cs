using UnityEngine;

public class bob : MonoBehaviour
{
    [Header("Bobbing Settings")]
    [Tooltip("How high and low the object moves.")]
    public float amplitude = 0.5f;

    [Tooltip("How fast the object bobs.")]
    public float frequency = 1f;

    // Stores the starting position of the object
    private Vector3 startPosition;

    void Start()
    {
        // Record the object's initial position
        startPosition = transform.position;
    }

    void Update()
    {
        // Calculate the new Y position using a sine wave
        float newY = startPosition.y + Mathf.Sin(Time.time * frequency) * amplitude;

        // Apply the new position while keeping X and Z the same
        transform.position = new Vector3(startPosition.x, newY, startPosition.z);
    }
}
